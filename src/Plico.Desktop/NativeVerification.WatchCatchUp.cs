using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
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
