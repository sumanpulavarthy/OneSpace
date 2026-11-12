using MongoDB.Bson;
using MongoDB.Driver;
using OneSpace.Application.Abstractions;
using OneSpace.Domain.Assistant;
using OneSpace.Domain.Policies;

namespace OneSpace.Infrastructure.Persistence;

public sealed class QueryLogRepository(MongoContext context) : IQueryLogRepository
{
    public Task AddAsync(QueryLogEntry entry, CancellationToken cancellationToken) =>
        context.QueryLogs.InsertOneAsync(entry, cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<QueryLogEntry>> GetRecentAsync(string userId, int limit, CancellationToken cancellationToken) =>
        await context.QueryLogs
            .Find(e => e.UserId == userId)
            .SortByDescending(e => e.CreatedAt)
            .Limit(Math.Clamp(limit, 1, 100))
            .ToListAsync(cancellationToken);
}

/// <summary>
/// Policy cache with full-text search. Cosmos DB for MongoDB vCore supports <c>$text</c>. Vector search over
/// embedded passages lives in <see cref="PolicyChunkStore"/>; the two are fused by HybridPolicySearch (FR-3).
/// </summary>
public sealed class PolicyDocumentRepository(MongoContext context) : IPolicyDocumentRepository
{
    private const int SnippetLength = 600;

    public async Task UpsertManyAsync(IReadOnlyCollection<PolicyDocument> documents, CancellationToken cancellationToken)
    {
        if (documents.Count == 0) return;
        var writes = documents.Select(d =>
            new ReplaceOneModel<PolicyDocument>(Builders<PolicyDocument>.Filter.Eq(x => x.Id, d.Id), d) { IsUpsert = true });
        await context.PolicyDocuments.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, cancellationToken);
    }

    public async Task<long> DeleteMissingAsync(IReadOnlyCollection<string> keepIds, CancellationToken cancellationToken)
    {
        var result = await context.PolicyDocuments.DeleteManyAsync(Builders<PolicyDocument>.Filter.Nin(x => x.Id, keepIds), cancellationToken);
        return result.DeletedCount;
    }

    public async Task<IReadOnlyList<PolicyDocument>> GetAllAsync(CancellationToken cancellationToken) =>
        await context.PolicyDocuments.Find(FilterDefinition<PolicyDocument>.Empty).ToListAsync(cancellationToken);

    public async Task<PolicyDocument?> GetAsync(string id, CancellationToken cancellationToken) =>
        await context.PolicyDocuments.Find(d => d.Id == id).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<PolicySearchResult>> SearchAsync(string text, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var filter = Builders<PolicyDocument>.Filter.Text(text);
        var projection = Builders<PolicyDocument>.Projection.MetaTextScore("score");
        var hits = await context.PolicyDocuments
            .Find(filter)
            .Project<BsonDocument>(projection)
            .Sort(Builders<PolicyDocument>.Sort.MetaTextScore("score"))
            .Limit(Math.Clamp(limit, 1, 10))
            .ToListAsync(cancellationToken);

        var terms = Terms(text);
        return hits.Select(h =>
        {
            var score = h.GetValue("score", 0).ToDouble();
            h.Remove("score");
            var doc = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<PolicyDocument>(h);
            return new PolicySearchResult(doc, score, Snippet(doc.Content, terms));
        }).ToList();
    }

    internal static string[] Terms(string text) =>
        text.Split([' ', ',', '.', '?', '!', '\'', '"'], StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2)
            .Select(t => t.ToLowerInvariant())
            .Distinct()
            .ToArray();

    /// <summary>The paragraph with the most query-term hits, trimmed to a readable length.</summary>
    internal static string Snippet(string content, IReadOnlyCollection<string> terms)
    {
        var paragraphs = content.Split(["\n\n", "\r\n\r\n"], StringSplitOptions.RemoveEmptyEntries);
        var best = paragraphs
            .Select(p => (Text: p.Trim(), Hits: terms.Count(t => p.Contains(t, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(p => p.Hits)
            .FirstOrDefault().Text ?? content;
        return best.Length <= SnippetLength ? best : best[..SnippetLength] + "…";
    }
}
