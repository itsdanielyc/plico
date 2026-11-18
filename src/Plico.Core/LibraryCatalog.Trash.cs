using Microsoft.Data.Sqlite;

namespace Plico.Core;

internal sealed record TrashFile(long DocumentId, string OriginalPath, byte[] Fingerprint);

internal sealed partial class LibraryCatalog
{
    private const string TrashMigration = """
        CREATE TABLE IF NOT EXISTS document_trash (
            document_id INTEGER PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
            original_path TEXT NOT NULL,
            trashed_at INTEGER NOT NULL
        );
        CREATE TABLE IF NOT EXISTS document_identity (
            slot INTEGER PRIMARY KEY CHECK (slot = 1),
            last_id INTEGER NOT NULL
        );
        INSERT OR IGNORE INTO document_identity SELECT 1, COALESCE(max(id), 0) FROM documents;
        CREATE TRIGGER IF NOT EXISTS documents_identity_insert AFTER INSERT ON documents BEGIN
            UPDATE document_identity SET last_id = max(last_id, new.id) WHERE slot = 1;
        END;
        """;

    internal IReadOnlyList<TrashedDocument> ReadTrash()
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT trash.document_id, COALESCE(d.display_title, d.title), trash.original_path, trash.trashed_at
            FROM document_trash trash JOIN documents d ON d.id = trash.document_id
            ORDER BY trash.trashed_at DESC, trash.document_id DESC;
            """;
        using var rows = command.ExecuteReader();
        var documents = new List<TrashedDocument>();
        while (rows.Read())
        {
            documents.Add(new(rows.GetInt64(0), rows.GetString(1), rows.GetString(2),
                DateTimeOffset.FromUnixTimeSeconds(rows.GetInt64(3))));
        }
        return documents;
    }

    internal TrashFile ReadTrashFile(long documentId)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT trash.original_path, d.pdf_sha256
            FROM document_trash trash JOIN documents d ON d.id = trash.document_id
            WHERE d.id = $id;
            """;
        command.Parameters.AddWithValue("$id", documentId);
        using var row = command.ExecuteReader();
        if (!row.Read()) throw new InvalidOperationException("This PDF is no longer in Trash.");
        return new(documentId, row.GetString(0), row.GetFieldValue<byte[]>(1));
    }

    internal IReadOnlyList<TrashFile> ReadTrashFiles()
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT trash.document_id, trash.original_path, d.pdf_sha256
            FROM document_trash trash JOIN documents d ON d.id = trash.document_id ORDER BY trash.document_id;
            """;
        using var rows = command.ExecuteReader();
        var files = new List<TrashFile>();
        while (rows.Read()) files.Add(new(rows.GetInt64(0), rows.GetString(1), rows.GetFieldValue<byte[]>(2)));
        return files;
    }

    internal void CommitTrashMove(PdfMoveIntent move, FileVersion version, string filename, bool toTrash)
    {
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE documents
            SET relative_path = $destination, modified_ticks = $modified, file_bytes = $bytes,
                is_present = $presence, pdf_sha256 = $hash,
                title = CASE WHEN $presence = -1 THEN title WHEN display_title IS NULL THEN $filename
                             ELSE display_title || char(10) || $filename END
            WHERE id = $id AND relative_path = $source AND is_present = $previousPresence;
            """;
        command.Parameters.AddWithValue("$destination", move.DestinationRelativePath);
        command.Parameters.AddWithValue("$source", move.SourceRelativePath);
        command.Parameters.AddWithValue("$modified", version.LastModifiedTicks);
        command.Parameters.AddWithValue("$bytes", version.Length);
        command.Parameters.AddWithValue("$presence", toTrash ? -1 : 1);
        command.Parameters.AddWithValue("$previousPresence", toTrash ? 1 : -1);
        command.Parameters.AddWithValue("$hash", move.Fingerprint);
        command.Parameters.AddWithValue("$filename", filename);
        command.Parameters.AddWithValue("$id", move.DocumentId);
        RequireSelection(command.ExecuteNonQuery(), 1);

        command.Parameters.Clear();
        command.Parameters.AddWithValue("$id", move.DocumentId);
        if (toTrash)
        {
            command.CommandText = """
                INSERT INTO document_trash (document_id, original_path, trashed_at) VALUES ($id, $path, $time);
                """;
            command.Parameters.AddWithValue("$path", move.SourceRelativePath);
            command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }
        else
        {
            command.CommandText = "DELETE FROM document_trash WHERE document_id = $id;";
        }
        RequireSelection(command.ExecuteNonQuery(), 1);
        command.Parameters.Clear();
        command.CommandText = "DELETE FROM pending_pdf_move WHERE slot = 1;";
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    internal void CompletePurge(long documentId)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM documents WHERE id = $id AND is_present = -1;";
        command.Parameters.AddWithValue("$id", documentId);
        RequireSelection(command.ExecuteNonQuery(), 1);
    }
}
