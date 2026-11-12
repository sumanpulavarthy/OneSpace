using OneSpace.Domain.Assistant;

namespace OneSpace.Application.Policies;

/// <summary>FR-3 hybrid retrieval: keyword search plus embeddings and vector search over the policy cache.</summary>
public sealed class RetrievalOptions
{
    public const string SectionName = "Retrieval";

    /// <summary>Embedding model. With <c>Type = None</c> search is keyword-only (the previous behaviour).</summary>
    public EmbeddingOptions Embeddings { get; set; } = new();

    /// <summary>
    /// BruteForce: cosine similarity in the API over the chunks (works on any MongoDB; fine for thousands of chunks).
    /// CosmosVCore: native <c>cosmosSearch</c> HNSW index on Azure Cosmos DB for MongoDB vCore (needs Embeddings:Dimensions).
    /// </summary>
    public VectorStoreKind VectorStore { get; set; } = VectorStoreKind.BruteForce;

    /// <summary>Target chunk length in characters; paragraphs are kept whole where they fit.</summary>
    public int ChunkSize { get; set; } = 800;
    public int ChunkOverlap { get; set; } = 100;

    /// <summary>Chunks fetched by vector search before fusion.</summary>
    public int VectorCandidates { get; set; } = 8;

    /// <summary>Cosine similarity below this is treated as unrelated.</summary>
    public double MinSimilarity { get; set; } = 0.3;

    /// <summary>Reciprocal-rank-fusion constant (60 is the usual default).</summary>
    public int RrfK { get; set; } = 60;
}

public enum VectorStoreKind
{
    BruteForce,
    CosmosVCore
}

public enum EmbeddingProviderType
{
    None,
    OpenAI,
    AzureOpenAI,
    Ollama
}

public sealed class EmbeddingOptions
{
    public EmbeddingProviderType Type { get; set; } = EmbeddingProviderType.None;

    /// <summary>Required for Azure OpenAI; Ollama defaults to http://localhost:11434/v1/.</summary>
    public string? Endpoint { get; set; }

    /// <summary>From Key Vault (<c>Retrieval--Embeddings--ApiKey</c>) or user-secrets only.</summary>
    public string? ApiKey { get; set; }

    /// <summary>e.g. text-embedding-3-small (for Azure OpenAI: the deployment name), nomic-embed-text for Ollama.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Requested vector size (models that support it); required for the Cosmos vCore index.</summary>
    public int? Dimensions { get; set; }

    /// <summary>
    /// Most sensitive data the embedding provider may receive (FR-7). Policy text and search queries are Internal,
    /// so it must be at least Internal or retrieval stays keyword-only.
    /// </summary>
    public DataClass MaxDataClass { get; set; } = DataClass.Internal;

    public bool Enabled => Type != EmbeddingProviderType.None && !string.IsNullOrWhiteSpace(Model);
}
