using Microsoft.Data.Sqlite;

namespace Plico.Core;

internal sealed partial class LibraryCatalog
{
    private const string PdfVersionsMigration = """
        CREATE TABLE IF NOT EXISTS pdf_versions (
            sequence INTEGER PRIMARY KEY,
            document_id INTEGER NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
            file_key TEXT NOT NULL CHECK(length(file_key) = 32),
            archived_ticks INTEGER NOT NULL,
            operation INTEGER NOT NULL,
            page_count INTEGER NOT NULL CHECK(page_count > 0),
            sha256 BLOB NOT NULL CHECK(length(sha256) = 32),
            UNIQUE(document_id, file_key)
        );
        CREATE INDEX IF NOT EXISTS pdf_versions_by_document ON pdf_versions(document_id, sequence DESC);
        CREATE TABLE IF NOT EXISTS pending_pdf_revision (
            slot INTEGER PRIMARY KEY CHECK(slot = 1),
            document_id INTEGER NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
            file_key TEXT NOT NULL CHECK(length(file_key) = 32),
            archived_ticks INTEGER NOT NULL,
            operation INTEGER NOT NULL,
            page_count INTEGER NOT NULL CHECK(page_count > 0),
            sha256 BLOB NOT NULL CHECK(length(sha256) = 32),
            relative_path TEXT NOT NULL,
            next_sha256 BLOB NOT NULL CHECK(length(next_sha256) = 32)
        );
        """;

    private const string PdfVersionColumns = "document_id, file_key, archived_ticks, operation, page_count, sha256";

    internal IReadOnlyList<PdfVersionFile> ReadPdfVersions(long? documentId = null)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        if (documentId is long id) command.Parameters.AddWithValue("$id", id);
        string filter = documentId.HasValue ? "WHERE document_id = $id" : "";
        command.CommandText = $"SELECT {PdfVersionColumns} FROM pdf_versions {filter} ORDER BY sequence DESC;";
        using var reader = command.ExecuteReader();
        var versions = new List<PdfVersionFile>();
        while (reader.Read()) versions.Add(ReadVersionFile(reader));
        return versions;
    }

    internal PdfVersionFile RequirePdfVersion(long documentId, Guid key)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {PdfVersionColumns} FROM pdf_versions WHERE document_id = $id AND file_key = $key;";
        command.Parameters.AddWithValue("$id", documentId);
        command.Parameters.AddWithValue("$key", key.ToString("N"));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadVersionFile(reader) : throw new InvalidOperationException("This PDF version is no longer available.");
    }

    internal byte[]? ReadCurrentPdfFingerprint(long documentId)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT pdf_sha256 FROM documents WHERE id = $id AND is_present = 1;";
        command.Parameters.AddWithValue("$id", documentId);
        return command.ExecuteScalar() as byte[];
    }

    internal PendingPdfRevision? ReadPendingPdfRevision()
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {PdfVersionColumns}, relative_path, next_sha256 FROM pending_pdf_revision WHERE slot = 1;";
        using var reader = command.ExecuteReader();
        return reader.Read() ? new PendingPdfRevision(reader.GetString(6), ReadVersionFile(reader), (byte[])reader[7]) : null;
    }

    internal void BeginPdfRevision(PendingPdfRevision pending)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO pending_pdf_revision(slot, {PdfVersionColumns}, relative_path, next_sha256)
            VALUES (1, $id, $key, $ticks, $operation, $pages, $sha, $path, $next);
            """;
        BindVersionFile(command, pending.Archived);
        command.Parameters.AddWithValue("$path", pending.RelativePath);
        command.Parameters.AddWithValue("$next", pending.ActiveFingerprint);
        command.ExecuteNonQuery();
    }

    internal void CommitPdfRevision(PendingPdfRevision pending, FileVersion version, PdfText text)
    {
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE documents SET modified_ticks = $ticks, file_bytes = $bytes, pdf_sha256 = $sha,
                page_count = $pages, has_text = $hasText, body = $body
            WHERE id = $id AND relative_path = $path AND is_present = 1;
            """;
        command.Parameters.AddWithValue("$id", pending.Archived.DocumentId);
        command.Parameters.AddWithValue("$path", pending.RelativePath);
        command.Parameters.AddWithValue("$ticks", version.LastModifiedTicks);
        command.Parameters.AddWithValue("$bytes", version.Length);
        command.Parameters.AddWithValue("$sha", pending.ActiveFingerprint);
        command.Parameters.AddWithValue("$pages", text.PageCount);
        command.Parameters.AddWithValue("$hasText", text.Body.Length != 0);
        command.Parameters.AddWithValue("$body", text.Body);
        RequireSelection(command.ExecuteNonQuery(), 1);
        command.Parameters.Clear();
        InsertPdfVersion(command, pending.Archived);
        command.Parameters.Clear();
        command.CommandText = "DELETE FROM pending_pdf_revision WHERE slot = 1;";
        RequireSelection(command.ExecuteNonQuery(), 1);
        transaction.Commit();
    }

    internal void SaveInitialPdfVersion(PdfVersionFile file)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        InsertPdfVersion(command, file);
    }

    internal void ClearPendingPdfRevision()
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM pending_pdf_revision WHERE slot = 1;";
        command.ExecuteNonQuery();
    }

    private static void InsertPdfVersion(SqliteCommand command, PdfVersionFile file)
    {
        command.CommandText = $"""
            INSERT INTO pdf_versions({PdfVersionColumns})
            VALUES ($id, $key, $ticks, $operation, $pages, $sha)
            ON CONFLICT(document_id, file_key) DO NOTHING;
            """;
        BindVersionFile(command, file);
        command.ExecuteNonQuery();
    }

    private static void BindVersionFile(SqliteCommand command, PdfVersionFile file)
    {
        command.Parameters.AddWithValue("$id", file.DocumentId);
        command.Parameters.AddWithValue("$key", file.Version.Key.ToString("N"));
        command.Parameters.AddWithValue("$ticks", file.Version.ArchivedAt.UtcTicks);
        command.Parameters.AddWithValue("$operation", (int)file.Version.Change);
        command.Parameters.AddWithValue("$pages", file.Version.PageCount);
        command.Parameters.AddWithValue("$sha", file.Fingerprint);
    }

    private static PdfVersionFile ReadVersionFile(SqliteDataReader reader) => new(reader.GetInt64(0),
        new PdfVersion(Guid.ParseExact(reader.GetString(1), "N"), new DateTimeOffset(reader.GetInt64(2), TimeSpan.Zero),
            (PdfRevisionKind)reader.GetInt32(3), reader.GetInt32(4)), (byte[])reader[5]);
}
