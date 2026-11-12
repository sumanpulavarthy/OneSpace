using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OneSpace.Application.Abstractions;
using OneSpace.Domain.Policies;

namespace OneSpace.Application.Policies;

/// <summary>
/// Keeps the vector index in step with the policy cache: embeds documents that are new or changed (by content hash)
/// or were embedded with another model, and drops chunks of removed documents. Idempotent; runs after each sync.
/// </summary>
public sealed partial class PolicyEmbeddingIndexer(
    IPolicyDocumentRepository documents,
    IPolicyChunkStore chunks,
    ITextEmbedder embedder,
    IOptions<RetrievalOptions> options,
    ILogger<PolicyEmbeddingIndexer> logger)
{
    /// <summary>Texts per embedding request; providers cap batch size and request size.</summary>
    internal const int BatchSize = 16;

    public async Task<PolicyIndexResult> IndexAsync(CancellationToken cancellationToken)
    {
        if (!embedder.IsEnabled) return new PolicyIndexResult(0, 0, 0, Enabled: false);

        var o = options.Value;
        var model = embedder.ModelId;
        var all = await documents.GetAllAsync(cancellationToken);
        var indexed = await chunks.GetIndexedHashesAsync(model, cancellationToken);

        var embedded = 0;
        var unchanged = 0;
        foreach (var document in all)
        {
            var hash = PolicyChunker.Hash(document.Title + "\n" + document.Category + "\n" + document.Content);
            if (indexed.TryGetValue(document.Id, out var existing) && existing == hash)
            {
                unchanged++;
                continue;
            }

            var passages = PolicyChunker.Split(document.Content, o.ChunkSize, o.ChunkOverlap);
            var vectors = new List<float[]>(passages.Count);
            foreach (var batch in passages.Chunk(BatchSize))
            {
                var inputs = batch.Select(p => PolicyChunker.EmbeddingInput(document.Title, document.Category, p)).ToList();
                vectors.AddRange(await embedder.EmbedAsync(inputs, cancellationToken));
            }

            await chunks.ReplaceChunksAsync(document.Id, passages.Select((text, i) => new PolicyChunk
            {
                Id = $"{document.Id}#{i}",
                DocumentId = document.Id,
                Ordinal = i,
                Title = document.Title,
                Category = document.Category,
                Text = text,
                ContentHash = hash,
                EmbeddingModel = model,
                Embedding = vectors[i]
            }).ToList(), cancellationToken);
            embedded++;
        }

        var removed = await chunks.DeleteStaleAsync(all.Select(d => d.Id).ToList(), model, cancellationToken);
        LogIndexed(logger, embedded, unchanged, removed, model);
        return new PolicyIndexResult(embedded, unchanged, removed, Enabled: true);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Policy vector index: {Embedded} embedded, {Unchanged} unchanged, {Removed} stale chunks removed ({Model})")]
    private static partial void LogIndexed(ILogger logger, int embedded, int unchanged, long removed, string model);
}

public sealed record PolicyIndexResult(int Embedded, int Unchanged, long Removed, bool Enabled);
