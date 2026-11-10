using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using OneSpace.Application.Abstractions;
using OneSpace.Application.Integrations;

namespace OneSpace.Connectors.Microsoft365;

public sealed class Microsoft365Options
{
    public const string SectionName = "Integrations:Microsoft365";

    public string Instance { get; set; } = "https://login.microsoftonline.com/";
    public string? TenantId { get; set; }

    /// <summary>The OneSpace API app registration (same one the UI requests tokens for).</summary>
    public string? ClientId { get; set; }

    /// <summary>Key Vault reference only. Prefer a certificate / managed identity federated credential in higher envs.</summary>
    public string? ClientSecret { get; set; }

    public string GraphBaseUrl { get; set; } = "https://graph.microsoft.com/v1.0/";

    public string[] Scopes { get; set; } = ["https://graph.microsoft.com/User.Read", "https://graph.microsoft.com/Calendars.Read"];

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TenantId) && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>Exchanges the employee's inbound token for a Graph token (on-behalf-of), so Graph enforces their own permissions.</summary>
public sealed class OnBehalfOfTokenProvider
{
    private readonly Microsoft365Options _options;
    private readonly Lazy<IConfidentialClientApplication> _app;

    public OnBehalfOfTokenProvider(IOptions<Microsoft365Options> options)
    {
        _options = options.Value;
        _app = new Lazy<IConfidentialClientApplication>(() => ConfidentialClientApplicationBuilder
            .Create(_options.ClientId)
            .WithClientSecret(_options.ClientSecret)
            .WithAuthority(new Uri(new Uri(_options.Instance), _options.TenantId))
            .Build());
    }

    public async Task<string> GetGraphTokenAsync(string userAssertion, CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured) throw new InvalidOperationException("Microsoft 365 integration is not configured.");

        // MSAL caches OBO tokens in memory keyed by the assertion; nothing is persisted.
        var result = await _app.Value
            .AcquireTokenOnBehalfOf(_options.Scopes, new UserAssertion(userAssertion))
            .ExecuteAsync(cancellationToken);
        return result.AccessToken;
    }
}

public sealed class GraphMicrosoft365Client(
    HttpClient http,
    OnBehalfOfTokenProvider tokens,
    IUserContextAccessor userContext) : IMicrosoft365Client
{
    public async Task<EmployeeProfile> GetMyProfileAsync(CancellationToken cancellationToken)
    {
        using var doc = await GetAsync("me?$select=displayName,mail,userPrincipalName,jobTitle,department,officeLocation", cancellationToken);
        var me = doc.RootElement;
        return new EmployeeProfile(
            Str(me, "displayName") ?? string.Empty,
            Str(me, "mail") ?? Str(me, "userPrincipalName"),
            Str(me, "jobTitle"),
            Str(me, "department"),
            Str(me, "officeLocation"));
    }

    public async Task<IReadOnlyList<CalendarEntry>> GetMyCalendarAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var query = string.Create(CultureInfo.InvariantCulture,
            $"me/calendarView?startDateTime={Uri.EscapeDataString(from.UtcDateTime.ToString("O"))}&endDateTime={Uri.EscapeDataString(to.UtcDateTime.ToString("O"))}&$select=subject,start,end,showAs,location&$orderby=start/dateTime&$top=50");
        using var doc = await GetAsync(query, cancellationToken, preferUtc: true);

        return doc.RootElement.GetProperty("value").EnumerateArray()
            .Select(e => new CalendarEntry(
                Str(e, "subject") ?? "(no subject)",
                ParseGraphDate(e.GetProperty("start")),
                ParseGraphDate(e.GetProperty("end")),
                Str(e, "showAs") ?? "unknown",
                e.TryGetProperty("location", out var loc) ? Str(loc, "displayName") : null))
            .ToList();
    }

    private async Task<JsonDocument> GetAsync(string relativeUrl, CancellationToken cancellationToken, bool preferUtc = false)
    {
        var assertion = userContext.Current.AccessToken
            ?? throw new InvalidOperationException("No user token is available for on-behalf-of exchange.");
        var token = await tokens.GetGraphTokenAsync(assertion, cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (preferUtc) request.Headers.TryAddWithoutValidation("Prefer", "outlook.timezone=\"UTC\"");

        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset ParseGraphDate(JsonElement dateTimeTimeZone) =>
        DateTimeOffset.Parse(Str(dateTimeTimeZone, "dateTime")!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
}
