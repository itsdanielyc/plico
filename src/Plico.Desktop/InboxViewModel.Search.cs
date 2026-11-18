using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(180);
    private readonly DispatcherTimer _searchTimer = new() { Interval = SearchDelay };
    private readonly SemaphoreSlim _searchSlot = new(initialCount: 1, maxCount: 1);
    private readonly Func<InboxLibrary, DocumentFilter, CancellationToken, DocumentPage> _searchDocuments;
    private CancellationTokenSource? _searchCancellation;
    private string? _appliedQuery;

    public bool HasPageMatchAction => !string.IsNullOrWhiteSpace(Query) && Query == _appliedQuery &&
        SelectedDocument is { HasText: true, PageCount: > 1, Match.Length: > 0 };

    [RelayCommand]
    private Task GoToMatchAsync()
    {
        if (!HasPageMatchAction || SelectedDocument is not { } document) return Task.CompletedTask;
        string query = Query;
        int startingPage = _pageNumber;
        return RunOperationAsync("Finding a matching page…", async token =>
        {
            var library = RequireLibrary();
            int? match = await Task.Run(() => library.FindFirstMatchingPage(document, query, token), token);
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_library, library) || Query != query || SelectedDocument?.Id != document.Id ||
                _pageNumber != startingPage)
            {
                Status = "Search or preview changed.";
                return;
            }
            if (match is not int page)
            {
                Status = "0 matching pages.";
                return;
            }
            if (page > document.PageCount)
            {
                Status = "Refresh the index to see the PDF’s new pages.";
                return;
            }
            ShowPreviewPage(page);
            Status = $"Match on page {page}.";
        });
    }

    partial void OnQueryChanged(string value)
    {
        CancelTypedSearch();
        OnPropertyChanged(nameof(HasPageMatchAction));
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
        _appliedQuery = filter.Query;
        ReplaceResults(filter.Offset > 0 ? [.. Documents, .. page.Items] : page.Items, selection, reloadPreview);
        HasMore = page.HasMore;
        ResultCount = HasMore ? $"Showing {DocumentCount(Documents.Count)}" : DocumentCount(Documents.Count);
        if (filter.Tag is not null && !string.IsNullOrWhiteSpace(filter.Query)) ResultCount += $" in {filter.Tag}";
    }

    private void ReportResultFailure(Exception error)
    {
        _appliedQuery = null;
        OnPropertyChanged(nameof(HasPageMatchAction));
        HasMore = false;
        ResultCount = "Results unavailable";
        Status = error.Message;
    }
}
