using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    [RelayCommand]
    private Task EditPdfPagesAsync()
    {
        if (SelectedDocument is not { } document) return Task.CompletedTask;
        int currentPage = _pageNumber;
        return RunOperationAsync(DesktopText.Translate("Choose a page edit…"), async token =>
        {
            var library = RequireLibrary();
            var request = await _dialogs.EditPdfPagesAsync(document, currentPage);
            if (request is null)
            {
                Status = DesktopText.Translate("Page editing closed.");
                return;
            }

            Status = DesktopText.Translate("Saving the PDF pages…");
            if (request is SplitPdfPages split)
            {
                var parts = await Task.Run(() => library.SplitPdfAsync(document.Id, split.PartStarts, token), token);
                RetireUndo();
                RevealNewInboxDocument(parts[0].Id);
                Status = DesktopText.Format($"Saved {DocumentCount(parts.Count)} from the split PDF.");
                return;
            }

            var changed = await Task.Run(() => request switch
            {
                ReorderPdfPages reorder => library.ReorderPdfPagesAsync(document.Id, reorder.Order, token),
                DeletePdfPages delete => library.DeletePdfPagesAsync(document.Id, delete.Selection, token),
                RotatePdfPages rotate => library.RotatePdfVersionAsync(document.Id, rotate.Selection, rotate.Rotation, token),
                _ => throw new InvalidOperationException("Choose a page edit.")
            }, token);
            ReloadPdfVersion(changed);
        });
    }

    [RelayCommand]
    private Task ShowPdfVersionsAsync()
    {
        if (SelectedDocument is not { } document) return Task.CompletedTask;
        return RunOperationAsync(DesktopText.Translate("Reading PDF version history…"), async token =>
        {
            var library = RequireLibrary();
            var history = await Task.Run(() => library.GetPdfVersions(document.Id), token);
            var choice = await _dialogs.ChoosePdfVersionAsync(document, history);
            if (choice is null)
            {
                Status = DesktopText.Translate("Version history closed.");
                return;
            }
            if (choice.Action == PdfVersionAction.Export)
            {
                string? destination = await _dialogs.PickFolderAsync(DesktopText.Translate("Choose where to export this PDF version"));
                if (destination is null)
                {
                    Status = DesktopText.Translate("Version export cancelled.");
                    return;
                }
                string exported = await Task.Run(() => library.ExportPdfVersionAsync(document.Id, choice.Key, destination, token), token);
                NoteExport(Path.GetDirectoryName(exported)!);
                AnnounceExport(DesktopText.Format($"Exported {Path.GetFileName(exported)} to {LatestExportLocation}."), Path.GetFileName(exported));
                return;
            }
            if (choice.Action == PdfVersionAction.Restore)
            {
                ReloadPdfVersion(await Task.Run(() => library.RestorePdfVersionAsync(document.Id, choice.Key, token), token));
                return;
            }
            string? replacement = await _dialogs.PickPdfAsync(DesktopText.Translate("Choose the revised PDF"));
            if (replacement is null)
            {
                Status = DesktopText.Translate("Version replacement cancelled.");
                return;
            }
            var language = RecognitionLanguage;
            ReloadPdfVersion(await Task.Run(() => library.ReplacePdfVersionAsync(document.Id, replacement, language, token), token));
        });
    }

    [RelayCommand]
    private Task ImportProtectedPdfAsync() => RunOperationAsync(DesktopText.Translate("Choose a password-protected PDF…"), async token =>
    {
        var library = RequireLibrary();
        string? source = await _dialogs.PickPdfAsync(DesktopText.Translate("Choose a password-protected PDF"));
        string? password = source is null ? null : await _dialogs.GetPdfPasswordAsync(source);
        if (source is null || password is null)
        {
            Status = DesktopText.Translate("Protected PDF import cancelled.");
            return;
        }
        var language = RecognitionLanguage;
        Status = DesktopText.Translate("Unlocking PDF…");
        var result = await Task.Run(() => library.UnlockAndImportPdfAsync(source, password, language, token), token);
        if (result.Added) RevealNewInboxDocument(result.Document.Id);
        else LoadResults(preferredId: result.Document.Id);
        Status = result.Added ? DesktopText.Format($"Imported “{result.Document.Title}” without its password.") : DesktopText.Translate("That PDF is already in this library.");
    });

    private void ReloadPdfVersion(DocumentSummary changed)
    {
        RetireUndo();
        LoadResults(preferredId: changed.Id, reloadPreview: true, retainLoadedResults: true);
        Status = DesktopText.Format($"Saved the current PDF for “{changed.Title}”.");
    }
}
