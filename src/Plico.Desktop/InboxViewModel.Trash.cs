using CommunityToolkit.Mvvm.Input;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    [RelayCommand]
    private Task TrashDocumentAsync()
    {
        if (!CanWork || SelectedDocument is not { } document) return Task.CompletedTask;
        var position = CaptureReviewPosition(document.Id);
        return RunOperationAsync("Moving PDF to Trash…", async token =>
        {
            var library = RequireLibrary();
            await Task.Run(() => library.TrashPdfAsync(document.Id, token), token);
            Tags = library.GetTags();
            RefreshInboxCount();
            LoadResults(retainLoadedResults: true, reviewPosition: position);
            Status = "Moved to Trash. Use Trash to restore the PDF.";
        });
    }

    [RelayCommand]
    private Task OpenTrashAsync() => RunOperationAsync("Reading Trash…", async token =>
    {
        var library = RequireLibrary();
        var trash = await Task.Run(library.GetTrash, token);
        var decision = await _dialogs.ShowTrashAsync(trash);
        if (decision is null)
        {
            Status = trash.Count == 0 ? "Trash is empty." : "Closed Trash.";
            return;
        }
        if (decision.Purge)
        {
            await Task.Run(() => library.PurgeTrashedPdfAsync(decision.DocumentId, token), token);
            Status = "PDF permanently deleted.";
            return;
        }

        var restored = await Task.Run(() => library.RestoreTrashedPdfAsync(decision.DocumentId, token), token);
        Tags = library.GetTags();
        RefreshInboxCount();
        Navigate(inboxOnly: false, preferredId: restored.Id);
        Status = $"Restored {restored.Title}.";
    });

    internal void SelectReviewNeighbour(int step)
    {
        if (SelectedDocument is not { } selected || CaptureReviewPosition(selected.Id) is not { } position) return;
        int index = position.Index + step;
        if (index >= 0 && index < Documents.Count) SelectedDocument = Documents[index];
    }
}
