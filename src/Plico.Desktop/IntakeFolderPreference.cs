namespace Plico.Desktop;

/// <summary>Remembers the intake destination and watched-scan choice for each library.</summary>
internal sealed class IntakeFolderPreference(string directory)
{
    private const string FolderSetting = "intake-folder";
    private const string WatchedScansSetting = "intake-folder-watched-scans";
    private const char RelativePathSeparator = '/';
    private readonly string _directory = Path.GetFullPath(directory);

    internal string? Load(string libraryRoot)
    {
        string? relative = ReadSetting(libraryRoot, FolderSetting);
        if (relative is not null) Validate(relative);
        return relative;
    }

    internal void Save(string libraryRoot, string relative)
    {
        Validate(relative);
        ApplicationSettings.WriteAtomically(PathFor(libraryRoot, FolderSetting), relative);
    }

    /// <summary>Reads the optional routing choice. Libraries with only a saved destination use the default.</summary>
    internal bool LoadIncludeWatchedScans(string libraryRoot) =>
        bool.TryParse(ReadSetting(libraryRoot, WatchedScansSetting), out bool include) && include;

    /// <summary>Persists the watched-scan choice without rewriting the existing destination.</summary>
    internal void SaveIncludeWatchedScans(string libraryRoot, bool include) =>
        ApplicationSettings.WriteAtomically(PathFor(libraryRoot, WatchedScansSetting), include.ToString());

    private string? ReadSetting(string libraryRoot, string setting)
    {
        try
        {
            return File.ReadAllText(PathFor(libraryRoot, setting));
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static void Validate(string relative)
    {
        if (relative == ".") return;
        if (relative.Contains('\0') || Path.IsPathRooted(relative) ||
            (Path.DirectorySeparatorChar != RelativePathSeparator && relative.Contains(Path.DirectorySeparatorChar)) ||
            relative.Split(RelativePathSeparator).Any(part => part is "" or "." or ".."))
        {
            throw new InvalidDataException("The saved intake folder path is invalid.");
        }
    }

    private string PathFor(string libraryRoot, string setting) =>
        Path.Combine(_directory, $"{setting}-{ApplicationSettings.LibraryKey(libraryRoot)}.txt");
}
