using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    [RelayCommand]
    private Task ExportHouseholdBinderAsync()
    {
        IReadOnlyList<DocumentSummary> selected = SelectedDocument is { } document ? [document] : [];
        if (HasBatchSelection)
        {
            var selectedIds = _batchSelection.Select(item => item.Id).ToHashSet();
            selected = Documents.Where(item => selectedIds.Contains(item.Id)).ToArray();
        }
        if (selected.Count == 0) return Task.CompletedTask;

        return RunOperationAsync(DesktopText.Translate("Choose the binder order…"), async token =>
        {
            var library = RequireLibrary();
            var request = await _dialogs.OrderHouseholdBinderAsync(selected);
            if (request is null)
            {
                Status = DesktopText.Translate("Binder export cancelled.");
                return;
            }
            string? destination = await _dialogs.PickFolderAsync(DesktopText.Translate("Choose where to export the household binder"));
            if (destination is null)
            {
                Status = DesktopText.Translate("Binder export cancelled.");
                return;
            }

            Status = DesktopText.Translate("Creating the household binder…");
            var progress = new Progress<int>(copied =>
            {
                if (!IsBusy || token.IsCancellationRequested) return;
                ReportProgress(copied, request.DocumentIds.Count);
                Status = DesktopText.Format($"Added {copied} of {request.DocumentIds.Count} PDFs to the binder…");
            });
            string exported = await Task.Run(() =>
                library.ExportHouseholdBinderAsync(request.DocumentIds, request.Title, destination, progress, token), token);
            NoteExport(Path.GetDirectoryName(exported)!);
            Status = DesktopText.Format($"Exported {Path.GetFileName(exported)} to {LatestExportLocation}.");
        });
    }
}
