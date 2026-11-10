using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OneSpace.Application.Abstractions;
using OneSpace.Domain.Users;

namespace OneSpace.Infrastructure.Identity;

/// <summary>
/// Resolves the employee from the validated Entra ID token. Claims are not remapped (MapInboundClaims = false),
/// so raw JWT claim names are used — same convention as cmart-accord.
/// </summary>
public sealed class HttpUserContextAccessor(IHttpContextAccessor httpContextAccessor) : IUserContextAccessor
{
    public UserContext Current
    {
        get
        {
            var context = httpContextAccessor.HttpContext
                ?? throw new InvalidOperationException("No HTTP request is in progress.");
            var user = context.User;
            if (user.Identity?.IsAuthenticated != true) throw new UnauthorizedAccessException("The request is not authenticated.");

            var objectId = First(user, "oid", "http://schemas.microsoft.com/identity/claims/objectidentifier", "sub")
                ?? throw new UnauthorizedAccessException("The token has no user identifier.");
            var upn = First(user, "preferred_username", "upn", "unique_name") ?? objectId;

            return new UserContext(
                objectId,
                upn,
                First(user, "name") ?? upn,
                First(user, "email") ?? (upn.Contains('@', StringComparison.Ordinal) ? upn : null),
                BearerToken(context));
        }
    }

    private static string? First(ClaimsPrincipal user, params string[] types) =>
        types.Select(t => user.FindFirst(t)?.Value).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string? BearerToken(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..].Trim() : null;
    }
}
