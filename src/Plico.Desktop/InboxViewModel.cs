using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

/// <summary>Coordinates the inbox, document actions, and the independently cancellable preview.</summary>
internal sealed partial class InboxViewModel : ObservableObject, IDisposable
{
    private static readonly SortChoice AutomaticSort = new(DocumentSort.Automatic, "Automatic");
    internal static readonly IReadOnlyList<SortChoice> AvailableSortChoices =
        [AutomaticSort, new(DocumentSort.Newest, "Newest added"), new(DocumentSort.Title, "Title A–Z"),
            new(DocumentSort.DocumentDate, "Document date")];

    private readonly IWorkspaceDialogs _dialogs;
    private readonly Action<string>? _rememberLocation;
    private InboxLibrary? _library;
    private CancellationTokenSource? _operationCancellation;
    private TaskCompletionSource? _operationCompletion;
    private CancellationTokenSource? _previewCancellation;
    private bool _inboxOnly = true;
    private bool _replacingResults;
    private string? _activeTag;
    private int _pageNumber = 1;

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private SortChoice? _sortOption = AutomaticSort;
    [ObservableProperty] private string _tagInput = "";
    [ObservableProperty] private string _heading = "Inbox";
    [ObservableProperty] private string _resultCount = "0 documents";
    [ObservableProperty] private string _emptyMessage = "Add a file to start your inbox.";
    [ObservableProperty] private string _status = "Choose a library folder to get started.";
    [ObservableProperty] private string _libraryPath = "";
    [ObservableProperty] private string _previewMessage = "";
    [ObservableProperty] private IReadOnlyList<DocumentSummary> _documents = [];
    [ObservableProperty] private IReadOnlyList<string> _tags = [];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DocumentDateAction))]
    private DocumentSummary? _selectedDocument;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    private Bitmap? _preview;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPageText))]
    private string _pageText = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageTextAction))]
    private bool _isPageTextView;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewFitAction))]
    private bool _fitPreviewWidth;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasMore;

    internal InboxViewModel(IWorkspaceDialogs dialogs, Action<string>? rememberLocation = null,
        WatchFolderPreference? watchPreference = null,
        Func<InboxLibrary, DocumentFilter, CancellationToken, DocumentPage>? searchDocuments = null,
        ImportFailureStore? importFailureStore = null)
    {
        _dialogs = dialogs;
        _rememberLocation = rememberLocation;
        _watchPreference = watchPreference;
        _importFailureStore = importFailureStore;
        _searchDocuments = searchDocuments ?? (static (library, filter, token) => library.Find(filter, token));
        _searchTimer.Tick += (_, _) => _ = LoadTypedSearchAsync();
    }

    public bool HasLibrary => _library is not null;
    public IReadOnlyList<SortChoice> SortChoices => AvailableSortChoices;
    public bool CanWork => HasLibrary && !IsBusy;
    public bool IsIdle => !IsBusy;
    public bool HasDocuments => Documents.Count > 0;
    public bool HasTags => Tags.Count > 0;
    public bool HasSelection => SelectedDocument is not null;
    public bool InboxSelected => _inboxOnly && _activeTag is null && string.IsNullOrWhiteSpace(Query);
    public bool AllSelected => !_inboxOnly && _activeTag is null && string.IsNullOrWhiteSpace(Query);
    public string LibraryName
    {
        get
        {
            if (!HasLibrary) return "Choose library";
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(LibraryPath));
            return name.Length == 0 ? LibraryPath : name;
        }
    }
    public string ReviewAction => SelectedDocument?.Reviewed == true ? "Return to inbox" : "Mark reviewed";
    public string PageLabel => SelectedDocument is { } document ? $"{_pageNumber} / {document.PageCount}" : "";
    public bool CanGoBack => _pageNumber > 1;
    public bool CanGoForward => _pageNumber < SelectedDocument?.PageCount;
    public bool HasPreviewMessage => PreviewMessage.Length > 0;
    public bool HasPreview => Preview is not null;
    public bool HasPageText => PageText.Length > 0;
    public string PageTextAction => IsPageTextView ? "Show page" : "Read text";
    public string PreviewFitAction => FitPreviewWidth ? "Fit page" : "Fit width";
    public bool HasNoSearchableText => SelectedDocument is { HasText: false };

    internal void InitializeLibrary(Func<string> locate)
    {
        try
        {
            OpenLibrary(locate());
        }
        catch (Exception error)
        {
            Status = error.Message;
        }
    }

    internal async Task PrepareCloseAsync()
    {
        CancelTypedSearch();
        await StopFolderWatchAsync();
        var completion = _operationCompletion;
        _operationCancellation?.Cancel();
        if (completion is not null) await completion.Task;
    }

    [RelayCommand]
    private Task AddFilesAsync() => RunOperationAsync("Choose documents to import…", async token =>
    {
        var files = await _dialogs.PickDocumentsAsync();
        if (files.Count == 0)
        {
            Status = "No files selected.";
            return;
        }
        await ImportFilesAsync(files, token);
    });

    internal Task ImportDroppedFilesAsync(IReadOnlyList<string> files, bool hasSupportedFile)
    {
        if (!hasSupportedFile)
        {
            Status = "Drop a PDF, PNG, JPEG, or TIFF document.";
            return Task.CompletedTask;
        }
        return RunOperationAsync("Importing dropped files…", token => ImportFilesAsync(files, token));
    }

    [RelayCommand]
    private Task ChooseLibraryAsync() => RunOperationAsync("Choose a library folder…", async _ =>
    {
        var selected = await _dialogs.PickFolderAsync("Choose a Plico library folder");
        if (selected is null)
        {
            Status = "Folder selection closed.";
            return;
        }
        if (_library is { } library && Path.GetRelativePath(library.RootPath, selected) != ".")
        {
            await StopFolderWatchAsync();
        }
        OpenLibrary(selected);
        _rememberLocation?.Invoke(LibraryPath);
    });

    [RelayCommand]
    private Task RefreshIndexAsync() => RunOperationAsync("Checking the PDF folder…", async token =>
    {
        var library = RequireLibrary();
        var result = await Task.Run(() => library.Refresh(token), token);
        Tags = library.GetTags();
        RefreshInboxCount();
        LoadResults(reloadPreview: result.Indexed > 0 || result.Relocated > 0);
        Status = $"{DocumentCount(result.Indexed)} indexed.";
        if (result.Relocated > 0) Status += $" {DocumentCount(result.Relocated)} moved.";
        if (result.Warnings.Count > 0) Status += $" {result.Warnings[0]}";
    });

    [RelayCommand] private void Cancel() => _operationCancellation?.Cancel();
    [RelayCommand] private void ShowInbox() => Navigate(inboxOnly: true);
    [RelayCommand] private void ShowAll() => Navigate(inboxOnly: false);
    [RelayCommand]
    private void LoadMore()
    {
        if (HasMore) LoadResults(append: true);
    }
    [RelayCommand] private void TogglePreviewFit() => FitPreviewWidth = !FitPreviewWidth;

    [RelayCommand]
    private void TogglePageText()
    {
        if (SelectedDocument is not { HasText: true }) return;
        IsPageTextView = !IsPageTextView;
        RequestPreview();
    }
    [RelayCommand] private void OpenPdfFolder() => OpenPath(() => RequireLibrary().PdfDirectory);

    [RelayCommand]
    private void OpenPdf()
    {
        if (SelectedDocument is { } document) OpenPath(() => RequireLibrary().ResolvePdf(document));
    }

    [RelayCommand]
    private void OpenDocumentFolder()
    {
        if (SelectedDocument is not { } document) return;
        OpenPath(() => Path.GetDirectoryName(RequireLibrary().ResolvePdf(document)) ??
            throw new InvalidDataException("The PDF has no containing folder."));
    }

    [RelayCommand]
    private Task CopyDocumentTextAsync()
    {
        if (SelectedDocument is not { HasText: true } document) return Task.CompletedTask;
        return RunOperationAsync("Reading document text…", async token =>
        {
            var library = RequireLibrary();
            string text = await Task.Run(() => library.ReadIndexedText(document.Id), token);
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(text))
            {
                Status = "No searchable text found.";
                return;
            }
            await _dialogs.CopyTextAsync(text);
            Status = "Document text copied.";
        });
    }

    [RelayCommand]
    private Task EditDocumentTitleAsync() => RunOperationAsync("Edit the document title…", async token =>
    {
        if (SelectedDocument is not { } document) return;
        var title = await _dialogs.EditTitleAsync(document.Title);
        if (title is null)
        {
            Status = "Title editor closed.";
            return;
        }
        var library = RequireLibrary();
        await Task.Run(() => library.SetTitle(document.Id, title), token);
        LoadResults(document.Id);
        Status = "Title saved.";
    });

    [RelayCommand]
    private void AddTag()
    {
        if (string.IsNullOrWhiteSpace(TagInput)) return;
        if (UpdateDocument((library, document) => library.AddTag(document.Id, TagInput), "Tag added.")) TagInput = "";
    }

    [RelayCommand]
    private void ToggleReview() => UpdateDocument(
        (library, document) =>
        {
            library.SetReviewed(document.Id, !document.Reviewed);
            RefreshInboxCount();
        },
        SelectedDocument?.Reviewed == true ? "Returned to inbox." : "Marked as reviewed.");

    [RelayCommand] private void PreviousPage() => NavigatePage(-1);
    [RelayCommand] private void NextPage() => NavigatePage(1);

    internal void FilterByTag(string tag) => Navigate(inboxOnly: false, tag);

    internal void RemoveTag(string tag) =>
        UpdateDocument((library, document) => library.RemoveTag(document.Id, tag), "Tag removed.");

    private void OpenLibrary(string directory)
    {
        string target = Path.GetFullPath(directory);
        if (_library is not null && Path.GetRelativePath(_library.RootPath, target) == ".")
        {
            Status = "Library already open.";
            return;
        }

        var replacement = InboxLibrary.Open(target);
        var previous = _library;
        CancelTypedSearch();
        _library = replacement;
        CancelPreview();
        previous?.Dispose();
        var importNotice = RestoreImportFailures(replacement.RootPath);
        OcrCopyBatchResults = [];
        LatestBackupLocation = null;
        LatestExportLocation = null;
        TagInput = "";
        BatchTagInput = "";
        SelectedDocument = null;
        LibraryPath = replacement.RootPath;
        var watchNotice = RestoreWatchFolder(replacement.RootPath);
        OnPropertyChanged(nameof(HasLibrary));
        OnPropertyChanged(nameof(CanWork));
        OnPropertyChanged(nameof(CanStartFolderWatch));
        OnPropertyChanged(nameof(LibraryName));
        Tags = replacement.GetTags();
        SavedSearches = replacement.GetSavedSearches();
        RefreshInboxCount();
        Navigate(inboxOnly: true);
        Status = watchNotice is null ? "Library ready." : $"Library ready. {watchNotice}";
        if (importNotice is not null) Status += $" {importNotice}";
        if (replacement.MoveRecoveryNotice is { } moveNotice) Status += $" {moveNotice}";
    }

    private async Task ImportFilesAsync(IReadOnlyList<string> files, CancellationToken token, bool revealAdded = true)
    {
        var library = RequireLibrary();
        var language = RecognitionLanguage;
        var failures = new List<ImportFailure>();
        bool reportChanged = false;
        string? saveWarning = null;
        DocumentSummary? lastAdded = null;
        int added = 0;
        int alreadyPresent = 0;
        int unreadable = 0;
        try
        {
            foreach (string file in files)
            {
                token.ThrowIfCancellationRequested();
                string filename = Path.GetFileName(file);
                Status = $"Processing {filename}…";
                try
                {
                    var result = await Task.Run(() => library.ImportFileAsync(file, token, language), token);
                    reportChanged |= _unresolvedImports.Remove(file);
                    if (!result.Added)
                    {
                        alreadyPresent++;
                        continue;
                    }
                    lastAdded = result.Document;
                    added++;
                    if (!lastAdded.HasText) unreadable++;
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    var failure = new ImportFailure(file, error.Message);
                    failures.Add(failure);
                    _unresolvedImports[file] = failure;
                    reportChanged = true;
                }
            }
        }
        finally
        {
            if (reportChanged)
            {
                var report = _unresolvedImports.Values.ToArray();
                ImportFailures = report;
                if (_importFailureStore is not null)
                {
                    // Record completed attempts even when the rest of the batch was cancelled.
                    saveWarning = await Task.Run(() => PersistImportFailures(library.RootPath, report));
                }
            }
            if (lastAdded is not null)
            {
                if (revealAdded) RevealNewInboxDocument(lastAdded.Id);
                else
                {
                    RefreshInboxCount();
                    RefreshAfterWatch();
                }
            }
        }

        var outcomes = new List<string>();
        if (added > 0) outcomes.Add($"{DocumentCount(added)} added.");
        if (alreadyPresent > 0) outcomes.Add($"{DocumentCount(alreadyPresent)} already in the library.");
        if (unreadable > 0) outcomes.Add($"No text recognized in {DocumentCount(unreadable)}.");
        if (failures.Count > 0) outcomes.Add($"{failures.Count} failed. {failures[0].Filename} — {failures[0].Reason}");
        if (saveWarning is not null) outcomes.Add(saveWarning);
        Status = string.Join(" ", outcomes);
    }

    private async Task RunOperationAsync(
        string workingStatus, Func<CancellationToken, Task> operation, CancellationToken externalCancellation = default)
    {
        if (IsBusy) return;
        using var cancellation = externalCancellation.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(externalCancellation)
            : new CancellationTokenSource();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _operationCancellation = cancellation;
        _operationCompletion = completion;
        IsBusy = true;
        Status = workingStatus;
        try
        {
            await operation(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Status = "Cancelled.";
        }
        catch (Exception error)
        {
            Status = error.Message;
        }
        finally
        {
            _operationCancellation = null;
            _operationCompletion = null;
            IsBusy = false;
            completion.SetResult();
        }
    }

    private void Navigate(bool inboxOnly, string? tag = null, long? preferredId = null)
    {
        ActiveSavedSearch = null;
        _inboxOnly = inboxOnly;
        _activeTag = tag;
        OnPropertyChanged(nameof(HasActiveTag));
        Query = "";
        LoadResults(preferredId);
    }

    private void ReplaceResults(IReadOnlyList<DocumentSummary> documents, long? selectedId, bool reloadPreview)
    {
        var previous = SelectedDocument;
        var selected = documents.FirstOrDefault(document => document.Id == selectedId) ?? documents.FirstOrDefault();
        // Avalonia clears selection while replacing ItemsSource. Preview state should follow the final selection.
        _replacingResults = true;
        try
        {
            Documents = documents;
            SelectedDocument = selected;
        }
        finally
        {
            _replacingResults = false;
        }
        UpdateSelection(previous, SelectedDocument, reloadPreview);
    }

    private bool UpdateDocument(Action<InboxLibrary, DocumentSummary> update, string success)
    {
        if (!CanWork || SelectedDocument is not { } document) return false;
        try
        {
            var library = RequireLibrary();
            update(library, document);
            Tags = library.GetTags();
            LoadResults(document.Id);
            Status = success;
            return true;
        }
        catch (Exception error)
        {
            Status = error.Message;
            return false;
        }
    }

    private void OpenPath(Func<string> resolve)
    {
        try
        {
            _dialogs.OpenPath(resolve());
        }
        catch (Exception error)
        {
            Status = error.Message;
        }
    }

    private InboxLibrary RequireLibrary() =>
        _library ?? throw new InvalidOperationException("Choose a library folder first.");

    private static string DocumentCount(long count) => count == 1 ? "1 document" : $"{count} documents";

    partial void OnDocumentsChanged(IReadOnlyList<DocumentSummary> value)
    {
        OnPropertyChanged(nameof(HasDocuments));
        OnPropertyChanged(nameof(HasBatchCandidates));
        OnPropertyChanged(nameof(HasResultActions));
    }
    partial void OnPreviewMessageChanged(string value) => OnPropertyChanged(nameof(HasPreviewMessage));

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanWork));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(CanStartFolderWatch));
    }

    partial void OnSelectedDocumentChanged(DocumentSummary? oldValue, DocumentSummary? newValue)
    {
        if (!_replacingResults) UpdateSelection(oldValue, newValue);
    }

    private void UpdateSelection(DocumentSummary? oldValue, DocumentSummary? newValue, bool reloadPreview = false)
    {
        bool changedDocument = oldValue?.Id != newValue?.Id;
        if (IsPageTextView && (changedDocument || newValue is not { HasText: true }))
        {
            IsPageTextView = false;
            reloadPreview = true;
        }
        if (changedDocument || (newValue is not null && _pageNumber > newValue.PageCount))
        {
            _pageNumber = 1;
            reloadPreview = true;
        }
        if (reloadPreview)
        {
            ResetPageMatchNavigation();
            RequestPreview();
        }
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ReviewAction));
        OnPropertyChanged(nameof(HasNoSearchableText));
        OnPropertyChanged(nameof(HasMultiplePages));
        OnPropertyChanged(nameof(SearchableCopyAction));
        OnPropertyChanged(nameof(HasPageMatchAction));
        NotifyPageChanged();
    }

    private void NavigatePage(int direction) => ShowPreviewPage(_pageNumber + direction);

    private void ShowPreviewPage(int pageNumber)
    {
        if (SelectedDocument is not { } document || pageNumber < 1 || pageNumber > document.PageCount || pageNumber == _pageNumber) return;
        CancelPageMatchSearch();
        _pageNumber = pageNumber;
        NotifyPageChanged();
        RequestPreview();
    }

    private void NotifyPageChanged()
    {
        OnPropertyChanged(nameof(PageLabel));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
    }

    private void RequestPreview()
    {
        CancelPreview();
        PreviewMessage = "";
        if (_library is not { } library || SelectedDocument is not { } document) return;
        var request = new CancellationTokenSource();
        _previewCancellation = request;
        if (IsPageTextView) PreviewMessage = "Reading page text…";
        _ = LoadPreviewAsync(library, document, _pageNumber, IsPageTextView, request);
    }

    private async Task LoadPreviewAsync(InboxLibrary library, DocumentSummary document, int page, bool readText,
        CancellationTokenSource request)
    {
        try
        {
            var token = request.Token;
            if (readText)
            {
                string text = await Task.Run(() => library.ReadPageText(document, page, token), token);
                if (token.IsCancellationRequested) return;
                PageText = text;
                PreviewMessage = text.Length == 0 ? "This page has no searchable text." : "";
                return;
            }

            using var rendered = await Task.Run(() => library.RenderPageAsync(document, page, token), token);
            if (!token.IsCancellationRequested) Preview = new Bitmap(rendered);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!request.IsCancellationRequested) PreviewMessage = readText
                ? "Page text unavailable. Try refreshing the index."
                : "Preview unavailable. Try opening the PDF.";
        }
        finally
        {
            if (_previewCancellation == request) _previewCancellation = null;
            request.Dispose();
        }
    }

    private void CancelPreview()
    {
        _previewCancellation?.Cancel();
        PageText = "";
        var previous = Preview;
        Preview = null;
        previous?.Dispose();
    }

    public void Dispose()
    {
        CancelTypedSearch();
        CancelPreview();
        var library = _library;
        _library = null;
        if (IsBusy || IsWatchingFolder) _ = FinishDisposalAsync(library);
        else library?.Dispose();
    }

    private async Task FinishDisposalAsync(InboxLibrary? library)
    {
        try
        {
            await PrepareCloseAsync();
        }
        finally
        {
            library?.Dispose();
        }
    }

    /// <summary>A catalog order and its visible label.</summary>
    internal sealed record SortChoice(DocumentSort Order, string Label)
    {
        public override string ToString() => Label;
    }
}
