using System.Globalization;
using Avalonia.Media.Imaging;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    private const int ThumbnailCacheLimit = 240;
    private const int ConcurrentThumbnailRenders = 2;
    private readonly Dictionary<ThumbnailKey, LinkedListNode<(ThumbnailKey Key, Bitmap Image)>> _thumbnailIndex = [];
    private readonly LinkedList<(ThumbnailKey Key, Bitmap Image)> _thumbnailOrder = new();
    private readonly SemaphoreSlim _thumbnailRenders = new(ConcurrentThumbnailRenders);
    private readonly HashSet<Task> _backgroundReads = [];
    private CancellationTokenSource _backgroundLifetime = new();
    private int _thumbnailRequests;

    /// <summary>True while thumbnails, suggestions or a page render may still be reading library files.</summary>
    internal bool IsReadingInBackground => _thumbnailRequests > 0 || _suggestionTimer.IsEnabled || _suggestionRequest is not null ||
        _backgroundReads.Any(task => !task.IsCompleted);
    private bool _libraryHasDocuments = true;

    /// <summary>
    /// Runs an optional read (a thumbnail or suggestions) off the UI thread and remembers it, so the library is
    /// only closed after the read has released its files.
    /// </summary>
    private Task<T> TrackBackgroundRead<T>(Func<Task<T>> read)
    {
        var task = Task.Run(read, _backgroundLifetime.Token);
        _backgroundReads.Add(task);
        task.ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.Post(() => _backgroundReads.Remove(task)), TaskScheduler.Default);
        return task;
    }

    private Task<T> TrackBackgroundRead<T>(Func<T> read) => TrackBackgroundRead(() => Task.FromResult(read()));

    /// <summary>Cancels optional reads and waits up to five seconds for them to finish with the open library.</summary>
    private void StopBackgroundReads()
    {
        _backgroundLifetime.Cancel();
        var pending = _backgroundReads.Where(task => !task.IsCompleted).ToArray();
        try
        {
            if (pending.Length > 0) Task.WaitAll(pending, TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Cancelled or failed reads have already released the library.
        }
        _backgroundReads.Clear();
        _backgroundLifetime.Dispose();
        _backgroundLifetime = new CancellationTokenSource();
    }

    /// <summary>The Inbox is empty: show "Inbox clear" or, for a new library, the ways to bring paperwork in.</summary>
    public bool ShowInboxZero => HasLibrary && InboxSelected && ActiveSavedSearch is null && !HasDocuments;
    public bool ShowWelcome => ShowInboxZero && !_libraryHasDocuments;
    public bool ShowInboxClear => ShowInboxZero && _libraryHasDocuments;
    public bool ShowFilingCard => HasSelection && !HasBatchSelection;
    public bool HasDueReminders => DueReminderCount > 0;
    public string DueReminderLabel => DueReminderCount == 0 ? "" : DueReminderCount.ToString(CultureInfo.CurrentCulture);
    public string ZeroStateDetail => DueReminderCount switch
    {
        0 => DesktopText.Translate("Everything you have added is filed. New scans and files will wait here."),
        1 => DesktopText.Translate("Everything is filed. One reminder is due."),
        _ => DesktopText.Format($"Everything is filed. {DueReminderCount} reminders are due.")
    };

    public string SelectedAddedLabel => SelectedDocument is { } document
        ? document.AddedAt.LocalDateTime.ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture) : "";
    public string SelectedPagesLabel => SelectedDocument is { PageCount: var count }
        ? (count == 1 ? DesktopText.Translate("1 page") : DesktopText.Format($"{count} pages")) : "";
    public string SelectedReviewState => SelectedDocument?.Reviewed == true
        ? DesktopText.Translate("Reviewed") : DesktopText.Translate("In the Inbox");
    public bool SelectedIsReviewed => SelectedDocument?.Reviewed == true;

    private void NotifyWorkspaceSelection()
    {
        OnPropertyChanged(nameof(ShowFilingCard));
        OnPropertyChanged(nameof(SelectedAddedLabel));
        OnPropertyChanged(nameof(SelectedPagesLabel));
        OnPropertyChanged(nameof(SelectedReviewState));
        OnPropertyChanged(nameof(SelectedIsReviewed));
        OnPropertyChanged(nameof(HasSuggestedDate));
        OnPropertyChanged(nameof(HasSuggestions));
        OnPropertyChanged(nameof(SuggestionCount));
        OnPropertyChanged(nameof(SuggestionSummary));
        OnPropertyChanged(nameof(SuggestionReason));
        RequestSuggestions(SelectedDocument);
    }

    private void NotifyZeroState()
    {
        if (ShowInboxZero && _library is { } library)
        {
            try
            {
                _libraryHasDocuments = library.Find(new DocumentFilter()).Items.Count > 0;
            }
            catch (Exception error) when (error is IOException or Microsoft.Data.Sqlite.SqliteException)
            {
                _libraryHasDocuments = true;
            }
        }
        OnPropertyChanged(nameof(ShowInboxZero));
        OnPropertyChanged(nameof(ShowWelcome));
        OnPropertyChanged(nameof(ShowInboxClear));
    }

    partial void OnDueReminderCountChanged(long value)
    {
        OnPropertyChanged(nameof(HasDueReminders));
        OnPropertyChanged(nameof(DueReminderLabel));
        OnPropertyChanged(nameof(ZeroStateDetail));
    }

    /// <summary>
    /// Returns a cached first-page thumbnail, rendering at most two at a time. O(1) cache lookups; the cache keeps
    /// the most recent <see cref="ThumbnailCacheLimit"/> small bitmaps.
    /// </summary>
    internal async Task<Bitmap?> GetDocumentThumbnailAsync(DocumentSummary document, CancellationToken token)
    {
        if (TryGetCachedThumbnail(document) is { } cached) return cached;
        if (_library is not { } library) return null;
        var lifetime = _backgroundLifetime.Token;
        _thumbnailRequests++;
        try
        {
            await _thumbnailRenders.WaitAsync(token);
        }
        catch
        {
            _thumbnailRequests--;
            throw;
        }
        try
        {
            if (TryGetCachedThumbnail(document) is { } rendered) return rendered;
            if (!ReferenceEquals(library, _library) || lifetime.IsCancellationRequested) return null;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime);
            var cancel = linked.Token;
            using var png = await TrackBackgroundRead(() => library.RenderPageThumbnailAsync(document, 1, cancel));
            if (!ReferenceEquals(library, _library)) return null;
            var image = new Bitmap(png);
            CacheThumbnail(ThumbnailKey.For(document), image);
            return image;
        }
        catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException)
        {
            return null;
        }
        finally
        {
            _thumbnailRenders.Release();
            _thumbnailRequests--;
        }
    }

    internal Bitmap? TryGetCachedThumbnail(DocumentSummary document)
    {
        if (!_thumbnailIndex.TryGetValue(ThumbnailKey.For(document), out var node)) return null;
        _thumbnailOrder.Remove(node);
        _thumbnailOrder.AddFirst(node);
        return node.Value.Image;
    }

    private void CacheThumbnail(ThumbnailKey key, Bitmap image)
    {
        if (_thumbnailIndex.Remove(key, out var existing)) _thumbnailOrder.Remove(existing);
        _thumbnailIndex[key] = _thumbnailOrder.AddFirst((key, image));
        while (_thumbnailOrder.Count > ThumbnailCacheLimit && _thumbnailOrder.Last is { } oldest)
        {
            // Rows may still show an evicted bitmap, so it is released by the collector rather than disposed here.
            _thumbnailOrder.RemoveLast();
            _thumbnailIndex.Remove(oldest.Value.Key);
        }
    }

    /// <summary>Drops thumbnails for documents an operation may have changed, so the next row render reads the new file.</summary>
    private void ForgetThumbnails(IEnumerable<long> documentIds)
    {
        var ids = documentIds.ToHashSet();
        if (ids.Count == 0) return;
        foreach (var node in _thumbnailIndex.Where(entry => ids.Contains(entry.Key.DocumentId)).ToArray())
        {
            _thumbnailOrder.Remove(node.Value);
            _thumbnailIndex.Remove(node.Key);
        }
    }

    private void ForgetAllThumbnails()
    {
        _thumbnailIndex.Clear();
        _thumbnailOrder.Clear();
    }

    private readonly record struct ThumbnailKey(long DocumentId, string RelativePath, int PageCount, bool HasText)
    {
        internal static ThumbnailKey For(DocumentSummary document) =>
            new(document.Id, document.RelativePath, document.PageCount, document.HasText);
    }
}
