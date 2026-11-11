using System.Text.Json.Nodes;
using OneSpace.Domain.Policies;

namespace OneSpace.Application.Integrations;

/// <summary>Zoho People via the Zoho MCP server (JSON-RPC). All calls are guarded by <see cref="IToolAllowlistGuard"/>.</summary>
public interface IZohoPeopleClient
{
    /// <summary>
    /// Tools from <c>tools/list</c>, filtered to the allowlist. New server tools never appear here
    /// unless the allowlist spec is changed.
    /// </summary>
    Task<IReadOnlyList<McpToolDescriptor>> ListAllowedToolsAsync(CancellationToken cancellationToken);

    /// <summary>Calls an allowlisted tool as the current employee. Throws <see cref="ToolBlockedException"/> otherwise.</summary>
    Task<McpToolResult> CallToolAsync(string toolName, JsonObject? arguments, CancellationToken cancellationToken);
}

public sealed record McpToolDescriptor(string Name, string? Description, JsonObject InputSchema);

public sealed record McpToolResult(string Text, bool IsError);

/// <summary>
/// Microsoft Graph, called on-behalf-of the signed-in employee (FR-2). Read-only, and every call is
/// scoped to <c>/me</c>: nothing here takes an argument that selects another employee.
/// </summary>
public interface IMicrosoft365Client
{
    Task<EmployeeProfile> GetMyProfileAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CalendarEntry>> GetMyCalendarAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>The caller's manager, or null when none is set in Entra ID.</summary>
    Task<EmployeeProfile?> GetMyManagerAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<EmployeeProfile>> GetMyDirectReportsAsync(CancellationToken cancellationToken);

    Task<PresenceStatus> GetMyPresenceAsync(CancellationToken cancellationToken);

    /// <summary>Newest messages first, or KQL search results when <paramref name="search"/> is set. Previews only, never bodies.</summary>
    Task<IReadOnlyList<MailMessageSummary>> GetMyMessagesAsync(string? search, int top, CancellationToken cancellationToken);

    /// <summary>Recently used OneDrive/SharePoint files, or a OneDrive search when <paramref name="search"/> is set.</summary>
    Task<IReadOnlyList<DriveItemSummary>> GetMyFilesAsync(string? search, int top, CancellationToken cancellationToken);

    Task<IReadOnlyList<TeamSummary>> GetMyTeamsAsync(CancellationToken cancellationToken);

    /// <summary>Most recently active Teams chats with a preview of the last message.</summary>
    Task<IReadOnlyList<ChatSummary>> GetMyChatsAsync(int top, CancellationToken cancellationToken);

    /// <summary>Open (not completed) Microsoft To Do tasks across the caller's lists.</summary>
    Task<IReadOnlyList<TodoTaskSummary>> GetMyTasksAsync(int top, CancellationToken cancellationToken);
}

public sealed record PresenceStatus(string Availability, string Activity);

public sealed record MailMessageSummary(
    string Subject,
    string? From,
    DateTimeOffset ReceivedAt,
    string? Preview,
    bool IsRead,
    string Importance,
    string? WebLink);

public sealed record DriveItemSummary(
    string Name,
    string? WebUrl,
    DateTimeOffset? LastModifiedAt,
    string? LastModifiedBy,
    long? SizeBytes,
    bool IsFolder);

public sealed record TeamSummary(string DisplayName, string? Description);

public sealed record ChatSummary(
    string? Topic,
    string ChatType,
    string? LastMessageFrom,
    string? LastMessagePreview,
    DateTimeOffset? LastMessageAt,
    string? WebUrl);

public sealed record TodoTaskSummary(
    string Title,
    string ListName,
    string Status,
    string Importance,
    DateTimeOffset? DueAt);

public sealed record EmployeeProfile(
    string DisplayName,
    string? Email,
    string? JobTitle,
    string? Department,
    string? OfficeLocation);

public sealed record CalendarEntry(
    string Subject,
    DateTimeOffset Start,
    DateTimeOffset End,
    string ShowAs,
    string? Location);

/// <summary>Reads policy documents from ComplyKaro for the scheduled sync (FR-3).</summary>
public interface IComplyKaroSource
{
    IAsyncEnumerable<PolicyDocument> FetchPoliciesAsync(CancellationToken cancellationToken);
}
