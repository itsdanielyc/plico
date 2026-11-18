using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifyFolderSearchPickerAsync(Window window, InboxViewModel model, FixtureDialogs dialogs, string directory)
    {
        var search = Control<TextBox>(window, "SearchBox");
        search.SetCurrentValue(TextBox.TextProperty, $"{LetterKeyword} folder:Filed");
        await UntilAsync(() => model.ResultCount != "Searching…", () => model.Status);
        var choose = Control<Button>(window, "ChooseSearchFolderButton");
        Require(choose.IsVisible && choose.IsEnabled && choose.Command is IAsyncRelayCommand,
            "The native search header did not offer folder selection.");
        string? originalFolder = dialogs.FolderPath;
        string pdfs = Path.Combine(model.LibraryPath, "PDFs");
        dialogs.FolderPath = Path.Combine(pdfs, "Exchange check");
        try
        {
            await ((IAsyncRelayCommand)choose.Command!).ExecuteAsync(null);
        }
        finally
        {
            dialogs.FolderPath = originalFolder;
        }

        Require(dialogs.LastFolderStart == pdfs && model.Query == $"{LetterKeyword} folder:\"Exchange check\"" &&
                model.Documents is [{ RelativePath: "Exchange check/Beta exchange.pdf" } document] && document.Tags.Contains("Exchange check"),
            "The folder picker did not retain the text query and scope it to the selected library folder.");
        await UntilAsync(() => model.Preview is not null, () => model.PreviewMessage);
        await CaptureAsync(window, directory, "folder-picker-results.png");
    }
}
