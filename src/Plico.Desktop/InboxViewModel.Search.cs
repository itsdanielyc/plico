using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    private const string WithoutTextQuery = "text:missing";
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(180);
    private readonly DispatcherTimer _searchTimer = new() { Interval = SearchDelay };
    private readonly SemaphoreSlim _searchSlot = new(initialCount: 1, maxCount: 1);
    private readonly Func<InboxLibrary, DocumentFilter, CancellationToken, DocumentPage> _searchDocuments;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _pageMatchCancellation;
    private CancellationTokenSource? _highlightCancellation;
    [ObservableProperty] private IReadOnlyList<PdfPageHighlight> _previewHighlights = [];
    private string? _appliedQuery;
    private bool HasExpandedResults => Documents.Count > InboxLibrary.DefaultResultPageSize;

    [RelayCommand]
    private Task ChooseSearchFolderAsync() => RunOperationAsync(DesktopText.Translate("Choose a library folder to search…"), async token =>
    {
        var library = RequireLibrary();
        string? folder = await _dialogs.PickFolderAsync(DesktopText.Translate("Choose a library folder to search"), library.PdfDirectory);
        if (folder is null)
        {
            Status = DesktopText.Translate("Folder search cancelled.");
            return;
        }

        token.ThrowIfCancellationRequested();
        Query = library.CreateFolderSearchQuery(folder, Query);
        Status = DesktopText.Translate("Folder search applied.");
        LoadResults();
    });

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageMatchActionLabel))]
    [NotifyPropertyChangedFor(nameof(PageMatchActionHelp))]
    private bool _pageMatchNavigationActive;

    public bool HasPageMatchAction => !string.IsNullOrWhiteSpace(Query) && Query == _appliedQuery &&
        SelectedDocument is { HasText: true, PageCount: > 1, Match.Length: > 0 };
    public string PageMatchActionLabel => PageMatchNavigationActive ? DesktopText.Translate("Next match") : DesktopText.Translate("Go to match");

    /// <summary>What the next-match chevron does, for screen readers; its tooltip is its name.</summary>
    public string PageMatchActionHelp => PageMatchNavigationActive
        ? DesktopText.Translate("Go to the next page that matches the text terms")
        : DesktopText.Translate("Go to the first page that matches the text terms");

    [RelayCommand]
    private Task GoToMatchAsync() => SeekPageMatchAsync(PageMatchDirection.Forward);

    [RelayCommand]
    private Task PreviousMatchAsync() => SeekPageMatchAsync(PageMatchDirection.Backward);

    private Task SeekPageMatchAsync(PageMatchDirection direction)
    {
        if (!HasPageMatchAction || SelectedDocument is not { } document) return Task.CompletedTask;
        bool findFirst = !PageMatchNavigationActive;
        if (findFirst && direction == PageMatchDirection.Backward) return Task.CompletedTask;
        string query = Query;
        int startingPage = _pageNumber;
        return RunOperationAsync(DesktopText.Translate("Finding a matching page…"), async token =>
        {
            var library = RequireLibrary();
            using var source = CancellationTokenSource.CreateLinkedTokenSource(token);
            _pageMatchCancellation = source;
            int? match;
            try
            {
                var searchToken = source.Token;
                match = await Task.Run(() => findFirst
                    ? library.FindFirstMatchingPage(document, query, searchToken)
                    : library.FindAdjacentMatchingPage(document, query, startingPage, direction, searchToken), searchToken);
                searchToken.ThrowIfCancellationRequested();
            }
            catch (Exception) when (source.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
                Status = DesktopText.Translate("Search or preview changed.");
                return;
            }
            finally
            {
                _pageMatchCancellation = null;
            }

            if (!ReferenceEquals(_library, library) || Query != query || SelectedDocument?.Id != document.Id ||
                _pageNumber != startingPage)
            {
                Status = DesktopText.Translate("Search or preview changed.");
                return;
            }
            if (match is not int page)
            {
                Status = (findFirst, direction) switch
                {
                    (true, _) => "0 matching pages.",
                    (false, PageMatchDirection.Forward) => "No later matching pages.",
                    _ => "No earlier matching pages."
                };
                return;
            }
            if (page > document.PageCount)
            {
                Status = DesktopText.Translate("Refresh the index to see the PDF’s new pages.");
                return;
            }
            ShowPreviewPage(page);
            PageMatchNavigationActive = true;
            Status = DesktopText.Format($"Match on page {page}.");
        });
    }

    partial void OnQueryChanged(string value)
    {
        // A correction still waiting for a pause belongs to the query before this key.
        _queryHintTimer.Stop();
        _queryHintAtOnce = false;
        CancelPreviewHighlights();
        OnPropertyChanged(nameof(ShowSaveSearch));
        if (_restoringSavedSearch) return;
        ActiveSavedSearch = null;
        ResetPageMatchNavigation();
        CancelTypedSearch();
        OnPropertyChanged(nameof(HasPageMatchAction));
        if (_library is null) return;
        SetSearchPending();
        _searchTimer.Start();
    }

    private void SetSearchPending()
    {
        HasMore = false;
        ResultCount = "Searching…";
        EmptyMessage = "";
    }

    private void ResetPageMatchNavigation()
    {
        CancelPageMatchSearch();
        PageMatchNavigationActive = false;
    }

    private void CancelPageMatchSearch() => _pageMatchCancellation?.Cancel();

    partial void OnSortOptionChanged(SortChoice? value)
    {
        if (_restoringSavedSearch) return;
        ActiveSavedSearch = null;
        LoadResults();
    }

    private void CancelTypedSearch()
    {
        _searchTimer.Stop();
        _searchCancellation?.Cancel();
        _searchCancellation = null;
    }

    /// <summary>
    /// Runs the query in the field. <paramref name="whileTyping"/> is the pause timer's search: a query that cannot run
    /// then keeps its correction back until typing has paused a little longer, so a filter still being typed
    /// ("status:i") never flashes a red line.
    /// </summary>
    private async Task LoadTypedSearchAsync(bool preserveView = false, bool whileTyping = false)
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
                page = await TrackBackgroundRead(() => _searchDocuments(library, filter, token));
            }
            finally
            {
                _searchSlot.Release();
            }

            // A cancelled provider can still finish its read. Only the current request can publish its page.
            if (token.IsCancellationRequested) return;
            if (page.QueryIssue is { } issue)
            {
                ShowQueryIssue(issue, atOnce: !whileTyping);
                return;
            }
            if (preserveView && (HasBatchSelection || HasExpandedResults ||
                (SelectedDocument is { } open && !page.Items.Any(document => document.Id == open.Id))))
            {
                HasDeferredWatchResults = true;
                return;
            }
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

    private void LoadResults(long? preferredId = null, bool append = false, bool reloadPreview = false,
        bool retainLoadedResults = false, ReviewPosition? reviewPosition = null)
    {
        CancelTypedSearch();
        int offset = append ? Documents.Count : 0;
        var filter = CurrentFilter(offset);
        if (_library is not { } library)
        {
            UpdateResultHeading(filter);
            return;
        }

        try
        {
            var page = ReadResults(library, filter, retainLoadedResults && !append);
            var issue = page.QueryIssue;
            if (issue is { } typedIssue)
            {
                ShowQueryIssue(typedIssue);
                // The queue still shows the results of the last search that could run, and the card one of them. A
                // change to a document reloads those results from the filter that found them, so neither keeps showing
                // what is no longer true (a reviewed document in the Inbox, a trashed one on the card).
                if (_shownFilter is not { } shown) return;
                filter = shown with { Offset = offset };
                page = ReadResults(library, filter, retainLoadedResults && !append);
                if (page.QueryIssue is not null) return;
            }
            UpdateResultHeading(filter);
            if (reviewPosition is { } position && position.Filter == filter)
            {
                preferredId = ChooseReviewResult(page.Items, position);
            }
            ApplyResultPage(filter, page, preferredId, reloadPreview, keepQueryHint: issue is not null);
        }
        catch (Exception error)
        {
            UpdateResultHeading(filter);
            ReportResultFailure(error);
        }
    }

    /// <summary>Reads a page of results, or as many rows as are loaded when an expanded list reloads in place.</summary>
    private DocumentPage ReadResults(InboxLibrary library, DocumentFilter filter, bool retainLoadedResults)
    {
        bool reloadExpandedResults = retainLoadedResults && HasExpandedResults && _appliedQuery == filter.Query;
        return reloadExpandedResults ? library.Find(filter, Documents.Count, CancellationToken.None) : library.Find(filter);
    }

    /// <summary>
    /// The filter that found the results on screen, from the first row. While the typed search cannot run it is the
    /// last search that could, and reloads after a change read it again.
    /// </summary>
    private DocumentFilter? _shownFilter;

    // The typed query that could not run, while it is still the one in the field.
    private string? _unrunnableQuery;

    /// <summary>True while the search field holds a query that cannot run, so the results on screen belong to an earlier one.</summary>
    private bool QueryCannotRun => _unrunnableQuery is not null && _unrunnableQuery == Query;

    private DocumentFilter CurrentFilter(int offset = 0) => new(Query, _activeTag,
        _inboxOnly && string.IsNullOrWhiteSpace(Query), offset, SortOption?.Order ?? DocumentSort.Automatic);

    private void UpdateResultHeading(DocumentFilter filter)
    {
        HasDeferredWatchResults = false;
        bool hasQuery = !string.IsNullOrWhiteSpace(filter.Query);
        if (WithoutTextSelected)
        {
            Heading = DesktopText.Translate("Without text");
            SetEmptyState(DesktopText.Translate("Everything is searchable"), DesktopText.Translate("No PDFs without searchable text."));
        }
        else
        {
            Heading = ActiveSavedSearch?.Name ?? (hasQuery ? DesktopText.Translate("Search results") : filter.Tag ?? (filter.InboxOnly ? DesktopText.Translate("Inbox") : DesktopText.Translate("All documents")));
            if (hasQuery || filter.Tag is not null)
                SetEmptyState(DesktopText.Translate("No documents match"), DesktopText.Translate("Try other words or a different filter."));
            else if (filter.InboxOnly)
                SetEmptyState(DesktopText.Translate("Nothing here yet"), DesktopText.Translate("Add a file to start your inbox."));
            else
                SetEmptyState(DesktopText.Translate("No documents yet"), DesktopText.Translate("This library has no documents yet."));
        }
        OnPropertyChanged(nameof(InboxSelected));
        OnPropertyChanged(nameof(AllSelected));
        OnPropertyChanged(nameof(WithoutTextSelected));
        NotifyZeroState();
    }

    private void ApplyResultPage(DocumentFilter filter, DocumentPage page, long? preferredId = null, bool reloadPreview = false,
        bool keepQueryHint = false)
    {
        long? selection = preferredId ?? SelectedDocument?.Id;
        bool queryChanged = _appliedQuery != filter.Query;
        _appliedQuery = filter.Query;
        _shownFilter = filter with { Offset = 0 };
        ReplaceResults(filter.Offset > 0 ? [.. Documents, .. page.Items] : page.Items, selection, reloadPreview);
        if (queryChanged && Preview is not null) UpdatePreviewHighlights();
        HasMore = page.HasMore;
        if (!keepQueryHint)
        {
            _unrunnableQuery = null;
            _queryHintTimer.Stop();
            SearchQueryHint = "";
        }

        ResultCount = HasMore ? $"Showing {DocumentCount(Documents.Count)}" : DocumentCount(Documents.Count);
        if (filter.Tag is not null && !string.IsNullOrWhiteSpace(filter.Query)) ResultCount += $" in {filter.Tag}";
        _shownResults = (ResultCount, EmptyTitle, EmptyMessage);
    }

    /// <summary>
    /// The one line under the search field while the typed search cannot run, saying how to repair it
    /// ("Use one review status: status:inbox or status:reviewed."); empty while the search runs.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchQueryHint))]
    private string _searchQueryHint = "";

    public bool HasSearchQueryHint => SearchQueryHint.Length > 0;

    /// <summary>
    /// How long typing must pause, after the pause search found the query cannot run, before the correction shows: with
    /// the search's own 180 ms, a little under a second without a key.
    /// </summary>
    internal TimeSpan QueryHintDelay { get; set; } = TimeSpan.FromMilliseconds(700);

    private readonly DispatcherTimer _queryHintTimer = new();
    private string _pendingQueryHint = "";

    // Set by Enter in the search field: the search the person asked for says at once whether it can run.
    private bool _queryHintAtOnce;

    /// <summary>
    /// Enter in the search field: runs a search still waiting for the pause now, and shows a correction still waiting
    /// for the pause at once.
    /// </summary>
    internal void SearchNow()
    {
        if (_library is null) return;
        _queryHintAtOnce = true;
        if (_searchTimer.IsEnabled) _ = LoadTypedSearchAsync();
        else if (_queryHintTimer.IsEnabled) ShowPendingQueryHint();
    }

    private void ShowPendingQueryHint()
    {
        _queryHintTimer.Stop();
        _queryHintAtOnce = false;
        if (QueryCannotRun) SearchQueryHint = _pendingQueryHint;
    }

    // What the queue said about the results it shows, so a search that cannot run leaves the count and the empty state
    // of the last results that could.
    private (string Count, string EmptyTitle, string EmptyMessage) _shownResults = ("", "", "");

    /// <summary>
    /// A typed search that cannot run keeps the last valid results, the open document and its page: only the line
    /// under the field changes. Nothing moves in the queue, so the correction never lands in the narrow strip.
    /// </summary>
    private void ShowQueryIssue(SearchQueryIssue issue, bool atOnce = true)
    {
        _unrunnableQuery = Query;
        HasMore = false;
        ResultCount = _shownResults.Count;
        SetEmptyState(_shownResults.EmptyTitle, _shownResults.EmptyMessage);
        _pendingQueryHint = SearchCorrection(issue);
        // A line already showing changes its words at once; a new one waits for typing to pause.
        if (atOnce || _queryHintAtOnce || HasSearchQueryHint)
        {
            ShowPendingQueryHint();
            return;
        }
        _queryHintTimer.Stop();
        _queryHintTimer.Interval = QueryHintDelay;
        _queryHintTimer.Start();
    }

    private static string SearchCorrection(SearchQueryIssue issue) => issue switch
    {
        SearchQueryIssue.Tag =>
            DesktopText.Format($"Use a tag of up to {InboxLibrary.MaximumTagNameLength} printable characters, like tag:tax or tag:\"Property tax\"."),
        SearchQueryIssue.AddedDate => DesktopText.Translate("Use one date added, like added:2024-02, or dates that overlap."),
        SearchQueryIssue.DocumentDate => DesktopText.Translate("Use one date on the paper, like dated:2024-02, or dates that overlap."),
        SearchQueryIssue.Folder => DesktopText.Translate("Use one folder or a folder inside it, like folder:\"Filed/Tax\"."),
        SearchQueryIssue.ReviewStatus => DesktopText.Translate("Use one review status: status:inbox or status:reviewed."),
        SearchQueryIssue.TextAvailability => DesktopText.Translate("Use one text filter: text:missing or text:present."),
        SearchQueryIssue.StructuredMetadata =>
            DesktopText.Translate("Use a detail like type:lease, correspondent:\"Northstar Properties\", field:deposit or asn:123."),
        SearchQueryIssue.BooleanSyntax => DesktopText.Translate("Finish the expression, like (receipt OR invoice) -tag:archive."),
        _ => DesktopText.Translate("Type a word, a number or a filter.")
    };

    private void ReportResultFailure(Exception error)
    {
        _appliedQuery = null;
        ResetPageMatchNavigation();
        OnPropertyChanged(nameof(HasPageMatchAction));
        HasMore = false;
        ResultCount = "Results unavailable";
        Status = error.Message;
    }

    private void UpdatePreviewHighlights()
    {
        CancelPreviewHighlights();
        if (_library is not { } library || Query != _appliedQuery ||
            SelectedDocument is not { MatchHighlights.Count: > 0, HasText: true } document) return;
        var request = new CancellationTokenSource();
        _highlightCancellation = request;
        _ = LoadPreviewHighlightsAsync(library, document, Query, _pageNumber, request);
    }

    private async Task LoadPreviewHighlightsAsync(InboxLibrary library, DocumentSummary document, string query, int page,
        CancellationTokenSource request)
    {
        try
        {
            var token = request.Token;
            var highlights = await TrackBackgroundRead(() => library.FindPageHighlights(document, query, page, token));
            if (!token.IsCancellationRequested) PreviewHighlights = highlights;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            // The rendered page can remain available while a changed file prevents text positioning.
        }
        finally
        {
            if (_highlightCancellation == request) _highlightCancellation = null;
            request.Dispose();
        }
    }

    private void CancelPreviewHighlights()
    {
        _highlightCancellation?.Cancel();
        if (PreviewHighlights.Count > 0) PreviewHighlights = [];
    }
}
