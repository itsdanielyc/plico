using Avalonia.Controls;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifyOcrProcessingChoicesAsync(Window window, InboxViewModel model, string evidenceDirectory)
    {
        var button = Control<Button>(window, "OcrProcessingButton");
        Require(button.IsEnabled && button.Flyout is MenuFlyout, "The OCR settings menu is unavailable.");
        var menu = (MenuFlyout)button.Flyout!;
        menu.ShowAt(button);
        try
        {
            var rotation = menu.Items.OfType<MenuItem>().Single(item => item.Name == "OrientPagesChoice");
            var engine = menu.Items.OfType<MenuItem>().Single(item => item.Name == "RapidOcrChoice");
            Require(engine.IsEnabled == model.CanUseRapidOcr && rotation.IsEnabled, "OCR availability did not match this platform.");
            rotation.SetCurrentValue(MenuItem.IsCheckedProperty, true);
            Require(model.AutoOrientPages, "The OCR menu did not enable page orientation.");
            rotation.SetCurrentValue(MenuItem.IsCheckedProperty, false);
            await CaptureAsync(window, evidenceDirectory, "ocr-processing.png");
        }
        finally
        {
            menu.Hide();
        }
    }
}
