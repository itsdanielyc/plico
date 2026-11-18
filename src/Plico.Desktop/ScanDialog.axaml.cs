using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
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
    private readonly Func<string, CancellationToken, Task<ScannerDevice>> _connect;
    private readonly SavedScannerStore? _settings;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<Uri, ScannerDevice> _connected = [];
    private IReadOnlyList<ScannerDevice> _discovered = [];
    private bool _checkingDevices;
    private string? _settingsError;

    internal ScanDialog(Func<CancellationToken, Task<ScanDiscovery>> discover,
        Func<string, CancellationToken, Task<ScannerDevice>>? connect = null, SavedScannerStore? settings = null)
    {
        _discover = discover;
        _connect = connect ?? Scanners.ConnectAsync;
        _settings = settings;
        InitializeComponent();
        SourceBox.ItemsSource = Sources;
        SourceBox.SelectedItem = Sources[0];
        PaperBox.ItemsSource = Enum.GetValues<ScanPaper>();
        PaperBox.SelectedItem = RegionInfo.CurrentRegion.IsMetric ? ScanPaper.A4 : ScanPaper.Letter;
        TrySettingsChange(() =>
        {
            foreach (var scanner in _settings?.Load() ?? []) _connected[scanner.Address!] = scanner;
        });
        ShowDevices(null);
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
    private async void Connect_Click(object? sender, RoutedEventArgs e) => await ConnectAsync();

    private void Forget_Click(object? sender, RoutedEventArgs e)
    {
        if (DeviceBox.SelectedItem is not ScannerDevice { Address: { } address }) return;
        if (!TrySettingsChange(() => _settings?.Remove(address)))
        {
            ScannerStatus.Text = _settingsError;
            return;
        }
        _connected.Remove(address);
        ShowDevices(null);
        ScannerStatus.Text = "Saved scanner address removed.";
    }

    private async void Address_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await ConnectAsync();
    }

    private async Task DiscoverAsync()
    {
        var token = _lifetime.Token;
        ScannerDevice? previouslySelected = DeviceBox.SelectedItem as ScannerDevice;
        SetCheckingDevices(true);
        ScannerStatus.Text = "Looking for scanners…";
        try
        {
            var result = await _discover(token);
            if (token.IsCancellationRequested) return;
            _discovered = result.Devices;
            ShowDevices(previouslySelected);
            var status = result.Warnings.Count > 0 ? string.Join(Environment.NewLine, result.Warnings) :
                DeviceBox.SelectedItem is null ? "No scanner found. Connect one by address or choose Refresh." :
                "Pages from this scan will be saved as one PDF.";
            ScannerStatus.Text = _settingsError is null ? status : $"{status}{Environment.NewLine}{_settingsError}";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested) ScannerStatus.Text = error.Message;
        }
        finally
        {
            if (!token.IsCancellationRequested) SetCheckingDevices(false);
        }
    }

    private async Task ConnectAsync()
    {
        if (_checkingDevices) return;
        var token = _lifetime.Token;
        SetCheckingDevices(true);
        ScannerStatus.Text = "Connecting to the scanner…";
        try
        {
            var scanner = await _connect(AddressBox.Text ?? "", token);
            if (token.IsCancellationRequested) return;
            var address = scanner.Address ?? throw new InvalidDataException("This scanner did not report a usable address.");
            _connected[address] = scanner;
            ShowDevices(scanner);
            var saved = TrySettingsChange(() => _settings?.AddOrUpdate(scanner));
            ScannerStatus.Text = saved ? $"Connected to {scanner.Name}." : $"Connected to {scanner.Name}. {_settingsError}";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested) ScannerStatus.Text = error.Message;
        }
        finally
        {
            if (!token.IsCancellationRequested) SetCheckingDevices(false);
        }
    }

    private void ShowDevices(ScannerDevice? preferred)
    {
        var available = _discovered.Concat(_connected.Values).Distinct().ToArray();
        DeviceBox.ItemsSource = available;
        DeviceBox.SelectedItem = available.FirstOrDefault(device => device == preferred) ?? available.FirstOrDefault();
    }

    private bool TrySettingsChange(Action change)
    {
        try
        {
            change();
            _settingsError = null;
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _settingsError = error.Message;
            return false;
        }
    }

    private void SetCheckingDevices(bool checking)
    {
        _checkingDevices = checking;
        RefreshButton.IsEnabled = !checking;
        ConnectButton.IsEnabled = !checking;
        UpdateStartButton();
    }

    private void Device_Changed(object? sender, SelectionChangedEventArgs e) => UpdateStartButton();

    private void UpdateStartButton()
    {
        if (StartButton is not null && DeviceBox is not null)
        {
            StartButton.IsEnabled = !_checkingDevices && DeviceBox.SelectedItem is ScannerDevice;
            if (ForgetButton is not null)
            {
                ForgetButton.IsVisible = DeviceBox.SelectedItem is ScannerDevice { Address: { } address } &&
                    _connected.ContainsKey(address);
                ForgetButton.IsEnabled = !_checkingDevices;
            }
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
