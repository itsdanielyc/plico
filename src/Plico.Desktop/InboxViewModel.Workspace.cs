using System.Globalization;
using Avalonia.Media.Imaging;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    private const int ThumbnailCacheLimit = 240;
    private const int ConcurrentThumbnailRenders = 2;
    private sealed record CachedThumbnail(long DocumentId, string ContentRevision, Bitmap Image);
    private readonly Dictionary<long, LinkedListNode<CachedThumbnail>> _thumbnailIndex = [];
    private readonly LinkedList<CachedThumbnail> _thumbnailOrder = new();
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

    /// <summary>Cancels readers and returns their worker tasks, so the library lease can outlive every file handle.</summary>
    internal Task StopBackgroundReads()
    {
        CancelTypedSearch();
        CancelPreview();
        ClearPageThumbnails();
        ClearSuggestions();
        _backgroundLifetime.Cancel();
        return Task.WhenAll(_backgroundReads);
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
    /// the most recent <see cref="ThumbnailCacheLimit"/> small bitmaps. A stale thumbnail is rendered again.
    /// </summary>
    internal async Task<Bitmap?> GetDocumentThumbnailAsync(DocumentSummary document, CancellationToken token)
    {
        if (TryGetFreshThumbnail(document) is { } cached) return cached;
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
            if (TryGetFreshThumbnail(document) is { } rendered) return rendered;
            if (!ReferenceEquals(library, _library) || lifetime.IsCancellationRequested) return null;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime);
            var cancel = linked.Token;
            using var png = await TrackBackgroundRead(() => library.RenderPageThumbnailAsync(document, 1, cancel));
            if (cancel.IsCancellationRequested || !ReferenceEquals(library, _library)) return null;
            var image = new Bitmap(png);
            CacheThumbnail(document, image);
            return image;
        }
        catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException)
        {
            // The file can no longer be read, so its earlier picture is no longer true either.
            if (!token.IsCancellationRequested && !lifetime.IsCancellationRequested) DropThumbnail(document.Id);
            return null;
        }
        finally
        {
            _thumbnailRenders.Release();
            _thumbnailRequests--;
        }
    }

    /// <summary>True when the cached first page was rendered from an earlier revision of this PDF.</summary>
    internal bool IsThumbnailStale(DocumentSummary document) =>
        _thumbnailIndex.TryGetValue(document.Id, out var node) && node.Value.ContentRevision != document.ContentRevision;

    private Bitmap? TryGetFreshThumbnail(DocumentSummary document) =>
        IsThumbnailStale(document) ? null : TryGetCachedThumbnail(document);

    /// <summary>Returns the cached thumbnail, which may be stale. Rows show it while a new render is on its way.</summary>
    internal Bitmap? TryGetCachedThumbnail(DocumentSummary document)
    {
        if (!_thumbnailIndex.TryGetValue(document.Id, out var node)) return null;
        _thumbnailOrder.Remove(node);
        _thumbnailOrder.AddFirst(node);
        return node.Value.Image;
    }

    private void CacheThumbnail(DocumentSummary document, Bitmap image)
    {
        DropThumbnail(document.Id);
        _thumbnailIndex[document.Id] = _thumbnailOrder.AddFirst(new CachedThumbnail(document.Id, document.ContentRevision, image));
        if (_thumbnailOrder.Count > ThumbnailCacheLimit) DropThumbnail(_thumbnailOrder.Last!.Value.DocumentId);
    }

    // Rows may still show a dropped bitmap, so it is released by the collector rather than disposed here.
    private void DropThumbnail(long documentId)
    {
        if (_thumbnailIndex.Remove(documentId, out var node)) _thumbnailOrder.Remove(node);
    }

    private void ForgetAllThumbnails()
    {
        _thumbnailIndex.Clear();
        _thumbnailOrder.Clear();
    }
}
