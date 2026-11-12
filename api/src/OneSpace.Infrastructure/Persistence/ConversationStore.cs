using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using OneSpace.Application.Abstractions;
using OneSpace.Domain.Assistant;

namespace OneSpace.Infrastructure.Persistence;

/// <summary>
/// Conversation memory. The document id combines user and session, so a session id guessed or reused by another
/// employee can never read or extend someone else's conversation. Documents expire via a TTL index.
/// </summary>
public sealed class ConversationStore(MongoContext context) : IConversationStore
{
    /// <summary>Turns kept per session; older ones are dropped on write.</summary>
    public const int MaxStoredTurns = 20;

    private IMongoCollection<ConversationDocument> Collection =>
        context.Database.GetCollection<ConversationDocument>(MongoContext.ConversationsCollection);

    public async Task<IReadOnlyList<ConversationTurn>> GetRecentTurnsAsync(string userId, string sessionId, int maxTurns, CancellationToken cancellationToken)
    {
        var document = await Collection.Find(d => d.Id == Key(userId, sessionId)).FirstOrDefaultAsync(cancellationToken);
        if (document is null || document.UserId != userId) return [];
        return document.Turns.TakeLast(Math.Clamp(maxTurns, 0, MaxStoredTurns)).ToList();
    }

    public Task AppendAsync(string userId, string sessionId, ConversationTurn turn, CancellationToken cancellationToken) =>
        Collection.UpdateOneAsync(
            d => d.Id == Key(userId, sessionId),
            Builders<ConversationDocument>.Update
                .SetOnInsert(d => d.UserId, userId)
                .SetOnInsert(d => d.SessionId, sessionId)
                .Set(d => d.UpdatedAt, DateTimeOffset.UtcNow)
                .PushEach(d => d.Turns, [turn], slice: -MaxStoredTurns),
            new UpdateOptions { IsUpsert = true },
            cancellationToken);

    private static string Key(string userId, string sessionId) => $"{userId}:{sessionId}";

    public sealed class ConversationDocument
    {
        [BsonId]
        public required string Id { get; set; }
        public required string UserId { get; set; }
        public required string SessionId { get; set; }
        public List<ConversationTurn> Turns { get; set; } = [];
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
