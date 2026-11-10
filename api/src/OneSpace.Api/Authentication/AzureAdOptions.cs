using Microsoft.IdentityModel.Tokens;

namespace OneSpace.Api.Authentication;

/// <summary>
/// Microsoft Entra ID app registration the API validates bearer tokens against (bound from "AzureAd").
/// Only public identifiers are needed to validate tokens. Same shape as cmart-accord.
/// </summary>
public sealed class AzureAdOptions
{
    public const string SectionName = "AzureAd";

    public string Instance { get; init; } = "https://login.microsoftonline.com/";

    public string TenantId { get; init; } = string.Empty;

    public string ClientId { get; init; } = string.Empty;

    public string Authority => $"{Instance.TrimEnd('/')}/{TenantId}/v2.0";

    public IReadOnlyList<string> ValidAudiences => [ClientId, $"api://{ClientId}"];

    public IReadOnlyList<string> ValidIssuers =>
    [
        $"{Instance.TrimEnd('/')}/{TenantId}/v2.0",
        $"https://sts.windows.net/{TenantId}/"
    ];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(TenantId) && !string.IsNullOrWhiteSpace(ClientId);

    public TokenValidationParameters CreateTokenValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuers = ValidIssuers,
        ValidateAudience = true,
        ValidAudiences = ValidAudiences,
        ValidateLifetime = true,
        RequireExpirationTime = true,
        RequireSignedTokens = true,
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.FromMinutes(1)
    };
}
