namespace OneSpace.Domain.Integrations;

/// <summary>
/// An employee's Zoho People connection (MCP OAuth). Plain values in memory only: the store encrypts both
/// tokens before they reach the database.
/// </summary>
public sealed record ZohoTokenSet(
    string UserId,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    IReadOnlyList<string> Scopes,
    DateTimeOffset ConnectedAt,
    DateTimeOffset UpdatedAt);

/// <summary>An in-flight authorization (PKCE). Single use, short-lived, bound to the employee who started it.</summary>
public sealed record PendingAuthorization(
    string State,
    string UserId,
    string CodeVerifier,
    string RedirectUri,
    DateTimeOffset ExpiresAt);

/// <summary>OneSpace's OAuth client registration with an authorization server (dynamic client registration).</summary>
public sealed record RegisteredOAuthClient(
    string Issuer,
    string ClientId,
    string? ClientSecret,
    string RedirectUri,
    DateTimeOffset RegisteredAt);
