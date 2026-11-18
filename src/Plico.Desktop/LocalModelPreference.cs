using Plico.Core;
using System.Text.Json;

namespace Plico.Desktop;

internal sealed class LocalModelPreference(string settingsDirectory)
{
    private readonly string _path = Path.Combine(settingsDirectory, "local-models.json");

    internal LocalModelSettings Load()
    {
        if (!File.Exists(_path)) return LocalModelSettings.Default;
        try
        {
            var settings = JsonSerializer.Deserialize<LocalModelSettings>(File.ReadAllText(_path));
            return settings?.Validate() ?? LocalModelSettings.Default;
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            return LocalModelSettings.Default;
        }
    }

    internal void Save(LocalModelSettings settings) =>
        ApplicationSettings.WriteAtomically(_path, JsonSerializer.Serialize(settings.Validate()));
}
