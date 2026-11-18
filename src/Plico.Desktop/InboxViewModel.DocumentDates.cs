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
            Status = DesktopText.Translate("Document date editor closed.");
            return;
        }

        var library = RequireLibrary();
        await Task.Run(() => library.SetDocumentDate(document.Id, edit.Date), token);
        LoadResults(document.Id);
        Status = edit.Date.HasValue ? DesktopText.Translate("Document date saved.") : DesktopText.Translate("Document date cleared.");
    });

    internal Task EditBatchDocumentDatesAsync()
    {
        if (!HasBatchSelection) return Task.CompletedTask;
        var selection = _batchSelection;
        var selectedIds = selection.Select(document => document.Id).ToArray();
        DateOnly? commonDate = selection[0].DocumentDate;
        if (commonDate.HasValue && selection.Any(document => document.DocumentDate != commonDate)) commonDate = null;

        return RunOperationAsync("Edit document dates…", async token =>
        {
            var edit = await _dialogs.EditBatchDocumentDatesAsync(commonDate, selectedIds.Length);
            if (edit is null)
            {
                Status = DesktopText.Translate("Document date editor closed.");
                return;
            }

            var library = RequireLibrary();
            await Task.Run(() => library.SetDocumentDates(selectedIds, edit.Date, token), token);
            LoadResults(retainLoadedResults: true);
            string count = DocumentCount(selectedIds.Length);
            Status = edit.Date.HasValue ? DesktopText.Format($"Document date saved for {count}.") : DesktopText.Format($"Document dates cleared for {count}.");
        });
    }
}
