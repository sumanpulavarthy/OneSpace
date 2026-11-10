namespace OneSpace.Connectors.Zoho;

public sealed class ZohoMcpOptions
{
    public const string SectionName = "Integrations:ZohoMcp";

    /// <summary>
    /// MCP server URL, <c>https://&lt;org&gt;.zohomcp.in/mcp/&lt;server-key&gt;/message</c>. It contains a key, so it is a secret:
    /// Key Vault (<c>Integrations--ZohoMcp--Endpoint</c>) or user-secrets, never appsettings.
    /// </summary>
    public string? Endpoint { get; set; }

    public string ProtocolVersion { get; set; } = "2025-06-18";

    /// <summary>
    /// The Zoho MCP server namespaces tools by app (verified 2026-11-10: <c>ZohoPeople_getLeaveBalance</c>).
    /// The allowlist and guard use the bare names from the PRD; only the wire name carries the prefix.
    /// </summary>
    public string ToolNamePrefix { get; set; } = "ZohoPeople_";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(20);

    public TimeSpan ToolListCacheDuration { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// FR-2: arguments that select other employees, departments or locations (names from the live Zoho schemas,
    /// 2026-11-10). They are removed before every call, so Zoho falls back to the token's own employee; they are
    /// also hidden from the model's tool schemas.
    /// </summary>
    public List<string> StrippedArguments { get; set; } =
    [
        "employee", "erecno", "employee_zoho_ids", "employee_department_ids", "employee_location_ids", "employee_status",
        "department", "departmentId", "locationId", "location", "shiftGroupId", "incReporteeOnly",
        "employeeId", "empId", "userId", "user", "email", "repType"
    ];

    /// <summary>Per-tool argument that must name the caller (bound to their email), for tools with no self-scope flag.</summary>
    public Dictionary<string, string> SelfEmailArguments { get; set; } = new() { ["getEmployeeShiftDetails"] = "emailId" };

    public ZohoOAuthOptions OAuth { get; set; } = new();

    public bool IsConfigured => Uri.TryCreate(Endpoint, UriKind.Absolute, out _);
}

/// <summary>Per-employee MCP OAuth (authorization code + PKCE). Verified against the server's metadata on 2026-11-10.</summary>
public sealed class ZohoOAuthOptions
{
    /// <summary>
    /// Phase 0 needs leave + attendance (shifts). Zoho offers only ".all" (read+write) scopes; the FR-5 allowlist
    /// guard is what keeps OneSpace read-only.
    /// </summary>
    public List<string> Scopes { get; set; } = ["zohopeople.leave.all", "zohopeople.attendance.all", "ZohoMCP.tool.execute"];

    /// <summary>The API's callback URL registered with Zoho. Empty: derived from the request (fine locally, set it in Azure).</summary>
    public string? RedirectUri { get; set; }

    /// <summary>UI page the browser returns to after connecting (fixed in config, never taken from the request).</summary>
    public string PostConnectRedirectUri { get; set; } = "/assistant";

    public string ClientName { get; set; } = "OneSpace";

    /// <summary>Optional pre-registered client. When empty, OneSpace registers itself (dynamic client registration).</summary>
    public string? ClientId { get; set; }

    /// <summary>Key Vault reference only.</summary>
    public string? ClientSecret { get; set; }

    public TimeSpan StateLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Refresh this long before the access token expires.</summary>
    public TimeSpan RefreshSkew { get; set; } = TimeSpan.FromSeconds(60);
}
