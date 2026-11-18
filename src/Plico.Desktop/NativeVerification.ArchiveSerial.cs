using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifyArchiveSerialDialogAsync(Window window, string evidenceDirectory)
    {
        var dialog = new ArchiveSerialDialog(ArchiveSerialAction.Labels, 470);
        var choice = dialog.ShowDialog<ArchiveSerialInput?>(window);
        try
        {
            Require(Control<TextBox>(dialog, "SerialInput").Text == "470", "The label dialog did not show its starting number.");
            Require(Control<NumericUpDown>(dialog, "LabelCount").IsEffectivelyVisible, "The label count was unavailable.");
            await CaptureAsync(dialog, evidenceDirectory, "archive-labels.png");
            Control<Button>(dialog, "AcceptSerial").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(await choice is { Number: 470, LabelCount: 30 }, "The print dialog did not retain its range.");
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close();
            await choice;
        }
    }
}
