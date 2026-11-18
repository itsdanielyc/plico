namespace Plico.Desktop;

internal enum WatchSource { Scanner, Phone }

/// <summary>Stores the machine-local intake paths and startup choices for each library.</summary>
internal sealed class WatchFolderPreference(string directory)
{
    private const string FolderSetting = "watch-folder";
    private const string CatchUpSetting = "watch-catch-up-on-open";
    private readonly string _directory = Path.GetFullPath(directory);

    internal string? Load(string libraryRoot, WatchSource source = WatchSource.Scanner)
    {
        string? folder = ReadSetting(PathFor(libraryRoot, FolderSetting, source));
        return folder is not null && Path.IsPathFullyQualified(folder) ? folder : null;
    }

    internal void Save(string libraryRoot, string watchedFolder, WatchSource source = WatchSource.Scanner) =>
        ApplicationSettings.WriteAtomically(PathFor(libraryRoot, FolderSetting, source), Path.GetFullPath(watchedFolder));

    internal bool LoadCatchUpOnOpen(string libraryRoot, WatchSource source = WatchSource.Scanner) =>
        bool.TryParse(ReadSetting(PathFor(libraryRoot, CatchUpSetting, source)), out bool enabled) && enabled;

    internal void SaveCatchUpOnOpen(string libraryRoot, bool enabled, WatchSource source = WatchSource.Scanner) =>
        ApplicationSettings.WriteAtomically(PathFor(libraryRoot, CatchUpSetting, source), enabled.ToString());

    private string PathFor(string libraryRoot, string setting, WatchSource source)
    {
        string sourcePrefix = source == WatchSource.Phone ? "phone-" : "";
        return Path.Combine(_directory, $"{sourcePrefix}{setting}-{ApplicationSettings.LibraryKey(libraryRoot)}.txt");
    }

    private static string? ReadSetting(string path)
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
