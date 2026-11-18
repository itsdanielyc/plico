using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Plico.Core;

namespace Plico.Desktop;

/// <summary>Lets the user pick a scanner and paper settings before capture begins.</summary>
internal sealed partial class ScanDialog : Window
{
    private sealed record SourceOption(string Label, ScanFeed Value)
    {
        public override string ToString() => Label;
    }

    private static readonly SourceOption[] Sources =
    [
        new("Automatic", ScanFeed.Automatic),
        new("Flatbed", ScanFeed.Flatbed),
        new("Feeder · one-sided", ScanFeed.Feeder),
        new("Feeder · two-sided", ScanFeed.Duplex)
    ];

    private readonly Func<CancellationToken, Task<ScanDiscovery>> _discover;
    private readonly CancellationTokenSource _lifetime = new();

    internal ScanDialog(Func<CancellationToken, Task<ScanDiscovery>> discover)
    {
        _discover = discover;
        InitializeComponent();
        SourceBox.ItemsSource = Sources;
        SourceBox.SelectedItem = Sources[0];
        PaperBox.ItemsSource = Enum.GetValues<ScanPaper>();
        PaperBox.SelectedItem = RegionInfo.CurrentRegion.IsMetric ? ScanPaper.A4 : ScanPaper.Letter;
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        await DiscoverAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        base.OnClosed(e);
    }

    private async void Refresh_Click(object? sender, RoutedEventArgs e) => await DiscoverAsync();

    private async Task DiscoverAsync()
    {
        var token = _lifetime.Token;
        ScannerDevice? previouslySelected = DeviceBox.SelectedItem as ScannerDevice;
        RefreshButton.IsEnabled = false;
        StartButton.IsEnabled = false;
        ScannerStatus.Text = "Looking for scanners…";
        try
        {
            var result = await _discover(token);
            if (token.IsCancellationRequested) return;
            DeviceBox.ItemsSource = result.Devices;
            DeviceBox.SelectedItem = result.Devices.FirstOrDefault(device => device == previouslySelected)
                ?? result.Devices.FirstOrDefault();
            ScannerStatus.Text = result.Warnings.Count > 0 ? string.Join(Environment.NewLine, result.Warnings) :
                result.Devices.Count == 0 ? "No scanner found. Connect a supported scanner and choose Refresh." :
                "Pages from this scan will be saved as one PDF.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested) ScannerStatus.Text = error.Message;
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                RefreshButton.IsEnabled = true;
                UpdateStartButton();
            }
        }
    }

    private void Device_Changed(object? sender, SelectionChangedEventArgs e) => UpdateStartButton();

    private void UpdateStartButton()
    {
        if (StartButton is not null && RefreshButton is not null)
        {
            StartButton.IsEnabled = RefreshButton.IsEnabled && DeviceBox.SelectedItem is ScannerDevice;
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();

    private void Start_Click(object? sender, RoutedEventArgs e)
    {
        if (DeviceBox.SelectedItem is ScannerDevice scanner && SourceBox.SelectedItem is SourceOption source &&
            PaperBox.SelectedItem is ScanPaper paper)
        {
            Close(new ScanRequest(scanner, source.Value, paper));
        }
    }
}
