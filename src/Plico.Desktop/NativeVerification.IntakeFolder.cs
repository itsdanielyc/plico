using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifyIntakeFolderAsync(Window window, InboxViewModel model, FixtureDialogs dialogs,
        string fixture, string evidenceDirectory)
    {
        const string folderName = "Intake check";
        const string fileName = "Intake lease.pdf";
        const string fixturePadding = "\n  \n\n";
        string target = Directory.CreateDirectory(Path.Combine(model.LibraryPath, "PDFs", folderName)).FullName;
        await ChooseNativeIntakeFolderAsync(window, model, dialogs, target);
        Require(model.IntakeFolderLabel == $"PDFs/{folderName}", "The native rail did not show the chosen intake folder.");

        string inputDirectory = Directory.CreateTempSubdirectory("plico-intake-").FullName;
        string source = Path.Combine(inputDirectory, fileName);
        var originalFiles = dialogs.SelectedFiles;
        try
        {
            File.Copy(Path.Combine(Path.GetDirectoryName(fixture)!, "Lease Renewal.pdf"), source);
            // Distinguish this PDF from earlier imports of the same fixture.
            File.AppendAllText(source, fixturePadding);
            dialogs.SelectedFiles = [source];
            long before = model.InboxCount;
            await ((IAsyncRelayCommand)Control<Button>(window, "AddFilesButton").Command!).ExecuteAsync(null);
            Require(model.SelectedDocument is { HasText: true, Reviewed: false } added &&
                    added.RelativePath == $"{folderName}/{fileName}" && model.InboxCount == before + 1 &&
                    File.Exists(Path.Combine(target, fileName)) && File.Exists(source),
                $"The native Add files action did not file the PDF in the selected folder. {model.Status}");
            await UntilAsync(() => model.Preview is not null, () => model.PreviewMessage);
            await CaptureAsync(window, evidenceDirectory, "intake-folder.png");
        }
        finally
        {
            dialogs.SelectedFiles = originalFiles;
            File.Delete(source);
            Directory.Delete(inputDirectory);
        }
    }

    private static async Task ChooseNativeIntakeFolderAsync(Window window, InboxViewModel model, FixtureDialogs dialogs, string target)
    {
        var choose = Control<Button>(window, "IntakeFolderButton");
        Require(choose.IsEffectivelyVisible && choose.IsEnabled && choose.Command is IAsyncRelayCommand,
            "The native library rail did not offer an intake folder choice.");
        string? originalFolder = dialogs.FolderPath;
        dialogs.FolderPath = target;
        try
        {
            await ((IAsyncRelayCommand)choose.Command!).ExecuteAsync(choose.CommandParameter);
            Require(dialogs.LastFolderStart == Path.Combine(model.LibraryPath, "PDFs"),
                "The intake folder picker did not start at the library's PDF directory.");
        }
        finally
        {
            dialogs.FolderPath = originalFolder;
        }
    }
}
