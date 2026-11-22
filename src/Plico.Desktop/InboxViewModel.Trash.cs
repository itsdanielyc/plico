using CommunityToolkit.Mvvm.Input;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    [RelayCommand]
    private Task TrashDocumentAsync()
    {
        if (!CanWork || SelectedDocument is not { } document) return Task.CompletedTask;
        var position = CaptureReviewPosition(document.Id);
        return RunOperationAsync(DesktopText.Translate("Moving PDF to Trash…"), async token =>
        {
            var library = RequireLibrary();
            await Task.Run(() => library.TrashPdfAsync(document.Id, token), token);
            Tags = library.GetTags();
            RefreshInboxCount();
            LoadResults(retainLoadedResults: true, reviewPosition: position);
            OfferUndo(TrashUndo(library, document), DesktopText.Translate("Moved to Trash. Use Trash to restore the PDF."));
        });
    }

    [RelayCommand]
    private Task OpenTrashAsync() => RunOperationAsync(DesktopText.Translate("Reading Trash…"), async token =>
    {
        var library = RequireLibrary();
        var trash = await Task.Run(library.GetTrash, token);
        var decision = await _dialogs.ShowTrashAsync(trash);
        if (decision is null)
        {
            Status = trash.Count == 0 ? DesktopText.Translate("Trash is empty.") : DesktopText.Translate("Closed Trash.");
            return;
        }
        if (decision.Purge)
        {
            await Task.Run(() => library.PurgeTrashedPdfAsync(decision.DocumentId, token), token);
            RetireUndo();
            Status = DesktopText.Translate("PDF permanently deleted.");
            return;
        }

        var restored = await Task.Run(() => library.RestoreTrashedPdfAsync(decision.DocumentId, token), token);
        RetireUndo();
        Tags = library.GetTags();
        RefreshInboxCount();
        Navigate(inboxOnly: false, preferredId: restored.Id);
        Status = DesktopText.Format($"Restored {restored.Title}.");
    });

    internal void SelectReviewNeighbour(int step)
    {
        if (SelectedDocument is not { } selected || CaptureReviewPosition(selected.Id) is not { } position) return;
        int index = position.Index + step;
        if (index >= 0 && index < Documents.Count) SelectedDocument = Documents[index];
    }
}
