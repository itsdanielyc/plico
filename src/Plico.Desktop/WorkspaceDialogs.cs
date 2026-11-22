using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
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
    Task<ManualDuplexDecision> ChooseDuplexActionAsync(ManualDuplexPrompt prompt, CancellationToken cancellationToken);
    Task<ArchiveSerialInput?> PickArchiveSerialAsync(ArchiveSerialAction action, long? current = null);
    Task<string?> PickArchiveSheetPathAsync(string filename);
    Task<DocumentDateEdit?> EditDocumentDateAsync(DateOnly? currentDate);
    Task<DocumentDateEdit?> EditBatchDocumentDatesAsync(DateOnly? commonDate, int documentCount);
    Task<SavedSearchDecision?> EditSavedSearchAsync(SavedSearchDraft draft, string? scopeTag, bool editing);
    Task<TagRenameRequest?> RenameTagAsync(string currentName, IReadOnlyList<string> catalogNames);
    Task<string?> ChooseTagToRemoveAsync(IReadOnlyList<BatchTagOption> options);
    Task<CombinePdfsRequest?> OrderPdfCombinationAsync(IReadOnlyList<DocumentSummary> selectedDocuments);
    Task<CombinePdfsRequest?> OrderHouseholdBinderAsync(IReadOnlyList<DocumentSummary> selectedDocuments);
    Task<PdfPageCopyRequest?> ChoosePdfPageCopyAsync(DocumentSummary document, int currentPage, PdfPageCopyAction action);
    Task<PdfPageEditRequest?> EditPdfPagesAsync(DocumentSummary document, int currentPage);
    Task<PdfVersionDecision?> ChoosePdfVersionAsync(DocumentSummary document, IReadOnlyList<PdfVersion> versions);
    Task<string?> PickPdfAsync(string title);
    Task<string?> GetPdfPasswordAsync(string sourcePath);
    Task<IReadOnlyList<PdfRedactionRegion>?> MarkPdfRedactionsAsync(int pageCount, int currentPage, Func<int, CancellationToken, Task<Bitmap>> preview);
    Task<PdfStampRequest?> ChoosePdfStampAsync(int pageCount, int currentPage);
    Task<PdfCompressionMode?> ChoosePdfCompressionAsync();
    Task<AppearanceOptions?> ChooseAppearanceAsync(AppearanceOptions current);
    Task<ImportErrorsAction> ShowImportErrorsAsync(IReadOnlyList<ImportFailure> failures);
    Task ShowOcrCopyBatchResultsAsync(IReadOnlyList<OcrCopyBatchItem> results);
    Task<IndexReportDecision?> ShowIndexRefreshReportAsync(IndexReport report);
    Task<string?> PickRestorePdfAsync();
    Task<TrashDecision?> ShowTrashAsync(IReadOnlyList<TrashedDocument> documents);
    Task<PdfExportOptions?> ChoosePdfExportAsync(OcrLanguage language);
    Task<IReadOnlyList<FilingSelection>?> ShowFilingSuggestionsAsync(IReadOnlyList<FilingSuggestion> suggestions);
    Task ShowFilingRulesAsync(IReadOnlyList<FilingRule> rules, Func<FilingRule, FilingRule> save, Action<long> remove);
    Task ShowIntegrityReportAsync(IntegrityReport report);
    Task<string?> PickMigrationSourceAsync(LibraryMigrationFormat format, bool isFolder);
    Task ShowMigrationReportAsync(LibraryMigrationReport report, Action<string> saveReport);
    Task CopyTextAsync(string text);
    void OpenPath(string path);
}

internal sealed partial class WorkspaceDialogs(Window owner) : IWorkspaceDialogs
{
    private static readonly string[] IntakeExtensions =
        ["pdf", "png", "jpg", "jpeg", "tif", "tiff", "heic", "heif", "eml", "msg", "txt", "md", "markdown",
         "doc", "docx", "odt", "rtf", "xls", "xlsx", "ods", "ppt", "pptx", "odp"];
    private static readonly FilePickerFileType SupportedDocuments = new("Supported documents")
    {
        Patterns = IntakeExtensions.SelectMany(extension => new[] { $"*.{extension}", $"*.{extension.ToUpperInvariant()}" }).ToArray(),
        MimeTypes = ["application/pdf", "image/png", "image/jpeg", "image/tiff", "image/heic", "image/heif",
            "message/rfc822", "application/vnd.ms-outlook", "text/plain", "text/markdown", "application/rtf",
            "application/msword", "application/vnd.ms-excel", "application/vnd.ms-powerpoint",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            "application/vnd.oasis.opendocument.text", "application/vnd.oasis.opendocument.spreadsheet",
            "application/vnd.oasis.opendocument.presentation"],
        AppleUniformTypeIdentifiers = ["com.adobe.pdf", "public.png", "public.jpeg", "public.tiff", "public.heic", "public.heif",
            "public.email-message", "com.microsoft.outlook-msg", "public.plain-text", "net.daringfireball.markdown", "public.rtf",
            "com.microsoft.word.doc", "com.microsoft.excel.xls", "com.microsoft.powerpoint.ppt",
            "org.openxmlformats.wordprocessingml.document", "org.openxmlformats.spreadsheetml.sheet",
            "org.openxmlformats.presentationml.presentation", "org.oasis-open.opendocument.text",
            "org.oasis-open.opendocument.spreadsheet", "org.oasis-open.opendocument.presentation"]
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
            Title = DesktopText.Translate("Add paperwork"),
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

    public async Task<string?> PickMigrationSourceAsync(LibraryMigrationFormat format, bool isFolder)
    {
        if (isFolder)
            return await PickFolderAsync(format == LibraryMigrationFormat.Mariner
                ? "Choose a Mariner Paperless library" : "Choose a Paperless-ngx export folder");
        bool evernote = format == LibraryMigrationFormat.Evernote;
        var fileTypes = new FilePickerFileType(evernote ? "Evernote export" : "Paperless-ngx ZIP export")
        {
            Patterns = evernote ? ["*.enex", "*.ENEX"] : ["*.zip", "*.ZIP"]
        };
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = evernote ? "Choose an Evernote ENEX file" : "Choose a Paperless-ngx ZIP export",
            FileTypeFilter = [fileTypes, FilePickerFileTypes.All]
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public Task ShowMigrationReportAsync(LibraryMigrationReport report, Action<string> saveReport) =>
        new MigrationReportDialog(report, saveReport).ShowDialog(owner);

    public Task<bool> ConfirmPartialScanAsync(ScanInterruption interruption, CancellationToken cancellationToken) =>
        new ScanInterruptedDialog(interruption).ShowDecisionAsync(owner, cancellationToken);

    public Task<ManualDuplexDecision> ChooseDuplexActionAsync(ManualDuplexPrompt prompt, CancellationToken cancellationToken) =>
        new ManualDuplexDialog(prompt).ShowDecisionAsync(owner, cancellationToken);

    public Task<ArchiveSerialInput?> PickArchiveSerialAsync(ArchiveSerialAction action, long? current = null) =>
        new ArchiveSerialDialog(action, current).ShowDialog<ArchiveSerialInput?>(owner);

    public async Task<string?> PickArchiveSheetPathAsync(string filename)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save a printable PDF",
            SuggestedFileName = filename,
            DefaultExtension = "pdf",
            FileTypeChoices = [PdfDocuments]
        });
        return file?.TryGetLocalPath();
    }

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

    public Task<CombinePdfsRequest?> OrderHouseholdBinderAsync(IReadOnlyList<DocumentSummary> selectedDocuments) =>
        new CombinePdfsDialog(selectedDocuments, isBinder: true).ShowDialog<CombinePdfsRequest?>(owner);

    public Task<PdfPageCopyRequest?> ChoosePdfPageCopyAsync(DocumentSummary document, int currentPage, PdfPageCopyAction action) =>
        new PdfPageCopyDialog(document.PageCount, currentPage, action).ShowDialog<PdfPageCopyRequest?>(owner);

    public Task<PdfPageEditRequest?> EditPdfPagesAsync(DocumentSummary document, int currentPage) =>
        new PdfPageEditor(document.PageCount, currentPage).ShowDialog<PdfPageEditRequest?>(owner);

    public Task<PdfVersionDecision?> ChoosePdfVersionAsync(DocumentSummary document, IReadOnlyList<PdfVersion> versions) =>
        new PdfVersionHistoryDialog(document, versions).ShowDialog<PdfVersionDecision?>(owner);

    public Task<string?> GetPdfPasswordAsync(string sourcePath) => new UnlockPdfDialog(sourcePath).ShowDialog<string?>(owner);

    public Task<IReadOnlyList<PdfRedactionRegion>?> MarkPdfRedactionsAsync(int pageCount, int currentPage,
        Func<int, CancellationToken, Task<Bitmap>> preview) =>
        new PdfRedactionDialog(pageCount, currentPage, preview).ShowDialog<IReadOnlyList<PdfRedactionRegion>?>(owner);

    public Task<PdfStampRequest?> ChoosePdfStampAsync(int pageCount, int currentPage) =>
        new PdfStampDialog(pageCount, currentPage).ShowDialog<PdfStampRequest?>(owner);

    public Task<PdfCompressionMode?> ChoosePdfCompressionAsync() =>
        new PdfCompressionDialog().ShowDialog<PdfCompressionMode?>(owner);

    public Task<AppearanceOptions?> ChooseAppearanceAsync(AppearanceOptions current) =>
        new AppearanceDialog(current).ShowDialog<AppearanceOptions?>(owner);

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

    public Task<string?> PickRestorePdfAsync() => PickPdfAsync(DesktopText.Translate("Choose a matching PDF"));

    public async Task<string?> PickPdfAsync(string title)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
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
