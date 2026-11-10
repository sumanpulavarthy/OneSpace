using OneSpace.Application.Integrations;

namespace OneSpace.Api.GraphQL;

/// <summary>"Connect Zoho People" (FR-2): per-employee consent; tokens are stored encrypted by the API.</summary>
[ExtendObjectType(typeof(Query))]
public sealed class ZohoConnectionQueries
{
    public Task<ZohoConnectionStatus> ZohoConnection(IZohoConnectionService zoho, CancellationToken cancellationToken) =>
        zoho.GetStatusAsync(cancellationToken);
}

[ExtendObjectType(typeof(Mutation))]
public sealed class ZohoConnectionMutations
{
    public const string CallbackPath = "/oauth/zoho/callback";

    /// <summary>Starts the Zoho consent flow; the UI sends the browser to the returned URL.</summary>
    public async Task<ConnectZohoPayload> ConnectZoho(
        IZohoConnectionService zoho,
        IHttpContextAccessor httpContext,
        CancellationToken cancellationToken)
    {
        var request = httpContext.HttpContext!.Request;
        var callback = new Uri($"{request.Scheme}://{request.Host}{request.PathBase}{CallbackPath}");
        return new ConnectZohoPayload((await zoho.StartAsync(callback, cancellationToken)).AbsoluteUri);
    }

    /// <summary>Revokes and forgets the employee's Zoho tokens.</summary>
    public async Task<bool> DisconnectZoho(IZohoConnectionService zoho, CancellationToken cancellationToken)
    {
        await zoho.DisconnectAsync(cancellationToken);
        return true;
    }
}

public sealed record ConnectZohoPayload(string AuthorizationUrl);
