using Avalonia.Threading;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(180);
    private readonly DispatcherTimer _searchTimer = new() { Interval = SearchDelay };
    private readonly SemaphoreSlim _searchSlot = new(initialCount: 1, maxCount: 1);
    private readonly Func<InboxLibrary, DocumentFilter, CancellationToken, DocumentPage> _searchDocuments;
    private CancellationTokenSource? _searchCancellation;

    partial void OnQueryChanged(string value)
    {
        CancelTypedSearch();
        if (_library is null) return;
        HasMore = false;
        ResultCount = "Searching…";
        _searchTimer.Start();
    }

    partial void OnSortOptionChanged(SortChoice? value) => LoadResults();

    private void CancelTypedSearch()
    {
        _searchTimer.Stop();
        _searchCancellation?.Cancel();
        _searchCancellation = null;
    }

    private async Task LoadTypedSearchAsync()
    {
        _searchTimer.Stop();
        if (_library is not { } library) return;

        var filter = CurrentFilter();
        using var search = new CancellationTokenSource();
        _searchCancellation = search;
        var token = search.Token;
        try
        {
            await _searchSlot.WaitAsync(token);
            DocumentPage page;
            try
            {
                page = await Task.Run(() => _searchDocuments(library, filter, token), token);
            }
            finally
            {
                _searchSlot.Release();
            }

            // A cancelled provider can still finish its read. Only the current request can publish its page.
            if (token.IsCancellationRequested) return;
            UpdateResultHeading(filter);
            ApplyResultPage(filter, page);
        }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested) ReportResultFailure(error);
        }
        finally
        {
            if (ReferenceEquals(_searchCancellation, search)) _searchCancellation = null;
        }
    }

    private void LoadResults(long? preferredId = null, bool append = false, bool reloadPreview = false)
    {
        CancelTypedSearch();
        var filter = CurrentFilter(append ? Documents.Count : 0);
        UpdateResultHeading(filter);
        if (_library is not { } library) return;

        try
        {
            ApplyResultPage(filter, library.Find(filter), preferredId, reloadPreview);
        }
        catch (Exception error)
        {
            ReportResultFailure(error);
        }
    }

    private DocumentFilter CurrentFilter(int offset = 0) => new(Query, _activeTag,
        _inboxOnly && string.IsNullOrWhiteSpace(Query), offset, SortOption?.Order ?? DocumentSort.Automatic);

    private void UpdateResultHeading(DocumentFilter filter)
    {
        _watchResultsPending = false;
        bool hasQuery = !string.IsNullOrWhiteSpace(filter.Query);
        Heading = hasQuery ? "Search results" : filter.Tag ?? (filter.InboxOnly ? "Inbox" : "All documents");
        EmptyMessage = hasQuery || filter.Tag is not null ? "No documents match." :
            filter.InboxOnly ? "Add a file to start your inbox." : "This library has no documents yet.";
        OnPropertyChanged(nameof(InboxSelected));
        OnPropertyChanged(nameof(AllSelected));
    }

    private void ApplyResultPage(DocumentFilter filter, DocumentPage page, long? preferredId = null, bool reloadPreview = false)
    {
        long? selection = preferredId ?? SelectedDocument?.Id;
        ReplaceResults(filter.Offset > 0 ? [.. Documents, .. page.Items] : page.Items, selection, reloadPreview);
        HasMore = page.HasMore;
        ResultCount = HasMore ? $"Showing {DocumentCount(Documents.Count)}" : DocumentCount(Documents.Count);
        if (filter.Tag is not null && !string.IsNullOrWhiteSpace(filter.Query)) ResultCount += $" in {filter.Tag}";
    }

    private void ReportResultFailure(Exception error)
    {
        HasMore = false;
        ResultCount = "Results unavailable";
        Status = error.Message;
    }
}
