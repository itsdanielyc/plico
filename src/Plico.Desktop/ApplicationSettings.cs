using System.Security.Cryptography;
using System.Text;

namespace Plico.Desktop;

/// <summary>Stores machine-specific settings separately from the user's portable library.</summary>
internal static class ApplicationSettings
{
    internal static string DirectoryPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify), "Plico");

    internal static string LibraryKey(string libraryRoot)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(libraryRoot));
        if (OperatingSystem.IsWindows()) normalized = normalized.ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    internal static void WriteAtomically(string path, string value)
    {
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $"settings-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, value);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
