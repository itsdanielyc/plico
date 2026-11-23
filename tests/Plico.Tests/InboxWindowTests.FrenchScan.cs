using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

/// <summary>A scan's message in French is a whole French sentence about the scan, never the Scan button's verb.</summary>
public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task AScanSaysWhatItAddedInFrench()
    {
        var token = TestContext.Current.CancellationToken;
        DesktopText.SetLanguage(InterfaceLanguage.French);
        await using var scanner = await NetworkScannerTests.StartInterruptedFeederAsync(token);
        var device = await Scanners.ConnectAsync(new Uri(scanner.Urls.Single()).Authority, token);
        using var temporary = new TemporaryLibrary();
        var dialogs = new FixtureDialogs
        {
            ScannerChoice = new ScanRequest(device, ScanFeed.Feeder, ScanPaper.Letter),
            // The feeder stops after two pages, and the person keeps them.
            PartialScanChoice = (_, _) => Task.FromResult(true)
        };
        using var model = new InboxViewModel(dialogs);
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            // A new library opens on the welcome page, where Scan is one of the two ways in.
            ClickAt(window, Assert.IsType<Button>(window.FindControl<Button>("WelcomeScanButton")));
            await WaitForAsync(() => !model.IsBusy && model.SelectedDocument is not null);
            await WaitForPreviewAsync(model);

            Assert.Equal("Numérisation partielle ajoutée à la boîte de réception.", model.Status);
            Assert.Equal("Numérisation partielle ajoutée à la boîte de réception", model.ToastTitle);
            Assert.DoesNotContain("Numériser", model.ToastStatus, StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
            DesktopText.SetLanguage(InterfaceLanguage.English);
        }
    }
}
