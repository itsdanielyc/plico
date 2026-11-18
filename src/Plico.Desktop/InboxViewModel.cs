using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

/// <summary>Coordinates the inbox, document actions, and the independently cancellable preview.</summary>
internal sealed partial class InboxViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(180);
    private readonly IWorkspaceDialogs _dialogs;
    private readonly Action<string>? _rememberLocation;
    private readonly DispatcherTimer _searchTimer = new() { Interval = SearchDelay };
    private InboxLibrary? _library;
    private CancellationTokenSource? _operationCancellation;
    private TaskCompletionSource? _operationCompletion;
    private CancellationTokenSource? _previewCancellation;
    private bool _inboxOnly = true;
    private string? _activeTag;
    private int _pageNumber = 1;

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private string _tagInput = "";
    [ObservableProperty] private string _heading = "Inbox";
    [ObservableProperty] private string _resultCount = "0 documents";
    [ObservableProperty] private string _emptyMessage = "Add a file to start your inbox.";
    [ObservableProperty] private string _status = "Choose a library folder to get started.";
    [ObservableProperty] private string _libraryPath = "";
    [ObservableProperty] private string _previewMessage = "";
    [ObservableProperty] private IReadOnlyList<DocumentSummary> _documents = [];
    [ObservableProperty] private IReadOnlyList<string> _tags = [];
    [ObservableProperty] private DocumentSummary? _selectedDocument;
    [ObservableProperty] private Bitmap? _preview;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasMore;

    internal InboxViewModel(IWorkspaceDialogs dialogs, Action<string>? rememberLocation = null)
    {
        _dialogs = dialogs;
        _rememberLocation = rememberLocation;
        _searchTimer.Tick += (_, _) => LoadResults();
    }

    public bool HasLibrary => _library is not null;
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
            Status = "Drop a PDF, PNG, or JPEG document.";
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
        LoadResults();
        Status = result.Warnings.Count == 0
            ? $"{DocumentCount(result.Indexed)} indexed."
            : $"{DocumentCount(result.Indexed)} indexed. {result.Warnings[0]}";
    });

    [RelayCommand] private void Cancel() => _operationCancellation?.Cancel();
    [RelayCommand] private void ShowInbox() => Navigate(inboxOnly: true);
    [RelayCommand] private void ShowAll() => Navigate(inboxOnly: false);
    [RelayCommand] private void LoadMore() => LoadResults(append: true);
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
        (library, document) => library.SetReviewed(document.Id, !document.Reviewed),
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
        _library = replacement;
        CancelPreview();
        previous?.Dispose();
        SelectedDocument = null;
        LibraryPath = replacement.RootPath;
        OnPropertyChanged(nameof(HasLibrary));
        OnPropertyChanged(nameof(CanWork));
        OnPropertyChanged(nameof(CanStartFolderWatch));
        OnPropertyChanged(nameof(LibraryName));
        Tags = replacement.GetTags();
        Navigate(inboxOnly: true);
        Status = "Library ready.";
    }

    private async Task ImportFilesAsync(IReadOnlyList<string> files, CancellationToken token, bool revealAdded = true)
    {
        var library = RequireLibrary();
        var failures = new List<string>();
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
                    var result = await Task.Run(() => library.ImportFileAsync(file, token), token);
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
                    failures.Add($"{filename} — {error.Message}");
                }
            }
        }
        finally
        {
            if (lastAdded is not null)
            {
                if (revealAdded) Navigate(inboxOnly: true, preferredId: lastAdded.Id);
                else LoadResults();
            }
        }

        var outcomes = new List<string>();
        if (added > 0) outcomes.Add($"{DocumentCount(added)} added.");
        if (alreadyPresent > 0) outcomes.Add($"{DocumentCount(alreadyPresent)} already in the library.");
        if (unreadable > 0) outcomes.Add($"No text recognized in {DocumentCount(unreadable)}.");
        if (failures.Count > 0) outcomes.Add($"{failures.Count} failed. {failures[0]}");
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
        _inboxOnly = inboxOnly;
        _activeTag = tag;
        Query = "";
        LoadResults(preferredId);
    }

    private void LoadResults(long? preferredId = null, bool append = false)
    {
        _searchTimer.Stop();
        bool hasQuery = !string.IsNullOrWhiteSpace(Query);
        Heading = hasQuery ? "Search results" : _activeTag ?? (_inboxOnly ? "Inbox" : "All documents");
        EmptyMessage = hasQuery || _activeTag is not null ? "No documents match." :
            _inboxOnly ? "Add a file to start your inbox." : "This library has no documents yet.";
        OnPropertyChanged(nameof(InboxSelected));
        OnPropertyChanged(nameof(AllSelected));
        if (_library is null) return;

        try
        {
            long? selection = preferredId ?? SelectedDocument?.Id;
            var page = _library.Find(new DocumentFilter(Query, _activeTag, _inboxOnly && !hasQuery, append ? Documents.Count : 0));
            Documents = append ? [.. Documents, .. page.Items] : page.Items;
            HasMore = page.HasMore;
            ResultCount = HasMore ? $"Showing {DocumentCount(Documents.Count)}" : DocumentCount(Documents.Count);
            if (_activeTag is not null && hasQuery) ResultCount += $" in {_activeTag}";
            SelectedDocument = Documents.FirstOrDefault(document => document.Id == selection) ?? Documents.FirstOrDefault();
        }
        catch (Exception error)
        {
            Status = error.Message;
        }
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

    private static string DocumentCount(int count) => count == 1 ? "1 document" : $"{count} documents";

    partial void OnQueryChanged(string value)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    partial void OnDocumentsChanged(IReadOnlyList<DocumentSummary> value) => OnPropertyChanged(nameof(HasDocuments));
    partial void OnTagsChanged(IReadOnlyList<string> value) => OnPropertyChanged(nameof(HasTags));
    partial void OnPreviewMessageChanged(string value) => OnPropertyChanged(nameof(HasPreviewMessage));

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanWork));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(CanStartFolderWatch));
    }

    partial void OnSelectedDocumentChanged(DocumentSummary? oldValue, DocumentSummary? newValue)
    {
        if (oldValue?.Id != newValue?.Id)
        {
            _pageNumber = 1;
            RequestPreview();
        }
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ReviewAction));
        OnPropertyChanged(nameof(HasNoSearchableText));
        NotifyPageChanged();
    }

    private void NavigatePage(int direction)
    {
        int target = _pageNumber + direction;
        if (target < 1 || target > SelectedDocument?.PageCount) return;
        _pageNumber = target;
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
        _ = RenderPreviewAsync(library, document, _pageNumber, request);
    }

    private async Task RenderPreviewAsync(InboxLibrary library, DocumentSummary document, int page, CancellationTokenSource request)
    {
        try
        {
            using var rendered = await Task.Run(() => library.RenderPageAsync(document, page, request.Token), request.Token);
            if (!request.IsCancellationRequested) Preview = new Bitmap(rendered);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!request.IsCancellationRequested) PreviewMessage = "Preview unavailable. Try opening the PDF.";
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
        var previous = Preview;
        Preview = null;
        previous?.Dispose();
    }

    public void Dispose()
    {
        _searchTimer.Stop();
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
}
