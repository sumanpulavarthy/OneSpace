using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OneSpace.Application.Abstractions;
using OneSpace.Domain.Policies;

namespace OneSpace.Application.Policies;

/// <summary>
/// FR-3 hybrid retrieval. Keyword ($text) and vector (embeddings) results are combined by reciprocal rank fusion,
/// so a policy found by meaning ("can I work remotely?" → Work From Home Policy) ranks even when no keyword
/// matches, and an exact-term hit is not lost to a vaguely similar passage. Without embeddings, or if the
/// embedding call fails, this is plain keyword search: retrieval degrades, it never fails.
/// </summary>
public sealed partial class HybridPolicySearch(
    IPolicyDocumentRepository documents,
    IPolicyChunkStore chunks,
    ITextEmbedder embedder,
    IOptions<RetrievalOptions> options,
    ILogger<HybridPolicySearch> logger) : IPolicySearch
{
    public async Task<IReadOnlyList<PolicySearchResult>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        limit = Math.Clamp(limit, 1, 10);
        var o = options.Value;

        var keyword = await documents.SearchAsync(query, Math.Max(limit, 5), cancellationToken);
        var semantic = await VectorSearchAsync(query, o, cancellationToken);
        if (semantic.Count == 0) return keyword.Take(limit).ToList();

        // Best passage per document, in similarity order.
        var vectorHits = semantic
            .Where(m => m.Similarity >= o.MinSimilarity)
            .GroupBy(m => m.Chunk.DocumentId)
            .Select(g => g.OrderByDescending(m => m.Similarity).First())
            .OrderByDescending(m => m.Similarity)
            .ToList();
        if (vectorHits.Count == 0) return keyword.Take(limit).ToList();

        var fused = new Dictionary<string, (double Score, PolicySearchResult? Keyword, PolicyChunkMatch? Vector)>();
        for (var i = 0; i < keyword.Count; i++)
        {
            var id = keyword[i].Document.Id;
            fused[id] = (Rrf(i, o.RrfK), keyword[i], null);
        }
        for (var i = 0; i < vectorHits.Count; i++)
        {
            var id = vectorHits[i].Chunk.DocumentId;
            var existing = fused.GetValueOrDefault(id);
            fused[id] = (existing.Score + Rrf(i, o.RrfK), existing.Keyword, vectorHits[i]);
        }

        var results = new List<PolicySearchResult>();
        foreach (var (id, hit) in fused.OrderByDescending(f => f.Value.Score).Take(limit))
        {
            var document = hit.Keyword?.Document ?? await documents.GetAsync(id, cancellationToken);
            if (document is null) continue; // chunk of a document deleted since indexing
            // The passage that matched by meaning is usually the better excerpt for the LLM to quote.
            var snippet = hit.Vector?.Chunk.Text ?? hit.Keyword!.Snippet;
            results.Add(new PolicySearchResult(document, hit.Score, snippet));
        }

        LogFused(logger, keyword.Count, vectorHits.Count, results.Count);
        return results;
    }

    private async Task<IReadOnlyList<PolicyChunkMatch>> VectorSearchAsync(string query, RetrievalOptions o, CancellationToken cancellationToken)
    {
        if (!embedder.IsEnabled) return [];
        try
        {
            var vector = (await embedder.EmbedAsync([query], cancellationToken))[0];
            return await chunks.SearchAsync(vector, embedder.ModelId, o.VectorCandidates, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogVectorFailed(logger, ex);
            return [];
        }
    }

    /// <summary>Reciprocal rank fusion score for a 0-based rank.</summary>
    public static double Rrf(int rank, int k) => 1.0 / (k + rank + 1);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Vector search failed; falling back to keyword search")]
    private static partial void LogVectorFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hybrid policy search: {Keyword} keyword + {Vector} vector hits → {Results} results")]
    private static partial void LogFused(ILogger logger, int keyword, int vector, int results);
}
