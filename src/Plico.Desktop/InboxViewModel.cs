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
    [ObservableProperty] private string _heading = DesktopText.Translate("Inbox");
    [ObservableProperty] private string _resultCount = DesktopText.Translate("0 documents");
    [ObservableProperty] private string _emptyMessage = DesktopText.Translate("Add a file to start your Inbox.");
    private string _status = DesktopText.Translate("Choose a library folder to get started.");
    [ObservableProperty] private string _libraryPath = "";
    [ObservableProperty] private string _previewMessage = "";
    [ObservableProperty] private IReadOnlyList<DocumentSummary> _documents = [];
    [ObservableProperty] private IReadOnlyList<string> _tags = [];
    [ObservableProperty] private DocumentSummary? _selectedDocument;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    private Bitmap? _preview;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPageText))]
    private string _pageText = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageTextAction))]
    [NotifyPropertyChangedFor(nameof(HasVisiblePageThumbnails))]
    private bool _isPageTextView;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewFitAction))]
    [NotifyPropertyChangedFor(nameof(IsPreviewScrollable))]
    private bool _fitPreviewWidth;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasMore;

    internal InboxViewModel(IWorkspaceDialogs dialogs, Action<string>? rememberLocation = null,
        WatchFolderPreference? watchPreference = null,
        Func<InboxLibrary, DocumentFilter, CancellationToken, DocumentPage>? searchDocuments = null,
        ImportFailureStore? importFailureStore = null, IntakeFolderPreference? intakeFolderPreference = null)
    {
        _dialogs = dialogs;
        _rememberLocation = rememberLocation;
        _watchPreference = watchPreference;
        _importFailureStore = importFailureStore;
        _intakeFolderPreference = intakeFolderPreference;
        _searchDocuments = searchDocuments ?? (static (library, filter, token) => library.Find(filter, token));
        _searchTimer.Tick += (_, _) => _ = LoadTypedSearchAsync(whileTyping: true);
        _queryHintTimer.Tick += (_, _) => ShowPendingQueryHint();
        _suggestionTimer.Tick += (_, _) => StartSuggestionRequest();
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
    public bool WithoutTextSelected => _activeTag is null && ActiveSavedSearch is null &&
        Query.AsSpan().Trim().Equals(WithoutTextQuery, StringComparison.OrdinalIgnoreCase);
    public string LibraryName
    {
        get
        {
            if (!HasLibrary) return DesktopText.Translate("Choose library");
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(LibraryPath));
            return name.Length == 0 ? LibraryPath : name;
        }
    }
    public string ReviewAction => SelectedDocument?.Reviewed == true ? DesktopText.Translate("Back to Inbox") : DesktopText.Translate("Mark reviewed");
    public string PageLabel => SelectedDocument is { } document ? $"{_pageNumber} / {document.PageCount}" : "";
    public bool CanGoBack => _pageNumber > 1;
    public bool CanGoForward => _pageNumber < SelectedDocument?.PageCount;
    public bool HasPreviewMessage => PreviewMessage.Length > 0;
    public bool HasPreview => Preview is not null;
    public bool HasPageText => PageText.Length > 0;
    public string PageTextAction => IsPageTextView ? DesktopText.Translate("Show page") : DesktopText.Translate("Read text");
    public string PreviewFitAction => IsPreviewScrollable ? DesktopText.Translate("Fit page") : DesktopText.Translate("Fit width");
    public bool HasNoSearchableText => SelectedDocument is { HasText: false };

    internal void InitializeLibrary(Func<string> locate)
    {
        try
        {
            string target = Path.GetFullPath(locate());
            if (!AlreadyOpen(target)) _ = ActivateLibrary(InboxLibrary.Open(target));
        }
        catch (Exception error)
        {
            Status = error.Message;
        }
    }

    internal async Task PrepareCloseAsync()
    {
        CancelTypedSearch();
        ClearPageThumbnails();
        var completion = _operationCompletion;
        _operationCancellation?.Cancel();
        await StopAllWatchesAsync();
        if (_thumbnailLoad is { } thumbnailLoad) await thumbnailLoad;
        if (completion is not null) await completion.Task;
    }

    [RelayCommand]
    private Task AddFilesAsync() => RunOperationAsync(DesktopText.Translate("Choose documents to import…"), async token =>
    {
        var files = await _dialogs.PickDocumentsAsync();
        if (files.Count == 0)
        {
            Status = DesktopText.Translate("No files selected.");
            return;
        }
        await ImportFilesAsync(files, token);
    });

    internal Task ImportDroppedFilesAsync(IReadOnlyList<string> files, bool hasSupportedFile)
    {
        if (!hasSupportedFile)
        {
            Status = DesktopText.Translate("Drop a supported document or image.");
            return Task.CompletedTask;
        }
        return RunOperationAsync(DesktopText.Translate("Importing dropped files…"), token => ImportFilesAsync(files, token));
    }

    [RelayCommand]
    private Task ChooseLibraryAsync() => RunOperationAsync(DesktopText.Translate("Choose a library folder…"), async token =>
    {
        var selected = await _dialogs.PickFolderAsync("Choose a Plico library folder");
        if (selected is null)
        {
            Status = DesktopText.Translate("Folder selection closed.");
            return;
        }
        token.ThrowIfCancellationRequested();
        string target = Path.GetFullPath(selected);
        if (!AlreadyOpen(target))
        {
            var replacement = await Task.Run(() => InboxLibrary.Open(target), token);
            try
            {
                token.ThrowIfCancellationRequested();
                await StopAllWatchesAsync();
                await ActivateLibrary(replacement);
            }
            finally
            {
                if (!ReferenceEquals(_library, replacement)) replacement.Dispose();
            }
            _rememberLocation?.Invoke(LibraryPath);
            await CatchUpSavedWatchesAsync(token);
            return;
        }
        _rememberLocation?.Invoke(LibraryPath);
    });

    [RelayCommand] private void Cancel() => _operationCancellation?.Cancel();
    [RelayCommand] private void ShowInbox() => Navigate(inboxOnly: true);
    [RelayCommand] private void ShowAll() => Navigate(inboxOnly: false);
    [RelayCommand] private void ShowWithoutText() => Navigate(inboxOnly: false, query: WithoutTextQuery);
    [RelayCommand]
    private void LoadMore()
    {
        if (HasMore) LoadResults(append: true);
    }
    [RelayCommand]
    private void TogglePreviewFit()
    {
        if (IsPreviewZoomed)
        {
            ResetManualZoom();
            RequestPreview();
            return;
        }
        FitPreviewWidth = !FitPreviewWidth;
    }

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
        return RunOperationAsync(DesktopText.Translate("Reading document text…"), async token =>
        {
            var library = RequireLibrary();
            string text = await Task.Run(() => library.ReadIndexedText(document.Id), token);
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(text))
            {
                Status = DesktopText.Translate("No searchable text found.");
                return;
            }
            await _dialogs.CopyTextAsync(text);
            Status = DesktopText.Translate("Document text copied.");
        });
    }

    [RelayCommand]
    private void AddTag()
    {
        if (string.IsNullOrWhiteSpace(TagInput)) return;
        string label = TagInput.Trim();
        if (HasLibrary && SelectedDocument?.Tags.FirstOrDefault(tag => SameTag(tag, label)) is { } present)
        {
            // A tag the document already has changes nothing: the toast says so and the current Undo offer stays.
            TagInput = "";
            Announce(DesktopText.Format($"Already tagged “{present}”."), DesktopText.Translate("Already tagged"), $"“{present}”");
            return;
        }
        if (UpdateDocument((library, document) =>
        {
            library.AddTag(document.Id, TagInput);
            return TagUndo(library, document.Id, label, added: true);
        }, DesktopText.Translate("Tag added."))) TagInput = "";
    }

    /// <summary>Whether two labels name the same tag, as the catalog compares them; a label it cannot take names none.</summary>
    private static bool SameTag(string first, string second)
    {
        try
        {
            return InboxLibrary.GetTagKey(first) == InboxLibrary.GetTagKey(second);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    [RelayCommand] private void PreviousPage() => NavigatePage(-1);
    [RelayCommand] private void NextPage() => NavigatePage(1);

    internal void FilterByTag(string tag) => Navigate(inboxOnly: false, tag);

    internal void RemoveTag(string tag) => UpdateDocument((library, document) =>
    {
        library.RemoveTag(document.Id, tag);
        return TagUndo(library, document.Id, tag, added: false);
    }, DesktopText.Translate("Tag removed."));

    private bool AlreadyOpen(string target)
    {
        if (_library is null || Path.GetRelativePath(_library.RootPath, target) != ".") return false;
        Status = DesktopText.Translate("Library already open.");
        return true;
    }

    private Task ActivateLibrary(InboxLibrary replacement)
    {
        Task released = Task.CompletedTask;
        if (_library is { } previous)
        {
            released = ReleaseLibraryAfterReadsAsync(previous, StopBackgroundReads());
            _backgroundLifetime.Dispose();
            _backgroundLifetime = new CancellationTokenSource();
        }
        _library = replacement;
        replacement.ConfigureOcr(_ocrProcessing);
        UndoOffer = null;
        ForgetAllThumbnails();
        var importNotice = RestoreImportFailures(replacement.RootPath);
        var intakeNotice = RestoreIntakeFolder(replacement.RootPath);
        OcrCopyBatchResults = [];
        LatestIndexRefresh = null;
        FolderRestoreNotices = [];
        LatestBackupLocation = null;
        LatestExportLocation = null;
        TagInput = "";
        BatchTagInput = "";
        SelectedDocument = null;
        LibraryPath = replacement.RootPath;
        var watchNotice = RestoreWatchFolders(replacement.RootPath);
        OnPropertyChanged(nameof(HasLibrary));
        OnPropertyChanged(nameof(CanWork));
        OnPropertyChanged(nameof(CanStartFolderWatch));
        OnPropertyChanged(nameof(LibraryName));
        Tags = replacement.GetTags();
        SavedSearches = replacement.GetSavedSearches();
        RefreshInboxCount();
        RefreshUnavailableCount();
        PrepareReminderPolling();
        Navigate(inboxOnly: true);
        if (watchNotice is null) Note(DesktopText.Translate("Library ready."));
        else Status = DesktopText.Format($"Library ready. {watchNotice}");
        if (importNotice is not null) Status += $" {importNotice}";
        if (intakeNotice is not null) Status += $" {intakeNotice}";
        if (replacement.MoveRecoveryNotice is { } moveNotice) Status += $" {moveNotice}";
        return released;
    }

    private static async Task ReleaseLibraryAfterReadsAsync(InboxLibrary library, Task reads)
    {
        // The requesting UI reports any read failure. Lease release only depends on the workers having exited.
        await reads.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        library.Dispose();
    }

    private Task ImportFilesAsync(IReadOnlyList<string> files, CancellationToken token,
        ImportOrigin origin = ImportOrigin.Manual, bool revealAdded = true) =>
        ImportFilesAsync(files.Select(sourcePath => (sourcePath, origin)), token, revealAdded);

    private async Task ImportFilesAsync(IEnumerable<(string SourcePath, ImportOrigin Origin)> files,
        CancellationToken token, bool revealAdded = true)
    {
        var library = RequireLibrary();
        var language = RecognitionLanguage;
        string? manualDestination = IntakeDestination(library);
        string? watchedDestination = IncludeWatchedScans ? manualDestination : null;
        var failures = new List<ImportFailure>();
        bool reportChanged = false;
        string? saveWarning = null;
        DocumentSummary? lastAdded = null;
        int added = 0;
        int alreadyPresent = 0;
        int unreadable = 0;
        try
        {
            foreach (var (sourcePath, origin) in files)
            {
                token.ThrowIfCancellationRequested();
                string filename = Path.GetFileName(sourcePath);
                Status = DesktopText.Format($"Processing {filename}…");
                try
                {
                    string? destination = origin == ImportOrigin.FolderWatch ? watchedDestination : manualDestination;
                    var result = await Task.Run(() => library.ImportFileAsync(sourcePath, token, language, destination), token);
                    reportChanged |= _unresolvedImports.Remove(sourcePath);
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
                    var failure = new ImportFailure(sourcePath, error.Message, origin);
                    failures.Add(failure);
                    _unresolvedImports[sourcePath] = failure;
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
        if (added > 0) outcomes.Add(Counted(added, "1 document added.", $"{added} documents added."));
        if (alreadyPresent > 0) outcomes.Add(DesktopText.Format($"{DocumentCount(alreadyPresent)} already in the library."));
        if (unreadable > 0) outcomes.Add(DesktopText.Format($"No text recognised in {DocumentCount(unreadable)}."));
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
            Status = DesktopText.Translate("Cancelled.");
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

    private void Navigate(bool inboxOnly, string? tag = null, long? preferredId = null, string query = "")
    {
        ActiveSavedSearch = null;
        _inboxOnly = inboxOnly;
        _activeTag = tag;
        OnPropertyChanged(nameof(HasActiveTag));
        Query = query;
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

    /// <summary>
    /// Applies one change to the open document, reloads it and announces it. The change returns its Undo offer, which
    /// replaces the current one, or null when there is nothing to undo, which retires the current one.
    /// </summary>
    private bool UpdateDocument(Func<InboxLibrary, DocumentSummary, UndoOffer?> update, string success, ToastText? toast = null)
    {
        // Like the title and the date, a tag is one quick catalog write, so it is saved while Plico works in the background.
        if (!HasLibrary || SelectedDocument is not { } document) return false;
        try
        {
            var library = RequireLibrary();
            var offer = update(library, document);
            Tags = library.GetTags();
            LoadResults(document.Id);
            if (offer is not null)
            {
                OfferUndo(offer, success);
                return true;
            }
            RetireUndo();
            _pendingToast = toast;
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

    private static string DocumentCount(long count) => count == 1 ? "1 document" : DesktopText.Format($"{count} documents");

    /// <summary>
    /// A message about a number of documents, worded for one or for several, so a language whose words agree with the
    /// number reads naturally: "1 document ajouté", "2 documents ajoutés". Each wording has its own catalog row.
    /// </summary>
    private static string Counted(long count, string one, FormattableString several) =>
        count == 1 ? DesktopText.Translate(one) : DesktopText.Format(several);

    partial void OnDocumentsChanged(IReadOnlyList<DocumentSummary> value)
    {
        OnPropertyChanged(nameof(HasDocuments));
        OnPropertyChanged(nameof(HasBatchCandidates));
        OnPropertyChanged(nameof(HasResultActions));
        NotifyZeroState();
    }
    partial void OnPreviewMessageChanged(string value) => OnPropertyChanged(nameof(HasPreviewMessage));

    partial void OnIsBusyChanged(bool value)
    {
        WorkProgress = -1;
        OnPropertyChanged(nameof(CanWork));
        OnPropertyChanged(nameof(CanGoToPreviousMatch));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(CanStartFolderWatch));
        OnPropertyChanged(nameof(ShowIndexReport));
    }

    partial void OnSelectedDocumentChanged(DocumentSummary? oldValue, DocumentSummary? newValue)
    {
        if (!_replacingResults) UpdateSelection(oldValue, newValue);
    }

    private void UpdateSelection(DocumentSummary? oldValue, DocumentSummary? newValue, bool reloadPreview = false)
    {
        bool changedDocument = oldValue?.Id != newValue?.Id;
        if (changedDocument) CloseTitleEditFor(newValue);
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
            ClearPageThumbnails();
            ResetPageMatchNavigation();
            RequestPreview();
        }
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ReviewAction));
        OnPropertyChanged(nameof(HasNoSearchableText));
        OnPropertyChanged(nameof(HasMultiplePages));
        OnPropertyChanged(nameof(HasVisiblePageThumbnails));
        OnPropertyChanged(nameof(SearchableCopyAction));
        OnPropertyChanged(nameof(HasPageMatchAction));
        NotifyPageChanged();
        NotifyWorkspaceSelection();
        SyncDocumentDateField();
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
        RefreshPageThumbnails();
        PreviewMessage = "";
        if (_library is not { } library || SelectedDocument is not { } document) return;
        var request = new CancellationTokenSource();
        _previewCancellation = request;
        if (IsPageTextView) PreviewMessage = DesktopText.Translate("Reading page text…");
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
                string text = await TrackBackgroundRead(() => library.ReadPageText(document, page, token));
                if (token.IsCancellationRequested) return;
                PageText = text;
                PreviewMessage = text.Length == 0 ? DesktopText.Translate("This page has no searchable text.") : "";
                return;
            }

            int zoom = PreviewZoomPercent;
            if (zoom != 0)
            {
                var scaled = await TrackBackgroundRead(() => library.RenderScaledPdfPageAsync(document, page, zoom, token));
                using var image = scaled.Image;
                if (token.IsCancellationRequested) return;
                var bitmap = new Bitmap(image);
                bool rotatedDimensions = (bitmap.Size.Width >= bitmap.Size.Height) != (scaled.DisplayWidth >= scaled.DisplayHeight);
                PreviewDisplayWidth = rotatedDimensions ? scaled.DisplayHeight : scaled.DisplayWidth;
                PreviewDisplayHeight = rotatedDimensions ? scaled.DisplayWidth : scaled.DisplayHeight;
                Preview = bitmap;
                UpdatePreviewHighlights();
                return;
            }

            using var rendered = await TrackBackgroundRead(() => library.RenderPageAsync(document, page, token));
            if (!token.IsCancellationRequested)
            {
                Preview = new Bitmap(rendered);
                UpdatePreviewHighlights();
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!request.IsCancellationRequested) PreviewMessage = readText
                ? DesktopText.Translate("Page text unavailable. Try refreshing the index.")
                : DesktopText.Translate("Preview unavailable. Try opening the PDF.");
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
        CancelPreviewHighlights();
        PageText = "";
        var previous = Preview;
        Preview = null;
        previous?.Dispose();
    }

    public void Dispose()
    {
        if (_library is not { } library) return;
        Task reads = StopBackgroundReads();
        _reminderTimer?.Stop();
        _library = null;
        ForgetAllThumbnails();
        if (IsBusy || HasActiveWatches) _ = FinishDisposalAsync(library, reads);
        else ReleaseLibraryAfterReadsAsync(library, reads).GetAwaiter().GetResult();
    }

    private async Task FinishDisposalAsync(InboxLibrary library, Task reads)
    {
        try
        {
            await PrepareCloseAsync();
        }
        finally
        {
            await ReleaseLibraryAfterReadsAsync(library, reads);
        }
    }

    /// <summary>A catalog order and its visible label.</summary>
    internal sealed record SortChoice(DocumentSort Order, string EnglishLabel)
    {
        public string Label => DesktopText.Translate(EnglishLabel);
        public override string ToString() => Label;
    }
}
