using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

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
        return RunOperationAsync(DesktopText.Translate("Choose an export destination…"), async token =>
        {
            var library = RequireLibrary();
            var options = await _dialogs.ChoosePdfExportAsync(RecognitionLanguage);
            if (options is null)
            {
                Status = DesktopText.Translate("Export cancelled.");
                return;
            }
            string? destination = await _dialogs.PickFolderAsync(DesktopText.Translate("Choose where to export the PDF"));
            if (destination is null)
            {
                Status = DesktopText.Translate("Export destination selection closed.");
                return;
            }

            Status = options.Format == PdfExportFormat.PdfA ? DesktopText.Translate("Creating an archival PDF…") : DesktopText.Translate("Copying PDF…");
            string export = await Task.Run(() => library.ExportPdfAsync(document.Id, destination, options, token), token);
            LatestExportLocation = Path.GetDirectoryName(export)!;
            Status = DesktopText.Format($"Exported {Path.GetFileName(export)} to {LatestExportLocation}.");
        });
    }

    [RelayCommand]
    private Task ExportSelectedPdfsAsync()
    {
        if (!HasBatchSelection) return Task.CompletedTask;
        var selectedIds = _batchSelection.Select(document => document.Id).ToArray();
        return RunOperationAsync(DesktopText.Translate("Choose an export destination…"), async token =>
        {
            var library = RequireLibrary();
            string? destination = await _dialogs.PickFolderAsync(DesktopText.Translate("Choose where to export the selected PDFs"));
            if (destination is null)
            {
                Status = DesktopText.Translate("Export destination selection closed.");
                return;
            }

            Status = DesktopText.Translate("Copying selected PDFs…");
            var progress = new Progress<int>(copied =>
            {
                if (!IsBusy || token.IsCancellationRequested) return;
                ReportProgress(copied, selectedIds.Length);
                Status = DesktopText.Format($"Copied {copied} of {selectedIds.Length} PDFs…");
            });
            var export = await Task.Run(() => library.ExportPdfsAsync(selectedIds, destination, progress, token), token);
            LatestExportLocation = export.Location;
            Status = DesktopText.Format($"Exported {DocumentCount(export.PdfCount)} to {export.Location}.");
        });
    }

    [RelayCommand]
    private void OpenExportFolder()
    {
        if (LatestExportLocation is { } location) OpenPath(() => location);
    }
}
