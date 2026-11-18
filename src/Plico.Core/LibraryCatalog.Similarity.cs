using Microsoft.Data.Sqlite;
using System.Buffers.Binary;
using System.Text.Json;

namespace Plico.Core;

internal sealed record SavedEmbedding(byte[] Signature, int Dimensions, int Chunks, byte[] Vectors);

internal sealed partial class LibraryCatalog
{
    internal const int MaximumSourceCharacters = 10_800;
    private const string EmbeddingMigration = """
        CREATE TABLE IF NOT EXISTS semantic_embeddings (
            model_key TEXT NOT NULL,
            document_id INTEGER NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
            signature BLOB NOT NULL,
            dimensions INTEGER NOT NULL,
            chunks INTEGER NOT NULL,
            vectors BLOB NOT NULL,
            PRIMARY KEY(model_key, document_id)
        ) WITHOUT ROWID;
        CREATE INDEX IF NOT EXISTS semantic_documents ON semantic_embeddings(document_id);
        """;

    internal SimilaritySource ReadSimilaritySource(long documentId)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, COALESCE(display_title, title), substr(body, 1, $characters)
            FROM documents WHERE id = $id AND is_present = 1;
            """;
        command.Parameters.AddWithValue("$id", documentId);
        command.Parameters.AddWithValue("$characters", MaximumSourceCharacters);
        using var row = command.ExecuteReader();
        return row.Read() ? new(row.GetInt64(0), row.GetString(1), row.GetString(2))
            : throw new InvalidOperationException(DocumentUnavailableMessage);
    }

    internal IReadOnlyList<SimilaritySource> ReadRelatedCandidates(string expression, long documentId, int maximum, CancellationToken token)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.id, COALESCE(d.display_title, d.title), substr(d.body, 1, $characters)
            FROM documents_search JOIN documents d ON d.id = documents_search.rowid
            WHERE documents_search MATCH $query AND d.is_present = 1 AND d.id <> $id
            ORDER BY rank, d.id LIMIT $maximum;
            """;
        command.Parameters.AddWithValue("$characters", MaximumSourceCharacters);
        command.Parameters.AddWithValue("$query", expression);
        command.Parameters.AddWithValue("$id", documentId);
        command.Parameters.AddWithValue("$maximum", maximum);
        return ReadSimilaritySources(command, token);
    }

    internal IReadOnlyList<SimilaritySource> ReadSimilarityBatch(long afterId, int maximum, CancellationToken token)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, COALESCE(display_title, title), substr(body, 1, $characters)
            FROM documents WHERE is_present = 1 AND id > $after ORDER BY id LIMIT $maximum;
            """;
        command.Parameters.AddWithValue("$characters", MaximumSourceCharacters);
        command.Parameters.AddWithValue("$after", afterId);
        command.Parameters.AddWithValue("$maximum", maximum);
        return ReadSimilaritySources(command, token);
    }

    private static IReadOnlyList<SimilaritySource> ReadSimilaritySources(SqliteCommand command, CancellationToken token)
    {
        using var rows = command.ExecuteReader();
        var candidates = new List<SimilaritySource>();
        while (rows.Read())
        {
            token.ThrowIfCancellationRequested();
            candidates.Add(new(rows.GetInt64(0), rows.GetString(1), rows.GetString(2)));
        }
        return candidates;
    }

    internal Dictionary<long, SavedEmbedding> ReadEmbeddings(string modelKey, IReadOnlyList<SimilaritySource> batch)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT e.document_id, e.signature, e.dimensions, e.chunks, e.vectors
            FROM json_each($ids) ids JOIN semantic_embeddings e ON e.document_id = ids.value
            WHERE e.model_key = $model;
            """;
        command.Parameters.AddWithValue("$ids", JsonSerializer.Serialize(batch.Select(item => item.Id)));
        command.Parameters.AddWithValue("$model", modelKey);
        using var rows = command.ExecuteReader();
        var cache = new Dictionary<long, SavedEmbedding>(batch.Count);
        while (rows.Read()) cache.Add(rows.GetInt64(0), new(rows.GetFieldValue<byte[]>(1), rows.GetInt32(2),
            rows.GetInt32(3), rows.GetFieldValue<byte[]>(4)));
        return cache;
    }

    internal void SaveEmbeddings(string modelKey, IReadOnlyList<(long DocumentId, SavedEmbedding Embedding)> entries)
    {
        if (entries.Count == 0) return;
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO semantic_embeddings (model_key, document_id, signature, dimensions, chunks, vectors)
            SELECT $model, $id, $hash, $dimensions, $chunks, $vectors
            WHERE EXISTS (SELECT 1 FROM documents WHERE id = $id AND is_present = 1)
            ON CONFLICT(model_key, document_id) DO UPDATE SET signature = excluded.signature,
                dimensions = excluded.dimensions, chunks = excluded.chunks, vectors = excluded.vectors;
            """;
        command.Parameters.AddWithValue("$model", modelKey);
        var id = command.Parameters.Add("$id", SqliteType.Integer);
        var hash = command.Parameters.Add("$hash", SqliteType.Blob);
        var dimensions = command.Parameters.Add("$dimensions", SqliteType.Integer);
        var chunks = command.Parameters.Add("$chunks", SqliteType.Integer);
        var vectors = command.Parameters.Add("$vectors", SqliteType.Blob);
        command.Prepare();
        foreach (var (documentId, entry) in entries)
        {
            id.Value = documentId;
            hash.Value = entry.Signature;
            dimensions.Value = entry.Dimensions;
            chunks.Value = entry.Chunks;
            vectors.Value = entry.Vectors;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    internal void ClearEmbeddings()
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM semantic_embeddings;";
        command.ExecuteNonQuery();
    }
}
