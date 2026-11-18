using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Plico.Core;

namespace Plico.Desktop;

/// <summary>The machine's preferred source, format, resolution, and image adjustments.</summary>
internal sealed record CaptureSettings(ScanFeed Feed, ScanPaper Paper, ScanResolution Resolution, ScanCleanup? Cleanup = null)
{
    internal static CaptureSettings RegionalDefaults => new(ScanFeed.Automatic,
        RegionInfo.CurrentRegion.IsMetric ? ScanPaper.A4 : ScanPaper.Letter, ScanResolution.Standard);

    internal bool IsValid => Enum.IsDefined(Feed) && Enum.IsDefined(Paper) && Enum.IsDefined(Resolution) && (Cleanup?.IsValid() ?? true);
}

internal sealed record ScanProfile(string Name, CaptureSettings Options)
{
    public override string ToString() => Name;
}

/// <summary>Stores the capture choices for each scanner and the most recently submitted scanner.</summary>
internal sealed class ScanOptionsPreference(string directory)
{
    private const string DefaultOptionsFilename = "scan-options.json";
    private const string LastScannerFilename = "last-scan-device.txt";
    private const string ProfilesFilename = "scan-profiles.json";
    internal const int MaximumProfileNameLength = 48;
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

    internal IReadOnlyList<ScanProfile> LoadProfiles()
    {
        var json = ReadText(Path.Combine(_directory, ProfilesFilename));
        if (json is null) return [];
        try
        {
            return JsonSerializer.Deserialize<ScanProfile[]>(json, Serialization)?
                .Where(profile => profile is { Options.IsValid: true } && IsProfileName(profile.Name))
                .DistinctBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase).ToArray() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal void SaveProfile(string name, CaptureSettings options)
    {
        string label = name.Trim();
        if (!IsProfileName(label)) throw new InvalidDataException($"Enter a profile name of 1 to {MaximumProfileNameLength} characters.");
        if (!options.IsValid) throw new InvalidDataException("Check the scan profile settings.");
        WriteProfiles([.. OtherProfiles(label), new ScanProfile(label, options)]);
    }

    internal void RemoveProfile(string name) => WriteProfiles(OtherProfiles(name).ToArray());

    private IEnumerable<ScanProfile> OtherProfiles(string name) =>
        LoadProfiles().Where(profile => !StringComparer.OrdinalIgnoreCase.Equals(profile.Name, name));

    private void WriteProfiles(ScanProfile[] profiles) =>
        ApplicationSettings.WriteAtomically(Path.Combine(_directory, ProfilesFilename), JsonSerializer.Serialize(profiles, Serialization));

    private static bool IsProfileName(string? name) =>
        name is { Length: > 0 and <= MaximumProfileNameLength } && !name.Any(char.IsControl);

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
