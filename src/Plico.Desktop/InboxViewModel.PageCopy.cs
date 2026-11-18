using Plico.Core;
using CommunityToolkit.Mvvm.Input;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    public bool HasMultiplePages => SelectedDocument is { PageCount: > 1 };

    [RelayCommand]
    private Task ExtractPdfPagesAsync()
    {
        if (SelectedDocument is not { PageCount: > 1 } document) return Task.CompletedTask;
        return CreatePdfPageCopyAsync(document, PdfPageCopyAction.Extract);
    }

    [RelayCommand]
    private Task RotatePdfPagesAsync() => SelectedDocument is { } document
        ? CreatePdfPageCopyAsync(document, PdfPageCopyAction.Rotate)
        : Task.CompletedTask;

    private Task CreatePdfPageCopyAsync(DocumentSummary document, PdfPageCopyAction action)
    {
        bool rotate = action == PdfPageCopyAction.Rotate;
        int currentPage = _pageNumber;
        return RunOperationAsync(rotate ? "Choose which pages to rotate…" : "Choose which pages to extract…", async token =>
        {
            var library = RequireLibrary();
            var request = await _dialogs.ChoosePdfPageCopyAsync(document, currentPage, action);
            if (request is null)
            {
                Status = rotate ? "Page rotation closed." : "Page extraction closed.";
                return;
            }

            Status = rotate ? "Rotating selected pages…" : "Extracting selected pages…";
            var copy = await Task.Run(() => rotate
                ? library.RotatePdfPagesAsync(document.Id, request.Selection,
                    request.Rotation ?? throw new InvalidOperationException("Choose a rotation."), request.Title, token)
                : library.ExtractPdfPagesAsync(document.Id, request.Selection, request.Title, token), token);
            RevealNewInboxDocument(copy.Id);
            int pageCount = request.Selection.SelectedPageCount;
            string pages = pageCount == 1 ? "1 page" : $"{pageCount} pages";
            Status = rotate ? $"Rotated {pages} in a new copy." : $"Extracted {pages}.";
        });
    }
}
