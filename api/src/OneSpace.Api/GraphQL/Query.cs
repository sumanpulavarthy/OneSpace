using OneSpace.Application.Abstractions;
using OneSpace.Application.Integrations;
using OneSpace.Domain.Assistant;
using OneSpace.Domain.Policies;

namespace OneSpace.Api.GraphQL;

/// <summary>
/// Read-only queries. Every field resolves for the calling employee only; there is no argument that
/// selects another employee (FR-2).
/// </summary>
public sealed class Query
{
    /// <summary>The signed-in employee, from the token (no downstream call).</summary>
    public Me Me(IUserContextAccessor user)
    {
        var u = user.Current;
        return new Me(u.ObjectId, u.DisplayName, u.UserPrincipalName, u.Email);
    }

    /// <summary>Microsoft 365 profile (Graph, on-behalf-of).</summary>
    public Task<EmployeeProfile> MyProfile(IMicrosoft365Client m365, CancellationToken cancellationToken) =>
        m365.GetMyProfileAsync(cancellationToken);

    /// <summary>Leave balance straight from Zoho People (allowlisted tool, self-scope).</summary>
    public async Task<string> LeaveBalance(IZohoPeopleClient zoho, CancellationToken cancellationToken) =>
        (await zoho.CallToolAsync("getLeaveBalance", null, cancellationToken)).Text;

    /// <summary>Search the synced ComplyKaro policy cache.</summary>
    public Task<IReadOnlyList<PolicySearchResult>> SearchPolicies(
        string query,
        IPolicyDocumentRepository policies,
        CancellationToken cancellationToken,
        int limit = 5) =>
        policies.SearchAsync(query, limit, cancellationToken);

    /// <summary>The caller's own recent assistant questions (FR-4 log), newest first.</summary>
    public Task<IReadOnlyList<QueryLogEntry>> RecentQueries(
        IUserContextAccessor user,
        IQueryLogRepository queryLog,
        CancellationToken cancellationToken,
        int limit = 20) =>
        queryLog.GetRecentAsync(user.Current.ObjectId, limit, cancellationToken);
}

public sealed record Me(string Id, string DisplayName, string UserPrincipalName, string? Email);
