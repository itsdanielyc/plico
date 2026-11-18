using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Input;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task PhoneInboxKeepsItsOwnWatchAndLibraryPreference()
    {
        using var library = new TemporaryLibrary();
        using var otherLibrary = new TemporaryLibrary();
        using var scannerOutput = new TemporaryLibrary();
        using var phoneInbox = new TemporaryLibrary();
        using var settings = new TemporaryLibrary();
        var dialogs = new FixtureDialogs { FolderPath = scannerOutput.Path };
        using var model = new InboxViewModel(dialogs, watchPreference: new WatchFolderPreference(settings.Path));
        model.InitializeLibrary(() => library.Path);
        var window = new MainWindow { DataContext = model, Width = 900, Height = 590 };
        window.Show();
        try
        {
            var phoneButton = Assert.IsType<Button>(window.FindControl<Button>("PhoneInboxButton"));
            var menu = Assert.IsType<MenuFlyout>(phoneButton.Flyout);
            menu.ShowAt(phoneButton);
            var choose = Assert.Single(menu.Items.OfType<MenuItem>(), item => item.Name == "ChoosePhoneInboxFolder");
            var catchUp = Assert.Single(menu.Items.OfType<MenuItem>(), item => item.Name == "CatchUpPhoneInbox");
            var stop = Assert.Single(menu.Items.OfType<MenuItem>(), item => item.Name == "StopPhoneInbox");
            var chooseCommand = Assert.IsAssignableFrom<IAsyncRelayCommand>(choose.Command);
            var catchUpCommand = Assert.IsAssignableFrom<IAsyncRelayCommand>(catchUp.Command);
            var stopCommand = Assert.IsAssignableFrom<IAsyncRelayCommand>(stop.Command);
            menu.Hide();

            await model.StartFolderWatchCommand.ExecuteAsync(null);
            await chooseCommand.ExecuteAsync(null);
            Assert.Contains("already being watched", model.Status, StringComparison.OrdinalIgnoreCase);
            Assert.False(model.IsWatchingPhoneInbox);

            dialogs.FolderPath = phoneInbox.Path;
            await chooseCommand.ExecuteAsync(null);
            Assert.True(model.IsWatchingFolder && model.IsWatchingPhoneInbox);

            string fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
            File.Copy(Path.Combine(fixtures, "Lease Renewal.pdf"), Path.Combine(scannerOutput.Path, "Scanner scan.pdf"));
            File.Copy(Path.Combine(fixtures, "Match Ledger.pdf"), Path.Combine(phoneInbox.Path, "Phone scan.pdf"));
            await WaitForAsync(() => !model.IsBusy && model.InboxCount == 2);
            await stopCommand.ExecuteAsync(null);
            Assert.True(model.IsWatchingFolder && !model.IsWatchingPhoneInbox);
            menu.ShowAt(phoneButton);
            Assert.Equal("Resume and catch up", catchUp.Header);
            menu.Hide();
            await catchUpCommand.ExecuteAsync(null);
            Assert.True(model.IsWatchingPhoneInbox);

            dialogs.FolderPath = otherLibrary.Path;
            await model.ChooseLibraryCommand.ExecuteAsync(null);
            Assert.False(model.IsWatchingFolder || model.IsWatchingPhoneInbox || model.CanCatchUpPhoneInbox);
            dialogs.FolderPath = library.Path;
            await model.ChooseLibraryCommand.ExecuteAsync(null);
            Assert.True(model.CanCatchUpPhoneInbox);
            Assert.Equal(phoneInbox.Path, model.PhoneInboxFolder);

            menu.ShowAt(phoneButton);
            var startup = Assert.Single(menu.Items.OfType<MenuItem>(), item => item.Name == "PhoneCatchUpOnOpen");
            Assert.True(startup.IsVisible);
            startup.SetCurrentValue(MenuItem.IsCheckedProperty, true);
            menu.Hide();
            Assert.True(model.PhoneCatchUpOnOpen);
            Assert.False(model.CatchUpOnOpen);
            await model.CatchUpWhenWindowOpensAsync();
            Assert.True(model.IsWatchingPhoneInbox);
            Assert.False(model.IsWatchingFolder);
        }
        finally
        {
            await model.PrepareCloseAsync();
            if (model.SelectedDocument is not null) await WaitForPreviewAsync(model);
            window.Close();
        }
    }
}
