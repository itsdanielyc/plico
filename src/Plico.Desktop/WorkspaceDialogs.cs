using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Plico.Core;

namespace Plico.Desktop;

/// <summary>The window's file pickers and handoff to the operating system.</summary>
internal interface IWorkspaceDialogs
{
    Task<IReadOnlyList<string>> PickDocumentsAsync();
    Task<string?> PickFolderAsync(string title);
    Task<ScanRequest?> PickScannerAsync();
    Task<string?> EditTitleAsync(string currentTitle);
    void OpenPath(string path);
}

internal sealed class WorkspaceDialogs(Window owner) : IWorkspaceDialogs
{
    private static readonly FilePickerFileType SupportedDocuments = new("PDF and images")
    {
        Patterns = ["*.pdf", "*.png", "*.jpg", "*.jpeg", "*.PDF", "*.PNG", "*.JPG", "*.JPEG"],
        MimeTypes = ["application/pdf", "image/png", "image/jpeg"],
        AppleUniformTypeIdentifiers = ["com.adobe.pdf", "public.png", "public.jpeg"]
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

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    public Task<ScanRequest?> PickScannerAsync() =>
        new ScanDialog(Scanners.DiscoverAsync, settings: new SavedScannerStore(ApplicationSettings.DirectoryPath),
            captureOptions: new ScanOptionsPreference(ApplicationSettings.DirectoryPath))
            .ShowDialog<ScanRequest?>(owner);

    public Task<string?> EditTitleAsync(string currentTitle) =>
        new DocumentTitleDialog(currentTitle).ShowDialog<string?>(owner);

    public void OpenPath(string path)
    {
        if (!Path.Exists(path)) throw new FileNotFoundException("This file or folder is unavailable. Refresh the library.");
        using var launched = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
