using OneSpace.Domain.Assistant;
using OneSpace.Domain.Policies;
using OneSpace.Domain.Users;

namespace OneSpace.Application.Abstractions;

public interface IUserContextAccessor
{
    /// <summary>The authenticated employee. Throws when the request is anonymous.</summary>
    UserContext Current { get; }
}

public interface IQueryLogRepository
{
    Task AddAsync(QueryLogEntry entry, CancellationToken cancellationToken);

    /// <summary>The caller's own recent queries, newest first.</summary>
    Task<IReadOnlyList<QueryLogEntry>> GetRecentAsync(string userId, int limit, CancellationToken cancellationToken);
}

/// <summary>Short-lived conversation memory per employee and session (expires; see Database:ConversationRetentionHours).</summary>
public interface IConversationStore
{
    /// <summary>The newest <paramref name="maxTurns"/> turns of the caller's own session, oldest first.</summary>
    Task<IReadOnlyList<ConversationTurn>> GetRecentTurnsAsync(string userId, string sessionId, int maxTurns, CancellationToken cancellationToken);

    Task AppendAsync(string userId, string sessionId, ConversationTurn turn, CancellationToken cancellationToken);
}

public interface IPolicyDocumentRepository
{
    Task UpsertManyAsync(IReadOnlyCollection<PolicyDocument> documents, CancellationToken cancellationToken);

    /// <summary>Removes cached documents that no longer exist in ComplyKaro.</summary>
    Task<long> DeleteMissingAsync(IReadOnlyCollection<string> keepIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<PolicySearchResult>> SearchAsync(string text, int limit, CancellationToken cancellationToken);

    Task<PolicyDocument?> GetAsync(string id, CancellationToken cancellationToken);

    /// <summary>Every cached document (the cache is small: policies, not records). Used to build the vector index.</summary>
    Task<IReadOnlyList<PolicyDocument>> GetAllAsync(CancellationToken cancellationToken);
}

/// <summary>Turns text into embedding vectors. Implemented over Microsoft.Extensions.AI in OneSpace.Assistant.</summary>
public interface ITextEmbedder
{
    /// <summary>False when no embedding model is configured: retrieval is keyword-only.</summary>
    bool IsEnabled { get; }

    string ModelId { get; }

    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);
}

/// <summary>Embedded policy passages and vector search over them.</summary>
public interface IPolicyChunkStore
{
    /// <summary>Content hash per document whose chunks were embedded with <paramref name="model"/>.</summary>
    Task<IReadOnlyDictionary<string, string>> GetIndexedHashesAsync(string model, CancellationToken cancellationToken);

    /// <summary>Replaces all chunks of one document.</summary>
    Task ReplaceChunksAsync(string documentId, IReadOnlyList<PolicyChunk> chunks, CancellationToken cancellationToken);

    /// <summary>Removes chunks of documents no longer in the cache, and chunks embedded with another model.</summary>
    Task<long> DeleteStaleAsync(IReadOnlyCollection<string> keepDocumentIds, string model, CancellationToken cancellationToken);

    /// <summary>The <paramref name="limit"/> chunks most similar to <paramref name="vector"/> (cosine), best first.</summary>
    Task<IReadOnlyList<PolicyChunkMatch>> SearchAsync(float[] vector, string model, int limit, CancellationToken cancellationToken);
}

/// <summary>Policy search as the assistant and API use it: hybrid keyword + vector when embeddings are configured.</summary>
public interface IPolicySearch
{
    Task<IReadOnlyList<PolicySearchResult>> SearchAsync(string query, int limit, CancellationToken cancellationToken);
}
