using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed class ScanOptionsTests
{
    [Fact]
    public void InvalidDeviceOptionsFallBackToTheExistingDefaults()
    {
        using var directory = new TemporaryLibrary();
        var first = Scanners.FromAddress("first-scanner.test", "Shared model");
        var second = Scanners.FromAddress("second-scanner.test", "Shared model");
        var preference = new ScanOptionsPreference(directory.Path);
        var defaults = new CaptureSettings(ScanFeed.Feeder, ScanPaper.Letter, ScanResolution.Quick);
        var deviceOptions = new CaptureSettings(ScanFeed.Duplex, ScanPaper.A4, ScanResolution.Detailed);
        preference.Save(defaults);
        preference.Save(deviceOptions, first);
        preference.Save(deviceOptions, second);
        string damagedPath = Path.Combine(directory.Path, $"scan-options-{first.SettingsKey}.json");
        const string damaged = "{\"Feed\":\"Unknown\"}";
        File.WriteAllText(damagedPath, damaged);

        Assert.Equal(defaults, preference.Load(first));
        Assert.Equal(deviceOptions, preference.Load(second));
        Assert.Equal(damaged, File.ReadAllText(damagedPath));
    }

    [Fact]
    public void UnreadableOptionsLeaveTheRegionalDefaultsUsable()
    {
        using var directory = new TemporaryLibrary();
        string file = Path.Combine(directory.Path, "scan-options.json");
        const string invalid = """{"Feed":"Feeder","Paper":"Unknown","Resolution":"Detailed"}""";
        File.WriteAllText(file, invalid);

        var preference = new ScanOptionsPreference(directory.Path);
        Assert.Equal(CaptureSettings.RegionalDefaults, preference.Load());
        Assert.Equal(invalid, File.ReadAllText(file));
    }

    [AvaloniaFact]
    public void ScannerPickerRemembersAllThreeOptionsAcrossWindows()
    {
        using var directory = new TemporaryLibrary();
        var preference = new ScanOptionsPreference(directory.Path);
        var first = new ScanDialog(_ => Task.FromResult(new ScanDiscovery([], [])), captureOptions: preference);
        first.Show();
        try
        {
            var source = FindCombo(first, "SourceBox");
            var paper = FindCombo(first, "PaperBox");
            var resolution = FindCombo(first, "ResolutionBox");
            source.SelectedItem = source.Items.Single(value => value?.ToString() == "Feeder · one-sided");
            paper.SelectedItem = ScanPaper.Letter;
            resolution.SelectedItem = resolution.Items.Single(value => value?.ToString() == "600 dpi");
        }
        finally
        {
            first.Close();
        }

        var reopened = new ScanDialog(_ => Task.FromResult(new ScanDiscovery([], [])),
            captureOptions: new ScanOptionsPreference(directory.Path));
        reopened.Show();
        try
        {
            Assert.Equal("Feeder · one-sided", FindCombo(reopened, "SourceBox").SelectedItem?.ToString());
            Assert.Equal(ScanPaper.Letter, FindCombo(reopened, "PaperBox").SelectedItem);
            Assert.Equal("600 dpi", FindCombo(reopened, "ResolutionBox").SelectedItem?.ToString());
        }
        finally
        {
            reopened.Close();
        }
    }

    [AvaloniaFact]
    public async Task ScannerPickerRestoresDeviceOptionsAndTheLastStartedScanner()
    {
        using var directory = new TemporaryLibrary();
        var first = Scanners.FromAddress("first-scanner.test", "Shared model");
        var second = Scanners.FromAddress("second-scanner.test", "Shared model");
        var renamed = Scanners.FromAddress("second-scanner.test", "Updated model label");
        var preference = new ScanOptionsPreference(directory.Path);
        preference.Save(new CaptureSettings(ScanFeed.Automatic, ScanPaper.Letter, ScanResolution.Standard));
        Task<ScanDiscovery> DiscoverBoth(CancellationToken _) => Task.FromResult(new ScanDiscovery([first, second], []));
        var owner = new Window();
        owner.Show();
        try
        {
            var dialog = new ScanDialog(DiscoverBoth, captureOptions: preference);
            var result = dialog.ShowDialog<ScanRequest?>(owner);
            try
            {
                var devices = FindCombo(dialog, "DeviceBox");
                Assert.Equal(first, devices.SelectedItem);
                ChooseOptions(dialog, "Flatbed", ScanPaper.A4, "150 dpi");
                devices.SelectedItem = second;
                AssertOptions(dialog, "Automatic", ScanPaper.Letter, "300 dpi");
                ChooseOptions(dialog, "Feeder · two-sided", ScanPaper.Letter, "600 dpi");
                Assert.IsType<Button>(dialog.FindControl<Button>("StartButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var request = Assert.IsType<ScanRequest>(await result);
                Assert.Equal(second, request.Device);
                Assert.Equal(ScanFeed.Duplex, request.Feed);
                Assert.Equal(ScanResolution.Detailed, request.Resolution);
            }
            finally
            {
                if (dialog.IsVisible) dialog.Close();
                await result;
            }

            var missing = new ScanDialog(_ => Task.FromResult(new ScanDiscovery([first], [])),
                captureOptions: new ScanOptionsPreference(directory.Path));
            missing.Show();
            try
            {
                Assert.Equal(first, FindCombo(missing, "DeviceBox").SelectedItem);
                AssertOptions(missing, "Flatbed", ScanPaper.A4, "150 dpi");
            }
            finally
            {
                missing.Close();
            }

            var saved = new SavedScannerStore(directory.Path);
            saved.AddOrUpdate(first);
            var reopened = new ScanDialog(_ => Task.FromResult(new ScanDiscovery([first, renamed], [])),
                settings: saved, captureOptions: new ScanOptionsPreference(directory.Path));
            reopened.Show();
            try
            {
                var devices = FindCombo(reopened, "DeviceBox");
                Assert.Equal(renamed, devices.SelectedItem);
                AssertOptions(reopened, "Feeder · two-sided", ScanPaper.Letter, "600 dpi");
                devices.SelectedItem = first;
                Assert.IsType<Button>(reopened.FindControl<Button>("RefreshButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(first, devices.SelectedItem);
                AssertOptions(reopened, "Flatbed", ScanPaper.A4, "150 dpi");
            }
            finally
            {
                reopened.Close();
            }
        }
        finally
        {
            owner.Close();
        }
    }

    private static void ChooseOptions(Window dialog, string source, ScanPaper paper, string resolution)
    {
        var sourceBox = FindCombo(dialog, "SourceBox");
        var resolutionBox = FindCombo(dialog, "ResolutionBox");
        sourceBox.SelectedItem = sourceBox.Items.Single(item => item?.ToString() == source);
        FindCombo(dialog, "PaperBox").SelectedItem = paper;
        resolutionBox.SelectedItem = resolutionBox.Items.Single(item => item?.ToString() == resolution);
    }

    private static void AssertOptions(Window dialog, string source, ScanPaper paper, string resolution)
    {
        Assert.Equal(source, FindCombo(dialog, "SourceBox").SelectedItem?.ToString());
        Assert.Equal(paper, FindCombo(dialog, "PaperBox").SelectedItem);
        Assert.Equal(resolution, FindCombo(dialog, "ResolutionBox").SelectedItem?.ToString());
    }

    private static ComboBox FindCombo(Window dialog, string name) =>
        Assert.IsType<ComboBox>(dialog.FindControl<ComboBox>(name));
}
