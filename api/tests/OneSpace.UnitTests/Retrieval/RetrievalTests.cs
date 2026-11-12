using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OneSpace.Application.Abstractions;
using OneSpace.Application.Policies;
using OneSpace.Assistant.Retrieval;
using OneSpace.Domain.Policies;

namespace OneSpace.UnitTests.Retrieval;

public class RetrievalTests
{
    private readonly InMemoryPolicies _documents = new(
        Policy("wfh", "Work From Home Policy", "Employees may work remotely up to two days per week with manager approval."),
        Policy("leave", "Leave Policy", "Casual leave cannot be carried forward. Earned leave carries forward up to 30 days."),
        Policy("travel", "Travel and Expense Policy", "Submit travel claims within 30 days with receipts."));

    private readonly InMemoryChunks _chunks = new();
    private readonly BagOfWordsEmbedder _embedder = new();
    private readonly IOptions<RetrievalOptions> _options = Options.Create(new RetrievalOptions { MinSimilarity = 0.1 });

    [Fact]
    public void Chunker_keeps_paragraphs_whole_and_splits_long_ones_with_overlap()
    {
        var content = "Short intro.\n\nSecond paragraph.\n\n" + string.Join(' ', Enumerable.Range(1, 120).Select(i => $"Rule {i} applies."));

        var chunks = PolicyChunker.Split(content, 300, 50);

        Assert.Equal("Short intro.\n\nSecond paragraph.", chunks[0]);
        Assert.All(chunks, c => Assert.True(c.Length <= 300, $"{c.Length} > 300"));
        Assert.All(chunks.Skip(1), c => Assert.StartsWith("Rule", c, StringComparison.Ordinal));
        // Overlap: consecutive chunks of the long paragraph share text.
        Assert.Contains(chunks[1][^20..].Trim().Split(' ')[^1], chunks[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Indexer_embeds_new_documents_skips_unchanged_and_reembeds_changed()
    {
        var indexer = Indexer();

        var first = await indexer.IndexAsync(CancellationToken.None);
        var second = await indexer.IndexAsync(CancellationToken.None);
        _documents.Docs[1].Content = "Casual leave lapses at year end.";
        var third = await indexer.IndexAsync(CancellationToken.None);

        Assert.Equal((3, 0), (first.Embedded, first.Unchanged));
        Assert.Equal((0, 3), (second.Embedded, second.Unchanged));
        Assert.Equal((1, 2), (third.Embedded, third.Unchanged));
        Assert.Contains(_chunks.All, c => c.DocumentId == "leave" && c.Text.Contains("lapses", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Indexer_removes_chunks_of_deleted_documents_and_other_models()
    {
        await Indexer().IndexAsync(CancellationToken.None);
        _documents.Docs.RemoveAt(2);
        _chunks.All.Add(Chunk("legacy", "old-model"));

        var result = await Indexer().IndexAsync(CancellationToken.None);

        Assert.DoesNotContain(_chunks.All, c => c.DocumentId is "travel" or "legacy");
        Assert.Equal(2, result.Removed);
    }

    [Fact]
    public async Task Indexer_does_nothing_without_an_embedding_model()
    {
        var indexer = new PolicyEmbeddingIndexer(_documents, _chunks, DisabledTextEmbedder.Instance, _options, NullLogger<PolicyEmbeddingIndexer>.Instance);

        var result = await indexer.IndexAsync(CancellationToken.None);

        Assert.False(result.Enabled);
        Assert.Empty(_chunks.All);
    }

    [Fact]
    public async Task Vector_search_finds_a_policy_the_keyword_search_misses()
    {
        await Indexer().IndexAsync(CancellationToken.None);

        // The keyword fake needs the exact phrase; "remotely" only matches by meaning (here: shared words).
        var results = await Search().SearchAsync("can I work remotely", 3, CancellationToken.None);

        Assert.Equal("wfh", results[0].Document.Id);
        Assert.Contains("remotely", results[0].Snippet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Document_found_by_both_searches_ranks_first()
    {
        await Indexer().IndexAsync(CancellationToken.None);
        _documents.KeywordHits = ["travel", "leave"];

        var search = new HybridPolicySearch(_documents, _chunks, _embedder, Options.Create(new RetrievalOptions { MinSimilarity = 0.3 }),
            NullLogger<HybridPolicySearch>.Instance);

        var results = await search.SearchAsync("leave carried forward", 3, CancellationToken.None);

        Assert.Equal("leave", results[0].Document.Id);
        Assert.True(results[0].Score > results[1].Score);
    }

    [Fact]
    public async Task Without_embeddings_search_is_keyword_only()
    {
        _documents.KeywordHits = ["travel"];
        var search = new HybridPolicySearch(_documents, _chunks, DisabledTextEmbedder.Instance, _options, NullLogger<HybridPolicySearch>.Instance);

        var results = await search.SearchAsync("travel claims", 3, CancellationToken.None);

        Assert.Equal("travel", Assert.Single(results).Document.Id);
    }

    [Fact]
    public async Task Embedding_failure_falls_back_to_keyword_results()
    {
        await Indexer().IndexAsync(CancellationToken.None);
        _documents.KeywordHits = ["travel"];
        _embedder.Fail = true;

        var results = await Search().SearchAsync("travel claims", 3, CancellationToken.None);

        Assert.Equal("travel", Assert.Single(results).Document.Id);
    }

    [Fact]
    public void Reciprocal_rank_fusion_rewards_agreement()
    {
        var both = HybridPolicySearch.Rrf(0, 60) + HybridPolicySearch.Rrf(1, 60);
        Assert.True(both > HybridPolicySearch.Rrf(0, 60));
    }

    [Fact]
    public void Embedder_is_disabled_without_a_model_and_validates_provider_settings()
    {
        Assert.False(TextEmbedder.Create(new EmbeddingOptions(), NullLoggerFactory.Instance).IsEnabled);
        Assert.Throws<InvalidOperationException>(() => TextEmbedder.Create(
            new EmbeddingOptions { Type = EmbeddingProviderType.AzureOpenAI, Model = "embed", ApiKey = "k" }, NullLoggerFactory.Instance));
        Assert.Throws<InvalidOperationException>(() => TextEmbedder.Create(
            new EmbeddingOptions { Type = EmbeddingProviderType.OpenAI, Model = "text-embedding-3-small" }, NullLoggerFactory.Instance));

        using var ollama = (TextEmbedder)TextEmbedder.Create(
            new EmbeddingOptions { Type = EmbeddingProviderType.Ollama, Model = "nomic-embed-text" }, NullLoggerFactory.Instance);
        Assert.Equal("nomic-embed-text", ollama.ModelId);
    }

    private PolicyEmbeddingIndexer Indexer() =>
        new(_documents, _chunks, _embedder, _options, NullLogger<PolicyEmbeddingIndexer>.Instance);

    private HybridPolicySearch Search() =>
        new(_documents, _chunks, _embedder, _options, NullLogger<HybridPolicySearch>.Instance);

    private static PolicyDocument Policy(string id, string title, string content) =>
        new() { Id = id, Title = title, Content = content, Category = "General" };

    private static PolicyChunk Chunk(string documentId, string model) => new()
    {
        Id = $"{documentId}#0", DocumentId = documentId, Title = documentId, Text = "x", ContentHash = "h", EmbeddingModel = model, Embedding = [1f]
    };

    /// <summary>Deterministic stand-in for an embedding model: hashed bag of words, so shared words = similarity.</summary>
    internal sealed class BagOfWordsEmbedder : ITextEmbedder
    {
        private const int Size = 256;
        public bool Fail { get; set; }
        public bool IsEnabled => true;
        public string ModelId => "bow-test";

        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
        {
            if (Fail) throw new HttpRequestException("embedding service down");
            return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(Embed).ToList());
        }

        private static float[] Embed(string text)
        {
            var vector = new float[Size];
            foreach (var word in text.ToLowerInvariant().Split([' ', '.', ',', '?', '\n', '(', ')'], StringSplitOptions.RemoveEmptyEntries))
                vector[(int)((uint)StringComparer.Ordinal.GetHashCode(word) % Size)] += 1;
            return vector;
        }
    }

    private sealed class InMemoryPolicies(params PolicyDocument[] documents) : IPolicyDocumentRepository
    {
        public List<PolicyDocument> Docs { get; } = [.. documents];

        /// <summary>Ids the "keyword index" returns, in order; empty = no keyword match.</summary>
        public List<string> KeywordHits { get; set; } = [];

        public Task<IReadOnlyList<PolicySearchResult>> SearchAsync(string text, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PolicySearchResult>>(KeywordHits
                .Select((id, i) => new PolicySearchResult(Docs.First(d => d.Id == id), 10 - i, Docs.First(d => d.Id == id).Content))
                .ToList());

        public Task<PolicyDocument?> GetAsync(string id, CancellationToken cancellationToken) => Task.FromResult(Docs.FirstOrDefault(d => d.Id == id));
        public Task<IReadOnlyList<PolicyDocument>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PolicyDocument>>(Docs.ToList());
        public Task UpsertManyAsync(IReadOnlyCollection<PolicyDocument> documents, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<long> DeleteMissingAsync(IReadOnlyCollection<string> keepIds, CancellationToken cancellationToken) => Task.FromResult(0L);
    }

    private sealed class InMemoryChunks : IPolicyChunkStore
    {
        public List<PolicyChunk> All { get; } = [];

        public Task<IReadOnlyDictionary<string, string>> GetIndexedHashesAsync(string model, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(All.Where(c => c.EmbeddingModel == model)
                .GroupBy(c => c.DocumentId).ToDictionary(g => g.Key, g => g.First().ContentHash));

        public Task ReplaceChunksAsync(string documentId, IReadOnlyList<PolicyChunk> chunks, CancellationToken cancellationToken)
        {
            All.RemoveAll(c => c.DocumentId == documentId);
            All.AddRange(chunks);
            return Task.CompletedTask;
        }

        public Task<long> DeleteStaleAsync(IReadOnlyCollection<string> keepDocumentIds, string model, CancellationToken cancellationToken) =>
            Task.FromResult((long)All.RemoveAll(c => !keepDocumentIds.Contains(c.DocumentId) || c.EmbeddingModel != model));

        public Task<IReadOnlyList<PolicyChunkMatch>> SearchAsync(float[] vector, string model, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PolicyChunkMatch>>(All.Where(c => c.EmbeddingModel == model)
                .Select(c => new PolicyChunkMatch(c, Cosine(c.Embedding, vector)))
                .OrderByDescending(m => m.Similarity).Take(limit).ToList());

        private static double Cosine(float[] a, float[] b)
        {
            double dot = 0, na = 0, nb = 0;
            for (var i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
            return na == 0 || nb == 0 ? 0 : dot / Math.Sqrt(na * nb);
        }
    }
}
