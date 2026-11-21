using System.Text.Json;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace Plico.Core;

/// <summary>Maintains the transactionally updated local catalog and FTS index.</summary>
internal sealed partial class LibraryCatalog
{
    private const int ConnectionTimeoutSeconds = 5;
    private const int SearchCancellationInstructions = 1_000;
    internal const int PageSize = 60;
    private const string NewestCatalogEntry = "d.id DESC";
    private const string TitleCollation = "plico_title";
    private const string DocumentUnavailableMessage = "This document is no longer in the library.";

    private const string Schema = $$"""
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
            title, body, content='documents', content_rowid='id', tokenize='{{SearchTerms.FtsTokenizer}}'
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
    private const string OcrCopyMigration = """
        ALTER TABLE documents ADD COLUMN ocr_copy_source_id INTEGER REFERENCES documents(id) ON DELETE SET NULL;
        ALTER TABLE documents ADD COLUMN ocr_copy_language TEXT;
        CREATE INDEX documents_ocr_copies ON documents(ocr_copy_source_id, source_sha256, ocr_copy_language)
            WHERE ocr_copy_language IS NOT NULL;
        """;
    private const string TextPresenceMigration = """
        CREATE INDEX documents_text_presence ON documents(has_text, id DESC) WHERE is_present = 1;
        """;
    private static readonly string[] Migrations =
        [Schema, FingerprintMigration, DisplayTitleMigration, PdfFingerprintMigration, OcrCopyMigration, AddedDateIndexMigration,
            PdfMoveMigration, SavedSearchMigration, DocumentDateMigration, TextPresenceMigration, TrashMigration, FilingMigration,
            StructuredMetadataMigration, ArchiveSerialMigration, PdfVersionsMigration, DeadlineMigration, EmbeddingMigration,
            AssistantUndoMigration];

    internal static long SchemaVersion => Migrations.Length;

    private readonly string _connectionString;

    internal LibraryCatalog(string path, TimeZoneInfo? addedDateTimeZone = null)
    {
        _addedDateTimeZone = addedDateTimeZone;
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
        byte[] pdfFingerprint, byte[]? sourceFingerprint = null, OcrCopySource? ocrCopySource = null,
        IReadOnlyList<long>? metadataSourceIds = null, OcrLanguage? recognizedLanguage = null, MigrationWrite? migration = null,
        long? archiveSerialNumber = null)
    {
        metadataSourceIds ??= ocrCopySource is { } origin ? [origin.DocumentId] : null;
        using var connection = Connect();
        using var transaction = metadataSourceIds is { Count: > 0 } || recognizedLanguage.HasValue || migration is not null ||
                                archiveSerialNumber.HasValue
            ? connection.BeginTransaction() : null;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        string initialDocumentDay = "NULL";
        if (metadataSourceIds is { Count: > 0 })
        {
            BindSelection(command, metadataSourceIds);
            initialDocumentDay = SharedSourceDocumentDay;
        }
        command.CommandText = $$"""
            INSERT INTO documents (id, title, relative_path, added_at, modified_ticks, file_bytes, page_count, has_text, body,
                                   source_sha256, pdf_sha256, ocr_copy_source_id, ocr_copy_language, document_day)
            VALUES ((SELECT last_id + 1 FROM document_identity), $title, $path, $added, $modified, $bytes, $pages, $hasText, $text, $source, $pdf, $ocrSource, $ocrLanguage,
                    {{initialDocumentDay}})
            ON CONFLICT(relative_path) DO UPDATE SET
                title = CASE WHEN documents.display_title IS NULL THEN excluded.title
                             ELSE documents.display_title || char(10) || excluded.title END,
                modified_ticks = excluded.modified_ticks, file_bytes = excluded.file_bytes,
                page_count = excluded.page_count, has_text = excluded.has_text, body = excluded.body, is_present = 1,
                source_sha256 = excluded.source_sha256, pdf_sha256 = excluded.pdf_sha256,
                ocr_copy_source_id = excluded.ocr_copy_source_id, ocr_copy_language = excluded.ocr_copy_language
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
        command.Parameters.Add("$ocrSource", SqliteType.Integer).Value = (object?)ocrCopySource?.DocumentId ?? DBNull.Value;
        command.Parameters.Add("$ocrLanguage", SqliteType.Text).Value = (object?)ocrCopySource?.CatalogKey ?? DBNull.Value;
        long documentId = (long)command.ExecuteScalar()!;
        if (metadataSourceIds is { Count: > 0 })
        {
            command.CommandText = """
                INSERT OR IGNORE INTO document_tags (document_id, tag_key)
                SELECT $copy, dt.tag_key FROM json_each($ids) source
                JOIN document_tags dt ON dt.document_id = source.value;
                """;
            command.Parameters.AddWithValue("$copy", documentId);
            command.ExecuteNonQuery();
        }
        if (recognizedLanguage is { } language)
        {
            command.Parameters.Clear();
            command.CommandText = """
                INSERT INTO document_ocr_languages (document_id, language) VALUES ($id, $language)
                ON CONFLICT(document_id) DO UPDATE SET language = excluded.language;
                """;
            command.Parameters.AddWithValue("$id", documentId);
            command.Parameters.AddWithValue("$language", language.ToString());
            command.ExecuteNonQuery();
        }
        if (migration is not null) WriteMigratedMetadata(connection, transaction!, documentId, migration, isNew: true);
        if (archiveSerialNumber is long serial) WriteArchiveSerialNumber(connection, transaction!, documentId, serial);
        transaction?.Commit();
        return documentId;
    }

    internal bool ReservesPath(string relativePath)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM documents WHERE relative_path = $path);";
        command.Parameters.AddWithValue("$path", relativePath);
        return (long)command.ExecuteScalar()! == 1;
    }

    internal IReadOnlyList<CatalogFileReference> FindSource(byte[] fingerprint) =>
        ReadImportCandidates("source_sha256 = $value AND ocr_copy_language IS NULL", fingerprint);

    // The first edit archives the generated OCR bytes. An edited copy is reusable again if those bytes are restored.
    internal IReadOnlyList<CatalogFileReference> FindOcrCopies(byte[] fingerprint, OcrCopySource source) =>
        ReadImportCandidates("""
            source_sha256 = $value AND ocr_copy_source_id = $ocrSource AND ocr_copy_language = $ocrLanguage AND has_text = 1
            AND pdf_sha256 = COALESCE(
                (SELECT v.sha256 FROM pdf_versions v WHERE v.document_id = documents.id ORDER BY v.sequence LIMIT 1),
                pdf_sha256)
            """, fingerprint, source);

    internal IReadOnlyList<CatalogFileReference> FindLegacyPdfs(long fileBytes) =>
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

    private IReadOnlyList<CatalogFileReference> ReadImportCandidates(string condition, object value, OcrCopySource? ocrCopySource = null)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT id, relative_path, modified_ticks, file_bytes FROM documents
            WHERE is_present = 1 AND {condition} ORDER BY id DESC;
            """;
        command.Parameters.AddWithValue("$value", value);
        if (ocrCopySource is { } source)
        {
            command.Parameters.AddWithValue("$ocrSource", source.DocumentId);
            command.Parameters.AddWithValue("$ocrLanguage", source.CatalogKey);
        }
        return ReadFileReferences(command);
    }

    internal IReadOnlyList<CatalogFileReference> ReadSelectedFiles(IReadOnlyList<long> documentIds)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.id, d.relative_path, d.modified_ticks, d.file_bytes
            FROM json_each($ids) selection JOIN documents d ON d.id = selection.value
            WHERE d.is_present = 1 ORDER BY selection.key;
            """;
        int expected = BindSelection(command, documentIds);
        var files = ReadFileReferences(command);
        RequireSelection(files.Count, expected);
        return files;
    }

    private static List<CatalogFileReference> ReadFileReferences(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var candidates = new List<CatalogFileReference>();
        while (reader.Read())
        {
            candidates.Add(new(reader.GetInt64(0), reader.GetString(1),
                new FileVersion(reader.GetInt64(2), reader.GetInt64(3), Present: true)));
        }
        return candidates;
    }

    internal DocumentSummary Get(long documentId) => Query(new DocumentFilter(), documentId).Items.SingleOrDefault() ??
        throw new InvalidOperationException(DocumentUnavailableMessage);

    internal FileVersion GetFileVersion(long documentId)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT modified_ticks, file_bytes FROM documents WHERE id = $id AND is_present = 1;";
        command.Parameters.AddWithValue("$id", documentId);
        using var result = command.ExecuteReader();
        if (!result.Read()) throw new InvalidOperationException(DocumentUnavailableMessage);
        return new FileVersion(result.GetInt64(0), result.GetInt64(1), Present: true);
    }

    internal DocumentPage Find(DocumentFilter filter, CancellationToken cancellationToken = default, int maximumResults = PageSize) =>
        Query(filter, cancellationToken: cancellationToken, maximumResults: maximumResults);

    internal string ReadIndexedText(long documentId)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT body FROM documents WHERE id = $id AND is_present = 1;";
        command.Parameters.AddWithValue("$id", documentId);
        return command.ExecuteScalar() as string ?? throw new InvalidOperationException(DocumentUnavailableMessage);
    }

    // O(log n + p) for p pending documents among n catalog rows, with constant application space.
    internal (long Unreviewed, bool HasDocuments) ReadInboxStatus()
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT count(*), EXISTS(SELECT 1 FROM documents WHERE is_present = 1)
            FROM documents WHERE is_present = 1 AND reviewed = 0;
            """;
        using var row = command.ExecuteReader();
        row.Read();
        return (row.GetInt64(0), row.GetBoolean(1));
    }

    // O(log n + m) for m unavailable entries among n catalog rows, with constant application space.
    internal long CountUnavailableDocuments()
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM documents WHERE is_present = 0;";
        return (long)command.ExecuteScalar()!;
    }

    // O(log n + m log m) time and O(m) output space for m unavailable paths among n rows.
    internal IReadOnlyList<string> ReadUnavailablePaths(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT relative_path FROM documents WHERE is_present = 0 ORDER BY relative_path;";
        using var rows = command.ExecuteReader();
        var paths = new List<string>();
        while (rows.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            paths.Add(rows.GetString(0));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return paths;
    }

    private const string UnavailablePdfQuery = """
        SELECT id, relative_path, modified_ticks, file_bytes, pdf_sha256
        FROM documents WHERE is_present = 0
        """;

    internal IReadOnlyList<IndexedPdf> ReadUnavailablePdfs(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = UnavailablePdfQuery + " ORDER BY relative_path;";
        using var rows = command.ExecuteReader();
        var result = new List<IndexedPdf>();
        while (rows.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(ReadUnavailablePdf(rows));
        }
        return result;
    }

    internal IndexedPdf GetUnavailablePdf(string relativePath)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = UnavailablePdfQuery + " AND relative_path = $path;";
        command.Parameters.AddWithValue("$path", relativePath);
        using var rows = command.ExecuteReader();
        if (!rows.Read()) throw new InvalidOperationException("This PDF is no longer marked unavailable. Refresh the index.");
        return ReadUnavailablePdf(rows);
    }

    private static IndexedPdf ReadUnavailablePdf(SqliteDataReader row)
    {
        var version = new FileVersion(row.GetInt64(2), row.GetInt64(3), Present: false);
        var fingerprint = row.IsDBNull(4) ? null : row.GetFieldValue<byte[]>(4);
        return new IndexedPdf(row.GetInt64(0), row.GetString(1), version, fingerprint);
    }

    private DocumentPage Query(DocumentFilter filter, long? documentId = null,
        CancellationToken cancellationToken = default, int maximumResults = PageSize, Action<DocumentSummary>? visit = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegative(filter.Offset);
        var search = SearchTerms.Compile(filter.Query);
        if (search.QueryIssue is not null || (filter.InboxOnly && search.Reviewed == true))
        {
            return new([], HasMore: false) { QueryIssue = search.QueryIssue };
        }

        using var connection = Connect();
        if (cancellationToken.CanBeCanceled)
        {
            // This connection is unpooled, so the handler's lifetime is the current read.
            raw.sqlite3_progress_handler(connection.Handle!, SearchCancellationInstructions,
                static state => state is CancellationToken { IsCancellationRequested: true } ? 1 : 0, cancellationToken);
        }
        using var command = connection.CreateCommand();
        var conditions = new List<string> { "d.is_present = 1" };
        string from = ScopeDocumentRows(command, conditions, search.FolderPath);
        string join = "";
        string match = "''";
        bool hasSearch = search.FullTextExpression.Length > 0;
        bool mixedSearch = search.Advanced is { NativeText: null };
        if (hasSearch)
        {
            join = mixedSearch
                ? $"LEFT JOIN documents_search($expression, '{SearchRank}') ON documents_search.rowid = d.id"
                : "JOIN documents_search ON documents_search.rowid = d.id";
            if (visit is null)
                match = mixedSearch ? $"CASE WHEN documents_search.rowid IS NULL THEN '' ELSE {SearchSnippet} END" : SearchSnippet;
            if (!mixedSearch)
            {
                conditions.Add("documents_search MATCH $expression");
                conditions.Add($"documents_search.rank MATCH '{SearchRank}'");
            }
            command.Parameters.AddWithValue("$expression", search.FullTextExpression);
        }
        if (mixedSearch && search.Advanced is { } advanced)
            conditions.Add(BooleanCondition(connection, command, advanced.Root));
        if (filter.InboxOnly || search.Reviewed.HasValue)
        {
            conditions.Add("d.reviewed = $reviewed");
            command.Parameters.AddWithValue("$reviewed", search.Reviewed.GetValueOrDefault());
        }
        if (search.HasText is bool hasText)
        {
            conditions.Add("d.has_text = $hasText");
            command.Parameters.AddWithValue("$hasText", hasText);
        }
        if (filter.Tag is not null)
        {
            conditions.Add("""
                d.id IN (
                    WITH RECURSIVE branch(key) AS (
                        SELECT $tag UNION
                        SELECT appearance.tag_key FROM tag_appearance appearance JOIN branch ON appearance.parent_key = branch.key
                    )
                    SELECT dt.document_id FROM document_tags dt JOIN branch ON dt.tag_key = branch.key
                )
                """);
            command.Parameters.AddWithValue("$tag", SearchTerms.TagKey(filter.Tag));
        }
        if (search.TagKeys.Count > 0)
        {
            conditions.Add("""
                d.id IN (
                    SELECT dt.document_id
                    FROM json_each($tagKeys) requested JOIN document_tags dt ON dt.tag_key = requested.value
                    GROUP BY dt.document_id HAVING count(*) = $tagCount
                )
                """);
            command.Parameters.AddWithValue("$tagKeys", JsonSerializer.Serialize(search.TagKeys));
            command.Parameters.AddWithValue("$tagCount", search.TagKeys.Count);
        }
        if (search.AddedPeriod is { } addedPeriod)
        {
            ApplyAddedDateFilter(connection, command, conditions, addedPeriod);
        }
        if (search.DocumentPeriod is { } documentPeriod)
        {
            ApplyDocumentDateFilter(command, conditions, documentPeriod);
        }
        foreach (var metadata in search.MetadataTerms) conditions.Add(MetadataSearchCondition(command, metadata));
        if (documentId is not null)
        {
            conditions.Add("d.id = $id");
            command.Parameters.AddWithValue("$id", documentId.Value);
        }

        string order = SortOrder(connection, filter.Sort, hasSearch, mixedSearch);
        command.CommandText = $"""
            SELECT d.id, COALESCE(d.display_title, d.title) AS display_name, d.relative_path, d.added_at, d.reviewed, d.page_count, d.has_text,
                   (SELECT json_group_array(t.label ORDER BY t.tag_key)
                    FROM document_tags dt JOIN tags t USING (tag_key) WHERE dt.document_id = d.id),
                   {match}, d.document_day,
                   CASE WHEN d.pdf_sha256 IS NULL THEN printf('%lld:%lld', d.modified_ticks, d.file_bytes)
                        ELSE hex(d.pdf_sha256) END
            FROM {from} {join}
            WHERE {string.Join(" AND ", conditions)}
            ORDER BY {order}
            LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$limit", (long)maximumResults + 1);
        command.Parameters.AddWithValue("$offset", filter.Offset);
        try
        {
            return ReadResultPage(command, maximumResults, cancellationToken, visit);
        }
        catch (SqliteException error) when (error.SqliteErrorCode == raw.SQLITE_INTERRUPT && cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("The catalog read was cancelled.", error, cancellationToken);
        }
    }

    private static DocumentPage ReadResultPage(SqliteCommand command, int maximumResults, CancellationToken cancellationToken,
        Action<DocumentSummary>? visit)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var results = command.ExecuteReader();
        var documents = new List<DocumentSummary>(Math.Min(PageSize, maximumResults));
        while (results.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (documents.Count == maximumResults)
            {
                return new(documents, HasMore: true);
            }
            var (excerpt, highlights) = ReadSnippet(results.GetString(8));
            var document = new DocumentSummary(
                results.GetInt64(0), results.GetString(1), results.GetString(2),
                DateTimeOffset.FromUnixTimeSeconds(results.GetInt64(3)), results.GetBoolean(4),
                results.GetInt32(5), results.GetBoolean(6),
                JsonSerializer.Deserialize<string[]>(results.GetString(7)) ?? [],
                excerpt)
            {
                DocumentDate = results.IsDBNull(9) ? null : DateOnly.FromDayNumber(results.GetInt32(9)),
                MatchHighlights = highlights,
                ContentRevision = results.GetString(10)
            };
            if (visit is null) documents.Add(document);
            else visit(document);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(documents, HasMore: false);
    }

    private static string SortOrder(SqliteConnection connection, DocumentSort sort, bool hasSearch, bool mixedSearch)
    {
        switch (sort)
        {
            case DocumentSort.Automatic:
                if (!hasSearch) return NewestCatalogEntry;
                return mixedSearch ? $"documents_search.rank NULLS LAST, {NewestCatalogEntry}" : $"documents_search.rank, {NewestCatalogEntry}";
            case DocumentSort.Newest:
                return NewestCatalogEntry;
            case DocumentSort.DocumentDate:
                return $"d.document_day DESC, {NewestCatalogEntry}";
            case DocumentSort.Title:
                // SQLite's built-in case-insensitive collation only folds ASCII.
                connection.CreateCollation(TitleCollation, StringComparer.InvariantCultureIgnoreCase.Compare);
                return $"display_name COLLATE {TitleCollation}, {NewestCatalogEntry}";
            default:
                throw new ArgumentOutOfRangeException(nameof(sort), sort, "Unknown document sort order.");
        }
    }

    internal IReadOnlyList<string> Tags(bool includeUnused = false)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = includeUnused ? "SELECT label FROM tags ORDER BY tag_key;" : """
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
            throw new InvalidOperationException(DocumentUnavailableMessage);
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
            throw new InvalidOperationException(DocumentUnavailableMessage);
        }
    }

    internal void SetReviewedDocuments(IReadOnlyList<long> documentIds, bool reviewed)
    {
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE documents SET reviewed = $reviewed
            WHERE is_present = 1 AND id IN (SELECT value FROM json_each($ids));
            """;
        command.Parameters.AddWithValue("$reviewed", reviewed);
        int expected = BindSelection(command, documentIds);
        RequireSelection(command.ExecuteNonQuery(), expected);
        transaction.Commit();
    }

    internal void AddTagToDocuments(IReadOnlyList<long> documentIds, string tag)
    {
        var label = SearchTerms.CleanLabel(tag);
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        BindAndRequirePresentSelection(command, documentIds);

        command.CommandText = """
            INSERT INTO tags (tag_key, label) VALUES ($tag, $label) ON CONFLICT(tag_key) DO NOTHING;
            INSERT OR IGNORE INTO document_tags (document_id, tag_key)
            SELECT value, $tag FROM json_each($ids);
            """;
        command.Parameters.AddWithValue("$tag", label.ToUpperInvariant());
        command.Parameters.AddWithValue("$label", label);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    internal int RemoveTagFromDocuments(IReadOnlyList<long> documentIds, string tag, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string tagKey = SearchTerms.TagKey(tag);
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        BindAndRequirePresentSelection(command, documentIds);
        command.CommandText = """
            DELETE FROM document_tags
            WHERE tag_key = $tag AND document_id IN (SELECT value FROM json_each($ids));
            """;
        command.Parameters.AddWithValue("$tag", tagKey);
        int changed = command.ExecuteNonQuery();
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
        return changed;
    }

    private static void BindAndRequirePresentSelection(SqliteCommand command, IReadOnlyList<long> documentIds)
    {
        command.CommandText = """
            SELECT count(*) FROM documents
            WHERE is_present = 1 AND id IN (SELECT value FROM json_each($ids));
            """;
        int expected = BindSelection(command, documentIds);
        RequireSelection((long)command.ExecuteScalar()!, expected);
    }

    private static int BindSelection(SqliteCommand command, IReadOnlyList<long> documentIds)
    {
        var seen = new HashSet<long>();
        var uniqueIds = new List<long>(documentIds.Count);
        foreach (long id in documentIds)
        {
            if (seen.Add(id)) uniqueIds.Add(id);
        }
        command.Parameters.AddWithValue("$ids", JsonSerializer.Serialize(uniqueIds));
        return uniqueIds.Count;
    }

    private static void RequireSelection(long matched, int expected)
    {
        if (matched != expected)
        {
            throw new InvalidOperationException("A selected document is unavailable. Refresh the index and select the documents again.");
        }
    }

    internal Dictionary<string, IndexedPdf> ReadIndexedFiles()
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, relative_path, modified_ticks, file_bytes, is_present, pdf_sha256, has_text FROM documents WHERE is_present >= 0;";
        using var results = command.ExecuteReader();
        var versions = new Dictionary<string, IndexedPdf>(StringComparer.Ordinal);
        while (results.Read())
        {
            string path = results.GetString(1);
            var version = new FileVersion(results.GetInt64(2), results.GetInt64(3), results.GetBoolean(4));
            var fingerprint = results.IsDBNull(5) ? null : results.GetFieldValue<byte[]>(5);
            versions.Add(path, new IndexedPdf(results.GetInt64(0), path, version, fingerprint, results.GetBoolean(6)));
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

    internal IReadOnlyList<string> MarkMissing(IEnumerable<string> relativePaths)
    {
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE documents SET is_present = 0 WHERE relative_path = $path AND is_present = 1;";
        var path = command.Parameters.Add("$path", SqliteType.Text);
        List<string>? unavailable = null;
        foreach (var relativePath in relativePaths)
        {
            path.Value = relativePath;
            if (command.ExecuteNonQuery() == 1) (unavailable ??= new List<string>()).Add(relativePath);
        }
        transaction.Commit();
        return unavailable is null ? [] : unavailable;
    }

    internal void BackupTo(string destination)
    {
        using var source = Connect();
        var settings = new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false };
        using var backup = new SqliteConnection(settings.ToString());
        backup.Open();
        source.BackupDatabase(backup);
    }

    private SqliteConnection Connect()
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
            // A write that returns rows commits when its reader closes, a step Microsoft.Data.Sqlite does not retry.
            // SQLite's own busy handler makes that commit wait for a reader on another connection.
            using var wait = connection.CreateCommand();
            wait.CommandText = $"PRAGMA busy_timeout = {ConnectionTimeoutSeconds * 1000};";
            wait.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}
