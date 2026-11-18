namespace Plico.Desktop;

/// <summary>Stores the machine-local watched folder and startup choice for each library.</summary>
internal sealed class WatchFolderPreference(string directory)
{
    private const string FolderSetting = "watch-folder";
    private const string CatchUpSetting = "watch-catch-up-on-open";
    private readonly string _directory = Path.GetFullPath(directory);

    internal string? Load(string libraryRoot)
    {
        string? folder = ReadSetting(PathFor(libraryRoot, FolderSetting));
        return folder is not null && Path.IsPathFullyQualified(folder) ? folder : null;
    }

    internal void Save(string libraryRoot, string watchedFolder) =>
        ApplicationSettings.WriteAtomically(PathFor(libraryRoot, FolderSetting), Path.GetFullPath(watchedFolder));

    internal bool LoadCatchUpOnOpen(string libraryRoot) =>
        bool.TryParse(ReadSetting(PathFor(libraryRoot, CatchUpSetting)), out bool enabled) && enabled;

    internal void SaveCatchUpOnOpen(string libraryRoot, bool enabled) =>
        ApplicationSettings.WriteAtomically(PathFor(libraryRoot, CatchUpSetting), enabled.ToString());

    private string PathFor(string libraryRoot, string setting) =>
        Path.Combine(_directory, $"{setting}-{ApplicationSettings.LibraryKey(libraryRoot)}.txt");

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
