namespace Plico.Core;

public sealed partial class InboxLibrary
{
    /// <summary>Scopes an existing query to a selected library folder while preserving the other terms.</summary>
    /// <param name="folder">An existing directory selected inside the library's PDF directory.</param>
    /// <param name="query">The current search expression.</param>
    /// <exception cref="ArgumentException">The folder is outside the library or the query cannot represent the new scope.</exception>
    public string CreateFolderSearchQuery(string folder, string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(query);
        string pdfRoot = ResolveDirectoryLinks(PdfDirectory);
        string selected = ResolveDirectoryLinks(folder);
        if (!IsAtOrWithinFolder(pdfRoot, selected))
        {
            throw new ArgumentException("Choose a folder inside the library's PDF directory.", nameof(folder));
        }
        string relative = Path.GetRelativePath(pdfRoot, selected).Replace(Path.DirectorySeparatorChar, SearchTerms.FolderSeparator);
        return SearchTerms.WithFolderScope(query, relative);
    }
}
