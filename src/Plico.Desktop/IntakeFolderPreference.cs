namespace Plico.Desktop;

/// <summary>Remembers the manual intake destination for each library.</summary>
internal sealed class IntakeFolderPreference(string directory)
{
    private const char RelativePathSeparator = '/';
    private readonly string _directory = Path.GetFullPath(directory);

    internal string? Load(string libraryRoot)
    {
        try
        {
            string relative = File.ReadAllText(PathFor(libraryRoot));
            Validate(relative);
            return relative;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    internal void Save(string libraryRoot, string relative)
    {
        Validate(relative);
        ApplicationSettings.WriteAtomically(PathFor(libraryRoot), relative);
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

    private string PathFor(string libraryRoot) => Path.Combine(_directory, $"intake-folder-{ApplicationSettings.LibraryKey(libraryRoot)}.txt");
}
