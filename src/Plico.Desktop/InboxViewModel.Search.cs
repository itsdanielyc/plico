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
    public string PageMatchActionHelp => PageMatchNavigationActive
        ? "Go to the next page that matches the text terms"
        : "Go to the first page that matches the text terms";

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

    private async Task LoadTypedSearchAsync(bool preserveView = false)
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
        var filter = CurrentFilter(append ? Documents.Count : 0);
        UpdateResultHeading(filter);
        if (_library is not { } library) return;

        try
        {
            bool reloadExpandedResults = retainLoadedResults && !append && HasExpandedResults && _appliedQuery == filter.Query;
            var page = reloadExpandedResults ? library.Find(filter, Documents.Count, CancellationToken.None) : library.Find(filter);
            if (reviewPosition is { } position && position.Filter == filter)
            {
                preferredId = ChooseReviewResult(page.Items, position);
            }
            ApplyResultPage(filter, page, preferredId, reloadPreview);
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

    private void ApplyResultPage(DocumentFilter filter, DocumentPage page, long? preferredId = null, bool reloadPreview = false)
    {
        long? selection = preferredId ?? SelectedDocument?.Id;
        bool queryChanged = _appliedQuery != filter.Query;
        _appliedQuery = filter.Query;
        ReplaceResults(filter.Offset > 0 ? [.. Documents, .. page.Items] : page.Items, selection, reloadPreview);
        if (queryChanged && Preview is not null) UpdatePreviewHighlights();
        HasMore = page.HasMore;

        if (page.QueryIssue is { } issue)
        {
            ResultCount = QueryIssueCount;
            SetEmptyState(DesktopText.Translate("Check the search"), SearchCorrection(issue));
            return;
        }

        ResultCount = HasMore ? $"Showing {DocumentCount(Documents.Count)}" : DocumentCount(Documents.Count);
        if (filter.Tag is not null && !string.IsNullOrWhiteSpace(filter.Query)) ResultCount += $" in {filter.Tag}";
    }

    /// <summary>The result count while a search cannot run; the empty state carries the correction.</summary>
    private const string QueryIssueCount = "Check search";

    private static string SearchCorrection(SearchQueryIssue issue) => issue switch
    {
        SearchQueryIssue.Tag =>
            $"Use a tag of up to {InboxLibrary.MaximumTagNameLength} printable characters. Try tag:tax or tag:\"Property tax\".",
        SearchQueryIssue.AddedDate => "Use a date such as added:2024-02. Added-date filters must overlap.",
        SearchQueryIssue.DocumentDate => "Use a date such as dated:2024-02. Document-date filters must overlap.",
        SearchQueryIssue.Folder => "Use a folder such as folder:\"Filed/Tax\". Use one folder or nested folders.",
        SearchQueryIssue.ReviewStatus => "Use one review status. Try status:inbox or status:reviewed.",
        SearchQueryIssue.TextAvailability => "Use one text filter. Try text:missing or text:present.",
        SearchQueryIssue.StructuredMetadata => "Try type:lease, correspondent:\"Northstar Properties\", field:deposit, or asn:123.",
        SearchQueryIssue.BooleanSyntax => "Complete the expression. Try (receipt OR invoice) -tag:archive.",
        _ => "Enter a word, number, or search filter."
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
