using System.Security.Cryptography;
using System.Text;

namespace Plico.Desktop;

/// <summary>Stores a machine-local watch folder under the path of the library it feeds.</summary>
internal sealed class WatchFolderPreference(string directory)
{
    private readonly string _directory = Path.GetFullPath(directory);

    internal string? Load(string libraryRoot)
    {
        var path = PathFor(libraryRoot);
        if (!File.Exists(path)) return null;
        var folder = File.ReadAllText(path);
        return Path.IsPathFullyQualified(folder) ? folder : null;
    }

    internal void Save(string libraryRoot, string watchedFolder) =>
        ApplicationSettings.WriteAtomically(PathFor(libraryRoot), Path.GetFullPath(watchedFolder));

    private string PathFor(string libraryRoot)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(libraryRoot));
        if (OperatingSystem.IsWindows()) normalized = normalized.ToUpperInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return Path.Combine(_directory, $"watch-folder-{key}.txt");
    }
}
