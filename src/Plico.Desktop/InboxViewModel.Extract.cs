using CommunityToolkit.Mvvm.Input;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    public bool HasMultiplePages => SelectedDocument is { PageCount: > 1 };

    [RelayCommand]
    private Task ExtractPdfPagesAsync()
    {
        if (SelectedDocument is not { PageCount: > 1 } document) return Task.CompletedTask;
        int currentPage = _pageNumber;
        return RunOperationAsync("Choose which pages to extract…", async token =>
        {
            var library = RequireLibrary();
            var request = await _dialogs.ChoosePdfPagesAsync(document, currentPage);
            if (request is null)
            {
                Status = "Page extraction closed.";
                return;
            }

            Status = "Extracting selected pages…";
            var excerpt = await Task.Run(() => library.ExtractPdfPagesAsync(document.Id, request.Selection, request.Title, token), token);
            Navigate(inboxOnly: true, preferredId: excerpt.Id);
            Status = excerpt.PageCount == 1 ? "Extracted 1 page." : $"Extracted {excerpt.PageCount} pages.";
        });
    }
}
