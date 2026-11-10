namespace OneSpace.Domain.Users;

/// <summary>
/// The employee making the request, resolved from the validated Entra ID token.
/// <see cref="AccessToken"/> is the inbound bearer token, used only as the user assertion for
/// on-behalf-of exchange; it is never persisted (NFR: no tokens beyond session).
/// </summary>
public sealed record UserContext(
    string ObjectId,
    string UserPrincipalName,
    string DisplayName,
    string? Email,
    string? AccessToken)
{
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(ObjectId);
}
