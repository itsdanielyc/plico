using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    private const string MovePickerTitle = "Choose a folder inside PDFs";
    private const string MovePickerClosed = "Move folder selection closed.";

    [RelayCommand]
    private Task MovePdfAsync()
    {
        if (SelectedDocument is not { } document) return Task.CompletedTask;
        return RunOperationAsync(DesktopText.Translate("Choose a library folder…"), async token =>
        {
            var library = RequireLibrary();
            string? folder = await _dialogs.PickFolderAsync(MovePickerTitle, library.PdfDirectory);
            if (folder is null)
            {
                Status = MovePickerClosed;
                return;
            }

            Status = DesktopText.Translate("Moving the PDF…");
            var moved = await Task.Run(() => library.MovePdfAsync(document.Id, folder, token), token);
            LoadResults(moved.Id, reloadPreview: Preview is null);
            Status = moved.RelativePath == document.RelativePath
                ? "This PDF is already in that folder."
                : $"Moved to {moved.RelativePath}.";
        });
    }

    [RelayCommand]
    private Task MoveSelectedPdfsAsync()
    {
        if (!HasBatchSelection) return Task.CompletedTask;
        var selectedIds = _batchSelection.Select(document => document.Id).ToHashSet();
        var orderedIds = Documents.Where(document => selectedIds.Contains(document.Id)).Select(document => document.Id).ToArray();
        if (orderedIds.Length < 2) return Task.CompletedTask;
        return RunOperationAsync(DesktopText.Translate("Choose a library folder…"), async token =>
        {
            var library = RequireLibrary();
            string? folder = await _dialogs.PickFolderAsync(MovePickerTitle, library.PdfDirectory);
            if (folder is null)
            {
                Status = MovePickerClosed;
                return;
            }

            Status = DesktopText.Translate("Moving selected PDFs…");
            var progress = new Progress<int>(processed =>
            {
                if (!IsBusy || token.IsCancellationRequested) return;
                ReportProgress(processed, orderedIds.Length);
                Status = DesktopText.Format($"Filed {processed} of {orderedIds.Length} PDFs…");
            });
            long? displayedDocument = SelectedDocument?.Id;
            var result = await Task.Run(() => library.MoveSelectedPdfsAsync(orderedIds, folder, progress, token), token);
            LoadResults(displayedDocument, reloadPreview: Preview is null);
            Status = BatchMoveStatus(result);
        });
    }

    private static string BatchMoveStatus(BatchMoveResult result)
    {
        var messages = new List<string>(3);
        if (result.Moved > 0) messages.Add($"Moved {DocumentCount(result.Moved)}.");
        if (result.AlreadyInFolder > 0) messages.Add($"{DocumentCount(result.AlreadyInFolder)} already in that folder.");
        if (result.Cancelled) messages.Add($"Cancelled with {DocumentCount(result.Remaining)} left to file.");
        if (result.Failure is { } failure)
        {
            messages.Add($"Stopped at “{failure.RelativePath}” with {DocumentCount(result.Remaining)} to finish. {failure.Reason}");
        }
        return string.Join(' ', messages);
    }
}
