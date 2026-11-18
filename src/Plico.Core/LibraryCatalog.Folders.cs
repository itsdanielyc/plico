using Microsoft.Data.Sqlite;

namespace Plico.Core;

internal sealed partial class LibraryCatalog
{
    private const string AllDocumentRows = "documents d";

    private static string ScopeDocumentRows(SqliteCommand command, List<string> conditions, string? folderPath)
    {
        if (folderPath is null) return AllDocumentRows;
        if (folderPath == SearchTerms.RootFolderScope)
        {
            conditions.Add("instr(d.relative_path, '/') = 0");
            return AllDocumentRows;
        }

        // The slash and the next ASCII character bound the whole branch in the existing binary path index.
        const char upperBound = (char)(SearchTerms.FolderSeparator + 1);
        command.Parameters.AddWithValue("$folderStart", folderPath + SearchTerms.FolderSeparator);
        command.Parameters.AddWithValue("$folderEnd", folderPath + upperBound);
        conditions.Add("d.id = folder_entries.id AND folder_entries.relative_path >= $folderStart AND folder_entries.relative_path < $folderEnd");
        // Lead with the bounded branch so the candidate scan is proportional to the requested folder.
        return "documents folder_entries CROSS JOIN documents d";
    }
}
