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

/// <summary>Microsoft Graph, called on-behalf-of the signed-in employee (FR-2). Read-only.</summary>
public interface IMicrosoft365Client
{
    Task<EmployeeProfile> GetMyProfileAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CalendarEntry>> GetMyCalendarAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

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
