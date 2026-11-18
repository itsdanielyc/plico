using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Plico.Core;

internal sealed partial class LibraryCatalog
{
    private const string AssistantUndoMigration = """
        CREATE TABLE IF NOT EXISTS assistant_filing_undo (
            document_id INTEGER PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
            before_json TEXT NOT NULL,
            applied_json TEXT NOT NULL
        );
        """;

    private sealed record FilingSnapshot(string SearchTitle, string? DisplayTitle, int? DocumentDay, string RelativePath,
        IReadOnlyList<string> TagKeys, DocumentMetadata Metadata);

    internal void ApplyLocalFiling(LocalFilingEdit edit, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(edit);
        token.ThrowIfCancellationRequested();
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        var before = ReadFilingSnapshot(connection, transaction, edit.DocumentId);
        string? title = edit.Title is null ? null : InboxLibrary.NormalizeTitle(edit.Title);
        var tags = before.TagKeys.ToHashSet(StringComparer.Ordinal);
        foreach (string tag in edit.Tags)
        {
            token.ThrowIfCancellationRequested();
            tags.Add(EnsureMetadataTag(connection, transaction, tag));
        }
        var after = before with
        {
            DisplayTitle = title ?? before.DisplayTitle,
            SearchTitle = title is null ? before.SearchTitle : title + "\n" + Path.GetFileNameWithoutExtension(before.RelativePath),
            DocumentDay = edit.DocumentDate?.DayNumber ?? before.DocumentDay,
            TagKeys = tags.Order(StringComparer.Ordinal).ToArray(),
            Metadata = before.Metadata with
            {
                DocumentTypeId = edit.DocumentTypeId ?? before.Metadata.DocumentTypeId,
                CorrespondentIds = before.Metadata.CorrespondentIds.Concat(edit.CorrespondentIds).Distinct().Order().ToArray()
            }
        };
        string previous = JsonSerializer.Serialize(before, MetadataJson);
        string applied = JsonSerializer.Serialize(after, MetadataJson);
        if (previous == applied) return;
        WriteFilingSnapshot(connection, transaction, edit.DocumentId, after, token);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO assistant_filing_undo (document_id, before_json, applied_json) VALUES ($id, $before, $after)
            ON CONFLICT(document_id) DO UPDATE SET before_json = excluded.before_json, applied_json = excluded.applied_json;
            """;
        command.Parameters.AddWithValue("$id", edit.DocumentId);
        command.Parameters.AddWithValue("$before", previous);
        command.Parameters.AddWithValue("$after", applied);
        command.ExecuteNonQuery();
        token.ThrowIfCancellationRequested();
        transaction.Commit();
    }

    internal bool UndoLocalFiling(long documentId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT before_json, applied_json FROM assistant_filing_undo WHERE document_id = $id;";
        command.Parameters.AddWithValue("$id", documentId);
        string previous;
        string applied;
        using (var row = command.ExecuteReader())
        {
            if (!row.Read()) return false;
            previous = row.GetString(0);
            applied = row.GetString(1);
        }
        string current = JsonSerializer.Serialize(ReadFilingSnapshot(connection, transaction, documentId), MetadataJson);
        if (current != applied) throw new InvalidOperationException("Metadata changed after local filing. Keep your later edits in the document editor.");
        WriteFilingSnapshot(connection, transaction, documentId, ReadMetadataJson<FilingSnapshot>(previous), token);
        command.CommandText = "DELETE FROM assistant_filing_undo WHERE document_id = $id;";
        command.ExecuteNonQuery();
        token.ThrowIfCancellationRequested();
        transaction.Commit();
        return true;
    }

    private static FilingSnapshot ReadFilingSnapshot(SqliteConnection connection, SqliteTransaction transaction, long documentId)
    {
        var metadata = ReadDocumentMetadata(connection, transaction, documentId);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT title, display_title, document_day, relative_path,
                (SELECT json_group_array(tag_key ORDER BY tag_key) FROM document_tags WHERE document_id = d.id)
            FROM documents d WHERE id = $id AND is_present = 1;
            """;
        command.Parameters.AddWithValue("$id", documentId);
        using var row = command.ExecuteReader();
        if (!row.Read()) throw new InvalidOperationException(DocumentUnavailableMessage);
        return new(row.GetString(0), row.IsDBNull(1) ? null : row.GetString(1), row.IsDBNull(2) ? null : row.GetInt32(2),
            row.GetString(3), ReadMetadataJson<string[]>(row.GetString(4)), metadata);
    }

    private static void WriteFilingSnapshot(SqliteConnection connection, SqliteTransaction transaction, long documentId,
        FilingSnapshot snapshot, CancellationToken token)
    {
        WriteDocumentMetadata(connection, transaction, documentId, snapshot.Metadata, token);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE documents SET title = $title, display_title = $display, document_day = $day WHERE id = $id;
            DELETE FROM document_tags WHERE document_id = $id;
            INSERT INTO document_tags (document_id, tag_key) SELECT $id, value FROM json_each($keys);
            """;
        command.Parameters.AddWithValue("$id", documentId);
        command.Parameters.AddWithValue("$title", snapshot.SearchTitle);
        command.Parameters.AddWithValue("$display", DbValue(snapshot.DisplayTitle));
        command.Parameters.AddWithValue("$day", DbValue(snapshot.DocumentDay));
        command.Parameters.AddWithValue("$keys", JsonSerializer.Serialize(snapshot.TagKeys, MetadataJson));
        command.ExecuteNonQuery();
    }
}
