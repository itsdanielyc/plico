using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Plico.Core;

/// <summary>Maintains the transactionally updated local catalog and FTS index.</summary>
internal sealed class LibraryCatalog
{
    private const int SchemaVersion = 1;
    private const int ConnectionTimeoutSeconds = 5;
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
        using var migration = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = migration;
        command.CommandText = "PRAGMA user_version";
        var version = (long)command.ExecuteScalar()!;
        if (version == 0)
        {
            command.CommandText = $"{Schema}\nPRAGMA user_version = {SchemaVersion};";
            command.ExecuteNonQuery();
        }
        else if (version != SchemaVersion)
        {
            throw new InvalidDataException($"This library uses catalog version {version}. Open it with a version of Plico that supports that catalog.");
        }
        migration.Commit();
    }

    internal long Upsert(string relativePath, string title, PdfText text, FileVersion version)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO documents (title, relative_path, added_at, modified_ticks, file_bytes, page_count, has_text, body)
            VALUES ($title, $path, $added, $modified, $bytes, $pages, $hasText, $text)
            ON CONFLICT(relative_path) DO UPDATE SET
                title = excluded.title, modified_ticks = excluded.modified_ticks, file_bytes = excluded.file_bytes,
                page_count = excluded.page_count, has_text = excluded.has_text, body = excluded.body, is_present = 1
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
        return (long)command.ExecuteScalar()!;
    }

    internal DocumentSummary Get(long documentId) => Query(new DocumentFilter(), documentId).Items.Single();

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
            match = "snippet(documents_search, 1, '', '', ' … ', 18)";
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
            SELECT d.id, d.title, d.relative_path, d.added_at, d.reviewed, d.page_count, d.has_text,
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
                results.GetString(8)));
        }
        return new(documents, HasMore: false);
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

    internal Dictionary<string, FileVersion> ReadFileVersions()
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT relative_path, modified_ticks, file_bytes, is_present FROM documents;";
        using var results = command.ExecuteReader();
        var versions = new Dictionary<string, FileVersion>(StringComparer.Ordinal);
        while (results.Read())
        {
            versions.Add(results.GetString(0), new(results.GetInt64(1), results.GetInt64(2), results.GetBoolean(3)));
        }
        return versions;
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
