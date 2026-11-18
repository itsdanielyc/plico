using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Plico.Core;

namespace Plico.Desktop;

/// <summary>The window's file pickers and handoff to the operating system.</summary>
internal interface IWorkspaceDialogs
{
    Task<IReadOnlyList<string>> PickDocumentsAsync();
    Task<string?> PickFolderAsync(string title, string? startFolder = null);
    Task<ScanRequest?> PickScannerAsync();
    Task<bool> ConfirmPartialScanAsync(ScanInterruption interruption, CancellationToken cancellationToken);
    Task<string?> EditTitleAsync(string currentTitle);
    Task<SavedSearchDecision?> EditSavedSearchAsync(SavedSearchDraft draft, string? scopeTag, bool editing);
    Task<TagRenameRequest?> RenameTagAsync(string currentName, IReadOnlyList<string> catalogNames);
    Task<CombinePdfsRequest?> OrderPdfCombinationAsync(IReadOnlyList<DocumentSummary> selectedDocuments);
    Task<PdfPageCopyRequest?> ChoosePdfPageCopyAsync(DocumentSummary document, int currentPage, PdfPageCopyAction action);
    Task<ImportErrorsAction> ShowImportErrorsAsync(IReadOnlyList<ImportFailure> failures);
    Task CopyTextAsync(string text);
    void OpenPath(string path);
}

internal sealed class WorkspaceDialogs(Window owner) : IWorkspaceDialogs
{
    private static readonly FilePickerFileType SupportedDocuments = new("PDF and images")
    {
        Patterns = ["*.pdf", "*.png", "*.jpg", "*.jpeg", "*.tif", "*.tiff", "*.PDF", "*.PNG", "*.JPG", "*.JPEG", "*.TIF", "*.TIFF"],
        MimeTypes = ["application/pdf", "image/png", "image/jpeg", "image/tiff"],
        AppleUniformTypeIdentifiers = ["com.adobe.pdf", "public.png", "public.jpeg", "public.tiff"]
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

    public Task<SavedSearchDecision?> EditSavedSearchAsync(SavedSearchDraft draft, string? scopeTag, bool editing) =>
        new SavedSearchDialog(draft, scopeTag, editing).ShowDialog<SavedSearchDecision?>(owner);

    public Task<TagRenameRequest?> RenameTagAsync(string currentName, IReadOnlyList<string> catalogNames) =>
        new TagRenameDialog(currentName, catalogNames).ShowDialog<TagRenameRequest?>(owner);

    public Task<CombinePdfsRequest?> OrderPdfCombinationAsync(IReadOnlyList<DocumentSummary> selectedDocuments) =>
        new CombinePdfsDialog(selectedDocuments).ShowDialog<CombinePdfsRequest?>(owner);

    public Task<PdfPageCopyRequest?> ChoosePdfPageCopyAsync(DocumentSummary document, int currentPage, PdfPageCopyAction action) =>
        new PdfPageCopyDialog(document.PageCount, currentPage, action).ShowDialog<PdfPageCopyRequest?>(owner);

    public Task<ImportErrorsAction> ShowImportErrorsAsync(IReadOnlyList<ImportFailure> failures) =>
        new ImportErrorsDialog(failures).ShowDialog<ImportErrorsAction>(owner);

    public Task CopyTextAsync(string text) =>
        (owner.Clipboard ?? throw new InvalidOperationException("The clipboard is unavailable.")).SetTextAsync(text);

    public void OpenPath(string path)
    {
        if (!Path.Exists(path)) throw new FileNotFoundException("This file or folder is unavailable. Refresh the library.");
        using var launched = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
