using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using OneSpace.Domain.Assistant;
using OneSpace.Domain.Policies;

namespace OneSpace.Infrastructure.Persistence;

/// <summary>Creates indexes at startup (idempotent).</summary>
public sealed class MongoIndexInitializer(MongoContext context) : IHostedService
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

        // Abandoned authorizations disappear on their own.
        await context.Database.GetCollection<OAuthStateStore.StateDocument>(MongoContext.OAuthStatesCollection).Indexes.CreateOneAsync(
            new CreateIndexModel<OAuthStateStore.StateDocument>(
                Builders<OAuthStateStore.StateDocument>.IndexKeys.Ascending(d => d.ExpiresAt),
                new CreateIndexOptions { ExpireAfter = TimeSpan.Zero }),
            cancellationToken: cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
