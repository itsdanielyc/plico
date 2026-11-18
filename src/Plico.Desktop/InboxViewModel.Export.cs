using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLatestExport))]
    private string? _latestExportLocation;

    public bool HasLatestExport => LatestExportLocation is not null;

    [RelayCommand]
    private Task ExportPdfAsync()
    {
        if (SelectedDocument is not { } document) return Task.CompletedTask;
        return RunOperationAsync("Choose an export destination…", async token =>
        {
            var library = RequireLibrary();
            string? destination = await _dialogs.PickFolderAsync("Choose where to export the PDF");
            if (destination is null)
            {
                Status = "Export destination selection closed.";
                return;
            }

            Status = "Copying PDF…";
            string export = await Task.Run(() => library.ExportPdfAsync(document.Id, destination, token), token);
            LatestExportLocation = Path.GetDirectoryName(export)!;
            Status = $"Exported {Path.GetFileName(export)} to {LatestExportLocation}.";
        });
    }

    [RelayCommand]
    private Task ExportSelectedPdfsAsync()
    {
        if (!HasBatchSelection) return Task.CompletedTask;
        var selectedIds = _batchSelection.Select(document => document.Id).ToArray();
        return RunOperationAsync("Choose an export destination…", async token =>
        {
            var library = RequireLibrary();
            string? destination = await _dialogs.PickFolderAsync("Choose where to export the selected PDFs");
            if (destination is null)
            {
                Status = "Export destination selection closed.";
                return;
            }

            Status = "Copying selected PDFs…";
            var progress = new Progress<int>(copied =>
            {
                if (IsBusy && !token.IsCancellationRequested) Status = $"Copied {copied} of {selectedIds.Length} PDFs…";
            });
            var export = await Task.Run(() => library.ExportPdfsAsync(selectedIds, destination, progress, token), token);
            LatestExportLocation = export.Location;
            Status = $"Exported {DocumentCount(export.PdfCount)} to {export.Location}.";
        });
    }

    [RelayCommand]
    private void OpenExportFolder()
    {
        if (LatestExportLocation is { } location) OpenPath(() => location);
    }
}
