using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using OneSpace.Application.Abstractions;
using OneSpace.Application.Integrations;

namespace OneSpace.Connectors.Zoho;

/// <summary>
/// Thin MCP client (JSON-RPC 2.0 over Streamable HTTP) for the Zoho People MCP server.
/// Every <c>tools/call</c> is authorised by <see cref="IToolAllowlistGuard"/> first; a denied call never leaves the process.
/// Requests carry the employee's own Zoho OAuth token, so Zoho applies their permissions (FR-2).
/// </summary>
public sealed class ZohoMcpClient(
    HttpClient http,
    IToolAllowlistGuard guard,
    IUserContextAccessor userContext,
    IZohoAccessTokenProvider tokens,
    ZohoToolListCache toolCache,
    IOptions<ZohoMcpOptions> options) : IZohoPeopleClient
{
    private const string SessionHeader = "Mcp-Session-Id";
    private readonly ZohoMcpOptions _options = options.Value;
    private string? _sessionId;
    private string? _accessToken;
    private int _nextId;

    public async Task<IReadOnlyList<McpToolDescriptor>> ListAllowedToolsAsync(CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured) return [];

        try
        {
            var all = await ListServerToolsAsync(cancellationToken);

            // Map wire names (ZohoPeople_x) to bare names, then filter to the allowlist:
            // new server tools never become callable implicitly (FR-5).
            return all
                .Where(t => t.Name.StartsWith(_options.ToolNamePrefix, StringComparison.Ordinal))
                .Select(t => t with { Name = t.Name[_options.ToolNamePrefix.Length..] })
                .Where(t => guard.AllowedTools.Contains(t.Name))
                .Select(t => t with { InputSchema = ModelFacingSchema(t.Name, t.InputSchema) })
                .ToList();
        }
        catch (ZohoConnectionRequiredException)
        {
            // The catalogue needs a token. Offer the allowlisted names anyway so the assistant can try one,
            // get "connection required" back, and prompt the employee to connect.
            return guard.AllowedTools
                .Select(name => new McpToolDescriptor(name, $"Zoho People tool {name} (read-only)",
                    new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }))
                .ToList();
        }
    }

    public async Task<McpToolResult> CallToolAsync(string toolName, JsonObject? arguments, CancellationToken cancellationToken)
    {
        var decision = guard.Authorize(toolName, BindToCurrentUser(toolName, arguments));
        if (!decision.Allowed) throw new ToolBlockedException(toolName, decision.Reason ?? "denied");
        if (!_options.IsConfigured) throw new InvalidOperationException("Zoho MCP integration is not configured.");

        // The MCP server only serves tools enabled in its Zoho configuration; calling any other returns a
        // "Tool not configured" text result that would otherwise read like an answer.
        // The guard decided on the bare name; only the authorised name is prefixed for the wire.
        var wireName = _options.ToolNamePrefix + toolName;
        var catalog = await ListServerToolsAsync(cancellationToken);
        if (catalog.All(t => t.Name != wireName))
            throw new InvalidOperationException($"Zoho MCP tool '{wireName}' is not enabled on the MCP server.");

        // Zoho's tools take their parameters wrapped as { body: { arguments: {...} } } (from the live schemas).
        var result = await SendAsync("tools/call", new JsonObject
        {
            ["name"] = wireName,
            ["arguments"] = new JsonObject { ["body"] = new JsonObject { ["arguments"] = decision.Arguments } }
        }, cancellationToken);

        var isError = result["isError"]?.GetValue<bool>() ?? false;
        var text = result["content"] is JsonArray content
            ? string.Join('\n', content.Where(c => c?["type"]?.GetValue<string>() == "text").Select(c => c!["text"]?.GetValue<string>()))
            : result.ToJsonString();
        return new McpToolResult(text, isError);
    }

    /// <summary>The server's <c>initialize</c> instructions (per user: Zoho includes the current employee's details).</summary>
    public string? ServerInstructions { get; private set; }

    /// <summary>Initialises this (per-request) client if needed and returns the per-user server instructions.</summary>
    public async Task<string?> GetServerInstructionsAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        return ServerInstructions;
    }

    /// <summary>Every tool the MCP server exposes (unfiltered, cached). For diagnostics and catalogue checks only.</summary>
    public Task<IReadOnlyList<McpToolDescriptor>> ListServerToolsAsync(CancellationToken cancellationToken) =>
        toolCache.GetOrAddAsync(async () =>
        {
            await EnsureInitializedAsync(cancellationToken);
            var result = await SendAsync("tools/list", new JsonObject(), cancellationToken);
            return ParseTools(result);
        });

    /// <summary>
    /// FR-2: removes arguments that could select other employees (Zoho then uses the token's own employee) and
    /// binds per-tool self arguments to the caller's email. Runs before the guard, whatever the model supplied.
    /// </summary>
    internal JsonObject BindToCurrentUser(string toolName, JsonObject? arguments)
    {
        var bound = arguments is null ? new JsonObject() : (JsonObject)arguments.DeepClone();
        foreach (var name in _options.StrippedArguments) bound.Remove(name);

        if (_options.SelfEmailArguments.TryGetValue(toolName, out var emailArgument))
        {
            var user = userContext.Current;
            bound[emailArgument] = user.Email ?? user.UserPrincipalName;
        }
        return bound;
    }

    /// <summary>
    /// What the model sees: the inner <c>body.arguments</c> schema, without arguments OneSpace strips or binds itself,
    /// so it never tries to pick an employee, department or location.
    /// </summary>
    internal JsonObject ModelFacingSchema(string toolName, JsonObject wireSchema)
    {
        var inner = wireSchema["properties"]?["body"]?["properties"]?["arguments"] as JsonObject ?? wireSchema;
        var schema = (JsonObject)inner.DeepClone();
        schema.Remove("description");
        schema["type"] = "object";

        var hidden = new HashSet<string>(_options.StrippedArguments, StringComparer.Ordinal);
        if (_options.SelfEmailArguments.TryGetValue(toolName, out var emailArgument)) hidden.Add(emailArgument);

        if (schema["properties"] is JsonObject props)
            foreach (var name in hidden) props.Remove(name);

        if (schema["required"] is JsonArray required)
        {
            var kept = required.Select(r => r!.GetValue<string>()).Where(r => !hidden.Contains(r)).Select(r => (JsonNode)r).ToArray();
            schema["required"] = new JsonArray(kept);
        }
        return schema;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_sessionId is not null) return;

        var init = await SendAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = _options.ProtocolVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "onespace-api", ["version"] = "0.1.0" }
        }, cancellationToken);
        ServerInstructions = init["instructions"]?.GetValue<string>();
        _sessionId ??= string.Empty; // Stateless servers return no session id; still only initialise once.
        using var _ = await PostAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" }, cancellationToken);
    }

    private async Task<JsonObject> SendAsync(string method, JsonObject parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        using var response = await PostAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters
        }, cancellationToken);
        if (response.Headers.TryGetValues(SessionHeader, out var values)) _sessionId = values.FirstOrDefault();

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = response.Content.Headers.ContentType?.MediaType == "text/event-stream"
            ? McpResponseParser.FromEventStream(body, id)
            : JsonNode.Parse(body) as JsonObject;

        return McpResponseParser.ResultOrThrow(message, method);
    }

    /// <summary>Sends with the employee's token. On 401 the token is refreshed once; a second 401 means reconnect.</summary>
    private async Task<HttpResponseMessage> PostAsync(JsonObject payload, CancellationToken cancellationToken)
    {
        var userId = userContext.Current.ObjectId;
        _accessToken ??= await tokens.GetAccessTokenAsync(userId, forceRefresh: false, cancellationToken);

        for (var attempt = 0; ; attempt++)
        {
            using var request = CreateRequest(payload, _accessToken);
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                response.EnsureSuccessStatusCode();
                return response;
            }

            response.Dispose();
            if (attempt > 0) throw new ZohoConnectionRequiredException("Zoho rejected the token");
            _accessToken = await tokens.GetAccessTokenAsync(userId, forceRefresh: true, cancellationToken);
        }
    }

    private HttpRequestMessage CreateRequest(JsonObject payload, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", _options.ProtocolVersion);
        if (!string.IsNullOrEmpty(_sessionId)) request.Headers.TryAddWithoutValidation(SessionHeader, _sessionId);
        return request;
    }

    private static List<McpToolDescriptor> ParseTools(JsonObject result) =>
        result["tools"] is JsonArray tools
            ? tools.OfType<JsonObject>()
                .Select(t => new McpToolDescriptor(
                    t["name"]!.GetValue<string>(),
                    t["description"]?.GetValue<string>(),
                    t["inputSchema"] as JsonObject is { } schema ? (JsonObject)schema.DeepClone() : new JsonObject { ["type"] = "object" }))
                .ToList()
            : [];
}

/// <summary>Parses MCP JSON-RPC responses, including Streamable HTTP's SSE framing.</summary>
public static class McpResponseParser
{
    public static JsonObject? FromEventStream(string body, int id)
    {
        foreach (var evt in body.Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n\n"))
        {
            var data = string.Join("\n", evt.Split('\n')
                .Where(l => l.StartsWith("data:", StringComparison.Ordinal))
                .Select(l => l[5..].TrimStart()));
            if (data.Length == 0) continue;

            if (JsonNode.Parse(data) is JsonObject message && message["id"]?.GetValue<int>() == id) return message;
        }
        return null;
    }

    public static JsonObject ResultOrThrow(JsonObject? message, string method)
    {
        if (message is null) throw new InvalidOperationException($"MCP '{method}' returned no response.");
        if (message["error"] is JsonObject error)
            throw new InvalidOperationException($"MCP '{method}' failed: {error["message"]?.GetValue<string>() ?? "unknown error"}");
        return message["result"] as JsonObject ?? new JsonObject();
    }
}

/// <summary>Process-wide cache of <c>tools/list</c>; the tool catalogue is server-level, not per user.</summary>
public sealed class ZohoToolListCache(IOptions<ZohoMcpOptions> options, TimeProvider clock)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyList<McpToolDescriptor>? _tools;
    private DateTimeOffset _expires;

    public async Task<IReadOnlyList<McpToolDescriptor>> GetOrAddAsync(Func<Task<List<McpToolDescriptor>>> factory)
    {
        if (_tools is not null && clock.GetUtcNow() < _expires) return _tools;
        await _lock.WaitAsync();
        try
        {
            if (_tools is null || clock.GetUtcNow() >= _expires)
            {
                _tools = await factory();
                _expires = clock.GetUtcNow() + options.Value.ToolListCacheDuration;
            }
            return _tools;
        }
        finally
        {
            _lock.Release();
        }
    }
}
