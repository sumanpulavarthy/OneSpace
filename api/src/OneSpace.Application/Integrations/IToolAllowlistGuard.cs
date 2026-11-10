using System.Text.Json.Nodes;

namespace OneSpace.Application.Integrations;

/// <summary>
/// FR-5: decides, in code, whether an MCP tool may be called. Deny-by-default.
/// Every Zoho MCP <c>tools/call</c> MUST pass through this guard, whatever the LLM requested.
/// </summary>
public interface IToolAllowlistGuard
{
    IReadOnlyCollection<string> AllowedTools { get; }

    /// <summary>
    /// Returns the decision and, when allowed, the arguments to send (with self-scope arguments enforced).
    /// </summary>
    ToolAuthorization Authorize(string toolName, JsonObject? arguments);
}

public sealed record ToolAuthorization(bool Allowed, string? Reason, JsonObject Arguments)
{
    public static ToolAuthorization Deny(string reason) => new(false, reason, []);
}

/// <summary>Raised when a tool call is rejected by the allowlist guard. Logged as a blocked attempt.</summary>
public sealed class ToolBlockedException(string toolName, string reason)
    : InvalidOperationException($"Tool '{toolName}' is not permitted: {reason}")
{
    public string ToolName { get; } = toolName;
    public string Reason { get; } = reason;
}
