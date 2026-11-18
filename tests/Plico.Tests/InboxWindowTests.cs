using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using NAPS2.Scan;
using Plico.Desktop;
using Plico.Core;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Plico.Tests.HeadlessApplication))]

namespace Plico.Tests;

public static class HeadlessApplication
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).WithInterFont();
}

public sealed class InboxWindowTests
{
    [AvaloniaFact]
    public void OpensWithVisibleInboxHeading()
    {
        using var temporary = new TemporaryLibrary();
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            var heading = Assert.IsType<TextBlock>(window.FindControl<TextBlock>("InboxHeading"));
            Assert.Equal("Inbox", heading.Text);
            Assert.True(heading.IsVisible);
            Assert.True(heading.Bounds.Width > 0);
            var search = Assert.IsType<TextBox>(window.FindControl<TextBox>("SearchBox"));
            var add = Assert.IsType<Button>(window.FindControl<Button>("AddFilesButton"));
            Assert.True(search.Bounds.Width > add.Bounds.Width);
            Assert.True(add.IsEnabled);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ScannerPickerSurfacesMissingDriverAndDisablesCapture()
    {
        const string missingDriver = "SANE is unavailable.";
        var dialog = new ScanDialog(_ => Task.FromResult(new ScanDiscovery([], [missingDriver])));
        dialog.Show();
        try
        {
            var capture = Assert.IsType<Button>(dialog.FindControl<Button>("StartButton"));
            var refresh = Assert.IsType<Button>(dialog.FindControl<Button>("RefreshButton"));
            var status = Assert.IsType<TextBlock>(dialog.FindControl<TextBlock>("ScannerStatus"));
            Assert.Equal(missingDriver, status.Text);
            Assert.False(capture.IsEnabled);
            Assert.True(refresh.IsEnabled);
        }
        finally
        {
            dialog.Close();
        }
    }

    [AvaloniaFact]
    public void ManualScannerStaysSelectedWhenDiscoveryIsRefreshed()
    {
        const string address = "192.0.2.54";
        var device = new ScannerDevice(new ScanDevice(Driver.Escl, $"http://{address}/eSCL", "Office scanner"));
        string? connectedAddress = null;
        var dialog = new ScanDialog(_ => Task.FromResult(new ScanDiscovery([], [])), (entered, _) =>
        {
            connectedAddress = entered;
            return Task.FromResult(device);
        });
        dialog.Show();
        try
        {
            var addressBox = Assert.IsType<TextBox>(dialog.FindControl<TextBox>("AddressBox"));
            var connect = Assert.IsType<Button>(dialog.FindControl<Button>("ConnectButton"));
            var refresh = Assert.IsType<Button>(dialog.FindControl<Button>("RefreshButton"));
            var start = Assert.IsType<Button>(dialog.FindControl<Button>("StartButton"));
            var devices = Assert.IsType<ComboBox>(dialog.FindControl<ComboBox>("DeviceBox"));
            addressBox.Text = address;
            connect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(address, connectedAddress);
            Assert.Equal(device, devices.SelectedItem);
            Assert.True(start.IsEnabled);
        }
        finally
        {
            dialog.Close();
        }
    }

    [AvaloniaFact]
    public void SavedScannerCanBeSelectedAndForgottenWithoutConnectingFirst()
    {
        using var directory = new TemporaryLibrary();
        var settings = new SavedScannerStore(directory.Path);
        var scanner = Scanners.FromAddress("http://scanner.test:8080", "Saved scanner");
        settings.AddOrUpdate(scanner);
        int connections = 0;
        var dialog = new ScanDialog(_ => Task.FromResult(new ScanDiscovery([], [])), (_, _) =>
        {
            connections++;
            return Task.FromResult(scanner);
        }, settings);
        dialog.Show();
        try
        {
            var devices = Assert.IsType<ComboBox>(dialog.FindControl<ComboBox>("DeviceBox"));
            var forget = Assert.IsType<Button>(dialog.FindControl<Button>("ForgetButton"));
            var start = Assert.IsType<Button>(dialog.FindControl<Button>("StartButton"));
            Assert.Equal(scanner, devices.SelectedItem);
            Assert.True(forget.IsVisible);
            Assert.True(start.IsEnabled);

            forget.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(0, connections);
            Assert.Empty(settings.Load());
            Assert.Null(devices.SelectedItem);
            Assert.False(start.IsEnabled);
        }
        finally
        {
            dialog.Close();
        }
    }

    [AvaloniaFact]
    public void InvalidSavedAddressesLeaveTheConnectedScannerAvailable()
    {
        using var directory = new TemporaryLibrary();
        string settingsFile = Path.Combine(directory.Path, "scanners.json");
        File.WriteAllText(settingsFile, "{");
        var scanner = Scanners.FromAddress("desk-scanner.test", "Desk scanner");
        var dialog = new ScanDialog(_ => Task.FromResult(new ScanDiscovery([], [])), (_, _) => Task.FromResult(scanner),
            new SavedScannerStore(directory.Path));
        dialog.Show();
        try
        {
            var addressBox = Assert.IsType<TextBox>(dialog.FindControl<TextBox>("AddressBox"));
            var connect = Assert.IsType<Button>(dialog.FindControl<Button>("ConnectButton"));
            var start = Assert.IsType<Button>(dialog.FindControl<Button>("StartButton"));
            var status = Assert.IsType<TextBlock>(dialog.FindControl<TextBlock>("ScannerStatus"));
            addressBox.Text = "desk-scanner.test";
            connect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.True(start.IsEnabled);
            Assert.Contains("Connected to Desk scanner.", status.Text);
            Assert.Equal("{", File.ReadAllText(settingsFile));
        }
        finally
        {
            dialog.Close();
        }
    }

    [AvaloniaFact]
    public void TitleEditorRejectsAnEmptyTitleAndStaysOpen()
    {
        var dialog = new DocumentTitleDialog("Existing title");
        dialog.Show();
        try
        {
            var titleBox = Assert.IsType<TextBox>(dialog.FindControl<TextBox>("TitleBox"));
            var save = Assert.IsType<Button>(dialog.FindControl<Button>("SaveTitleButton"));
            var validation = Assert.IsType<TextBlock>(dialog.FindControl<TextBlock>("ValidationMessage"));
            Assert.Equal("Existing title", titleBox.Text);
            titleBox.Text = "";
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.True(dialog.IsVisible);
            Assert.True(validation.IsVisible);
            Assert.Contains("Enter a title", validation.Text);
        }
        finally
        {
            dialog.Close();
        }
    }

    [AvaloniaFact]
    public async Task DocumentPathOpensTheContainingFolderForANestedPdf()
    {
        using var temporary = new TemporaryLibrary();
        string expectedFolder;
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            expectedFolder = Directory.CreateDirectory(Path.Combine(library.PdfDirectory, "Home receipts")).FullName;
            string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "Lease Renewal.pdf");
            File.Copy(fixture, Path.Combine(expectedFolder, "Lease.pdf"));
            library.Refresh(TestContext.Current.CancellationToken);
        }
        var dialogs = new FixtureDialogs();
        using var model = new InboxViewModel(dialogs);
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var location = Assert.IsType<Button>(window.FindControl<Button>("PdfLocationButton"));
            Assert.True(location.IsVisible);
            Assert.NotNull(location.Command);
            location.Command.Execute(null);

            Assert.Equal(expectedFolder, dialogs.LastOpenedPath);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SelectShownCanReviewSeveralDocumentsAtOnce()
    {
        using var directory = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(directory.Path))
        {
            BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        }
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => directory.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var select = Assert.IsType<Button>(window.FindControl<Button>("SelectShownButton"));
            select.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var review = Assert.IsType<Button>(window.FindControl<Button>("BatchReviewButton"));
            Assert.True(review.IsVisible);
            var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(review.Command);
            await command.ExecuteAsync(null);

            Assert.Empty(model.Documents);
            Assert.Equal("2 documents marked reviewed.", model.Status);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ApplyingABatchTagRetainsTheSelectionForReview()
    {
        using var directory = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(directory.Path))
        {
            BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        }
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => directory.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var select = Assert.IsType<Button>(window.FindControl<Button>("SelectShownButton"));
            select.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var tag = Assert.IsType<TextBox>(window.FindControl<TextBox>("BatchTagBox"));
            tag.SetCurrentValue(TextBox.TextProperty, "Property");
            var apply = Assert.IsType<Button>(window.FindControl<Button>("ApplyBatchTagButton"));
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => model.Status == "Tag applied to 2 documents." && model.HasBatchSelection);
            await WaitForPreviewAsync(model);

            Assert.All(model.Documents, document => Assert.Contains("Property", document.Tags));
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task WaitForPreviewAsync(InboxViewModel model)
        => await WaitForAsync(() => model.Preview is not null || model.HasPreviewMessage);

    [AvaloniaFact]
    public void LanguageSelectorLoadsAndUpdatesTheLocalChoice()
    {
        using var directory = new TemporaryLibrary();
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => directory.Path);
        OcrLanguage? saved = null;
        model.InitializeRecognitionLanguage(() => OcrLanguage.German, language => saved = language);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            var languages = Assert.IsType<ComboBox>(window.FindControl<ComboBox>("OcrLanguageBox"));
            Assert.Equal(OcrLanguage.German, languages.SelectedItem);
            languages.SetCurrentValue(ComboBox.SelectedItemProperty, OcrLanguage.French);
            Assert.Equal(OcrLanguage.French, saved);
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
        }
    }
}
