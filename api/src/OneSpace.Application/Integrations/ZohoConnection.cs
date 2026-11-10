using OneSpace.Domain.Integrations;

namespace OneSpace.Application.Integrations;

/// <summary>"Connect Zoho People": per-employee MCP OAuth (FR-2).</summary>
public interface IZohoConnectionService
{
    Task<ZohoConnectionStatus> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>Starts authorization for the current employee and returns the Zoho consent URL to send the browser to.</summary>
    Task<Uri> StartAsync(Uri callbackUri, CancellationToken cancellationToken);

    /// <summary>Handles Zoho's redirect. Anonymous: the single-use state identifies the employee.</summary>
    Task<bool> CompleteAsync(string? state, string? code, string? error, CancellationToken cancellationToken);

    /// <summary>Revokes the employee's Zoho tokens and forgets them.</summary>
    Task DisconnectAsync(CancellationToken cancellationToken);
}

/// <summary>Supplies a valid Zoho access token for an employee, refreshing it when needed.</summary>
public interface IZohoAccessTokenProvider
{
    /// <summary>Throws <see cref="ZohoConnectionRequiredException"/> when the employee has not connected (or consent was revoked).</summary>
    Task<string> GetAccessTokenAsync(string userId, bool forceRefresh, CancellationToken cancellationToken);
}

public sealed record ZohoConnectionStatus(bool Connected, DateTimeOffset? ConnectedAt, IReadOnlyList<string> Scopes);

/// <summary>The employee must connect Zoho People before Zoho data can be read for them.</summary>
public sealed class ZohoConnectionRequiredException(string reason)
    : InvalidOperationException($"Zoho People is not connected: {reason}");

/// <summary>Persistence for OAuth material. Implementations must encrypt tokens, verifiers and client secrets at rest.</summary>
public interface IZohoTokenStore
{
    Task<ZohoTokenSet?> GetAsync(string userId, CancellationToken cancellationToken);
    Task SaveAsync(ZohoTokenSet tokens, CancellationToken cancellationToken);
    Task DeleteAsync(string userId, CancellationToken cancellationToken);
}

public interface IOAuthStateStore
{
    Task SaveAsync(PendingAuthorization pending, CancellationToken cancellationToken);

    /// <summary>Returns and deletes the pending authorization (single use). Null when unknown or expired.</summary>
    Task<PendingAuthorization?> TakeAsync(string state, CancellationToken cancellationToken);
}

public interface IOAuthClientStore
{
    Task<RegisteredOAuthClient?> GetAsync(string issuer, CancellationToken cancellationToken);
    Task SaveAsync(RegisteredOAuthClient client, CancellationToken cancellationToken);
}
