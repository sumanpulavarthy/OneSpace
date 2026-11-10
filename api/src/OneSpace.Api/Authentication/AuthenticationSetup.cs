using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace OneSpace.Api.Authentication;

public static class AuthenticationSetup
{
    public const string DevelopmentUserSetting = "Authentication:AllowDevelopmentUser";

    /// <summary>
    /// Validates Entra ID bearer tokens (signature, issuer, audience, expiry). Fails at startup outside
    /// Development when the "AzureAd" section is missing, so a misconfigured host never runs open.
    /// </summary>
    public static IServiceCollection AddOneSpaceAuthentication(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var azureAd = configuration.GetSection(AzureAdOptions.SectionName).Get<AzureAdOptions>() ?? new AzureAdOptions();
        if (!azureAd.IsConfigured && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"Microsoft Entra ID authentication is not configured. Set {AzureAdOptions.SectionName}:TenantId and {AzureAdOptions.SectionName}:ClientId.");
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = azureAd.Authority;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = azureAd.CreateTokenValidationParameters();
            });

        services.AddTransient<AuthenticationMiddleware>();
        return services;
    }
}

/// <summary>
/// Rejects requests without a valid Entra ID bearer token (401) before they reach GraphQL.
/// Only in the Development environment, and only when <c>Authentication:AllowDevelopmentUser</c> is true,
/// a request without a token runs as a fixed local developer identity (no downstream OBO is possible for it).
/// </summary>
public sealed partial class AuthenticationMiddleware(
    IWebHostEnvironment environment,
    IConfiguration configuration,
    ILogger<AuthenticationMiddleware> logger) : IMiddleware
{
    // The OAuth callback is a browser redirect from Zoho (no bearer token); its state parameter authenticates it.
    private static readonly PathString[] AnonymousPaths = ["/health", "/oauth/zoho/callback"];

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (HttpMethods.IsOptions(context.Request.Method) || AnonymousPaths.Any(p => context.Request.Path.StartsWithSegments(p)) || IsPlaygroundPage(context))
        {
            await next(context);
            return;
        }

        var result = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        if (result.Succeeded && result.Principal is not null)
        {
            context.User = result.Principal;
            await next(context);
            return;
        }

        if (environment.IsDevelopment() && configuration.GetValue<bool>(AuthenticationSetup.DevelopmentUserSetting)
            && !context.Request.Headers.ContainsKey("Authorization"))
        {
            context.User = DevelopmentUser();
            await next(context);
            return;
        }

        LogRejected(logger, context.Request.Path.Value ?? "/", result.Failure?.Message ?? "No bearer token.");
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = JwtBearerDefaults.AuthenticationScheme;
    }

    private bool IsPlaygroundPage(HttpContext context) =>
        environment.IsDevelopment() && HttpMethods.IsGet(context.Request.Method) && context.Request.Path.StartsWithSegments("/graphql")
        && context.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);

    private static ClaimsPrincipal DevelopmentUser() => new(new ClaimsIdentity(
    [
        new Claim("oid", "00000000-0000-0000-0000-00000000dev1"),
        new Claim("preferred_username", "dev.user@onespace.local"),
        new Claim("name", "Local Developer"),
    ], authenticationType: "Development"));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected unauthenticated request to {Path}: {Reason}")]
    private static partial void LogRejected(ILogger logger, string path, string reason);
}
