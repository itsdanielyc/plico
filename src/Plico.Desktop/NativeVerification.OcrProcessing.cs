using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    /// <summary>The engine and orientation choices are switches in Settings › Text recognition.</summary>
    private static async Task VerifyOcrProcessingChoicesAsync(Window window, InboxViewModel model, string evidenceDirectory)
    {
        var engine = SettingsControl<CheckBox>(window, "RecognitionGroup", "RapidOcrChoice");
        var settings = (Window)TopLevel.GetTopLevel(engine)!;
        var rotation = Control<CheckBox>(settings, "OrientPagesChoice");
        try
        {
            Require(engine.IsEnabled == model.CanUseRapidOcr && rotation.IsEnabled && rotation.IsEffectivelyVisible,
                "OCR availability did not match this platform.");
            rotation.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            Require(model.AutoOrientPages, "The orientation switch did not enable page orientation.");
            await CaptureAsync(settings, evidenceDirectory, "ocr-processing.png");
            rotation.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
            Require(!model.AutoOrientPages, "The orientation switch did not turn page orientation off again.");
        }
        finally
        {
            CloseSettings(window);
        }
    }
}
