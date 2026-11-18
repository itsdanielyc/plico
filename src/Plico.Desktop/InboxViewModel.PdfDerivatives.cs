using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    [RelayCommand]
    private Task ExportRedactedPdfAsync()
    {
        if (SelectedDocument is not { } document) return Task.CompletedTask;
        return RunOperationAsync(DesktopText.Translate("Mark the areas to redact…"), async token =>
        {
            var library = RequireLibrary();
            var marks = await _dialogs.MarkPdfRedactionsAsync(document.PageCount, _pageNumber,
                (page, previewToken) => Task.Run(async () =>
                {
                    using var combined = CancellationTokenSource.CreateLinkedTokenSource(previewToken, token);
                    using var png = await library.RenderRedactionPreviewAsync(document, page, combined.Token);
                    return new Bitmap(png);
                }, previewToken));
            if (marks is null)
            {
                Status = DesktopText.Translate("Redaction cancelled.");
                return;
            }

            string? destination = await PickEditedPdfDestinationAsync();
            if (destination is null) return;
            Status = DesktopText.Translate("Removing the marked areas and rebuilding searchable text…");
            var language = RecognitionLanguage;
            string path = await Task.Run(() => library.ExportRedactedPdfAsync(document.Id, marks, destination, language, token), token);
            ReportEditedPdfExport(path);
        });
    }

    [RelayCommand]
    private Task ExportStampedPdfAsync()
    {
        if (SelectedDocument is not { } document) return Task.CompletedTask;
        return RunOperationAsync(DesktopText.Translate("Choose a stamp…"), async token =>
        {
            var library = RequireLibrary();
            var request = await _dialogs.ChoosePdfStampAsync(document.PageCount, _pageNumber);
            if (request is null)
            {
                Status = DesktopText.Translate("Stamp export cancelled.");
                return;
            }
            string? destination = await PickEditedPdfDestinationAsync();
            if (destination is null) return;
            Status = DesktopText.Translate("Stamping the PDF…");
            string path = await Task.Run(() => library.ExportStampedPdfAsync(document.Id, request.Pages, request.Stamp, destination, token), token);
            ReportEditedPdfExport(path);
        });
    }

    [RelayCommand]
    private Task ExportCompressedPdfAsync()
    {
        if (SelectedDocument is not { } document) return Task.CompletedTask;
        return RunOperationAsync(DesktopText.Translate("Choose PDF compression…"), async token =>
        {
            var library = RequireLibrary();
            var mode = await _dialogs.ChoosePdfCompressionAsync();
            if (mode is null)
            {
                Status = DesktopText.Translate("Compression cancelled.");
                return;
            }
            string? destination = await PickEditedPdfDestinationAsync();
            if (destination is null) return;
            Status = DesktopText.Translate("Optimizing the PDF…");
            var result = await Task.Run(() => library.ExportCompressedPdfAsync(document.Id, mode.Value, destination, token), token);
            if (result.Location is null)
            {
                Status = DesktopText.Translate("The original PDF is already as small as this mode can make it.");
                return;
            }
            ReportEditedPdfExport(result.Location);
        });
    }

    private async Task<string?> PickEditedPdfDestinationAsync()
    {
        string? path = await _dialogs.PickFolderAsync(DesktopText.Translate("Choose where to export the PDF"));
        if (path is null) Status = DesktopText.Translate("Export cancelled.");
        return path;
    }

    private void ReportEditedPdfExport(string path)
    {
        LatestExportLocation = Path.GetDirectoryName(path)!;
        Status = DesktopText.Format($"Exported {Path.GetFileName(path)} to {LatestExportLocation}.");
    }
}
