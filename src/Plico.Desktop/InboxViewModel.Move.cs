using CommunityToolkit.Mvvm.Input;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    [RelayCommand]
    private Task MovePdfAsync()
    {
        if (SelectedDocument is not { } document) return Task.CompletedTask;
        return RunOperationAsync("Choose a library folder…", async token =>
        {
            var library = RequireLibrary();
            string? folder = await _dialogs.PickFolderAsync("Choose a folder inside PDFs", library.PdfDirectory);
            if (folder is null)
            {
                Status = "Move folder selection closed.";
                return;
            }

            Status = "Moving the PDF…";
            var moved = await Task.Run(() => library.MovePdfAsync(document.Id, folder, token), token);
            LoadResults(moved.Id, reloadPreview: Preview is null);
            Status = moved.RelativePath == document.RelativePath
                ? "This PDF is already in that folder."
                : $"Moved to {moved.RelativePath}.";
        });
    }
}
