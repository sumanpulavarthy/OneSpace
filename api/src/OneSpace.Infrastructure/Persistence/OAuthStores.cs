using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using MongoDB.Driver;
using OneSpace.Application.Integrations;
using OneSpace.Domain.Integrations;

namespace OneSpace.Infrastructure.Persistence;

/// <summary>Encrypts OAuth secrets with Data Protection before they are written. Plaintext never reaches the database.</summary>
public sealed class OAuthSecretProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("OneSpace.OAuth.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);

    /// <summary>Lookup key for values that must be findable but never stored (e.g. the OAuth state).</summary>
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed class ZohoTokenStore(MongoContext context, OAuthSecretProtector protector) : IZohoTokenStore
{
    private IMongoCollection<TokenDocument> Tokens => context.Database.GetCollection<TokenDocument>(MongoContext.ZohoConnectionsCollection);

    public async Task<ZohoTokenSet?> GetAsync(string userId, CancellationToken cancellationToken)
    {
        var doc = await Tokens.Find(d => d.Id == userId).FirstOrDefaultAsync(cancellationToken);
        if (doc is null) return null;

        try
        {
            return new ZohoTokenSet(doc.Id, protector.Unprotect(doc.AccessToken), doc.AccessTokenExpiresAt,
                protector.Unprotect(doc.RefreshToken), doc.Scopes, doc.ConnectedAt, doc.UpdatedAt);
        }
        catch (CryptographicException)
        {
            // Key ring lost or rotated out: the stored tokens are unusable, so the employee must reconnect.
            await DeleteAsync(userId, cancellationToken);
            return null;
        }
    }

    public Task SaveAsync(ZohoTokenSet tokens, CancellationToken cancellationToken) =>
        Tokens.ReplaceOneAsync(d => d.Id == tokens.UserId, new TokenDocument
        {
            Id = tokens.UserId,
            AccessToken = protector.Protect(tokens.AccessToken),
            AccessTokenExpiresAt = tokens.AccessTokenExpiresAt,
            RefreshToken = protector.Protect(tokens.RefreshToken),
            Scopes = [.. tokens.Scopes],
            ConnectedAt = tokens.ConnectedAt,
            UpdatedAt = tokens.UpdatedAt
        }, new ReplaceOptions { IsUpsert = true }, cancellationToken);

    public Task DeleteAsync(string userId, CancellationToken cancellationToken) =>
        Tokens.DeleteOneAsync(d => d.Id == userId, cancellationToken);

    internal sealed class TokenDocument
    {
        public required string Id { get; init; }
        public required string AccessToken { get; init; }
        public DateTimeOffset AccessTokenExpiresAt { get; init; }
        public required string RefreshToken { get; init; }
        public List<string> Scopes { get; init; } = [];
        public DateTimeOffset ConnectedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }
}

public sealed class OAuthStateStore(MongoContext context, OAuthSecretProtector protector, TimeProvider clock) : IOAuthStateStore
{
    private IMongoCollection<StateDocument> States => context.Database.GetCollection<StateDocument>(MongoContext.OAuthStatesCollection);

    public Task SaveAsync(PendingAuthorization pending, CancellationToken cancellationToken) =>
        States.InsertOneAsync(new StateDocument
        {
            // Only a hash of the state is stored, so a database reader cannot complete someone else's authorization.
            Id = OAuthSecretProtector.Hash(pending.State),
            UserId = pending.UserId,
            CodeVerifier = protector.Protect(pending.CodeVerifier),
            RedirectUri = pending.RedirectUri,
            ExpiresAt = pending.ExpiresAt
        }, cancellationToken: cancellationToken);

    public async Task<PendingAuthorization?> TakeAsync(string state, CancellationToken cancellationToken)
    {
        var doc = await States.FindOneAndDeleteAsync(d => d.Id == OAuthSecretProtector.Hash(state), cancellationToken: cancellationToken);
        if (doc is null || doc.ExpiresAt <= clock.GetUtcNow()) return null;
        return new PendingAuthorization(state, doc.UserId, protector.Unprotect(doc.CodeVerifier), doc.RedirectUri, doc.ExpiresAt);
    }

    internal sealed class StateDocument
    {
        public required string Id { get; init; }
        public required string UserId { get; init; }
        public required string CodeVerifier { get; init; }
        public required string RedirectUri { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
    }
}

public sealed class OAuthClientStore(MongoContext context, OAuthSecretProtector protector) : IOAuthClientStore
{
    private IMongoCollection<ClientDocument> Clients => context.Database.GetCollection<ClientDocument>(MongoContext.OAuthClientsCollection);

    public async Task<RegisteredOAuthClient?> GetAsync(string issuer, CancellationToken cancellationToken)
    {
        var doc = await Clients.Find(d => d.Id == issuer).FirstOrDefaultAsync(cancellationToken);
        if (doc is null) return null;
        try
        {
            return new RegisteredOAuthClient(doc.Id, doc.ClientId, doc.ClientSecret is null ? null : protector.Unprotect(doc.ClientSecret),
                doc.RedirectUri, doc.RegisteredAt);
        }
        catch (CryptographicException)
        {
            return null; // Re-register.
        }
    }

    public Task SaveAsync(RegisteredOAuthClient client, CancellationToken cancellationToken) =>
        Clients.ReplaceOneAsync(d => d.Id == client.Issuer, new ClientDocument
        {
            Id = client.Issuer,
            ClientId = client.ClientId,
            ClientSecret = client.ClientSecret is null ? null : protector.Protect(client.ClientSecret),
            RedirectUri = client.RedirectUri,
            RegisteredAt = client.RegisteredAt
        }, new ReplaceOptions { IsUpsert = true }, cancellationToken);

    internal sealed class ClientDocument
    {
        public required string Id { get; init; }
        public required string ClientId { get; init; }
        public string? ClientSecret { get; init; }
        public required string RedirectUri { get; init; }
        public DateTimeOffset RegisteredAt { get; init; }
    }
}
