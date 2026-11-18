using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using CommunityToolkit.Mvvm.Input;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifyWatchCatchUpOnOpenAsync(Window window, InboxViewModel model, FixtureDialogs dialogs,
        string fixture, string directory)
    {
        const string missedName = "Saved before reopen.pdf";
        string library = model.LibraryPath;
        string source = model.RememberedWatchFolder ?? throw new InvalidOperationException("The watch fixture has no saved folder.");
        var check = Control<CheckBox>(window, "CatchUpOnOpenCheckBox");
        Require(check.IsEffectivelyVisible && check.IsEnabled && check.IsChecked == false && !model.IsWatchingFolder,
            "The paused watch did not offer the opt-in startup choice.");
        check.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        Require(model.CatchUpOnOpen && !model.IsWatchingFolder, "The startup preference did not save for the current library.");

        long inboxCount = model.InboxCount;
        string? originalFolder = dialogs.FolderPath;
        try
        {
            string otherLibrary = Directory.CreateDirectory(Path.Combine(library, "Other library")).FullName;
            dialogs.FolderPath = otherLibrary;
            await model.ChooseLibraryCommand.ExecuteAsync(null);
            Require(model.LibraryPath == otherLibrary && !model.IsWatchingFolder && !model.CatchUpOnOpen,
                "The startup setting followed the window to another library.");

            string missed = Path.Combine(source, missedName);
            File.Copy(Path.Combine(Path.GetDirectoryName(fixture)!, "Match Ledger.pdf"), missed);
            // Keep the scan distinct from both ledger copies imported earlier in the journey.
            File.AppendAllText(missed, "\n\n \n\n");
            dialogs.FolderPath = library;
            await model.ChooseLibraryCommand.ExecuteAsync(null);
            await UntilAsync(() => !model.IsBusy && model.IsWatchingFolder && model.InboxCount == inboxCount + 1 &&
                model.Status == "1 document added. 2 documents already in the library.", () => model.Status);
            Require(model.LibraryPath == library && check.IsChecked == true && File.Exists(missed) &&
                    File.Exists(Path.Combine(library, "PDFs", missedName)), "Opening the enabled library did not import the saved scan.");
            await UntilAsync(() => model.Preview is not null, () => model.PreviewMessage);
            await CaptureAsync(window, directory, "watch-catch-up-on-open.png");
        }
        finally
        {
            dialogs.FolderPath = originalFolder;
            await model.StopFolderWatchCommand.ExecuteAsync(null);
        }
    }

    private static async Task VerifyWatchCatchUpAsync(Window window, InboxViewModel model, string fixture, string directory)
    {
        const string missedName = "Saved while closed.pdf";
        string folder = model.RememberedWatchFolder ?? throw new InvalidOperationException("The watch fixture has no saved folder.");
        Require(!model.IsWatchingFolder, "The watch fixture should be paused before the catch-up check.");
        string source = Path.Combine(folder, missedName);
        File.Copy(Path.Combine(Path.GetDirectoryName(fixture)!, "Match Ledger.pdf"), source);
        // The same ledger is imported earlier in the journey. Trailing whitespace gives this fixture distinct source bytes.
        File.AppendAllText(source, "\n \n \n");

        var action = Control<Button>(window, "ImportWatchFolderFilesButton");
        Require(action.IsEffectivelyVisible && action.IsEnabled && Equals(action.Content, "Resume and catch up") &&
                action.Command is IAsyncRelayCommand, "The paused watch did not offer catch-up.");
        long inboxCount = model.InboxCount;
        long? openDocument = model.SelectedDocument?.Id;
        string query = model.Query;
        try
        {
            await ((IAsyncRelayCommand)action.Command!).ExecuteAsync(action.CommandParameter);
            await UntilAsync(() => !model.IsBusy && model.Status == "1 document added. 1 document already in the library.", () => model.Status);
            Require(model.IsWatchingFolder && model.InboxCount == inboxCount + 1 && model.Query == query &&
                    model.SelectedDocument?.Id == openDocument && Equals(action.Content, "Import existing files") &&
                    File.Exists(Path.Combine(model.LibraryPath, "PDFs", missedName)) && File.Exists(source),
                "The watch did not catch up without changing the current review view or consuming its source.");
            await UntilAsync(() => model.Preview is not null, () => model.PreviewMessage);
            await CaptureAsync(window, directory, "watch-catch-up.png");
        }
        finally
        {
            await model.StopFolderWatchCommand.ExecuteAsync(null);
        }
    }
}
