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

        return RunOperationAsync("Choose the binder order…", async token =>
        {
            var library = RequireLibrary();
            var request = await _dialogs.OrderHouseholdBinderAsync(selected);
            if (request is null)
            {
                Status = "Binder export cancelled.";
                return;
            }
            string? destination = await _dialogs.PickFolderAsync("Choose where to export the household binder");
            if (destination is null)
            {
                Status = "Binder export cancelled.";
                return;
            }

            Status = "Creating the household binder…";
            var progress = new Progress<int>(copied =>
            {
                if (IsBusy && !token.IsCancellationRequested)
                    Status = $"Added {copied} of {request.DocumentIds.Count} PDFs to the binder…";
            });
            string exported = await Task.Run(() =>
                library.ExportHouseholdBinderAsync(request.DocumentIds, request.Title, destination, progress, token), token);
            LatestExportLocation = Path.GetDirectoryName(exported)!;
            Status = $"Exported {Path.GetFileName(exported)} to {LatestExportLocation}.";
        });
    }
}
