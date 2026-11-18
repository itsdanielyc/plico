using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifyAdditionalIntakeAsync(MainWindow window, InboxViewModel model, FixtureDialogs dialogs,
        string fixture, string evidenceDirectory)
    {
        string markdown = Path.Combine(model.LibraryPath, "Import formats verification.md");
        File.WriteAllText(markdown, "# Native paperwork\n\n**FORMATS470** passed through the file picker.");
        string image = Path.Combine(Path.GetDirectoryName(fixture)!, "Phone Receipt.heic");
        var previous = dialogs.SelectedFiles;
        try
        {
            dialogs.SelectedFiles = [image, markdown];
            var import = (IAsyncRelayCommand)Control<Button>(window, "AddFilesButton").Command!;
            await import.ExecuteAsync(null);
            Control<TextBox>(window, "SearchBox").SetCurrentValue(TextBox.TextProperty, "FORMATS470");
            await UntilAsync(() => model.SelectedDocument?.Title == "Import formats verification" && model.Preview is not null,
                () => model.Status);
            Require(!model.HasImportFailures && File.Exists(markdown) && File.Exists(image), "A new intake format failed.");
            await CaptureAsync(window, evidenceDirectory, "additional-intake-formats.png");
        }
        finally
        {
            dialogs.SelectedFiles = previous;
        }
    }
}
