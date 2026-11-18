using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Plico.Core;

namespace Plico.Desktop;

/// <summary>The window's file pickers and handoff to the operating system.</summary>
internal partial interface IWorkspaceDialogs
{
    Task<IReadOnlyList<string>> PickDocumentsAsync();
    Task<string?> PickFolderAsync(string title, string? startFolder = null);
    Task<ScanRequest?> PickScannerAsync();
    Task<bool> ConfirmPartialScanAsync(ScanInterruption interruption, CancellationToken cancellationToken);
    Task<string?> EditTitleAsync(string currentTitle);
    Task<DocumentDateEdit?> EditDocumentDateAsync(DateOnly? currentDate);
    Task<DocumentDateEdit?> EditBatchDocumentDatesAsync(DateOnly? commonDate, int documentCount);
    Task<SavedSearchDecision?> EditSavedSearchAsync(SavedSearchDraft draft, string? scopeTag, bool editing);
    Task<TagRenameRequest?> RenameTagAsync(string currentName, IReadOnlyList<string> catalogNames);
    Task<string?> ChooseTagToRemoveAsync(IReadOnlyList<BatchTagOption> options);
    Task<CombinePdfsRequest?> OrderPdfCombinationAsync(IReadOnlyList<DocumentSummary> selectedDocuments);
    Task<PdfPageCopyRequest?> ChoosePdfPageCopyAsync(DocumentSummary document, int currentPage, PdfPageCopyAction action);
    Task<ImportErrorsAction> ShowImportErrorsAsync(IReadOnlyList<ImportFailure> failures);
    Task ShowOcrCopyBatchResultsAsync(IReadOnlyList<OcrCopyBatchItem> results);
    Task<IndexReportDecision?> ShowIndexRefreshReportAsync(IndexReport report);
    Task<string?> PickRestorePdfAsync();
    Task<TrashDecision?> ShowTrashAsync(IReadOnlyList<TrashedDocument> documents);
    Task<PdfExportOptions?> ChoosePdfExportAsync(OcrLanguage language);
    Task<IReadOnlyList<FilingSelection>?> ShowFilingSuggestionsAsync(IReadOnlyList<FilingSuggestion> suggestions);
    Task ShowFilingRulesAsync(IReadOnlyList<FilingRule> rules, Func<FilingRule, FilingRule> save, Action<long> remove);
    Task ShowIntegrityReportAsync(IntegrityReport report);
    Task CopyTextAsync(string text);
    void OpenPath(string path);
}

internal sealed partial class WorkspaceDialogs(Window owner) : IWorkspaceDialogs
{
    private static readonly FilePickerFileType SupportedDocuments = new("PDF and images")
    {
        Patterns = ["*.pdf", "*.png", "*.jpg", "*.jpeg", "*.tif", "*.tiff", "*.PDF", "*.PNG", "*.JPG", "*.JPEG", "*.TIF", "*.TIFF"],
        MimeTypes = ["application/pdf", "image/png", "image/jpeg", "image/tiff"],
        AppleUniformTypeIdentifiers = ["com.adobe.pdf", "public.png", "public.jpeg", "public.tiff"]
    };

    private static readonly FilePickerFileType PdfDocuments = new("PDF files")
    {
        Patterns = ["*.pdf", "*.PDF"],
        MimeTypes = ["application/pdf"],
        AppleUniformTypeIdentifiers = ["com.adobe.pdf"]
    };

    public async Task<IReadOnlyList<string>> PickDocumentsAsync()
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add paperwork",
            AllowMultiple = true,
            FileTypeFilter = [SupportedDocuments, FilePickerFileTypes.All]
        });
        return files.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray();
    }

    public async Task<string?> PickFolderAsync(string title, string? startFolder = null)
    {
        using var suggested = startFolder is null
            ? null
            : await owner.StorageProvider.TryGetFolderFromPathAsync(new Uri(Path.GetFullPath(startFolder)));
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = suggested
        });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    public Task<ScanRequest?> PickScannerAsync() =>
        new ScanDialog(Scanners.DiscoverAsync, settings: new SavedScannerStore(ApplicationSettings.DirectoryPath),
            captureOptions: new ScanOptionsPreference(ApplicationSettings.DirectoryPath))
            .ShowDialog<ScanRequest?>(owner);

    public Task<bool> ConfirmPartialScanAsync(ScanInterruption interruption, CancellationToken cancellationToken) =>
        new ScanInterruptedDialog(interruption).ShowDecisionAsync(owner, cancellationToken);

    public Task<string?> EditTitleAsync(string currentTitle) =>
        new DocumentTitleDialog(currentTitle).ShowDialog<string?>(owner);

    public Task<DocumentDateEdit?> EditDocumentDateAsync(DateOnly? currentDate) =>
        new DocumentDateDialog(currentDate).ShowDialog<DocumentDateEdit?>(owner);

    public Task<DocumentDateEdit?> EditBatchDocumentDatesAsync(DateOnly? commonDate, int documentCount) =>
        new DocumentDateDialog(commonDate, documentCount).ShowDialog<DocumentDateEdit?>(owner);

    public Task<SavedSearchDecision?> EditSavedSearchAsync(SavedSearchDraft draft, string? scopeTag, bool editing) =>
        new SavedSearchDialog(draft, scopeTag, editing).ShowDialog<SavedSearchDecision?>(owner);

    public Task<TagRenameRequest?> RenameTagAsync(string currentName, IReadOnlyList<string> catalogNames) =>
        new TagRenameDialog(currentName, catalogNames).ShowDialog<TagRenameRequest?>(owner);

    public Task<string?> ChooseTagToRemoveAsync(IReadOnlyList<BatchTagOption> options) =>
        new BatchTagRemovalDialog(options).ShowDialog<string?>(owner);

    public Task<CombinePdfsRequest?> OrderPdfCombinationAsync(IReadOnlyList<DocumentSummary> selectedDocuments) =>
        new CombinePdfsDialog(selectedDocuments).ShowDialog<CombinePdfsRequest?>(owner);

    public Task<PdfPageCopyRequest?> ChoosePdfPageCopyAsync(DocumentSummary document, int currentPage, PdfPageCopyAction action) =>
        new PdfPageCopyDialog(document.PageCount, currentPage, action).ShowDialog<PdfPageCopyRequest?>(owner);

    public Task<ImportErrorsAction> ShowImportErrorsAsync(IReadOnlyList<ImportFailure> failures) =>
        new ImportErrorsDialog(failures).ShowDialog<ImportErrorsAction>(owner);

    public Task ShowOcrCopyBatchResultsAsync(IReadOnlyList<OcrCopyBatchItem> results) =>
        new OcrCopyResultsDialog(results).ShowDialog(owner);

    public Task<IndexReportDecision?> ShowIndexRefreshReportAsync(IndexReport report) =>
        new IndexReportDialog(report).ShowDialog<IndexReportDecision?>(owner);

    public Task<TrashDecision?> ShowTrashAsync(IReadOnlyList<TrashedDocument> documents) =>
        new TrashDialog(documents).ShowDialog<TrashDecision?>(owner);

    public Task<PdfExportOptions?> ChoosePdfExportAsync(OcrLanguage language) =>
        new PdfExportDialog(language).ShowDialog<PdfExportOptions?>(owner);

    public Task<IReadOnlyList<FilingSelection>?> ShowFilingSuggestionsAsync(IReadOnlyList<FilingSuggestion> suggestions) =>
        new FilingSuggestionsDialog(suggestions).ShowDialog<IReadOnlyList<FilingSelection>?>(owner);

    public Task ShowFilingRulesAsync(IReadOnlyList<FilingRule> rules, Func<FilingRule, FilingRule> save, Action<long> remove) =>
        new FilingRulesDialog(rules, save, remove).ShowDialog(owner);

    public Task ShowIntegrityReportAsync(IntegrityReport report) => new IntegrityReportDialog(report).ShowDialog(owner);

    public async Task<string?> PickRestorePdfAsync()
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a matching PDF",
            AllowMultiple = false,
            FileTypeFilter = [PdfDocuments]
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public Task CopyTextAsync(string text) =>
        (owner.Clipboard ?? throw new InvalidOperationException("The clipboard is unavailable.")).SetTextAsync(text);

    public void OpenPath(string path)
    {
        if (!Path.Exists(path)) throw new FileNotFoundException("This file or folder is unavailable. Refresh the library.");
        using var launched = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
