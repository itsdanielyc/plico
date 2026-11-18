using System.IO.Compression;

namespace Plico.Core;

/// <summary>Reads only the manifest and selected document entries from a directory or ZIP export.</summary>
internal sealed class PaperlessExportFiles : IDisposable
{
    internal const string ManifestName = "manifest.json";
    private readonly string? _directory;
    private readonly ZipArchive? _archive;
    private readonly string _prefix;

    internal PaperlessExportFiles(string path)
    {
        if (Directory.Exists(path))
        {
            _directory = Path.GetFullPath(path);
            _prefix = "";
            return;
        }
        _archive = ZipFile.OpenRead(path);
        string? manifest = _archive.Entries.Select(entry => entry.FullName).FirstOrDefault(name => name == ManifestName)
            ?? _archive.Entries.Select(entry => entry.FullName).SingleOrDefault(name => name.EndsWith("/" + ManifestName, StringComparison.Ordinal));
        _prefix = manifest is null ? "" : manifest[..^ManifestName.Length];
    }

    internal Stream? Open(string relative)
    {
        string normalized = MigrationFiles.ValidateRelativePath(relative);
        if (_archive is not null) return _archive.GetEntry(_prefix + normalized)?.Open();
        return MigrationFiles.LocateFile(_directory!, normalized) is { } path ? File.OpenRead(path) : null;
    }

    internal IEnumerable<string> SplitManifests()
    {
        if (_archive is not null)
        {
            foreach (var entry in _archive.Entries)
            {
                if (entry.FullName.StartsWith(_prefix, StringComparison.Ordinal) &&
                    entry.FullName.EndsWith("-manifest.json", StringComparison.OrdinalIgnoreCase))
                    yield return entry.FullName[_prefix.Length..];
            }
            yield break;
        }
        var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint, RecurseSubdirectories = true };
        foreach (string path in Directory.EnumerateFiles(_directory!, "*-manifest.json", options))
            yield return Path.GetRelativePath(_directory!, path).Replace(Path.DirectorySeparatorChar, '/');
    }

    public void Dispose() => _archive?.Dispose();
}
