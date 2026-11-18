using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed class ScanOptionsTests
{
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

    private static ComboBox FindCombo(Window dialog, string name) =>
        Assert.IsType<ComboBox>(dialog.FindControl<ComboBox>(name));
}
