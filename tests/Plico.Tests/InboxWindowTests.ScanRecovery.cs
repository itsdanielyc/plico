using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task ScanButtonLetsTheUserSavePagesAfterAFeederError()
    {
        var token = TestContext.Current.CancellationToken;
        await using var scanner = await NetworkScannerTests.StartInterruptedFeederAsync(token);
        var device = await Scanners.ConnectAsync(new Uri(scanner.Urls.Single()).Authority, token);
        using var temporary = new TemporaryLibrary();
        var dialogs = new FixtureDialogs { ScannerChoice = new ScanRequest(device, ScanFeed.Feeder, ScanPaper.Letter) };
        using var model = new InboxViewModel(dialogs);
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            dialogs.PartialScanChoice = (interruption, cancellation) =>
            {
                Assert.True(Dispatcher.UIThread.CheckAccess());
                var dialog = new ScanInterruptedDialog(interruption);
                var confirm = dialog.ShowDecisionAsync(window, cancellation);
                Assert.Equal("2 pages captured", dialog.FindControl<TextBlock>("CapturedPageMessage")?.Text);
                var save = Assert.IsType<Button>(dialog.FindControl<Button>("SavePagesButton"));
                Assert.True(save.IsEffectivelyVisible);
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return confirm;
            };
            var scan = Assert.IsType<Button>(window.FindControl<Button>("ScanButton"));
            var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(scan.Command);
            await command.ExecuteAsync(scan.CommandParameter);

            Assert.Equal("Partial scan added to the Inbox.", model.Status);
            await WaitForPreviewAsync(model);
            Assert.NotNull(model.Preview);
            var saved = Assert.IsType<DocumentSummary>(model.SelectedDocument);
            Assert.EndsWith("(partial)", saved.Title);
            Assert.Equal(2, saved.PageCount);
            Assert.True(saved.HasText);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CancellingAnOpenScanRecoveryClosesItsDialog()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var owner = new Window();
        owner.Show();
        var dialog = new ScanInterruptedDialog(new ScanInterruption(1, "Paper jam"));
        var result = dialog.ShowDecisionAsync(owner, cancellation.Token);
        try
        {
            Assert.True(dialog.IsVisible);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => result);
            Assert.False(dialog.IsVisible);
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close(false);
            owner.Close();
        }
    }
}
