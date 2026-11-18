using System.Text.Json;
using Avalonia.Styling;

namespace Plico.Desktop;

internal enum AppearanceTheme { System, Light, Dark }
internal enum InterfaceLanguage { System, English, French }

internal sealed record AppearanceOptions(AppearanceTheme Theme, InterfaceLanguage Language)
{
    internal static AppearanceOptions Default { get; } = new(AppearanceTheme.System, InterfaceLanguage.System);
    internal ThemeVariant RequestedTheme => Theme switch
    {
        AppearanceTheme.Light => ThemeVariant.Light,
        AppearanceTheme.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default
    };
}

/// <summary>Remembers per-user desktop appearance separately from library and OCR preferences.</summary>
internal sealed class AppearancePreference(string directory)
{
    private readonly string _path = Path.Combine(Path.GetFullPath(directory), "appearance.json");

    internal AppearanceOptions Load()
    {
        try
        {
            var options = JsonSerializer.Deserialize<AppearanceOptions>(File.ReadAllText(_path));
            return options is not null && Enum.IsDefined(options.Theme) && Enum.IsDefined(options.Language)
                ? options : AppearanceOptions.Default;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return AppearanceOptions.Default;
        }
    }

    internal void Save(AppearanceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(options.Theme) || !Enum.IsDefined(options.Language))
            throw new ArgumentException("Choose a supported appearance and interface language.", nameof(options));
        ApplicationSettings.WriteAtomically(_path, JsonSerializer.Serialize(options));
    }
}
