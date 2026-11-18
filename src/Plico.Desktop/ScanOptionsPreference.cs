using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Plico.Core;

namespace Plico.Desktop;

/// <summary>The machine's preferred paper source, format, and requested resolution.</summary>
internal sealed record CaptureSettings(ScanFeed Feed, ScanPaper Paper, ScanResolution Resolution)
{
    internal static CaptureSettings RegionalDefaults => new(ScanFeed.Automatic,
        RegionInfo.CurrentRegion.IsMetric ? ScanPaper.A4 : ScanPaper.Letter, ScanResolution.Standard);

    internal bool IsValid => Enum.IsDefined(Feed) && Enum.IsDefined(Paper) && Enum.IsDefined(Resolution);
}

/// <summary>Stores the capture choices for each scanner and the most recently submitted scanner.</summary>
internal sealed class ScanOptionsPreference(string directory)
{
    private const string DefaultOptionsFilename = "scan-options.json";
    private const string LastScannerFilename = "last-scan-device.txt";
    private static readonly JsonSerializerOptions Serialization = new()
    {
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    private readonly string _directory = Path.GetFullPath(directory);

    internal CaptureSettings Load(ScannerDevice? scanner = null)
    {
        if (scanner is not null && ReadOptions(OptionsPath(scanner)) is { } options) return options;
        return ReadOptions(OptionsPath(null)) ?? CaptureSettings.RegionalDefaults;
    }

    internal void Save(CaptureSettings options, ScannerDevice? scanner = null)
    {
        if (!options.IsValid) throw new ArgumentOutOfRangeException(nameof(options));
        ApplicationSettings.WriteAtomically(OptionsPath(scanner), JsonSerializer.Serialize(options, Serialization));
    }

    internal string? LoadLastScannerKey() => ReadText(Path.Combine(_directory, LastScannerFilename))?.Trim();

    internal void RememberLastScanner(ScannerDevice scanner) =>
        ApplicationSettings.WriteAtomically(Path.Combine(_directory, LastScannerFilename), scanner.SettingsKey);

    private string OptionsPath(ScannerDevice? scanner) => Path.Combine(_directory,
        scanner is null ? DefaultOptionsFilename : $"scan-options-{scanner.SettingsKey}.json");

    private static CaptureSettings? ReadOptions(string path)
    {
        var text = ReadText(path);
        if (text is null) return null;
        try
        {
            var options = JsonSerializer.Deserialize<CaptureSettings>(text, Serialization);
            return options is { IsValid: true } ? options : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadText(string path)
    {
        try
        {
            using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            using var reader = new StreamReader(file);
            return reader.ReadToEnd();
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }
}
