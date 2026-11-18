using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifyBatchOcrAsync(Window window, InboxViewModel model, string fixture, string directory, long priorSourceId)
    {
        const string scanName = "Batch OCR receipt.pdf";
        const string copyTitle = "Batch OCR receipt (searchable)";
        var scan = Path.Combine(Path.GetDirectoryName(fixture)!, "Scanned Receipt.pdf");
        File.Copy(scan, Path.Combine(model.LibraryPath, "PDFs", scanName));
        model.ShowAllCommand.Execute(null);
        await model.RefreshIndexCommand.ExecuteAsync(null);
        var newSource = model.Documents.Single(document => document.RelativePath == scanName);
        var priorSource = model.Documents.Single(document => document.Id == priorSourceId);

        Control<Button>(window, "WithoutTextButton").Command!.Execute(null);
        Require(model.Heading == "Without text" && model.Query == "text:missing" &&
                model.Documents.All(document => !document.HasText) &&
                model.Documents.Any(document => document.Id == priorSourceId) &&
                model.Documents.Any(document => document.Id == newSource.Id),
            "The Without text view did not find the image-only PDFs.");
        await UntilAsync(() => model.Preview is not null, () => model.PreviewMessage);
        await CaptureAsync(window, directory, "without-text.png");

        Control<ComboBox>(window, "OcrLanguageBox").SetCurrentValue(ComboBox.SelectedItemProperty, OcrLanguage.English);
        var list = Control<ListBox>(window, "DocumentList");
        list.SelectedItems = new List<DocumentSummary> { priorSource, newSource };
        var create = Control<Button>(window, "CreateSelectedOcrCopiesButton");
        Require(create.IsEffectivelyVisible && create.IsEnabled && create.Command is IAsyncRelayCommand,
            "The selected PDFs did not offer batch OCR.");
        await CaptureAsync(window, directory, "ocr-batch-selection.png");
        long before = model.InboxCount;
        await ((IAsyncRelayCommand)create.Command!).ExecuteAsync(null);
        await UntilAsync(() => model.SelectedDocument is { Title: copyTitle, HasText: true } && model.Preview is not null,
            () => model.Status);
        Require(model.InboxCount == before + 1 && model.Status == "1 OCR copy added. 1 reused." &&
                model.OcrCopyBatchResults.Count == 2 &&
                model.OcrCopyBatchResults.Any(item => item.SourcePath == priorSource.RelativePath && item.Outcome == OcrCopyOutcome.Reused) &&
                model.OcrCopyBatchResults.Any(item => item.SourcePath == scanName && item.Outcome == OcrCopyOutcome.Added),
            $"The batch did not distinguish the new and existing OCR copies. {model.Status}");

        var show = Control<Button>(window, "OcrCopyBatchResultsButton");
        Require(show.IsVisible && show.IsEnabled, "The OCR results did not stay available after the batch.");
        var dialog = new OcrCopyResultsDialog(model.OcrCopyBatchResults);
        var closed = dialog.ShowDialog(window);
        try
        {
            Require(Control<ListBox>(dialog, "OcrResultsList").ItemCount == 2, "The batch dialog did not show both PDFs.");
            await CaptureAsync(dialog, directory, "ocr-batch-results.png");
        }
        finally
        {
            dialog.Close();
            await closed;
        }

        Control<TextBox>(window, "SearchBox").SetCurrentValue(TextBox.TextProperty, $"{ReceiptKeyword} text:present");
        await UntilAsync(() => model.Heading == "Search results" &&
            model.Documents.Any(document => document.Title == copyTitle) &&
            model.Documents.All(document => document.HasText) && model.Preview is not null, () => model.Status);
    }
}
