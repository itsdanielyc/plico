using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifyIndexReportAsync(Window window, InboxViewModel model, FixtureDialogs dialogs,
        string directory, string sourcePath, DocumentSummary source)
    {
        string folder = Path.GetDirectoryName(sourcePath)!;
        string holdingPath = Path.Combine(model.LibraryPath, "held-for-index-report.pdf");
        string firstDamaged = Path.Combine(folder, "First damaged.pdf");
        string secondDamaged = Path.Combine(folder, "Second damaged.pdf");
        await SettleAsync(model);
        File.Move(sourcePath, holdingPath);
        File.WriteAllText(firstDamaged, "Invalid PDF fixture");
        File.WriteAllText(secondDamaged, "Another invalid PDF fixture");
        await model.RefreshIndexCommand.ExecuteAsync(null);

        var show = Control<Button>(window, "IndexReportButton");
        Require(show.IsVisible && show.IsEnabled && model.Status.Contains("1 document unavailable.", StringComparison.Ordinal) &&
                model.Status.Contains("2 index notices.", StringComparison.Ordinal),
            $"The index status omitted an unavailable file or notice. {model.Status}");
        var command = (IAsyncRelayCommand)show.Command!;
        await command.ExecuteAsync(null);
        var report = dialogs.InspectedIndexReport;
        Require(report is { Notices.Count: 2 } && report.UnavailablePaths.SequenceEqual([source.RelativePath]),
            "The report did not record the affected path and both notices.");
        await CaptureIndexReportAsync(window, report!, directory, "index-report.png");

        File.Delete(firstDamaged);
        File.Delete(secondDamaged);
        await model.RefreshIndexCommand.ExecuteAsync(null);
        Require(show.IsVisible && model.LatestIndexRefresh is { UnavailablePaths.Count: 0, Warnings.Count: 0 },
            "The index report disappeared while a cataloged PDF was still unavailable.");
        await command.ExecuteAsync(null);
        report = dialogs.InspectedIndexReport;
        Require(report is { Notices.Count: 0 } && report.UnavailablePaths.SequenceEqual([source.RelativePath]),
            "The latest report did not distinguish the remaining path from the completed refresh notices.");
        await CaptureIndexReportAsync(window, report!, directory, "unavailable-index-report.png");

        dialogs.IndexReportChoice = new IndexReportDecision.RestorePdf(source.RelativePath);
        dialogs.RestoreCandidate = holdingPath;
        try
        {
            await command.ExecuteAsync(null);
        }
        finally
        {
            dialogs.IndexReportChoice = null;
            dialogs.RestoreCandidate = null;
        }
        Require(model.Status == "PDF restored." && !show.IsVisible && File.Exists(holdingPath) && File.Exists(sourcePath),
            $"The report did not restore the PDF while preserving the selected copy. {model.Status}");
        await UntilAsync(() => model.SelectedDocument is { } restored && restored.Id == source.Id &&
            restored.Tags.SequenceEqual(source.Tags) && model.Preview is not null, () => model.Status);
        await CaptureAsync(window, directory, "restored-pdf.png");
        await VerifyFolderRestoreAsync(window, model, dialogs, directory, source);
    }

    private static async Task CaptureIndexReportAsync(Window window, IndexReport report, string directory, string filename)
    {
        var dialog = new IndexReportDialog(report);
        var closed = dialog.ShowDialog(window);
        try
        {
            Require(Control<ListBox>(dialog, "IndexReportList").ItemCount == report.UnavailablePaths.Count + report.Notices.Count,
                "The report did not display all its paths and notices.");
            Require(Control<Button>(dialog, "RestorePdfButton").IsEnabled, "The unavailable PDF did not offer restoration.");
            Require(Control<Button>(dialog, "RestoreFromFolderButton").IsVisible, "The index report did not offer folder restoration.");
            await CaptureAsync(dialog, directory, filename);
        }
        finally
        {
            dialog.Close();
            await closed;
        }
    }
}
