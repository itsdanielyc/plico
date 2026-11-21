using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifyFolderRestoreAsync(Window window, InboxViewModel model, FixtureDialogs dialogs,
        string directory, DocumentSummary source)
    {
        string relativeFolder = Path.GetDirectoryName(source.RelativePath)!;
        var search = Control<TextBox>(window, "SearchBox");
        search.SetCurrentValue(TextBox.TextProperty, $"folder:\"{relativeFolder}\"");
        await UntilAsync(() => model.Heading == "Search results" && model.Documents.Count == 2 &&
            model.Documents.All(document => Path.GetDirectoryName(document.RelativePath) == relativeFolder) &&
            model.Preview is not null, () => model.Status);
        await SettleAsync(model);
        var originals = model.Documents.ToArray();
        string backup = Path.Combine(model.LibraryPath, "Folder restore archive");
        string nested = Directory.CreateDirectory(Path.Combine(backup, "Old names")).FullName;
        for (int index = 0; index < originals.Length; index++)
        {
            string path = Path.Combine(model.LibraryPath, "PDFs", originals[index].RelativePath);
            File.Move(path, Path.Combine(nested, $"Renamed copy {index + 1}.PDF"));
        }

        await model.RefreshIndexCommand.ExecuteAsync(null);
        var show = Control<Button>(window, "IndexReportButton");
        Require(show.IsVisible && model.UnavailableCount == 2, "The index report did not identify both displaced PDFs.");
        var command = (IAsyncRelayCommand)show.Command!;
        await command.ExecuteAsync(null);
        var report = dialogs.InspectedIndexReport;
        Require(report is { UnavailablePaths.Count: 2 }, "The index report did not offer both paths for restoration.");
        await CaptureIndexReportAsync(window, report!, directory, "folder-restore-report.png");

        string? previousFolder = dialogs.FolderPath;
        dialogs.IndexReportChoice = new IndexReportDecision.RestoreFolder();
        dialogs.FolderPath = backup;
        try
        {
            await command.ExecuteAsync(null);
        }
        finally
        {
            dialogs.IndexReportChoice = null;
            dialogs.FolderPath = previousFolder;
        }
        Require(model.Status == "2 documents restored." && !show.IsVisible &&
                Directory.EnumerateFiles(nested).Count() == 2 &&
                originals.All(original => model.Documents.Any(document => document.Id == original.Id &&
                    document.RelativePath == original.RelativePath && document.Tags.SequenceEqual(original.Tags))),
            $"Folder restoration did not recover both original records from their renamed copies. {model.Status}");
        await UntilAsync(() => model.Preview is not null, () => model.PreviewMessage);
        await CaptureAsync(window, directory, "folder-restored-pdfs.png");
    }
}
