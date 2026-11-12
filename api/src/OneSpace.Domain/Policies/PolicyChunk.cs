namespace OneSpace.Domain.Policies;

/// <summary>
/// A passage of a policy document with its embedding, for semantic search (FR-3 vector retrieval).
/// Derived data: rebuilt from <see cref="PolicyDocument"/> whenever the document or the embedding model changes.
/// </summary>
public sealed class PolicyChunk
{
    /// <summary><c>{DocumentId}#{Ordinal}</c>.</summary>
    public required string Id { get; set; }
    public required string DocumentId { get; set; }
    public int Ordinal { get; set; }
    public required string Title { get; set; }
    public string Category { get; set; } = "General";
    public required string Text { get; set; }

    /// <summary>Hash of the document content the chunk was built from; a changed hash means re-embed.</summary>
    public required string ContentHash { get; set; }

    /// <summary>Vectors from different models are not comparable, so each chunk records its model.</summary>
    public required string EmbeddingModel { get; set; }
    public required float[] Embedding { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record PolicyChunkMatch(PolicyChunk Chunk, double Similarity);
