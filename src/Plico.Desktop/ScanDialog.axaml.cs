using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Plico.Core;

namespace Plico.Desktop;

/// <summary>Lets the user pick a scanner and paper settings before capture begins.</summary>
internal sealed partial class ScanDialog : Window
{
    private const int SdkAdjustmentUnitsPerPercent = 10;
    private sealed record SourceOption(string Label, ScanFeed Value)
    {
        public override string ToString() => Label;
    }

    private sealed record ResolutionOption(ScanResolution Value)
    {
        public override string ToString() => $"{(int)Value} dpi";
    }

    private sealed record ColourOption(string Label, ScanColour Value)
    {
        public override string ToString() => Label;
    }

    private static readonly ColourOption[] Colours =
    [
        new("Colour", ScanColour.Colour), new("Greyscale", ScanColour.Greyscale), new("Black & white", ScanColour.BlackAndWhite)
    ];

    private static readonly SourceOption[] Sources =
    [
        new("Automatic", ScanFeed.Automatic),
        new("Flatbed", ScanFeed.Flatbed),
        new("Feeder · one-sided", ScanFeed.Feeder),
        new("Feeder · two-sided", ScanFeed.Duplex)
    ];
    private static readonly ResolutionOption[] Resolutions =
        Enum.GetValues<ScanResolution>().Select(value => new ResolutionOption(value)).ToArray();

    private readonly Func<CancellationToken, Task<ScanDiscovery>> _discover;
    private readonly Func<string, CancellationToken, Task<ScannerDevice>> _connect;
    private readonly SavedScannerStore? _settings;
    private readonly ScanOptionsPreference? _captureOptions;
    private readonly string? _lastScannerKey;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<Uri, ScannerDevice> _connected = [];
    private IReadOnlyList<ScannerDevice> _discovered = [];
    private bool _checkingDevices;
    private bool _updatingPicker = true;
    private string? _settingsError;

    internal ScanDialog(Func<CancellationToken, Task<ScanDiscovery>> discover,
        Func<string, CancellationToken, Task<ScannerDevice>>? connect = null, SavedScannerStore? settings = null,
        ScanOptionsPreference? captureOptions = null)
    {
        _discover = discover;
        _connect = connect ?? Scanners.ConnectAsync;
        _settings = settings;
        _captureOptions = captureOptions;
        InitializeComponent();
        SourceBox.ItemsSource = Sources;
        PaperBox.ItemsSource = Enum.GetValues<ScanPaper>();
        ResolutionBox.ItemsSource = Resolutions;
        ScanColourBox.ItemsSource = Colours;
        ProfileControls.IsVisible = captureOptions is not null;
        TrySettingsChange(() => LoadProfiles());
        ApplyCaptureSettings(CaptureSettings.RegionalDefaults);
        TrySettingsChange(() =>
        {
            foreach (var scanner in _settings?.Load() ?? []) _connected[scanner.Address!] = scanner;
        });
        var addressError = _settingsError;
        string? lastScannerKey = null;
        TrySettingsChange(() => lastScannerKey = _captureOptions?.LoadLastScannerKey());
        _lastScannerKey = lastScannerKey;
        _settingsError ??= addressError;
        ShowDevices(_lastScannerKey);
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        await DiscoverAsync(preferLastScanner: true);
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

    private async Task DiscoverAsync(bool preferLastScanner = false)
    {
        var token = _lifetime.Token;
        string? previousKey = SelectedScanner?.SettingsKey;
        SetCheckingDevices(true);
        ScannerStatus.Text = "Looking for scanners…";
        try
        {
            var result = await _discover(token);
            if (token.IsCancellationRequested) return;
            _discovered = result.Devices;
            string? currentKey = SelectedScanner?.SettingsKey;
            string? preferredKey = preferLastScanner && currentKey == previousKey ? _lastScannerKey : currentKey;
            ShowDevices(preferredKey, fallbackKey: currentKey);
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
            TrySettingsChange(() => _settings?.AddOrUpdate(scanner));
            ShowDevices(scanner.SettingsKey);
            ScannerStatus.Text = _settingsError is null ? $"Connected to {scanner.Name}." : $"Connected to {scanner.Name}. {_settingsError}";
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

    private ScannerDevice? SelectedScanner => DeviceBox.SelectedItem as ScannerDevice;

    // O(n) time and space for the available devices, grouped by their native identity.
    private void ShowDevices(string? preferredKey, string? fallbackKey = null)
    {
        bool firstDisplay = _updatingPicker;
        string? previousKey = SelectedScanner?.SettingsKey;
        var available = _discovered.Concat(_connected.Values).DistinctBy(device => device.SettingsKey, StringComparer.Ordinal).ToArray();
        _updatingPicker = true;
        try
        {
            DeviceBox.ItemsSource = available;
            DeviceBox.SelectedItem = available.FirstOrDefault(device => device.SettingsKey == preferredKey) ??
                available.FirstOrDefault(device => device.SettingsKey == fallbackKey) ?? available.FirstOrDefault();
        }
        finally
        {
            _updatingPicker = false;
        }
        if (firstDisplay || previousKey != SelectedScanner?.SettingsKey) RestoreCaptureSettings();
        UpdateStartButton();
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

    private void Device_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingPicker) return;
        RestoreCaptureSettings();
        UpdateStartButton();
    }

    private void RestoreCaptureSettings()
    {
        if (_captureOptions is not { } preferences) return;
        var options = CaptureSettings.RegionalDefaults;
        var previousError = _settingsError;
        if (!TrySettingsChange(() => options = preferences.Load(SelectedScanner))) ScannerStatus.Text = _settingsError;
        _settingsError ??= previousError;
        ApplyCaptureSettings(options);
    }

    private void ApplyCaptureSettings(CaptureSettings options)
    {
        bool updating = _updatingPicker;
        _updatingPicker = true;
        try
        {
            SourceBox.SelectedItem = Sources.Single(option => option.Value == options.Feed);
            PaperBox.SelectedItem = options.Paper;
            ResolutionBox.SelectedItem = Resolutions.Single(option => option.Value == options.Resolution);
            var cleanup = options.Cleanup ?? ScanCleanup.Default;
            ScanColourBox.SelectedItem = Colours.Single(option => option.Value == cleanup.Colour);
            SkipBlankPages.IsChecked = cleanup.ExcludeBlankPages;
            DeskewScan.IsChecked = cleanup.Deskew;
            CropScan.IsChecked = cleanup.Crop;
            FlipScanBacks.IsChecked = cleanup.FlipBackSides;
            ScanBrightness.Value = cleanup.Brightness / (decimal)SdkAdjustmentUnitsPerPercent;
            ScanContrast.Value = cleanup.Contrast / (decimal)SdkAdjustmentUnitsPerPercent;
        }
        finally
        {
            _updatingPicker = updating;
        }
    }

    private void CaptureOptions_Changed(object? sender, RoutedEventArgs e)
    {
        if (_updatingPicker || _captureOptions is null || ReadCaptureSettings() is not { } options) return;
        if (!TrySettingsChange(() => _captureOptions.Save(options, SelectedScanner))) ScannerStatus.Text = _settingsError;
    }

    private CaptureSettings? ReadCaptureSettings()
    {
        if (SourceBox.SelectedItem is not SourceOption source || PaperBox.SelectedItem is not ScanPaper paper ||
            ResolutionBox.SelectedItem is not ResolutionOption resolution || ScanColourBox.SelectedItem is not ColourOption colour)
            return null;
        var cleanup = new ScanCleanup(colour.Value, SkipBlankPages.IsChecked == true, DeskewScan.IsChecked == true,
            CropScan.IsChecked == true, FlipScanBacks.IsChecked == true,
            (int)((ScanBrightness.Value ?? 0) * SdkAdjustmentUnitsPerPercent),
            (int)((ScanContrast.Value ?? 0) * SdkAdjustmentUnitsPerPercent));
        return new(source.Value, paper, resolution.Value, cleanup == ScanCleanup.Default ? null : cleanup);
    }

    private void LoadProfiles(string? selectedName = null)
    {
        var profiles = _captureOptions?.LoadProfiles() ?? [];
        ScanProfileBox.ItemsSource = profiles;
        ScanProfileBox.SelectedItem = profiles.FirstOrDefault(profile => StringComparer.OrdinalIgnoreCase.Equals(profile.Name, selectedName));
    }

    private void ScanProfile_Changed(object? sender, SelectionChangedEventArgs e)
    {
        var profile = ScanProfileBox.SelectedItem as ScanProfile;
        RemoveScanProfile.IsEnabled = profile is not null;
        if (profile is null) return;
        ApplyCaptureSettings(profile.Options);
        ScanProfileName.Text = profile.Name;
    }

    private void SaveProfile_Click(object? sender, RoutedEventArgs e)
    {
        if (_captureOptions is not { } preferences || ReadCaptureSettings() is not { } options) return;
        string name = ScanProfileName.Text?.Trim() ?? "";
        bool saved = TrySettingsChange(() =>
        {
            preferences.SaveProfile(name, options);
            LoadProfiles(name);
        });
        ScannerStatus.Text = saved ? $"Saved profile “{name}”." : _settingsError;
    }

    private void RemoveProfile_Click(object? sender, RoutedEventArgs e)
    {
        if (_captureOptions is not { } preferences || ScanProfileBox.SelectedItem is not ScanProfile profile) return;
        bool removed = TrySettingsChange(() =>
        {
            preferences.RemoveProfile(profile.Name);
            LoadProfiles();
            ScanProfileName.Text = "";
        });
        ScannerStatus.Text = removed ? $"Removed profile “{profile.Name}”." : _settingsError;
    }

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
        if (!_checkingDevices && SelectedScanner is { } scanner && ReadCaptureSettings() is { } options)
        {
            if (_captureOptions is { } preferences)
            {
                TrySettingsChange(() =>
                {
                    preferences.Save(options, scanner);
                    preferences.RememberLastScanner(scanner);
                });
            }
            Close(new ScanRequest(scanner, options.Feed, options.Paper, options.Resolution, options.Cleanup));
        }
    }
}
