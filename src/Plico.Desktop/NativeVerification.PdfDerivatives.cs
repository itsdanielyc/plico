using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Plico.Core;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifyPdfDerivativesAsync(MainWindow window, InboxViewModel model, FixtureDialogs dialogs, string evidence)
    {
        var original = model.SelectedDocument ?? throw new InvalidOperationException("A source PDF must be open before exporting edits.");
        Require(original.PageCount == 2, "The PDF derivative fixture must have two pages.");
        var destination = Directory.CreateTempSubdirectory("plico-verify-pdf-edits-");
        string? previousFolder = dialogs.FolderPath;
        dialogs.FolderPath = destination.FullName;
        string source = Path.Combine(model.LibraryPath, "PDFs", original.RelativePath);
        byte[] originalBytes = File.ReadAllBytes(source);
        try
        {
            dialogs.RedactionChoice = async (pageCount, pageNumber, preview) =>
            {
                var dialog = new PdfRedactionDialog(pageCount, pageNumber, preview);
                var response = dialog.ShowDialog<IReadOnlyList<PdfRedactionRegion>?>(window);
                await UntilAsync(() => Control<PdfRedactionCanvas>(dialog, "PageCanvas").Page is not null,
                    () => Control<TextBlock>(dialog, "PreviewNotice").Text ?? "Redaction preview");
                dialog.AddRegion(new Rect(0, 0, 1, 1));
                await CaptureAsync(dialog, evidence, "pdf-redaction-box.png");
                Control<Button>(dialog, "ConfirmRedactions").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return await response;
            };
            await model.ExportRedactedPdfCommand.ExecuteAsync(null);
            string? redacted = Directory.EnumerateFiles(destination.FullName, "* (redacted).pdf").SingleOrDefault();
            Require(redacted is not null, $"The redacted copy was not exported. {model.Status}");
            using (var pdf = PdfDocument.Open(redacted!))
            {
                Require(!ContentOrderTextExtractor.GetText(pdf.GetPage(1)).Contains(LetterKeyword, StringComparison.OrdinalIgnoreCase) &&
                    ContentOrderTextExtractor.GetText(pdf.GetPage(2)).Contains(ReceiptKeyword, StringComparison.OrdinalIgnoreCase),
                    "Redaction kept the covered page text or lost the unmarked receipt.");
            }
            File.Copy(redacted!, Path.Combine(evidence, "redacted-copy.pdf"), overwrite: true);

            dialogs.StampChoice = async (pageCount, pageNumber) =>
            {
                var dialog = new PdfStampDialog(pageCount, pageNumber);
                var response = dialog.ShowDialog<PdfStampRequest?>(window);
                await CaptureAsync(dialog, evidence, "pdf-stamp-dialog.png");
                Control<Button>(dialog, "ConfirmStamp").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return await response;
            };
            await model.ExportStampedPdfCommand.ExecuteAsync(null);
            string? stamped = Directory.EnumerateFiles(destination.FullName, "* (stamped).pdf").SingleOrDefault();
            Require(stamped is not null, $"The stamped copy was not exported. {model.Status}");
            using (var pdf = PdfDocument.Open(stamped!))
                Require(ContentOrderTextExtractor.GetText(pdf.GetPage(1)).Contains("PAID", StringComparison.Ordinal),
                    "The stamp was missing from the exported PDF.");
            File.Copy(stamped!, Path.Combine(evidence, "stamped-copy.pdf"), overwrite: true);

            dialogs.CompressionChoice = PdfCompressionMode.Lossless;
            await model.ExportCompressedPdfCommand.ExecuteAsync(null);
            string? compressed = Directory.EnumerateFiles(destination.FullName, "* (compressed).pdf").SingleOrDefault();
            Require(compressed is null ? model.Status.Contains("as small", StringComparison.Ordinal)
                : new FileInfo(compressed).Length < originalBytes.Length, $"Unexpected compression result. {model.Status}");
            Require(File.ReadAllBytes(source).AsSpan().SequenceEqual(originalBytes), "Exported edits changed the library copy.");
        }
        finally
        {
            dialogs.RedactionChoice = null;
            dialogs.StampChoice = null;
            dialogs.CompressionChoice = null;
            dialogs.FolderPath = previousFolder;
            model.LatestExportLocation = null;
            destination.Delete(recursive: true);
        }
    }
}
