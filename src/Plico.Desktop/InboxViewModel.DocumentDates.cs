using CommunityToolkit.Mvvm.Input;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    public string DocumentDateAction => SelectedDocument?.DocumentDate is { } date
        ? $"Dated {date:dd MMM yyyy}" : "Set document date…";

    [RelayCommand]
    private Task EditDocumentDateAsync() => RunOperationAsync("Edit document date…", async token =>
    {
        if (SelectedDocument is not { } document) return;
        var edit = await _dialogs.EditDocumentDateAsync(document.DocumentDate);
        if (edit is null)
        {
            Status = "Document date editor closed.";
            return;
        }

        var library = RequireLibrary();
        await Task.Run(() => library.SetDocumentDate(document.Id, edit.Date), token);
        LoadResults(document.Id);
        Status = edit.Date.HasValue ? "Document date saved." : "Document date cleared.";
    });
}
