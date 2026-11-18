using Avalonia.Controls;
using Plico.Core;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifyIndexReportAsync(Window window, InboxViewModel model, string directory, string sourcePath,
        DocumentSummary source)
    {
        string folder = Path.GetDirectoryName(sourcePath)!;
        string holdingPath = Path.Combine(model.LibraryPath, "held-for-index-report.pdf");
        string firstDamaged = Path.Combine(folder, "First damaged.pdf");
        string secondDamaged = Path.Combine(folder, "Second damaged.pdf");
        File.Move(sourcePath, holdingPath);
        File.WriteAllText(firstDamaged, "Invalid PDF fixture");
        File.WriteAllText(secondDamaged, "Another invalid PDF fixture");
        await model.RefreshIndexCommand.ExecuteAsync(null);

        var show = Control<Button>(window, "IndexReportButton");
        Require(show.IsVisible && show.IsEnabled && model.Status.Contains("1 document unavailable.", StringComparison.Ordinal) &&
                model.Status.Contains("2 index notices.", StringComparison.Ordinal),
            $"The index status omitted an unavailable file or notice. {model.Status}");
        var result = model.LatestIndexRefresh;
        Require(result is { Warnings.Count: 2 } && result.UnavailablePaths.SequenceEqual([source.RelativePath]),
            "The report did not record the affected path and both notices.");
        var dialog = new IndexReportDialog(result!);
        var closed = dialog.ShowDialog(window);
        try
        {
            Require(Control<ListBox>(dialog, "IndexReportList").ItemCount == 3, "The report did not display all three rows.");
            await CaptureAsync(dialog, directory, "index-report.png");
        }
        finally
        {
            dialog.Close();
            await closed;
        }

        File.Move(holdingPath, sourcePath);
        File.Delete(firstDamaged);
        File.Delete(secondDamaged);
        await model.RefreshIndexCommand.ExecuteAsync(null);
        Require(!show.IsVisible && model.Documents.Any(document => document.Id == source.Id && document.Tags.SequenceEqual(source.Tags)),
            "Returning the PDF did not restore its record and clear the report.");
    }
}
