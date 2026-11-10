using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OneSpace.Application.Abstractions;
using OneSpace.Application.Integrations;
using OneSpace.Domain.Integrations;

namespace OneSpace.Connectors.Zoho;

/// <summary>
/// "Connect Zoho People": MCP OAuth (RFC 9728 discovery, RFC 7591 dynamic registration, authorization code + PKCE S256,
/// refresh tokens with rotation, RFC 7009 revocation). Tokens are persisted only through the encrypting stores.
/// </summary>
public sealed partial class ZohoOAuthService(
    HttpClient http,
    IOptions<ZohoMcpOptions> options,
    IUserContextAccessor userContext,
    IZohoTokenStore tokens,
    IOAuthStateStore states,
    IOAuthClientStore clients,
    ZohoAuthMetadataCache metadataCache,
    TimeProvider clock,
    ILogger<ZohoOAuthService> logger) : IZohoConnectionService, IZohoAccessTokenProvider
{
    // One refresh at a time per employee, so a rotated refresh token is never used twice.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RefreshLocks = new();

    private readonly ZohoMcpOptions _options = options.Value;

    public async Task<ZohoConnectionStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var set = await tokens.GetAsync(userContext.Current.ObjectId, cancellationToken);
        return set is null ? new ZohoConnectionStatus(false, null, []) : new ZohoConnectionStatus(true, set.ConnectedAt, set.Scopes);
    }

    public async Task<Uri> StartAsync(Uri callbackUri, CancellationToken cancellationToken)
    {
        var user = userContext.Current;
        var metadata = await GetMetadataAsync(cancellationToken);
        var redirectUri = string.IsNullOrWhiteSpace(_options.OAuth.RedirectUri) ? callbackUri.AbsoluteUri : _options.OAuth.RedirectUri;
        var client = await EnsureClientAsync(metadata, redirectUri, cancellationToken);

        var verifier = RandomToken();
        var state = RandomToken();
        await states.SaveAsync(new PendingAuthorization(state, user.ObjectId, verifier, redirectUri,
            clock.GetUtcNow() + _options.OAuth.StateLifetime), cancellationToken);

        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = client.ClientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = string.Join(' ', _options.OAuth.Scopes),
            ["state"] = state,
            ["code_challenge"] = Challenge(verifier),
            ["code_challenge_method"] = "S256",
            ["resource"] = _options.Endpoint!,
            // Zoho issues a refresh token only for offline access.
            ["access_type"] = "offline",
            ["prompt"] = "consent",
        };
        return new Uri($"{metadata.AuthorizationEndpoint}?{string.Join('&', query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"))}");
    }

    public async Task<bool> CompleteAsync(string? state, string? code, string? error, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(state)) return false;

        // Single use: taken (deleted) whether or not the rest succeeds.
        var pending = await states.TakeAsync(state, cancellationToken);
        if (pending is null)
        {
            LogInvalidState(logger);
            return false;
        }

        if (!string.IsNullOrEmpty(error) || string.IsNullOrWhiteSpace(code))
        {
            LogConsentDenied(logger, error ?? "no code");
            return false;
        }

        var metadata = await GetMetadataAsync(cancellationToken);
        var client = await EnsureClientAsync(metadata, pending.RedirectUri, cancellationToken);
        var response = await PostTokenAsync(metadata, client, new()
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = pending.RedirectUri,
            ["code_verifier"] = pending.CodeVerifier,
        }, cancellationToken);

        if (response is null) return false;
        if (string.IsNullOrEmpty(response.RefreshToken)) LogNoRefreshToken(logger);

        var now = clock.GetUtcNow();
        await tokens.SaveAsync(new ZohoTokenSet(pending.UserId, response.AccessToken, ExpiresAt(response, now),
            response.RefreshToken ?? string.Empty, Scopes(response), now, now), cancellationToken);
        return true;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        var userId = userContext.Current.ObjectId;
        var set = await tokens.GetAsync(userId, cancellationToken);
        if (set is null) return;

        try
        {
            var metadata = await GetMetadataAsync(cancellationToken);
            if (metadata.RevocationEndpoint is not null && !string.IsNullOrEmpty(set.RefreshToken))
            {
                var client = await ExistingClientAsync(metadata, cancellationToken);
                using var body = new FormUrlEncodedContent(WithClient(client, new() { ["token"] = set.RefreshToken }));
                using var _ = await http.PostAsync(metadata.RevocationEndpoint, body, cancellationToken);
            }
        }
        catch (HttpRequestException ex)
        {
            // Forgetting the tokens matters more than a failed revoke; the employee can also revoke in Zoho.
            LogRevokeFailed(logger, ex);
        }

        await tokens.DeleteAsync(userId, cancellationToken);
    }

    public async Task<string> GetAccessTokenAsync(string userId, bool forceRefresh, CancellationToken cancellationToken)
    {
        var gate = RefreshLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var set = await tokens.GetAsync(userId, cancellationToken)
                ?? throw new ZohoConnectionRequiredException("not connected");

            if (!forceRefresh && set.AccessTokenExpiresAt - _options.OAuth.RefreshSkew > clock.GetUtcNow()) return set.AccessToken;

            if (string.IsNullOrEmpty(set.RefreshToken))
            {
                await tokens.DeleteAsync(userId, cancellationToken);
                throw new ZohoConnectionRequiredException("session expired");
            }

            var metadata = await GetMetadataAsync(cancellationToken);
            var client = await ExistingClientAsync(metadata, cancellationToken);
            var response = await PostTokenAsync(metadata, client, new()
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = set.RefreshToken,
            }, cancellationToken);

            if (response is null)
            {
                // invalid_grant: consent revoked or refresh token expired. Forget it and ask the employee to reconnect.
                await tokens.DeleteAsync(userId, cancellationToken);
                throw new ZohoConnectionRequiredException("consent expired or was revoked");
            }

            var now = clock.GetUtcNow();
            await tokens.SaveAsync(set with
            {
                AccessToken = response.AccessToken,
                AccessTokenExpiresAt = ExpiresAt(response, now),
                RefreshToken = string.IsNullOrEmpty(response.RefreshToken) ? set.RefreshToken : response.RefreshToken,
                Scopes = response.Scope is null ? set.Scopes : Scopes(response),
                UpdatedAt = now
            }, cancellationToken);
            return response.AccessToken;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<TokenResponse?> PostTokenAsync(
        AuthServerMetadata metadata, RegisteredOAuthClient client, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        form["resource"] = _options.Endpoint!;
        using var body = new FormUrlEncodedContent(WithClient(client, form));
        using var response = await http.PostAsync(metadata.TokenEndpoint, body, cancellationToken);

        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            LogTokenRejected(logger, (int)response.StatusCode, Truncate(error));
            return null;
        }

        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        return string.IsNullOrEmpty(token?.AccessToken) ? null : token;
    }

    private async Task<RegisteredOAuthClient> EnsureClientAsync(AuthServerMetadata metadata, string redirectUri, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.OAuth.ClientId))
            return new RegisteredOAuthClient(metadata.Issuer, _options.OAuth.ClientId, _options.OAuth.ClientSecret, redirectUri, DateTimeOffset.MinValue);

        var existing = await clients.GetAsync(metadata.Issuer, cancellationToken);
        if (existing is not null && existing.RedirectUri == redirectUri) return existing;

        if (metadata.RegistrationEndpoint is null)
            throw new InvalidOperationException("The Zoho authorization server does not support dynamic registration; configure OAuth:ClientId.");

        using var response = await http.PostAsJsonAsync(metadata.RegistrationEndpoint, new
        {
            client_name = _options.OAuth.ClientName,
            redirect_uris = new[] { redirectUri },
            grant_types = new[] { "authorization_code", "refresh_token" },
            response_types = new[] { "code" },
            token_endpoint_auth_method = "client_secret_post",
            scope = string.Join(' ', _options.OAuth.Scopes),
        }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var registration = await response.Content.ReadFromJsonAsync<RegistrationResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Empty client registration response.");

        var client = new RegisteredOAuthClient(metadata.Issuer, registration.ClientId, registration.ClientSecret, redirectUri, clock.GetUtcNow());
        await clients.SaveAsync(client, cancellationToken);
        return client;
    }

    private Task<AuthServerMetadata> GetMetadataAsync(CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured) throw new InvalidOperationException("Zoho MCP integration is not configured.");
        return metadataCache.GetAsync(new Uri(_options.Endpoint!), http, cancellationToken);
    }

    /// <summary>The client the tokens were issued to. Refresh and revoke must never trigger a new registration.</summary>
    private async Task<RegisteredOAuthClient> ExistingClientAsync(AuthServerMetadata metadata, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.OAuth.ClientId))
            return new RegisteredOAuthClient(metadata.Issuer, _options.OAuth.ClientId, _options.OAuth.ClientSecret, string.Empty, DateTimeOffset.MinValue);

        return await clients.GetAsync(metadata.Issuer, cancellationToken)
            ?? throw new ZohoConnectionRequiredException("OAuth client registration missing");
    }

    private static Dictionary<string, string> WithClient(RegisteredOAuthClient client, Dictionary<string, string> form)
    {
        form["client_id"] = client.ClientId;
        if (!string.IsNullOrEmpty(client.ClientSecret)) form["client_secret"] = client.ClientSecret;
        return form;
    }

    private static DateTimeOffset ExpiresAt(TokenResponse response, DateTimeOffset now) =>
        now.AddSeconds(response.ExpiresIn is > 0 ? response.ExpiresIn.Value : 3600);

    private IReadOnlyList<string> Scopes(TokenResponse response) =>
        response.Scope?.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries) ?? [.. _options.OAuth.Scopes];

    internal static string RandomToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    internal static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Truncate(string text) => text.Length <= 200 ? text : text[..200];

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int? ExpiresIn,
        [property: JsonPropertyName("scope")] string? Scope);

    private sealed record RegistrationResponse(
        [property: JsonPropertyName("client_id")] string ClientId,
        [property: JsonPropertyName("client_secret")] string? ClientSecret);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Zoho OAuth callback with unknown, expired or reused state")]
    private static partial void LogInvalidState(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Zoho consent not granted: {Error}")]
    private static partial void LogConsentDenied(ILogger logger, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Zoho returned no refresh token; the employee will need to reconnect when the access token expires")]
    private static partial void LogNoRefreshToken(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Zoho token endpoint rejected the request ({Status}): {Body}")]
    private static partial void LogTokenRejected(ILogger logger, int status, string body);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Zoho token revocation failed")]
    private static partial void LogRevokeFailed(ILogger logger, Exception ex);
}

/// <summary>RFC 9728 → RFC 8414 discovery, cached per process.</summary>
public sealed class ZohoAuthMetadataCache(TimeProvider clock)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private AuthServerMetadata? _metadata;
    private DateTimeOffset _expires;

    public async Task<AuthServerMetadata> GetAsync(Uri resource, HttpClient http, CancellationToken cancellationToken)
    {
        if (_metadata is not null && clock.GetUtcNow() < _expires) return _metadata;
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_metadata is null || clock.GetUtcNow() >= _expires)
            {
                var origin = resource.GetLeftPart(UriPartial.Authority);
                var protectedResource = await http.GetFromJsonAsync<JsonElement>($"{origin}/.well-known/oauth-protected-resource", cancellationToken);
                var issuer = protectedResource.TryGetProperty("authorization_servers", out var servers) && servers.GetArrayLength() > 0
                    ? servers[0].GetString()!.TrimEnd('/')
                    : origin;
                var json = await http.GetFromJsonAsync<JsonElement>($"{issuer}/.well-known/oauth-authorization-server", cancellationToken);

                _metadata = new AuthServerMetadata(
                    json.GetProperty("issuer").GetString()!,
                    json.GetProperty("authorization_endpoint").GetString()!,
                    json.GetProperty("token_endpoint").GetString()!,
                    json.TryGetProperty("registration_endpoint", out var reg) ? reg.GetString() : null,
                    json.TryGetProperty("revocation_endpoint", out var rev) ? rev.GetString() : null);
                _expires = clock.GetUtcNow().AddHours(1);
            }
            return _metadata;
        }
        finally
        {
            _lock.Release();
        }
    }
}

public sealed record AuthServerMetadata(
    string Issuer,
    string AuthorizationEndpoint,
    string TokenEndpoint,
    string? RegistrationEndpoint,
    string? RevocationEndpoint);
