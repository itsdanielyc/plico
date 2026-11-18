using CommunityToolkit.Mvvm.Input;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    [RelayCommand]
    private Task CombinePdfsAsync()
    {
        if (!HasBatchSelection) return Task.CompletedTask;
        var selectedIds = _batchSelection.Select(document => document.Id).ToHashSet();
        var selected = Documents.Where(document => selectedIds.Contains(document.Id)).ToArray();
        if (selected.Length < 2) return Task.CompletedTask;
        return RunOperationAsync(DesktopText.Translate("Choose the document order…"), async token =>
        {
            var library = RequireLibrary();
            var request = await _dialogs.OrderPdfCombinationAsync(selected);
            if (request is null)
            {
                Status = DesktopText.Translate("PDF combination closed.");
                return;
            }

            Status = DesktopText.Translate("Combining PDFs…");
            var progress = new Progress<int>(count =>
            {
                if (IsBusy && !token.IsCancellationRequested) Status = DesktopText.Format($"Combined {count} of {request.DocumentIds.Count} PDFs…");
            });
            var combined = await Task.Run(() => library.CombinePdfsAsync(request.DocumentIds, request.Title, progress, token), token);
            RevealNewInboxDocument(combined.Id);
            Status = DesktopText.Format($"Combined {DocumentCount(request.DocumentIds.Count)} into “{combined.Title}”.");
        });
    }
}
