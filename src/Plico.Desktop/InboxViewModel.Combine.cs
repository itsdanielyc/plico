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
        return RunOperationAsync("Choose the document order…", async token =>
        {
            var library = RequireLibrary();
            var request = await _dialogs.OrderPdfCombinationAsync(selected);
            if (request is null)
            {
                Status = "PDF combination closed.";
                return;
            }

            Status = "Combining PDFs…";
            var progress = new Progress<int>(count =>
            {
                if (IsBusy && !token.IsCancellationRequested) Status = $"Combined {count} of {request.DocumentIds.Count} PDFs…";
            });
            var combined = await Task.Run(() => library.CombinePdfsAsync(request.DocumentIds, request.Title, progress, token), token);
            Navigate(inboxOnly: true, preferredId: combined.Id);
            Status = $"Combined {DocumentCount(request.DocumentIds.Count)} into “{combined.Title}”.";
        });
    }
}
