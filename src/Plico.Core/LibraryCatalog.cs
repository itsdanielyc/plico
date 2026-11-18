using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Plico.Core;

/// <summary>Maintains the transactionally updated local catalog and FTS index.</summary>
internal sealed class LibraryCatalog
{
    private const int ConnectionTimeoutSeconds = 5;
    private const int SnippetMarker = 30;
    private const int SnippetLeadingCharacters = 20;
    internal const int PageSize = 60;

    private const string Schema = """
        CREATE TABLE documents(
            id INTEGER PRIMARY KEY,
            title TEXT NOT NULL,
            relative_path TEXT NOT NULL UNIQUE,
            added_at INTEGER NOT NULL,
            modified_ticks INTEGER NOT NULL,
            file_bytes INTEGER NOT NULL,
            is_present INTEGER NOT NULL DEFAULT 1,
            reviewed INTEGER NOT NULL DEFAULT 0,
            page_count INTEGER NOT NULL,
            has_text INTEGER NOT NULL,
            body TEXT NOT NULL
        );
        CREATE INDEX documents_inbox ON documents(is_present, reviewed, id DESC);
        CREATE TABLE tags(
            tag_key TEXT PRIMARY KEY,
            label TEXT NOT NULL
        ) WITHOUT ROWID;
        CREATE TABLE document_tags(
            document_id INTEGER NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
            tag_key TEXT NOT NULL REFERENCES tags(tag_key),
            PRIMARY KEY(document_id, tag_key)
        ) WITHOUT ROWID;
        CREATE INDEX document_tags_by_tag ON document_tags(tag_key, document_id);
        CREATE VIRTUAL TABLE documents_search USING fts5(
            title, body, content='documents', content_rowid='id', tokenize='unicode61 remove_diacritics 2'
        );
        CREATE TRIGGER documents_insert AFTER INSERT ON documents BEGIN
            INSERT INTO documents_search (rowid, title, body) VALUES (new.id, new.title, new.body);
        END;
        CREATE TRIGGER documents_update AFTER UPDATE OF title, body ON documents BEGIN
            INSERT INTO documents_search (documents_search, rowid, title, body) VALUES ('delete', old.id, old.title, old.body);
            INSERT INTO documents_search (rowid, title, body) VALUES (new.id, new.title, new.body);
        END;
        CREATE TRIGGER documents_delete AFTER DELETE ON documents BEGIN
            INSERT INTO documents_search (documents_search, rowid, title, body) VALUES ('delete', old.id, old.title, old.body);
        END;
        """;

    private const string FingerprintMigration = """
        ALTER TABLE documents ADD COLUMN source_sha256 BLOB;
        CREATE INDEX documents_source_sha256 ON documents(source_sha256) WHERE source_sha256 IS NOT NULL;
        CREATE INDEX documents_legacy_file_bytes ON documents(file_bytes) WHERE source_sha256 IS NULL AND is_present = 1;
        """;

    private const string DisplayTitleMigration = "ALTER TABLE documents ADD COLUMN display_title TEXT;";
    private const string PdfFingerprintMigration = "ALTER TABLE documents ADD COLUMN pdf_sha256 BLOB;";
    private static readonly string[] Migrations = [Schema, FingerprintMigration, DisplayTitleMigration, PdfFingerprintMigration];

    private readonly string _connectionString;

    internal LibraryCatalog(string path)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            DefaultTimeout = ConnectionTimeoutSeconds,
            ForeignKeys = true,
            Pooling = false
        }.ToString();

        using var connection = Connect();
        Migrate(connection);
    }

    private static void Migrate(SqliteConnection connection)
    {
        using var migration = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = migration;
        command.CommandText = "PRAGMA user_version";
        var version = (long)command.ExecuteScalar()!;
        if (version < 0 || version > Migrations.Length)
        {
            throw new InvalidDataException($"This library uses catalog version {version}. Open it with a version of Plico that supports that catalog.");
        }
        for (int step = (int)version; step < Migrations.Length; step++)
        {
            command.CommandText = Migrations[step];
            command.ExecuteNonQuery();
        }
        if (version != Migrations.Length)
        {
            command.CommandText = $"PRAGMA user_version = {Migrations.Length};";
            command.ExecuteNonQuery();
        }
        migration.Commit();
    }

    internal long Upsert(string relativePath, string title, PdfText text, FileVersion version,
        byte[] pdfFingerprint, byte[]? sourceFingerprint = null)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO documents (title, relative_path, added_at, modified_ticks, file_bytes, page_count, has_text, body, source_sha256, pdf_sha256)
            VALUES ($title, $path, $added, $modified, $bytes, $pages, $hasText, $text, $source, $pdf)
            ON CONFLICT(relative_path) DO UPDATE SET
                title = CASE WHEN documents.display_title IS NULL THEN excluded.title
                             ELSE documents.display_title || char(10) || excluded.title END,
                modified_ticks = excluded.modified_ticks, file_bytes = excluded.file_bytes,
                page_count = excluded.page_count, has_text = excluded.has_text, body = excluded.body, is_present = 1,
                source_sha256 = excluded.source_sha256, pdf_sha256 = excluded.pdf_sha256
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$path", relativePath);
        command.Parameters.AddWithValue("$added", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$modified", version.LastModifiedTicks);
        command.Parameters.AddWithValue("$bytes", version.Length);
        command.Parameters.AddWithValue("$pages", text.PageCount);
        command.Parameters.AddWithValue("$hasText", text.Body.Length > 0);
        command.Parameters.AddWithValue("$text", text.Body);
        command.Parameters.AddWithValue("$source", sourceFingerprint is null ? DBNull.Value : sourceFingerprint);
        command.Parameters.AddWithValue("$pdf", pdfFingerprint);
        return (long)command.ExecuteScalar()!;
    }

    internal bool ReservesPath(string relativePath)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM documents WHERE relative_path = $path);";
        command.Parameters.AddWithValue("$path", relativePath);
        return (long)command.ExecuteScalar()! == 1;
    }

    internal IReadOnlyList<ImportCandidate> FindSource(byte[] fingerprint) =>
        ReadImportCandidates("source_sha256 = $value", fingerprint);

    internal IReadOnlyList<ImportCandidate> FindLegacyPdfs(long fileBytes) =>
        ReadImportCandidates("source_sha256 IS NULL AND file_bytes = $value", fileBytes);

    internal void RecordSource(long id, byte[] fingerprint)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE documents SET source_sha256 = $source WHERE id = $id;";
        command.Parameters.AddWithValue("$source", fingerprint);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private IReadOnlyList<ImportCandidate> ReadImportCandidates(string condition, object value)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT id, relative_path, modified_ticks, file_bytes FROM documents
            WHERE is_present = 1 AND {condition} ORDER BY id DESC;
            """;
        command.Parameters.AddWithValue("$value", value);
        using var reader = command.ExecuteReader();
        var candidates = new List<ImportCandidate>();
        while (reader.Read())
        {
            candidates.Add(new(reader.GetInt64(0), reader.GetString(1),
                new FileVersion(reader.GetInt64(2), reader.GetInt64(3), Present: true)));
        }
        return candidates;
    }

    internal DocumentSummary Get(long documentId) => Query(new DocumentFilter(), documentId).Items.SingleOrDefault() ??
        throw new InvalidOperationException("This document is no longer in the library.");

    internal DocumentPage Find(DocumentFilter filter) => Query(filter);

    private DocumentPage Query(DocumentFilter filter, long? documentId = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(filter.Offset);
        var expression = SearchTerms.FullTextExpression(filter.Query);
        if (!string.IsNullOrWhiteSpace(filter.Query) && expression.Length == 0)
        {
            return new([], HasMore: false);
        }

        using var connection = Connect();
        using var command = connection.CreateCommand();
        var conditions = new List<string> { "d.is_present = 1" };
        string join = "";
        string match = "''";
        string order = "d.id DESC";
        if (expression.Length > 0)
        {
            join = "JOIN documents_search ON documents_search.rowid = d.id";
            match = $"replace(replace(snippet(documents_search, 1, char({SnippetMarker}), char({SnippetMarker}), ' … ', 18), char(13), ''), char(10), ' ')";
            order = "documents_search.rank, d.id DESC";
            conditions.Add("documents_search MATCH $expression");
            command.Parameters.AddWithValue("$expression", expression);
        }
        if (filter.InboxOnly)
        {
            conditions.Add("d.reviewed = 0");
        }
        if (filter.Tag is not null)
        {
            conditions.Add("d.id IN (SELECT document_id FROM document_tags WHERE tag_key = $tag)");
            command.Parameters.AddWithValue("$tag", SearchTerms.TagKey(filter.Tag));
        }
        if (documentId is not null)
        {
            conditions.Add("d.id = $id");
            command.Parameters.AddWithValue("$id", documentId.Value);
        }

        command.CommandText = $"""
            SELECT d.id, COALESCE(d.display_title, d.title), d.relative_path, d.added_at, d.reviewed, d.page_count, d.has_text,
                   (SELECT json_group_array(t.label ORDER BY t.tag_key)
                    FROM document_tags dt JOIN tags t USING (tag_key) WHERE dt.document_id = d.id),
                   {match}
            FROM documents d {join}
            WHERE {string.Join(" AND ", conditions)}
            ORDER BY {order}
            LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$limit", PageSize + 1);
        command.Parameters.AddWithValue("$offset", filter.Offset);
        using var results = command.ExecuteReader();
        var documents = new List<DocumentSummary>(PageSize);
        while (results.Read())
        {
            if (documents.Count == PageSize)
            {
                return new(documents, HasMore: true);
            }
            documents.Add(new DocumentSummary(
                results.GetInt64(0), results.GetString(1), results.GetString(2),
                DateTimeOffset.FromUnixTimeSeconds(results.GetInt64(3)), results.GetBoolean(4),
                results.GetInt32(5), results.GetBoolean(6),
                JsonSerializer.Deserialize<string[]>(results.GetString(7)) ?? [],
                TrimSnippet(results.GetString(8))));
        }
        return new(documents, HasMore: false);
    }

    private static string TrimSnippet(string excerpt)
    {
        const char marker = (char)SnippetMarker;
        int matchStart = excerpt.IndexOf(marker);
        if (matchStart < 0) return "";
        if (matchStart <= SnippetLeadingCharacters) return excerpt.Replace(marker.ToString(), "").Trim();

        int wordBreak = excerpt.IndexOf(' ', matchStart - SnippetLeadingCharacters);
        int start = wordBreak >= 0 && wordBreak < matchStart ? wordBreak + 1 : matchStart;
        return "… " + excerpt[start..].Replace(marker.ToString(), "").Trim();
    }

    internal IReadOnlyList<string> Tags()
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.label FROM tags t
            WHERE EXISTS (SELECT 1 FROM document_tags dt JOIN documents d ON dt.document_id = d.id
                          WHERE dt.tag_key = t.tag_key AND d.is_present = 1)
            ORDER BY t.tag_key;
            """;
        using var results = command.ExecuteReader();
        var tags = new List<string>();
        while (results.Read()) tags.Add(results.GetString(0));
        return tags;
    }

    internal void AddTag(long documentId, string tag)
    {
        var label = SearchTerms.CleanLabel(tag);
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO tags (tag_key, label) VALUES ($tag, $label) ON CONFLICT(tag_key) DO NOTHING;
            INSERT INTO document_tags (document_id, tag_key) VALUES ($id, $tag) ON CONFLICT DO NOTHING;
            """;
        command.Parameters.AddWithValue("$tag", label.ToUpperInvariant());
        command.Parameters.AddWithValue("$label", label);
        command.Parameters.AddWithValue("$id", documentId);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    internal void RemoveTag(long documentId, string tag)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM document_tags WHERE document_id = $id AND tag_key = $tag;";
        command.Parameters.AddWithValue("$id", documentId);
        command.Parameters.AddWithValue("$tag", SearchTerms.TagKey(tag));
        command.ExecuteNonQuery();
    }

    internal void SetReviewed(long documentId, bool reviewed)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE documents SET reviewed = $reviewed WHERE id = $id AND is_present = 1;";
        command.Parameters.AddWithValue("$reviewed", reviewed);
        command.Parameters.AddWithValue("$id", documentId);
        if (command.ExecuteNonQuery() == 0)
        {
            throw new InvalidOperationException("This document is no longer in the library.");
        }
    }

    internal void SetTitle(long documentId, string displayTitle, string filename)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE documents SET display_title = $display, title = $searchTitle
            WHERE id = $id AND is_present = 1;
            """;
        bool usesFilename = string.Equals(displayTitle, filename, StringComparison.Ordinal);
        command.Parameters.AddWithValue("$display", usesFilename ? DBNull.Value : displayTitle);
        command.Parameters.AddWithValue("$searchTitle", usesFilename ? filename : $"{displayTitle}\n{filename}");
        command.Parameters.AddWithValue("$id", documentId);
        if (command.ExecuteNonQuery() == 0)
        {
            throw new InvalidOperationException("This document is no longer in the library.");
        }
    }

    internal void SetReviewedDocuments(IReadOnlyList<long> documentIds, bool reviewed)
    {
        var uniqueIds = documentIds.Distinct().ToArray();
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE documents SET reviewed = $reviewed
            WHERE is_present = 1 AND id IN (SELECT value FROM json_each($ids));
            """;
        command.Parameters.AddWithValue("$reviewed", reviewed);
        command.Parameters.AddWithValue("$ids", JsonSerializer.Serialize(uniqueIds));
        if (command.ExecuteNonQuery() != uniqueIds.Length)
        {
            throw new InvalidOperationException("A selected document is unavailable. Refresh the index and select the documents again.");
        }
        transaction.Commit();
    }

    internal Dictionary<string, IndexedPdf> ReadIndexedFiles()
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, relative_path, modified_ticks, file_bytes, is_present, pdf_sha256 FROM documents;";
        using var results = command.ExecuteReader();
        var versions = new Dictionary<string, IndexedPdf>(StringComparer.Ordinal);
        while (results.Read())
        {
            string path = results.GetString(1);
            var version = new FileVersion(results.GetInt64(2), results.GetInt64(3), results.GetBoolean(4));
            var fingerprint = results.IsDBNull(5) ? null : results.GetFieldValue<byte[]>(5);
            versions.Add(path, new IndexedPdf(results.GetInt64(0), path, version, fingerprint));
        }
        return versions;
    }

    internal void UpdateFileStamp(long documentId, FileVersion version, byte[] pdfFingerprint)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE documents SET modified_ticks = $modified, file_bytes = $bytes, is_present = 1,
                pdf_sha256 = $pdf WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$modified", version.LastModifiedTicks);
        command.Parameters.AddWithValue("$bytes", version.Length);
        command.Parameters.AddWithValue("$pdf", pdfFingerprint);
        command.Parameters.AddWithValue("$id", documentId);
        command.ExecuteNonQuery();
    }

    internal void Relocate(long documentId, string relativePath, string filename, FileVersion version)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE documents SET relative_path = $path, modified_ticks = $modified, file_bytes = $bytes, is_present = 1,
                title = CASE WHEN display_title IS NULL THEN $name ELSE display_title || char(10) || $name END
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$path", relativePath);
        command.Parameters.AddWithValue("$modified", version.LastModifiedTicks);
        command.Parameters.AddWithValue("$bytes", version.Length);
        command.Parameters.AddWithValue("$name", filename);
        command.Parameters.AddWithValue("$id", documentId);
        command.ExecuteNonQuery();
    }

    internal void MarkMissing(IEnumerable<string> relativePaths)
    {
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE documents SET is_present = 0 WHERE relative_path = $path AND is_present = 1;";
        var path = command.Parameters.Add("$path", SqliteType.Text);
        foreach (var relativePath in relativePaths)
        {
            path.Value = relativePath;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private SqliteConnection Connect()
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}
