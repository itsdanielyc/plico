using Avalonia.Controls;
using Avalonia.Interactivity;
using Plico.Core;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifyManualDuplexDialogAsync(Window window, string evidenceDirectory)
    {
        var dialog = new ManualDuplexDialog(new ManualDuplexPrompt(2));
        var choice = dialog.ShowDecisionAsync(window, CancellationToken.None);
        try
        {
            Require(Control<TextBlock>(dialog, "FrontSidesMessage").Text == "2 front sides saved",
                "Manual duplex did not display the number of saved front sides.");
            Require(Control<Button>(dialog, "ScanBacksButton").IsEffectivelyVisible, "The back-side scan action is not visible.");
            await CaptureAsync(dialog, evidenceDirectory, "manual-duplex.png");
            Control<Button>(dialog, "LaterButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(await choice == ManualDuplexDecision.SaveForLater, "Finishing later did not preserve the front pass.");
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close(ManualDuplexDecision.SaveForLater);
            await choice;
        }
    }
}
