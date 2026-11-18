using Microsoft.Data.Sqlite;

namespace Plico.Core;

internal sealed partial class LibraryCatalog
{
    // O(n log(m)) time for n source links among m catalog links and O(1) application space.
    internal string RenameTag(string currentName, string newName, bool mergeIntoExisting, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string sourceKey = SearchTerms.TagKey(currentName);
        string label = SearchTerms.CleanLabel(newName);
        string destinationKey = label.ToUpperInvariant();
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$sourceKey", sourceKey);
        command.Parameters.AddWithValue("$label", label);
        command.CommandText = "SELECT label FROM tags WHERE tag_key = $sourceKey;";
        if (command.ExecuteScalar() is not string) throw new InvalidOperationException("Choose an available tag to rename.");

        string result = sourceKey == destinationKey
            ? UpdateTagLabel(command, label)
            : MoveTagLinks(command, destinationKey, label, mergeIntoExisting);
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
        return result;
    }

    private static string UpdateTagLabel(SqliteCommand command, string label)
    {
        command.CommandText = "UPDATE tags SET label = $label WHERE tag_key = $sourceKey;";
        command.ExecuteNonQuery();
        return label;
    }

    private static string MoveTagLinks(SqliteCommand command, string destinationKey, string label, bool mergeIntoExisting)
    {
        command.Parameters.AddWithValue("$destinationKey", destinationKey);
        command.CommandText = "SELECT label FROM tags WHERE tag_key = $destinationKey;";
        string? existingLabel = command.ExecuteScalar() as string;
        if (existingLabel is not null && !mergeIntoExisting)
        {
            throw new InvalidOperationException($"The tag “{existingLabel}” already exists. Choose Merge tags to combine them.");
        }

        command.CommandText = """
            INSERT INTO tags (tag_key, label) VALUES ($destinationKey, $label) ON CONFLICT(tag_key) DO NOTHING;
            INSERT OR IGNORE INTO document_tags (document_id, tag_key)
            SELECT document_id, $destinationKey FROM document_tags WHERE tag_key = $sourceKey;
            UPDATE saved_searches SET scope_tag_key = $destinationKey WHERE scope_tag_key = $sourceKey;
            DELETE FROM document_tags WHERE tag_key = $sourceKey;
            DELETE FROM tags WHERE tag_key = $sourceKey;
            """;
        command.ExecuteNonQuery();
        return existingLabel ?? label;
    }
}
