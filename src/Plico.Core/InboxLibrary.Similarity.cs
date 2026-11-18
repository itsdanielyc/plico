using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Plico.Core;

public sealed partial class InboxLibrary
{
    private const int RelatedTermLimit = 24;
    private const int RelatedCandidateLimit = 80;
    private const int SimilarityResultLimit = 40;
    private const int SemanticPageSize = 24;
    private const int SemanticChunkLength = 1_800;
    private const int SemanticQueryLimit = 1_200;
    private const int MaximumSemanticChunks = (LibraryCatalog.MaximumSourceCharacters / SemanticChunkLength) + 2;
    private const double MinimumRelatedScore = 0.12;

    /// <summary>Finds related present documents by weighted similarity of their indexed words.</summary>
    // O(n + k × m + r log k) for source text n, k bounded candidates of text length m, and r retained results.
    public IReadOnlyList<DocumentSimilarityMatch> FindRelatedDocuments(long documentId, int maximumResults = 10,
        CancellationToken cancellationToken = default)
    {
        ValidateResultLimit(maximumResults);
        var source = _catalog.ReadSimilaritySource(documentId);
        string text = source.Title + "\n" + source.Text;
        var terms = SimilarityWords.DistinctTerms(text, RelatedTermLimit);
        if (terms.Length == 0) return [];
        string expression = string.Join(" OR ", terms.Select(term => $"\"{term}\""));
        var candidates = _catalog.ReadRelatedCandidates(expression, documentId, RelatedCandidateLimit, cancellationToken);
        var vectorSpace = new List<(long Id, Dictionary<string, int> Words)>(candidates.Count);
        var documentsPerTerm = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var vector = SimilarityWords.Count(candidate.Title + "\n" + candidate.Text);
            vectorSpace.Add((candidate.Id, vector));
            foreach (var term in vector.Keys) documentsPerTerm[term] = documentsPerTerm.GetValueOrDefault(term) + 1;
        }
        var weights = documentsPerTerm.ToDictionary(pair => pair.Key,
            pair => Math.Log((candidates.Count + 1.0) / (pair.Value + 1.0)) + 1, StringComparer.Ordinal);
        var sourceVector = SimilarityWords.Count(text);
        double norm = Math.Sqrt(sourceVector.Sum(pair => pair.Value * Math.Pow(weights.GetValueOrDefault(pair.Key, 1), 2)));
        var rank = vectorSpace.Select(item => (item.Id, Score: SimilarityWords.Cosine(sourceVector, item.Words, weights, norm)))
            .Where(item => item.Score >= MinimumRelatedScore).OrderByDescending(item => item.Score).ThenByDescending(item => item.Id)
            .Take(maximumResults);
        return ReadRankedDocuments(rank, cancellationToken);
    }

    /// <summary>Ranks the present library by local embedding similarity and reuses unchanged indexed text.</summary>
    // O(n × c × d + n log r) on cache hits for n documents, up to c chunks, d coordinates and r results.
    // The catalog is read a page at a time. Memory is bounded by one page and the result heap.
    public async Task<IReadOnlyList<DocumentSimilarityMatch>> SearchSemanticallyAsync(string query, ILocalEmbeddingModel model,
        int maximumResults = 20, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateResultLimit(maximumResults);
        ArgumentNullException.ThrowIfNull(model);
        if (string.IsNullOrWhiteSpace(query) || query.Length > SemanticQueryLimit)
            throw new ArgumentException("Enter a specific search of up to 1,200 characters.", nameof(query));
        var embedded = await model.EmbedAsync([query.Trim()], EmbeddingInput.Query, cancellationToken).ConfigureAwait(false);
        if (embedded.Count != 1) throw new InvalidDataException("The local model did not return one query embedding.");
        float[] queryVector = NormalizeEmbedding(embedded[0]);
        var matches = new PriorityQueue<long, (double Score, long Id)>();
        int processed = 0;
        long cursor = 0;
        string modelKey = model.CacheKey;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = _catalog.ReadSimilarityBatch(cursor, SemanticPageSize, cancellationToken);
            if (batch.Count == 0) break;
            var cached = _catalog.ReadEmbeddings(modelKey, batch);
            var pending = new List<(long Id, byte[] Signature, string[] Chunks)>();
            foreach (var source in batch)
            {
                string text = source.Title + "\n" + source.Text;
                byte[] signature = SHA256.HashData(Encoding.UTF8.GetBytes(text));
                if (cached.TryGetValue(source.Id, out var result) && ValidCache(result, signature, queryVector.Length))
                    Consider(source.Id, ScoreEmbedding(queryVector, result));
                else pending.Add((source.Id, signature, SplitEmbeddingText(text)));
            }
            var generated = await EmbedBatchAsync(pending, model, queryVector.Length, cancellationToken).ConfigureAwait(false);
            _catalog.SaveEmbeddings(modelKey, generated);
            foreach (var (id, result) in generated) Consider(id, ScoreEmbedding(queryVector, result));
            processed += batch.Count;
            progress?.Report(processed);
            cursor = batch[^1].Id;
        }
        var ordered = matches.UnorderedItems.OrderByDescending(item => item.Priority)
            .Select(item => (item.Priority.Id, item.Priority.Score));
        return ReadRankedDocuments(ordered, cancellationToken);

        void Consider(long id, double score)
        {
            if (!double.IsFinite(score)) return;
            var rank = (score, id);
            if (matches.Count < maximumResults) matches.Enqueue(id, rank);
            else matches.EnqueueDequeue(id, rank);
        }
    }

    /// <summary>Clears generated vectors so an updated local model can rebuild its index.</summary>
    public void ClearSemanticIndex() => _catalog.ClearEmbeddings();

    private IReadOnlyList<DocumentSimilarityMatch> ReadRankedDocuments(IEnumerable<(long Id, double Score)> matches,
        CancellationToken cancellationToken)
    {
        var result = new List<DocumentSimilarityMatch>();
        foreach (var (id, score) in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { result.Add(new(_catalog.Get(id), score)); }
            catch (InvalidOperationException) { /* A concurrent refresh can remove a ranked result. */ }
        }
        return result;
    }

    private static async Task<IReadOnlyList<(long Id, SavedEmbedding Result)>> EmbedBatchAsync(
        IReadOnlyList<(long Id, byte[] Signature, string[] Chunks)> pending, ILocalEmbeddingModel model, int dimensions,
        CancellationToken token)
    {
        if (pending.Count == 0) return [];
        var vectors = new List<float[]>();
        foreach (var group in pending.SelectMany(item => item.Chunks).Chunk(LocalModelClient.MaximumEmbeddingBatch))
        {
            var batch = await model.EmbedAsync(group, EmbeddingInput.Document, token).ConfigureAwait(false);
            if (batch.Count != group.Length || batch.Any(vector => vector.Length != dimensions))
                throw new InvalidDataException("The local model returned a different embedding shape for documents.");
            vectors.AddRange(batch.Select(NormalizeEmbedding));
        }

        var results = new List<(long Id, SavedEmbedding Result)>(pending.Count);
        int offset = 0;
        foreach (var (id, signature, chunks) in pending)
        {
            byte[] bytes = new byte[dimensions * chunks.Length * sizeof(float)];
            int byteOffset = 0;
            for (int i = 0; i < chunks.Length; i++)
            {
                foreach (float component in vectors[offset++])
                {
                    BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(byteOffset), component);
                    byteOffset += sizeof(float);
                }
            }
            results.Add((id, new(signature, dimensions, chunks.Length, bytes)));
        }
        return results;
    }

    private static string[] SplitEmbeddingText(string text)
    {
        var chunks = new List<string>(MaximumSemanticChunks);
        for (int offset = 0; offset < text.Length && chunks.Count < MaximumSemanticChunks;)
        {
            int length = Math.Min(SemanticChunkLength, text.Length - offset);
            if (offset + length < text.Length && char.IsHighSurrogate(text[offset + length - 1])) length--;
            string chunk = text.Substring(offset, length).Trim();
            if (chunk.Length > 0) chunks.Add(chunk);
            offset += length;
        }
        return chunks.Count == 0 ? ["Untitled document"] : chunks.ToArray();
    }

    private static bool ValidCache(SavedEmbedding embedding, ReadOnlySpan<byte> signature, int dimensions) =>
        embedding.Dimensions == dimensions && embedding.Chunks is > 0 and <= MaximumSemanticChunks &&
        embedding.Vectors.Length == dimensions * embedding.Chunks * sizeof(float) && signature.SequenceEqual(embedding.Signature);

    private static double ScoreEmbedding(ReadOnlySpan<float> query, SavedEmbedding embedding)
    {
        double best = double.NegativeInfinity;
        int position = 0;
        for (int chunk = 0; chunk < embedding.Chunks; chunk++)
        {
            double score = 0;
            foreach (float coordinate in query)
            {
                score += coordinate * BinaryPrimitives.ReadSingleLittleEndian(embedding.Vectors.AsSpan(position));
                position += sizeof(float);
            }
            best = Math.Max(best, score);
        }
        return best;
    }

    private static float[] NormalizeEmbedding(float[] vector)
    {
        if (vector.Length is < 1 or > LocalModelClient.MaximumDimension || vector.Any(component => !float.IsFinite(component)))
            throw new InvalidDataException("The local model returned an invalid vector.");
        double length = Math.Sqrt(vector.Sum(component => (double)component * component));
        if (length <= 0) throw new InvalidDataException("The local model returned an empty vector.");
        return vector.Select(component => (float)(component / length)).ToArray();
    }

    private static void ValidateResultLimit(int maximumResults)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumResults);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumResults, SimilarityResultLimit);
    }
}
