using Microsoft.AspNetCore.DataProtection;
using MongoDB.Bson;
using MongoDB.Driver;
using OneSpace.Domain.Integrations;
using OneSpace.Infrastructure.Persistence;

namespace OneSpace.IntegrationTests;

public sealed class OAuthStoreTests(MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private readonly OAuthSecretProtector _protector = new(new EphemeralDataProtectionProvider());

    [Fact]
    public async Task Tokens_are_encrypted_at_rest_and_round_trip()
    {
        var store = new ZohoTokenStore(mongo.Context, _protector);
        var now = DateTimeOffset.UtcNow;
        await store.SaveAsync(new ZohoTokenSet("oid-enc", "plain-access-token", now.AddHours(1), "plain-refresh-token",
            ["zohopeople.leave.all"], now, now), CancellationToken.None);

        var raw = await mongo.Context.Database.GetCollection<BsonDocument>(MongoContext.ZohoConnectionsCollection)
            .Find(new BsonDocument("_id", "oid-enc")).FirstAsync();
        Assert.DoesNotContain("plain-access-token", raw.ToJson(), StringComparison.Ordinal);
        Assert.DoesNotContain("plain-refresh-token", raw.ToJson(), StringComparison.Ordinal);

        var loaded = await store.GetAsync("oid-enc", CancellationToken.None);
        Assert.Equal("plain-refresh-token", loaded!.RefreshToken);
    }

    [Fact]
    public async Task Tokens_written_with_a_lost_key_ring_are_discarded_not_thrown()
    {
        var now = DateTimeOffset.UtcNow;
        await new ZohoTokenStore(mongo.Context, _protector).SaveAsync(
            new ZohoTokenSet("oid-lost", "a", now, "r", [], now, now), CancellationToken.None);

        var otherKeys = new ZohoTokenStore(mongo.Context, new OAuthSecretProtector(new EphemeralDataProtectionProvider()));

        Assert.Null(await otherKeys.GetAsync("oid-lost", CancellationToken.None));
        Assert.Null(await new ZohoTokenStore(mongo.Context, _protector).GetAsync("oid-lost", CancellationToken.None));
    }

    [Fact]
    public async Task State_is_stored_hashed_single_use_and_expires()
    {
        var store = new OAuthStateStore(mongo.Context, _protector, TimeProvider.System);
        await store.SaveAsync(new PendingAuthorization("state-abc", "oid-1", "verifier-xyz", "https://cb", DateTimeOffset.UtcNow.AddMinutes(10)), CancellationToken.None);
        await store.SaveAsync(new PendingAuthorization("state-old", "oid-1", "v", "https://cb", DateTimeOffset.UtcNow.AddMinutes(-1)), CancellationToken.None);

        var raw = (await mongo.Context.Database.GetCollection<BsonDocument>(MongoContext.OAuthStatesCollection)
            .Find(FilterDefinition<BsonDocument>.Empty).ToListAsync()).ToJson();
        Assert.DoesNotContain("state-abc", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("verifier-xyz", raw, StringComparison.Ordinal);

        Assert.Equal("verifier-xyz", (await store.TakeAsync("state-abc", CancellationToken.None))!.CodeVerifier);
        Assert.Null(await store.TakeAsync("state-abc", CancellationToken.None));
        Assert.Null(await store.TakeAsync("state-old", CancellationToken.None));
    }
}
