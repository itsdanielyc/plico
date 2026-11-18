using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using NAPS2.Images;
using NAPS2.ImportExport;
using NAPS2.Scan;

namespace Plico.Core;

/// <summary>Streams Apple's local scanner discovery and completed page files from the macOS helper.</summary>
internal static class MacScannerBridge
{
    internal const string HelperName = "plico-mac-scanner";
    private static readonly TimeSpan DiscoveryDeadline = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan CaptureDeadline = TimeSpan.FromMinutes(20);
    private sealed record ScannerEvent(string? Id, string? Name, string? Page);

    internal static async Task<IReadOnlyList<ScannerDevice>> ListAsync(CancellationToken cancellationToken)
    {
        var devices = new List<ScannerDevice>();
        var known = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var reply in RunAsync(["list"], DiscoveryDeadline, cancellationToken).ConfigureAwait(false))
        {
            if (reply is { Id: { Length: > 0 } id, Name: { Length: > 0 } name } && known.Add(id))
            {
                devices.Add(new ScannerDevice(new ScanDevice(Driver.Apple, id, name)));
            }
        }
        return devices;
    }

    internal static async IAsyncEnumerable<ProcessedImage> CaptureAsync(ScanningContext context, ScanOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string pageFolder = Directory.CreateDirectory(Path.Combine(context.TempFolderPath, Path.GetRandomFileName())).FullName;
        var device = options.Device ?? throw new ArgumentException("Choose an Image Capture scanner.", nameof(options));
        string feed = options.PaperSource switch
        {
            PaperSource.Flatbed => "flatbed",
            PaperSource.Feeder => "feeder",
            PaperSource.Duplex => "duplex",
            _ => "auto"
        };
        string colour = options.BitDepth switch
        {
            BitDepth.BlackAndWhite => "mono",
            BitDepth.Grayscale => "gray",
            _ => "colour"
        };
        string paper = options.PageSize == PageSize.A4 ? "a4" : "letter";
        var importer = new ImageImporter(context);
        var import = new ImportParams { BarcodeDetectionOptions = options.BarcodeDetectionOptions };
        int pageNumber = 0;
        await foreach (var reply in RunAsync(
            ["scan", device.ID, feed, paper, options.Dpi.ToString(CultureInfo.InvariantCulture), colour, pageFolder],
            CaptureDeadline, cancellationToken).ConfigureAwait(false))
        {
            if (reply.Page is not { Length: > 0 } filename ||
                !string.Equals(Path.GetFileName(filename), filename, StringComparison.Ordinal))
            {
                throw new IOException("Image Capture returned an invalid page filename.");
            }
            string file = Path.Combine(pageFolder, filename);
            await foreach (var page in importer.Import(file, import, cancellationToken).ConfigureAwait(false))
            {
                pageNumber++;
                if (MacScanCleanup.Apply(context, page, options, pageNumber) is { } adjusted) yield return adjusted;
            }
        }
    }

    private static async IAsyncEnumerable<ScannerEvent> RunAsync(IReadOnlyList<string> arguments, TimeSpan limit,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Image Capture is available on macOS.");
        string helper = Path.Combine(AppContext.BaseDirectory, HelperName);
        if (!File.Exists(helper)) throw new IOException("The Image Capture helper is missing. Extract a fresh copy of Plico.");
        var start = new ProcessStartInfo(helper)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limit);
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.Start(start) ?? throw new IOException("Image Capture could not start.");
        var diagnostics = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            while (await ReadReplyAsync(process.StandardOutput, timeout.Token, cancellationToken).ConfigureAwait(false) is { } line)
            {
                yield return ReadEvent(line);
            }
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            string error = (await diagnostics.ConfigureAwait(false)).Trim();
            if (process.ExitCode != 0)
            {
                throw new IOException(error.Length == 0 ? "Image Capture stopped responding." : error);
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<string?> ReadReplyAsync(StreamReader reader, CancellationToken deadline, CancellationToken cancellationToken)
    {
        try
        {
            return await reader.ReadLineAsync(deadline).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException("Image Capture did not finish within its allotted time.");
        }
    }

    private static ScannerEvent ReadEvent(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<ScannerEvent>(line) ?? throw new IOException("Image Capture returned an empty response.");
        }
        catch (JsonException error)
        {
            throw new IOException("Image Capture returned an unreadable response.", error);
        }
    }
}
