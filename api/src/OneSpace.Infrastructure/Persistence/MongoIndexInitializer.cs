using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using OneSpace.Application.Policies;
using OneSpace.Domain.Assistant;
using OneSpace.Domain.Policies;

namespace OneSpace.Infrastructure.Persistence;

/// <summary>Creates indexes at startup (idempotent).</summary>
public sealed class MongoIndexInitializer(MongoContext context, IOptions<RetrievalOptions> retrieval) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await context.QueryLogs.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<QueryLogEntry>(Builders<QueryLogEntry>.IndexKeys.Ascending(e => e.UserId).Descending(e => e.CreatedAt)),
            new CreateIndexModel<QueryLogEntry>(
                Builders<QueryLogEntry>.IndexKeys.Ascending(e => e.CreatedAt),
                new CreateIndexOptions { ExpireAfter = TimeSpan.FromDays(context.Options.QueryLogRetentionDays) })
        ], cancellationToken);

        await context.PolicyDocuments.Indexes.CreateOneAsync(
            new CreateIndexModel<PolicyDocument>(
                Builders<PolicyDocument>.IndexKeys.Text(d => d.Title).Text(d => d.Content).Text(d => d.Category),
                new CreateIndexOptions { Name = "policy_text", Weights = new MongoDB.Bson.BsonDocument { { "title", 5 }, { "category", 2 }, { "content", 1 } } }),
            cancellationToken: cancellationToken);

        await context.Database.GetCollection<ConversationStore.ConversationDocument>(MongoContext.ConversationsCollection).Indexes.CreateOneAsync(
            new CreateIndexModel<ConversationStore.ConversationDocument>(
                Builders<ConversationStore.ConversationDocument>.IndexKeys.Ascending(d => d.UpdatedAt),
                new CreateIndexOptions { ExpireAfter = TimeSpan.FromHours(context.Options.ConversationRetentionHours) }),
            cancellationToken: cancellationToken);

        await context.PolicyChunks.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<PolicyChunk>(Builders<PolicyChunk>.IndexKeys.Ascending(c => c.DocumentId)),
            new CreateIndexModel<PolicyChunk>(Builders<PolicyChunk>.IndexKeys.Ascending(c => c.EmbeddingModel))
        ], cancellationToken);
        await CreateVectorIndexAsync(cancellationToken);

        // Abandoned authorizations disappear on their own.
        await context.Database.GetCollection<OAuthStateStore.StateDocument>(MongoContext.OAuthStatesCollection).Indexes.CreateOneAsync(
            new CreateIndexModel<OAuthStateStore.StateDocument>(
                Builders<OAuthStateStore.StateDocument>.IndexKeys.Ascending(d => d.ExpiresAt),
                new CreateIndexOptions { ExpireAfter = TimeSpan.Zero }),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Cosmos DB for MongoDB vCore HNSW index for <c>cosmosSearch</c>. Plain MongoDB has no equivalent, so BruteForce
    /// mode (the default, and local development) needs none.
    /// </summary>
    private async Task CreateVectorIndexAsync(CancellationToken cancellationToken)
    {
        var o = retrieval.Value;
        if (o.VectorStore != VectorStoreKind.CosmosVCore || !o.Embeddings.Enabled) return;

        await context.Database.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "createIndexes", MongoContext.PolicyChunksCollection },
            { "indexes", new BsonArray
                {
                    new BsonDocument
                    {
                        { "name", "policy_chunk_vector" },
                        { "key", new BsonDocument("embedding", "cosmosSearch") },
                        { "cosmosSearchOptions", new BsonDocument
                            {
                                { "kind", "vector-hnsw" },
                                { "m", 16 },
                                { "efConstruction", 64 },
                                { "similarity", "COS" },
                                { "dimensions", o.Embeddings.Dimensions!.Value }
                            }
                        }
                    }
                }
            }
        }, cancellationToken: cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
