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

    /// <summary>Outlook calendar entries in a range (max 31 days).</summary>
    public Task<IReadOnlyList<CalendarEntry>> MyCalendar(DateTimeOffset from, DateTimeOffset to, IMicrosoft365Client m365, CancellationToken cancellationToken) =>
        to <= from || to - from > TimeSpan.FromDays(31)
            ? throw new GraphQLException("'to' must be after 'from' and within 31 days.")
            : m365.GetMyCalendarAsync(from, to, cancellationToken);

    /// <summary>The caller's manager; null when none is set.</summary>
    public Task<EmployeeProfile?> MyManager(IMicrosoft365Client m365, CancellationToken cancellationToken) =>
        m365.GetMyManagerAsync(cancellationToken);

    public Task<IReadOnlyList<EmployeeProfile>> MyDirectReports(IMicrosoft365Client m365, CancellationToken cancellationToken) =>
        m365.GetMyDirectReportsAsync(cancellationToken);

    /// <summary>Teams presence.</summary>
    public Task<PresenceStatus> MyPresence(IMicrosoft365Client m365, CancellationToken cancellationToken) =>
        m365.GetMyPresenceAsync(cancellationToken);

    /// <summary>Newest Outlook messages (previews only), or KQL search results.</summary>
    public Task<IReadOnlyList<MailMessageSummary>> MyMail(IMicrosoft365Client m365, CancellationToken cancellationToken, string? search = null, int top = 10) =>
        m365.GetMyMessagesAsync(search, top, cancellationToken);

    /// <summary>Recently used OneDrive/SharePoint files, or a OneDrive search.</summary>
    public Task<IReadOnlyList<DriveItemSummary>> MyFiles(IMicrosoft365Client m365, CancellationToken cancellationToken, string? search = null, int top = 10) =>
        m365.GetMyFilesAsync(search, top, cancellationToken);

    public Task<IReadOnlyList<TeamSummary>> MyTeams(IMicrosoft365Client m365, CancellationToken cancellationToken) =>
        m365.GetMyTeamsAsync(cancellationToken);

    /// <summary>Most recently active Teams chats.</summary>
    public Task<IReadOnlyList<ChatSummary>> MyChats(IMicrosoft365Client m365, CancellationToken cancellationToken, int top = 10) =>
        m365.GetMyChatsAsync(top, cancellationToken);

    /// <summary>Open Microsoft To Do tasks, soonest due first.</summary>
    public Task<IReadOnlyList<TodoTaskSummary>> MyTasks(IMicrosoft365Client m365, CancellationToken cancellationToken, int top = 20) =>
        m365.GetMyTasksAsync(top, cancellationToken);

    /// <summary>Leave balance straight from Zoho People (allowlisted tool, self-scope).</summary>
    public async Task<string> LeaveBalance(IZohoPeopleClient zoho, CancellationToken cancellationToken) =>
        (await zoho.CallToolAsync("getLeaveBalance", null, cancellationToken)).Text;

    /// <summary>Search the synced ComplyKaro policy cache (hybrid keyword + vector when embeddings are configured, FR-3).</summary>
    public Task<IReadOnlyList<PolicySearchResult>> SearchPolicies(
        string query,
        IPolicySearch policies,
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
