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

/// <summary>Loads and atomically saves the choices shown when the scanner picker reopens.</summary>
internal sealed class ScanOptionsPreference(string directory)
{
    private static readonly JsonSerializerOptions Serialization = new()
    {
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    private readonly string _path = Path.Combine(Path.GetFullPath(directory), "scan-options.json");

    internal CaptureSettings Load()
    {
        if (!File.Exists(_path)) return CaptureSettings.RegionalDefaults;
        try
        {
            var options = JsonSerializer.Deserialize<CaptureSettings>(File.ReadAllText(_path), Serialization);
            return options is { IsValid: true } ? options : CaptureSettings.RegionalDefaults;
        }
        catch (JsonException)
        {
            return CaptureSettings.RegionalDefaults;
        }
    }

    internal void Save(CaptureSettings options)
    {
        if (!options.IsValid) throw new ArgumentOutOfRangeException(nameof(options));
        ApplicationSettings.WriteAtomically(_path, JsonSerializer.Serialize(options, Serialization));
    }
}
