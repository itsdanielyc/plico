using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NAPS2.Images;
using NAPS2.Images.Skia;
using NAPS2.Scan;

namespace Plico.Core;

/// <summary>A device and its platform driver, as presented in the scanner picker.</summary>
public sealed record ScannerDevice
{
    internal ScannerDevice(ScanDevice device)
    {
        NativeDevice = device;
        if (device.Driver == Driver.Escl && Uri.TryCreate(device.ID, UriKind.Absolute, out var address) &&
            address.Scheme is "http" or "https")
        {
            Address = address;
        }
        string identifier = Address?.AbsoluteUri ?? device.ID;
        SettingsKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{device.Driver}\n{identifier}")));
    }
    internal ScanDevice NativeDevice { get; }

    /// <summary>The name reported by the scanner driver.</summary>
    public string Name => NativeDevice.Name;

    /// <summary>The directly supplied eSCL address, when this device was connected by address.</summary>
    public Uri? Address { get; }

    /// <summary>The machine-local settings key derived from the driver and its device identifier.</summary>
    public string SettingsKey { get; }

    /// <summary>A label that distinguishes local and network entries for the same scanner.</summary>
    public string DisplayName => $"{Name} · {Address?.Authority ?? Scanners.DriverLabel(NativeDevice.Driver)}";
}

/// <summary>Capture from the scanner's preferred source, its bed, or its feeder.</summary>
public enum ScanFeed { Automatic, Flatbed, Feeder, Duplex, ManualDuplex }

/// <summary>The physical page format requested from the scanner.</summary>
public enum ScanPaper { A4, Letter }

/// <summary>The requested capture resolution in dots per inch.</summary>
public enum ScanResolution { Quick = 150, Standard = 300, Detailed = 600 }

/// <summary>The scanner's requested pixel mode.</summary>
public enum ScanColour { Colour, Greyscale, BlackAndWhite }

/// <summary>Optional image adjustments applied before the captured pages are saved.</summary>
/// <param name="Colour">The requested colour mode, with colour as the existing default.</param>
/// <param name="ExcludeBlankPages">Drops pages below the scanner adapter's ink coverage threshold.</param>
/// <param name="Deskew">Straightens slightly rotated pages.</param>
/// <param name="Crop">Trims empty space outside the scanned page.</param>
/// <param name="FlipBackSides">Turns the back sides of an explicitly duplex scan by 180 degrees.</param>
/// <param name="Brightness">An adjustment from -1000 to 1000, with zero neutral.</param>
/// <param name="Contrast">An adjustment from -1000 to 1000, with zero neutral.</param>
public sealed record ScanCleanup(ScanColour Colour = ScanColour.Colour, bool ExcludeBlankPages = false,
    bool Deskew = false, bool Crop = false, bool FlipBackSides = false, int Brightness = 0, int Contrast = 0)
{
    internal const int AdjustmentLimit = 1000;
    /// <summary>Capture with the scanner's usual colour mode and no additional adjustments.</summary>
    public static ScanCleanup Default { get; } = new();

    /// <summary>Whether the colour mode and both adjustments can be sent to the scanner.</summary>
    public bool IsValid() => Enum.IsDefined(Colour) && Brightness is >= -AdjustmentLimit and <= AdjustmentLimit &&
        Contrast is >= -AdjustmentLimit and <= AdjustmentLimit;
}

/// <summary>Capture parameters chosen by the user in the native desktop window.</summary>
public sealed record ScanRequest(ScannerDevice Device, ScanFeed Feed, ScanPaper Paper,
    ScanResolution Resolution = ScanResolution.Standard, ScanCleanup? Cleanup = null, ScanBatchOptions? Batch = null)
{
    /// <summary>Whether the scan can produce multiple documents or needs a second feeder pass.</summary>
    public bool RequiresBatchCapture => Feed == ScanFeed.ManualDuplex || Batch?.SplitOnSeparators == true;
}

/// <summary>Discovered scanners and actionable messages from unavailable drivers.</summary>
public sealed record ScanDiscovery(IReadOnlyList<ScannerDevice> Devices, IReadOnlyList<string> Warnings);

/// <summary>The completed pages and driver error from an interrupted scan.</summary>
/// <param name="CapturedPages">The number of complete pages delivered before the error.</param>
/// <param name="Reason">The message to show when asking whether to keep the pages.</param>
public sealed record ScanInterruption(int CapturedPages, string Reason);

/// <summary>The saved document, if any, and the reason a scan ended early.</summary>
/// <param name="Document">The saved PDF, or null if the captured pages were discarded.</param>
/// <param name="Interruption">The error and page count, or null for an ordinary completed scan.</param>
public sealed record ScanOutcome(DocumentSummary? Document, ScanInterruption? Interruption);

/// <summary>Discovers devices over supported host drivers and local network eSCL.</summary>
public static class Scanners
{
    private const string DefaultEsclPath = "/eSCL";
    private static readonly TimeSpan AddressLookupTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Queries each supported driver independently so a missing driver cannot hide other scanners.</summary>
    public static async Task<ScanDiscovery> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        Driver[] drivers = OperatingSystem.IsWindows() ? [Driver.Wia, Driver.Escl] :
            OperatingSystem.IsLinux() ? [Driver.Sane, Driver.Escl] : [Driver.Escl];
        var results = await Task.WhenAll(drivers.Select(driver =>
            Task.Run(() => DiscoverDriverAsync(driver, cancellationToken), cancellationToken))).ConfigureAwait(false);
        return new ScanDiscovery(
            results.SelectMany(result => result.Devices).OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            results.SelectMany(result => result.Warnings).ToArray());
    }

    /// <summary>Checks an explicitly supplied eSCL address and reads the scanner's reported model.</summary>
    public static async Task<ScannerDevice> ConnectAsync(string address, CancellationToken cancellationToken = default)
    {
        var endpoint = ParseAddress(address);
        var device = new ScanDevice(Driver.Escl, endpoint.AbsoluteUri, endpoint.Host);
        using var context = new ScanningContext(new SkiaImageContext());
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(AddressLookupTimeout);
        try
        {
            var capabilities = await new ScanController(context).GetCaps(device, timeout.Token).ConfigureAwait(false);
            var paper = capabilities.PaperSourceCaps;
            if (paper is null || (!paper.SupportsFlatbed && !paper.SupportsFeeder && !paper.SupportsDuplex))
            {
                throw new InvalidDataException("This address did not report an eSCL scanner with a paper source.");
            }
            var model = capabilities.MetadataCaps?.Model;
            return new ScannerDevice(device with { Name = string.IsNullOrWhiteSpace(model) ? endpoint.Host : model });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"The scanner at {endpoint.Host} did not respond.");
        }
    }

    /// <summary>Recreates a direct scanner entry from settings without contacting the scanner.</summary>
    public static ScannerDevice FromAddress(string address, string name)
    {
        var endpoint = ParseAddress(address);
        return new ScannerDevice(new ScanDevice(Driver.Escl, endpoint.AbsoluteUri,
            string.IsNullOrWhiteSpace(name) ? endpoint.Host : name));
    }

    internal static string DriverLabel(Driver driver) => driver switch
    {
        Driver.Wia => "Windows WIA",
        Driver.Sane => "SANE",
        Driver.Escl => "Network eSCL",
        _ => driver.ToString()
    };

    internal static ScanOptions CaptureOptions(ScanRequest request)
    {
        var cleanup = request.Cleanup ?? ScanCleanup.Default;
        if (!cleanup.IsValid()) throw new ArgumentOutOfRangeException(nameof(request), "The scan cleanup settings are out of range.");
        return new ScanOptions
        {
            Device = request.Device.NativeDevice,
            Dpi = Enum.IsDefined(request.Resolution) ? (int)request.Resolution
                : throw new ArgumentOutOfRangeException(nameof(request), "Unknown scan resolution."),
            PageSize = request.Paper switch
            {
                ScanPaper.A4 => PageSize.A4,
                ScanPaper.Letter => PageSize.Letter,
                _ => throw new ArgumentOutOfRangeException(nameof(request), "Unknown page format.")
            },
            PaperSource = request.Feed switch
            {
                ScanFeed.Automatic => PaperSource.Auto,
                ScanFeed.Flatbed => PaperSource.Flatbed,
                ScanFeed.Feeder or ScanFeed.ManualDuplex => PaperSource.Feeder,
                ScanFeed.Duplex => PaperSource.Duplex,
                _ => throw new ArgumentOutOfRangeException(nameof(request), "Unknown paper source.")
            },
            BitDepth = cleanup.Colour switch
            {
                ScanColour.Greyscale => BitDepth.Grayscale,
                ScanColour.BlackAndWhite => BitDepth.BlackAndWhite,
                _ => BitDepth.Color
            },
            // Keeping one-sided sheets in their original positions is required for duplex pairing.
            ExcludeBlankPages = cleanup.ExcludeBlankPages && request.Feed != ScanFeed.ManualDuplex,
            AutoDeskew = cleanup.Deskew,
            AutoCrop = cleanup.Crop,
            FlipDuplexedPages = request.Feed == ScanFeed.Duplex && cleanup.FlipBackSides,
            Brightness = cleanup.Brightness,
            Contrast = cleanup.Contrast,
            BrightnessContrastAfterScan = cleanup.Brightness != 0 || cleanup.Contrast != 0,
            BarcodeDetectionOptions = ScanBatchPages.BarcodeOptions(request.Batch)
        };
    }

    internal static string ScanTitle() =>
        $"Scan {DateTimeOffset.Now.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture)}";

    private static Uri ParseAddress(string address)
    {
        var candidate = address.Trim();
        if (candidate.Length == 0)
        {
            throw new ArgumentException("Enter the scanner's network address.", nameof(address));
        }
        if (!candidate.Contains("://", StringComparison.Ordinal)) candidate = $"http://{candidate}";
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme is not ("http" or "https")) || string.IsNullOrEmpty(endpoint.Host) ||
            endpoint.UserInfo.Length > 0 || endpoint.Query.Length > 0 || endpoint.Fragment.Length > 0)
        {
            throw new ArgumentException("Use a scanner hostname, IP address, or HTTP(S) eSCL address.", nameof(address));
        }

        return new UriBuilder(endpoint)
        {
            Path = endpoint.AbsolutePath == "/" ? DefaultEsclPath : endpoint.AbsolutePath.TrimEnd('/')
        }.Uri;
    }

    private static async Task<ScanDiscovery> DiscoverDriverAsync(Driver driver, CancellationToken cancellationToken)
    {
        using var context = new ScanningContext(new SkiaImageContext());
        var devices = new List<ScannerDevice>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            await foreach (var device in new ScanController(context).GetDevices(driver, cancellationToken).ConfigureAwait(false))
            {
                if (seen.Add(device.ID)) devices.Add(new ScannerDevice(device));
            }
            return new ScanDiscovery(devices, []);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            string warning = driver == Driver.Sane && error is DllNotFoundException
                ? "SANE is unavailable. Install your distribution's SANE runtime to use local scanner drivers."
                : $"{DriverLabel(driver)} could not list devices. {error.Message}";
            return new ScanDiscovery(devices, [warning]);
        }
    }
}
