using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifyRetryFilingAsync(Window window, InboxViewModel model, FixtureDialogs dialogs,
        string fixture, string directory)
    {
        const string addedFilename = "Added retry.pdf";
        const string watchedFilename = "Watched retry.pdf";
        const string intakeName = "Retry intake";
        string scenario = Directory.CreateDirectory(Path.Combine(model.LibraryPath, "Retry filing check")).FullName;
        string library = Directory.CreateDirectory(Path.Combine(scenario, "Library")).FullName;
        string blankLibrary = Directory.CreateDirectory(Path.Combine(scenario, "Blank library")).FullName;
        string incoming = Directory.CreateDirectory(Path.Combine(scenario, "Incoming")).FullName;
        string pdfs = Path.Combine(library, "PDFs");
        string addedSource = Path.Combine(scenario, addedFilename);
        string watchedSource = Path.Combine(incoming, watchedFilename);
        string? previousFolder = dialogs.FolderPath;
        var previousFiles = dialogs.SelectedFiles;

        try
        {
            dialogs.FolderPath = library;
            await model.ChooseLibraryCommand.ExecuteAsync(null);
            Require(model.LibraryPath == library, $"The retry library did not open. {model.Status}");
            string intake = Directory.CreateDirectory(Path.Combine(pdfs, intakeName)).FullName;
            await ChooseNativeIntakeFolderAsync(window, model, dialogs, intake);

            File.WriteAllText(addedSource, "Invalid test PDF");
            dialogs.SelectedFiles = [addedSource];
            await ((IAsyncRelayCommand)Control<Button>(window, "AddFilesButton").Command!).ExecuteAsync(null);
            Require(model.ImportFailures.Single().SourcePath == addedSource, "The added file did not report its error.");

            dialogs.FolderPath = incoming;
            await model.StartFolderWatchCommand.ExecuteAsync(null);
            Require(model.IsWatchingFolder, $"The retry fixture did not start watching. {model.Status}");
            File.WriteAllText(watchedSource, "Invalid test scan");
            await UntilAsync(() => !model.IsBusy && model.ImportFailures.Count == 2, () => model.Status);
            await model.StopFolderWatchCommand.ExecuteAsync(null);

            dialogs.FolderPath = blankLibrary;
            await model.ChooseLibraryCommand.ExecuteAsync(null);
            Require(model.LibraryPath == blankLibrary && model.ImportFailures.Count == 0,
                "The retry report followed the window to a different library.");
            dialogs.FolderPath = library;
            await model.ChooseLibraryCommand.ExecuteAsync(null);
            var errors = Control<Button>(window, "ImportErrorsButton");
            Require(model.LibraryPath == library && errors.IsEffectivelyVisible && model.ImportFailures.Count == 2 &&
                    model.ImportFailures.Any(failure => failure.SourcePath == addedSource) &&
                    model.ImportFailures.Any(failure => failure.SourcePath == watchedSource),
                $"Reopening the retry library did not restore both errors. {model.Status}");

            string fixtures = Path.GetDirectoryName(fixture)!;
            File.Copy(Path.Combine(fixtures, "Lease Renewal.pdf"), addedSource, overwrite: true);
            File.Copy(Path.Combine(fixtures, "Match Ledger.pdf"), watchedSource, overwrite: true);
            dialogs.ImportErrorsChoice = ImportErrorsAction.Retry;
            await ((IAsyncRelayCommand)errors.Command!).ExecuteAsync(null);
            Require(model.Status == "2 documents added." && model.ImportFailures.Count == 0 &&
                    File.Exists(Path.Combine(intake, addedFilename)) && File.Exists(Path.Combine(pdfs, watchedFilename)) &&
                    File.Exists(addedSource) && File.Exists(watchedSource),
                "Retry did not preserve the filing choice for both sources.");
            model.SelectedDocument = model.Documents.Single(document => document.RelativePath == $"{intakeName}/{addedFilename}");
            await UntilAsync(() => model.Preview is not null, () => model.PreviewMessage);
            await CaptureAsync(window, directory, "retry-filing.png");
        }
        finally
        {
            await model.StopFolderWatchCommand.ExecuteAsync(null);
            dialogs.FolderPath = previousFolder;
            dialogs.SelectedFiles = previousFiles;
        }
    }
}
