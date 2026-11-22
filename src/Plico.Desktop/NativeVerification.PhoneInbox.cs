using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifyPhoneInboxAsync(Window window, InboxViewModel model, FixtureDialogs dialogs,
        string fixture, string evidenceDirectory)
    {
        string scenario = Directory.CreateDirectory(Path.Combine(model.LibraryPath, "Phone inbox verification")).FullName;
        string library = Directory.CreateDirectory(Path.Combine(scenario, "Library")).FullName;
        string phone = Directory.CreateDirectory(Path.Combine(scenario, "From phone")).FullName;
        string scanner = Directory.CreateDirectory(Path.Combine(scenario, "From scanner")).FullName;
        string alternate = Directory.CreateDirectory(Path.Combine(scenario, "Alternate library")).FullName;
        string fixtures = Path.GetDirectoryName(fixture)!;

        dialogs.FolderPath = library;
        await model.ChooseLibraryCommand.ExecuteAsync(null);
        dialogs.FolderPath = scanner;
        await model.StartFolderWatchCommand.ExecuteAsync(null);
        var phoneButton = SettingsControl<Button>(window, "IntakeGroup", "PhoneInboxButton");
        Require(phoneButton.IsEffectivelyVisible && phoneButton.IsEnabled && phoneButton.Flyout is MenuFlyout,
            "Settings did not offer the phone inbox.");
        var menu = (MenuFlyout)phoneButton.Flyout!;
        menu.ShowAt(phoneButton);
        var choose = PhoneInboxCommand(menu, "ChoosePhoneInboxFolder");
        var stop = PhoneInboxCommand(menu, "StopPhoneInbox");
        var catchUp = PhoneInboxCommand(menu, "CatchUpPhoneInbox");
        menu.Hide();
        CloseSettings(window);

        dialogs.FolderPath = phone;
        await choose.ExecuteAsync(null);
        Require(model.IsWatchingFolder && model.IsWatchingPhoneInbox, $"Both watches did not start. {model.Status}");
        string phoneTransfer = Path.Combine(phone, "Phone receipt.pdf");
        File.WriteAllBytes(phoneTransfer, []);
        await catchUp.ExecuteAsync(null);
        Require(model.Status == "No additional files to check." && !model.HasImportFailures,
            $"The phone inbox tried to import an empty transfer. {model.Status}");

        string scannerTransfer = Path.Combine(scanner, "Scanner letter.pdf");
        File.Copy(Path.Combine(fixtures, "Lease Renewal.pdf"), scannerTransfer);
        File.Copy(Path.Combine(fixtures, "Match Ledger.pdf"), phoneTransfer, overwrite: true);
        await UntilAsync(() => !model.IsBusy && model.InboxCount == 2 && model.Documents.Count == 2, () => model.Status);
        Require(!model.HasImportFailures && File.Exists(scannerTransfer) && File.Exists(phoneTransfer),
            "A watched arrival failed or changed its source.");
        await CaptureAsync(window, evidenceDirectory, "phone-inbox.png");

        await stop.ExecuteAsync(null);
        Require(model.IsWatchingFolder && !model.IsWatchingPhoneInbox, "Stopping the phone inbox changed scanner intake.");
        await catchUp.ExecuteAsync(null);
        Require(model.IsWatchingPhoneInbox, $"The phone inbox did not resume. {model.Status}");
        dialogs.FolderPath = alternate;
        await model.ChooseLibraryCommand.ExecuteAsync(null);
        Require(!model.IsWatchingFolder && !model.IsWatchingPhoneInbox && !model.CanCatchUpPhoneInbox,
            "Switching libraries retained a watch from the previous library.");
    }

    private static IAsyncRelayCommand PhoneInboxCommand(MenuFlyout menu, string name) =>
        menu.Items.OfType<MenuItem>().Single(item => item.Name == name).Command as IAsyncRelayCommand
        ?? throw new InvalidOperationException($"The phone inbox command {name} was unavailable.");
}
