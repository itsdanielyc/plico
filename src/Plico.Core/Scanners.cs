using System.Globalization;
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
    }
    internal ScanDevice NativeDevice { get; }

    /// <summary>The name reported by the scanner driver.</summary>
    public string Name => NativeDevice.Name;

    /// <summary>The directly supplied eSCL address, when this device was connected by address.</summary>
    public Uri? Address { get; }

    /// <summary>A label that distinguishes local and network entries for the same scanner.</summary>
    public string DisplayName => $"{Name} · {Address?.Authority ?? Scanners.DriverLabel(NativeDevice.Driver)}";
}

/// <summary>Capture from the scanner's preferred source, its bed, or its feeder.</summary>
public enum ScanFeed { Automatic, Flatbed, Feeder, Duplex }

/// <summary>The physical page format requested from the scanner.</summary>
public enum ScanPaper { A4, Letter }

/// <summary>The requested capture resolution in dots per inch.</summary>
public enum ScanResolution { Quick = 150, Standard = 300, Detailed = 600 }

/// <summary>Capture parameters chosen by the user in the native desktop window.</summary>
public sealed record ScanRequest(ScannerDevice Device, ScanFeed Feed, ScanPaper Paper,
    ScanResolution Resolution = ScanResolution.Standard);

/// <summary>Discovered scanners and actionable messages from unavailable drivers.</summary>
public sealed record ScanDiscovery(IReadOnlyList<ScannerDevice> Devices, IReadOnlyList<string> Warnings);

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

    internal static ScanOptions CaptureOptions(ScanRequest request) => new()
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
            ScanFeed.Feeder => PaperSource.Feeder,
            ScanFeed.Duplex => PaperSource.Duplex,
            _ => throw new ArgumentOutOfRangeException(nameof(request), "Unknown paper source.")
        }
    };

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
