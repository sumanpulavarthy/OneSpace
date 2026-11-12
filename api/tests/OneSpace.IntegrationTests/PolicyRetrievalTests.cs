using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OneSpace.Application.Abstractions;
using OneSpace.Application.Policies;
using OneSpace.Domain.Policies;
using OneSpace.Infrastructure.Persistence;

namespace OneSpace.IntegrationTests;

/// <summary>FR-3 on a real MongoDB: sync → chunk + embed → store → brute-force vector search → hybrid fusion.</summary>
public sealed class PolicyRetrievalTests(MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private readonly IOptions<RetrievalOptions> _options = Options.Create(new RetrievalOptions { MinSimilarity = 0.1 });

    [Fact]
    public async Task Indexed_policies_are_found_by_meaning_and_by_keyword()
    {
        var documents = new PolicyDocumentRepository(mongo.Context);
        var store = new PolicyChunkStore(mongo.Context, _options, TimeProvider.System, NullLogger<PolicyChunkStore>.Instance);
        var embedder = new BagOfWordsEmbedder();
        await documents.UpsertManyAsync(
        [
            Policy("r-wfh", "Hybrid Working Guidelines", "Staff may work remotely two days per week.\n\nCore hours are 11:00 to 16:00."),
            Policy("r-travel", "Travel and Expense Rules", "Submit travel claims within 30 days with original receipts.")
        ], CancellationToken.None);

        var indexer = new PolicyEmbeddingIndexer(documents, store, embedder, _options, NullLogger<PolicyEmbeddingIndexer>.Instance);
        var indexed = await indexer.IndexAsync(CancellationToken.None);
        var search = new HybridPolicySearch(documents, store, embedder, _options, NullLogger<HybridPolicySearch>.Instance);

        var byMeaning = await search.SearchAsync("may I work remotely on fridays", 3, CancellationToken.None);
        var byKeyword = await search.SearchAsync("receipts", 3, CancellationToken.None);

        Assert.True(indexed.Embedded >= 2);
        Assert.Equal("r-wfh", byMeaning[0].Document.Id);
        Assert.Contains("remotely", byMeaning[0].Snippet, StringComparison.Ordinal);
        Assert.Equal("r-travel", byKeyword[0].Document.Id);
    }

    [Fact]
    public async Task Chunks_are_replaced_per_document_and_stale_ones_removed()
    {
        var store = new PolicyChunkStore(mongo.Context, _options, TimeProvider.System, NullLogger<PolicyChunkStore>.Instance);
        await store.ReplaceChunksAsync("c-1", [Chunk("c-1", 0, "m1", [1, 0]), Chunk("c-1", 1, "m1", [0, 1])], CancellationToken.None);
        await store.ReplaceChunksAsync("c-2", [Chunk("c-2", 0, "m0", [1, 1])], CancellationToken.None);
        await store.ReplaceChunksAsync("c-1", [Chunk("c-1", 0, "m1", [1, 0])], CancellationToken.None);

        var hashes = await store.GetIndexedHashesAsync("m1", CancellationToken.None);
        var nearest = await store.SearchAsync([0.9f, 0.1f], "m1", 5, CancellationToken.None);
        var removed = await store.DeleteStaleAsync(["c-1"], "m1", CancellationToken.None);

        Assert.Equal("hash-c-1", hashes["c-1"]);
        Assert.Equal("c-1#0", Assert.Single(nearest).Chunk.Id);
        Assert.True(nearest[0].Similarity > 0.9);
        Assert.True(removed >= 1); // c-2 (other model)
        Assert.Empty(await store.GetIndexedHashesAsync("m0", CancellationToken.None));
    }

    private static PolicyDocument Policy(string id, string title, string content) =>
        new() { Id = id, Title = title, Content = content, Category = "Workplace" };

    private static PolicyChunk Chunk(string documentId, int ordinal, string model, float[] vector) => new()
    {
        Id = $"{documentId}#{ordinal}", DocumentId = documentId, Ordinal = ordinal, Title = documentId,
        Text = $"text {ordinal}", ContentHash = $"hash-{documentId}", EmbeddingModel = model, Embedding = vector
    };

    /// <summary>Deterministic stand-in for an embedding model: hashed bag of words.</summary>
    private sealed class BagOfWordsEmbedder : ITextEmbedder
    {
        public bool IsEnabled => true;
        public string ModelId => "bow-test";

        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(t =>
            {
                var v = new float[256];
                foreach (var w in t.ToLowerInvariant().Split([' ', '.', ',', '?', '\n', '(', ')'], StringSplitOptions.RemoveEmptyEntries))
                    v[(int)((uint)StringComparer.Ordinal.GetHashCode(w) % 256)] += 1;
                return v;
            }).ToList());
    }
}
