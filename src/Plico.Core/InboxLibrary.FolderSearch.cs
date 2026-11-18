namespace Plico.Core;

public sealed partial class InboxLibrary
{
    /// <summary>Scopes an existing query to a selected library folder while preserving the other terms.</summary>
    /// <param name="folder">An existing directory selected inside the library's PDF directory.</param>
    /// <param name="query">The current search expression.</param>
    /// <exception cref="ArgumentException">The folder is outside the library or the query cannot represent the new scope.</exception>
    public string CreateFolderSearchQuery(string folder, string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return SearchTerms.WithFolderScope(query, GetRelativeLibraryFolderPath(folder));
    }

    /// <summary>Resolves a selected directory to its relative path within the library's PDF directory.</summary>
    /// <param name="folder">The existing directory to resolve.</param>
    /// <returns>A slash-separated relative path, or a single dot for the PDF directory.</returns>
    /// <exception cref="ArgumentException">The resolved directory is outside the library's PDF directory.</exception>
    /// <exception cref="DirectoryNotFoundException">The chosen directory or the PDF directory is unavailable.</exception>
    public string GetRelativeLibraryFolderPath(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        string pdfRoot = ResolveDirectoryLinks(PdfDirectory);
        string selected = ResolveDirectoryLinks(folder);
        if (!IsAtOrWithinFolder(pdfRoot, selected))
        {
            throw new ArgumentException("Choose a folder inside the library's PDF directory.", nameof(folder));
        }
        return Path.GetRelativePath(pdfRoot, selected).Replace(Path.DirectorySeparatorChar, SearchTerms.FolderSeparator);
    }

    private string ResolveIntakeDirectory(string folder)
    {
        try
        {
            return Path.GetFullPath(Path.Combine(PdfDirectory, GetRelativeLibraryFolderPath(folder)));
        }
        catch (DirectoryNotFoundException error)
        {
            throw new DirectoryNotFoundException("The intake folder is unavailable. Choose an existing library folder.", error);
        }
    }
}
