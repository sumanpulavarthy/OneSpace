using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OneSpace.Application.Abstractions;
using OneSpace.Application.Integrations;
using OneSpace.Connectors.Zoho;
using OneSpace.Domain.Integrations;
using OneSpace.Domain.Users;

namespace OneSpace.UnitTests.Connectors;

public class ZohoOAuthServiceTests
{
    private static readonly Uri Callback = new("https://api.test/oauth/zoho/callback");

    private readonly FakeAuthServer _server = new();
    private readonly MemoryStores _stores = new();
    private readonly MutableClock _clock = new();

    [Fact]
    public async Task Start_registers_once_and_builds_a_pkce_authorization_url()
    {
        var service = Service();

        var url1 = await service.StartAsync(Callback, CancellationToken.None);
        await service.StartAsync(Callback, CancellationToken.None);

        var query = Query(url1);
        Assert.StartsWith("https://auth.test/authorize?", url1.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("client-1", query["client_id"]);
        Assert.Equal(Callback.AbsoluteUri, query["redirect_uri"]);
        Assert.Equal("offline", query["access_type"]);
        Assert.Contains("zohopeople.leave.all", query["scope"], StringComparison.Ordinal);
        Assert.Equal(1, _server.Registrations);

        // The verifier stored for the state matches the challenge sent to Zoho.
        var pending = _stores.States[query["state"]];
        Assert.Equal(query["code_challenge"], ZohoOAuthService.Challenge(pending.CodeVerifier));
        Assert.Equal("oid-1", pending.UserId);
    }

    [Fact]
    public async Task Complete_exchanges_the_code_with_the_verifier_and_stores_tokens_for_that_employee()
    {
        var service = Service();
        var state = Query(await service.StartAsync(Callback, CancellationToken.None))["state"];

        Assert.True(await service.CompleteAsync(state, "code-1", null, CancellationToken.None));

        var exchange = _server.TokenRequests.Single();
        Assert.Equal("authorization_code", exchange["grant_type"]);
        Assert.Equal("code-1", exchange["code"]);
        Assert.False(string.IsNullOrEmpty(exchange["code_verifier"]));
        Assert.Equal("secret-1", exchange["client_secret"]);
        var stored = _stores.Tokens["oid-1"];
        Assert.Equal("access-1", stored.AccessToken);
        Assert.Equal("refresh-1", stored.RefreshToken);
    }

    [Fact]
    public async Task State_is_single_use_and_unknown_state_is_rejected()
    {
        var service = Service();
        var state = Query(await service.StartAsync(Callback, CancellationToken.None))["state"];

        Assert.True(await service.CompleteAsync(state, "code-1", null, CancellationToken.None));
        Assert.False(await service.CompleteAsync(state, "code-1", null, CancellationToken.None));
        Assert.False(await service.CompleteAsync("forged", "code-1", null, CancellationToken.None));
        Assert.Single(_server.TokenRequests);
    }

    [Fact]
    public async Task Consent_denied_stores_nothing()
    {
        var service = Service();
        var state = Query(await service.StartAsync(Callback, CancellationToken.None))["state"];

        Assert.False(await service.CompleteAsync(state, null, "access_denied", CancellationToken.None));
        Assert.Empty(_stores.Tokens);
    }

    [Fact]
    public async Task Expired_access_token_is_refreshed_and_a_rotated_refresh_token_is_kept()
    {
        var service = await ConnectedServiceAsync();
        _server.RotateRefreshToken = true;
        _clock.Advance(TimeSpan.FromHours(2));

        var token = await service.GetAccessTokenAsync("oid-1", forceRefresh: false, CancellationToken.None);

        Assert.Equal("access-2", token);
        Assert.Equal("refresh_token", _server.TokenRequests[^1]["grant_type"]);
        Assert.Equal("refresh-1", _server.TokenRequests[^1]["refresh_token"]);
        Assert.Equal("refresh-2", _stores.Tokens["oid-1"].RefreshToken);
    }

    [Fact]
    public async Task Valid_access_token_is_reused_without_a_network_call()
    {
        var service = await ConnectedServiceAsync();
        var calls = _server.TokenRequests.Count;

        Assert.Equal("access-1", await service.GetAccessTokenAsync("oid-1", forceRefresh: false, CancellationToken.None));
        Assert.Equal(calls, _server.TokenRequests.Count);
    }

    [Fact]
    public async Task Revoked_consent_forgets_tokens_and_requires_reconnect()
    {
        var service = await ConnectedServiceAsync();
        _server.RejectRefresh = true;

        await Assert.ThrowsAsync<ZohoConnectionRequiredException>(() =>
            service.GetAccessTokenAsync("oid-1", forceRefresh: true, CancellationToken.None));
        Assert.Empty(_stores.Tokens);
    }

    [Fact]
    public async Task Not_connected_requires_connect()
    {
        await Assert.ThrowsAsync<ZohoConnectionRequiredException>(() =>
            Service().GetAccessTokenAsync("oid-1", forceRefresh: false, CancellationToken.None));
    }

    [Fact]
    public async Task Disconnect_revokes_and_forgets()
    {
        var service = await ConnectedServiceAsync();

        await service.DisconnectAsync(CancellationToken.None);

        Assert.Equal("refresh-1", _server.Revoked.Single());
        Assert.Empty(_stores.Tokens);
        Assert.False((await service.GetStatusAsync(CancellationToken.None)).Connected);
    }

    private async Task<ZohoOAuthService> ConnectedServiceAsync()
    {
        var service = Service();
        var state = Query(await service.StartAsync(Callback, CancellationToken.None))["state"];
        Assert.True(await service.CompleteAsync(state, "code-1", null, CancellationToken.None));
        return service;
    }

    private ZohoOAuthService Service() => new(
        new HttpClient(_server),
        Options.Create(new ZohoMcpOptions { Endpoint = "https://mcp.test/mcp/key/message" }),
        new FixedUser(),
        _stores, _stores, _stores,
        new ZohoAuthMetadataCache(_clock),
        _clock,
        NullLogger<ZohoOAuthService>.Instance);

    private static Dictionary<string, string> Query(Uri uri) =>
        uri.Query.TrimStart('?').Split('&')
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));

    private sealed class FixedUser : IUserContextAccessor
    {
        public UserContext Current { get; } = new("oid-1", "me@example.com", "Me", "me@example.com", "entra-token");
    }

    private sealed class MutableClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
        public void Advance(TimeSpan by) => _now += by;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class MemoryStores : IZohoTokenStore, IOAuthStateStore, IOAuthClientStore
    {
        public Dictionary<string, ZohoTokenSet> Tokens { get; } = [];
        public Dictionary<string, PendingAuthorization> States { get; } = [];
        private readonly Dictionary<string, RegisteredOAuthClient> _clients = [];

        public Task<ZohoTokenSet?> GetAsync(string userId, CancellationToken cancellationToken) => Task.FromResult(Tokens.GetValueOrDefault(userId));
        public Task SaveAsync(ZohoTokenSet tokens, CancellationToken cancellationToken) { Tokens[tokens.UserId] = tokens; return Task.CompletedTask; }
        public Task DeleteAsync(string userId, CancellationToken cancellationToken) { Tokens.Remove(userId); return Task.CompletedTask; }
        public Task SaveAsync(PendingAuthorization pending, CancellationToken cancellationToken) { States[pending.State] = pending; return Task.CompletedTask; }
        public Task<PendingAuthorization?> TakeAsync(string state, CancellationToken cancellationToken) =>
            Task.FromResult(States.Remove(state, out var p) ? p : null);
        Task<RegisteredOAuthClient?> IOAuthClientStore.GetAsync(string issuer, CancellationToken cancellationToken) => Task.FromResult(_clients.GetValueOrDefault(issuer));
        public Task SaveAsync(RegisteredOAuthClient client, CancellationToken cancellationToken) { _clients[client.Issuer] = client; return Task.CompletedTask; }
    }

    /// <summary>Fake Zoho MCP authorization server: discovery, registration, token, revocation.</summary>
    private sealed class FakeAuthServer : HttpMessageHandler
    {
        private int _accessVersion;
        private int _refreshVersion = 1;
        public int Registrations { get; private set; }
        public List<Dictionary<string, string>> TokenRequests { get; } = [];
        public List<string> Revoked { get; } = [];
        public bool RotateRefreshToken { get; set; }
        public bool RejectRefresh { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            switch (path)
            {
                case "/.well-known/oauth-protected-resource" when request.RequestUri.Host == "mcp.test":
                    return Json(new { resource = "https://mcp.test/mcp/key/message", authorization_servers = new[] { "https://auth.test" } });
                case "/.well-known/oauth-authorization-server":
                    return Json(new
                    {
                        issuer = "https://auth.test",
                        authorization_endpoint = "https://auth.test/authorize",
                        token_endpoint = "https://auth.test/token",
                        registration_endpoint = "https://auth.test/register",
                        revocation_endpoint = "https://auth.test/revoke"
                    });
                case "/register":
                    Registrations++;
                    return Json(new { client_id = "client-1", client_secret = "secret-1" });
                case "/token":
                    var form = Form(await request.Content!.ReadAsStringAsync(cancellationToken));
                    TokenRequests.Add(form);
                    if (form["grant_type"] == "refresh_token" && RejectRefresh)
                        return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("""{"error":"invalid_grant"}""") };
                    _accessVersion++;
                    if (form["grant_type"] == "refresh_token" && RotateRefreshToken) _refreshVersion++;
                    return Json(new
                    {
                        access_token = $"access-{_accessVersion}",
                        refresh_token = form["grant_type"] == "authorization_code" || RotateRefreshToken ? $"refresh-{_refreshVersion}" : null,
                        expires_in = 3600,
                        scope = "zohopeople.leave.all ZohoMCP.tool.execute"
                    });
                case "/revoke":
                    Revoked.Add(Form(await request.Content!.ReadAsStringAsync(cancellationToken))["token"]);
                    return new HttpResponseMessage(HttpStatusCode.OK);
                default:
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        }

        private static HttpResponseMessage Json(object body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

        private static Dictionary<string, string> Form(string body) =>
            body.Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));
    }
}
