using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using OneSpace.Application.Abstractions;
using OneSpace.Application.Policies;
using OneSpace.Domain.Policies;

namespace OneSpace.Infrastructure.Persistence;

/// <summary>
/// Embedded policy passages in the <c>policyChunks</c> collection, with two search strategies:
/// <list type="bullet">
/// <item>BruteForce: vectors are cached in memory (normalised) and scored by cosine similarity. Works on any MongoDB,
/// including the local dev container; exact, and fast enough for a policy corpus (thousands of chunks).</item>
/// <item>CosmosVCore: Azure Cosmos DB for MongoDB vCore's native <c>cosmosSearch</c> over an HNSW index.</item>
/// </list>
/// </summary>
public sealed partial class PolicyChunkStore(
    MongoContext context,
    IOptions<RetrievalOptions> options,
    TimeProvider clock,
    ILogger<PolicyChunkStore> logger) : IPolicyChunkStore
{
    /// <summary>Other API instances see index changes within this long (the sync runs daily).</summary>
    internal static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    private readonly Lock _lock = new();
    private VectorCache? _cache;

    private IMongoCollection<PolicyChunk> Collection => context.PolicyChunks;

    public async Task<IReadOnlyDictionary<string, string>> GetIndexedHashesAsync(string model, CancellationToken cancellationToken)
    {
        var rows = await Collection.Find(c => c.EmbeddingModel == model)
            .Project(c => new { c.DocumentId, c.ContentHash })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.DocumentId).ToDictionary(g => g.Key, g => g.First().ContentHash);
    }

    public async Task ReplaceChunksAsync(string documentId, IReadOnlyList<PolicyChunk> chunks, CancellationToken cancellationToken)
    {
        await Collection.DeleteManyAsync(c => c.DocumentId == documentId, cancellationToken);
        if (chunks.Count > 0) await Collection.InsertManyAsync(chunks, cancellationToken: cancellationToken);
        Invalidate();
    }

    public async Task<long> DeleteStaleAsync(IReadOnlyCollection<string> keepDocumentIds, string model, CancellationToken cancellationToken)
    {
        var filter = Builders<PolicyChunk>.Filter.Or(
            Builders<PolicyChunk>.Filter.Nin(c => c.DocumentId, keepDocumentIds),
            Builders<PolicyChunk>.Filter.Ne(c => c.EmbeddingModel, model));
        var result = await Collection.DeleteManyAsync(filter, cancellationToken);
        if (result.DeletedCount > 0) Invalidate();
        return result.DeletedCount;
    }

    public Task<IReadOnlyList<PolicyChunkMatch>> SearchAsync(float[] vector, string model, int limit, CancellationToken cancellationToken) =>
        options.Value.VectorStore == VectorStoreKind.CosmosVCore
            ? CosmosSearchAsync(vector, model, limit, cancellationToken)
            : BruteForceSearchAsync(vector, model, limit, cancellationToken);

    private async Task<IReadOnlyList<PolicyChunkMatch>> BruteForceSearchAsync(float[] vector, string model, int limit, CancellationToken cancellationToken)
    {
        var cache = await LoadAsync(model, cancellationToken);
        var query = Normalize(vector);
        return cache.Entries
            .Where(e => e.Vector.Length == query.Length)
            .Select(e => new PolicyChunkMatch(e.Chunk, Dot(e.Vector, query)))
            .OrderByDescending(m => m.Similarity)
            .Take(Math.Clamp(limit, 1, 50))
            .ToList();
    }

    private async Task<IReadOnlyList<PolicyChunkMatch>> CosmosSearchAsync(float[] vector, string model, int limit, CancellationToken cancellationToken)
    {
        // https://learn.microsoft.com/azure/cosmos-db/mongodb/vcore/vector-search
        // Over-fetch: chunks of another model (mid re-index) are filtered out below.
        BsonDocument[] pipeline =
        [
            new("$search", new BsonDocument
            {
                { "cosmosSearch", new BsonDocument
                    {
                        { "vector", new BsonArray(vector.Select(v => (double)v)) },
                        { "path", "embedding" },
                        { "k", Math.Clamp(limit * 2, 2, 100) }
                    }
                },
                { "returnStoredSource", true }
            }),
            new("$project", new BsonDocument
            {
                { "similarityScore", new BsonDocument("$meta", "searchScore") },
                { "document", "$$ROOT" }
            })
        ];

        var rows = await Collection.Aggregate<BsonDocument>(pipeline, cancellationToken: cancellationToken).ToListAsync(cancellationToken);
        return rows
            .Select(r => new PolicyChunkMatch(BsonSerializer.Deserialize<PolicyChunk>(r["document"].AsBsonDocument), r["similarityScore"].ToDouble()))
            .Where(m => m.Chunk.EmbeddingModel == model)
            .Take(limit)
            .ToList();
    }

    private async Task<VectorCache> LoadAsync(string model, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_cache is { } cached && cached.Model == model && clock.GetUtcNow() - cached.LoadedAt < CacheLifetime) return cached;
        }

        var chunks = await Collection.Find(c => c.EmbeddingModel == model).ToListAsync(cancellationToken);
        var loaded = new VectorCache(model, clock.GetUtcNow(), chunks.Select(c => (c, Normalize(c.Embedding))).ToList());
        LogLoaded(logger, loaded.Entries.Count, model);
        lock (_lock) _cache = loaded;
        return loaded;
    }

    private void Invalidate()
    {
        lock (_lock) _cache = null;
    }

    internal static float[] Normalize(float[] vector)
    {
        double sum = 0;
        foreach (var v in vector) sum += (double)v * v;
        var norm = Math.Sqrt(sum);
        if (norm == 0) return vector;
        var result = new float[vector.Length];
        for (var i = 0; i < vector.Length; i++) result[i] = (float)(vector[i] / norm);
        return result;
    }

    internal static double Dot(float[] a, float[] b)
    {
        double sum = 0;
        for (var i = 0; i < a.Length; i++) sum += (double)a[i] * b[i];
        return sum;
    }

    private sealed record VectorCache(string Model, DateTimeOffset LoadedAt, List<(PolicyChunk Chunk, float[] Vector)> Entries);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Loaded {Count} policy vectors for {Model}")]
    private static partial void LogLoaded(ILogger logger, int count, string model);
}
