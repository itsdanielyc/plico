using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private const string InterruptedScannerVariable = "PLICO_VERIFY_INTERRUPTED_SCANNER";
    private const int InterruptedScannerPages = 2;

    private static async Task<string> VerifyScanRecoveryAsync(MainWindow window, InboxViewModel model,
        FixtureDialogs dialogs, string directory)
    {
        string? endpoint = Environment.GetEnvironmentVariable(InterruptedScannerVariable);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            var fixture = new ScanInterruption(InterruptedScannerPages, "The feeder reported a paper jam.");
            Require(!await ChooseCapturedPagesAsync(window, fixture, directory, save: false, CancellationToken.None),
                "Discarding the captured pages did not close the scanner prompt.");
            return "Verified the native scan recovery prompt.";
        }

        Require(Uri.TryCreate(endpoint, UriKind.Absolute, out var address) && address.IsLoopback,
            "Use a loopback endpoint for the interrupted scanner fixture.");
        var device = await Scanners.ConnectAsync(endpoint);
        dialogs.ScannerChoice = new ScanRequest(device, ScanFeed.Feeder, ScanPaper.Letter);
        dialogs.PartialScanChoice = (interruption, token) => ChooseCapturedPagesAsync(window, interruption, directory, save: true, token);
        try
        {
            var button = Control<Button>(window, "ScanButton");
            Require(button.IsEnabled && button.Command is IAsyncRelayCommand, "The Scan action is unavailable.");
            await ((IAsyncRelayCommand)button.Command!).ExecuteAsync(button.CommandParameter);
        }
        finally
        {
            dialogs.ScannerChoice = null;
            dialogs.PartialScanChoice = null;
        }

        var saved = model.SelectedDocument;
        Require(model.Status == "Partial scan added to inbox." && saved is { PageCount: InterruptedScannerPages, HasText: true } &&
                saved.Title.EndsWith("(partial)", StringComparison.Ordinal), model.Status);
        await UntilAsync(() => model.Preview is not null, () => model.PreviewMessage);
        await CaptureAsync(window, directory, "partial-scan-inbox.png");
        Control<TextBox>(window, "SearchBox").SetCurrentValue(TextBox.TextProperty, ReceiptKeyword);
        await UntilAsync(() => model.Documents.Any(item => item.Id == saved!.Id &&
            item.Match.Contains(ReceiptKeyword, StringComparison.OrdinalIgnoreCase)), () => model.Status);
        return "Verified the interrupted eSCL job saved a searchable two-page PDF through the native Scan action.";
    }

    private static async Task<bool> ChooseCapturedPagesAsync(Window owner, ScanInterruption interruption, string directory,
        bool save, CancellationToken cancellationToken)
    {
        var dialog = new ScanInterruptedDialog(interruption);
        var choice = dialog.ShowDecisionAsync(owner, cancellationToken);
        try
        {
            Require(interruption.CapturedPages == InterruptedScannerPages &&
                    Control<TextBlock>(dialog, "CapturedPageMessage").Text == "The scanner captured 2 pages.",
                "The recovery dialog did not report the delivered pages.");
            var saveButton = Control<Button>(dialog, "SavePagesButton");
            var discardButton = Control<Button>(dialog, "DiscardPagesButton");
            Require(saveButton.IsEffectivelyVisible && discardButton.IsEffectivelyVisible, "The recovery decision was unavailable.");
            await CaptureAsync(dialog, directory, "scan-interrupted.png");
            (save ? saveButton : discardButton).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return await choice;
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close(false);
            await choice;
        }
    }
}
