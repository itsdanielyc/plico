using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
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
}
