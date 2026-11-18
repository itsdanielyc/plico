namespace Plico.Desktop;

/// <summary>Remembers the last chosen folder independently from the portable library.</summary>
internal static class LibraryPreference
{
    private static string PreferencePath => Path.Combine(ApplicationSettings.DirectoryPath, "library-location.txt");

    internal static string LoadOrCreateDefault()
    {
        if (File.Exists(PreferencePath))
        {
            var preferred = File.ReadAllText(PreferencePath).TrimEnd('\r', '\n');
            if (preferred.Length > 0) return preferred;
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(documents))
        {
            documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents");
        }
        return Directory.CreateDirectory(Path.Combine(documents, "Plico")).FullName;
    }

    internal static void Save(string root) => ApplicationSettings.WriteAtomically(PreferencePath, root);
}
