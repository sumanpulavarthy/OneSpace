using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using OneSpace.Application.Abstractions;
using OneSpace.Application.Integrations;
using OneSpace.Assistant.Guard;
using OneSpace.Connectors.Zoho;
using OneSpace.Domain.Users;

namespace OneSpace.UnitTests.Connectors;

public class ZohoMcpClientTests
{
    [Fact]
    public async Task Blocked_tool_never_reaches_the_network()
    {
        var handler = new FakeMcpServer();
        var client = CreateClient(handler, new FakeTokens());

        await Assert.ThrowsAsync<ToolBlockedException>(() => client.CallToolAsync("applyLeave", null, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Allowed_tool_is_called_with_the_employees_token_enforced_scope_and_bound_identity()
    {
        var handler = new FakeMcpServer();
        var client = CreateClient(handler, new FakeTokens());

        var result = await client.CallToolAsync("getLeaveRecords", new JsonObject
        {
            ["from_date"] = "01-Jan-2026",
            ["to_date"] = "31-Dec-2026",
            ["employee_zoho_ids"] = new JsonArray(12345),
            ["employee_department_ids"] = new JsonArray(7),
        }, CancellationToken.None);

        Assert.Equal("ok", result.Text);
        Assert.All(handler.Requests, r => Assert.Equal("Bearer token-1", r.Authorization));
        var call = handler.Requests.Select(r => JsonNode.Parse(r.Body)!).Single(r => r["method"]?.GetValue<string>() == "tools/call");
        Assert.Equal("ZohoPeople_getLeaveRecords", call["params"]!["name"]!.GetValue<string>());

        // Zoho's wrapping: { body: { arguments: {...} } }
        var args = call["params"]!["arguments"]!["body"]!["arguments"]!.AsObject();
        Assert.Equal("MINE", args["data_select"]!.GetValue<string>());
        Assert.Equal("01-Jan-2026", args["from_date"]!.GetValue<string>());
        Assert.False(args.ContainsKey("employee_zoho_ids"));
        Assert.False(args.ContainsKey("employee_department_ids"));
    }

    [Fact]
    public async Task Leave_balance_is_bound_to_the_callers_own_erecno_whatever_the_model_sends()
    {
        var handler = new FakeMcpServer();
        var client = CreateClient(handler, new FakeTokens());

        await client.CallToolAsync("getLeaveBalance", new JsonObject { ["employee"] = 999 }, CancellationToken.None);

        var call = handler.Requests.Select(r => JsonNode.Parse(r.Body)!).Single(r => r["method"]?.GetValue<string>() == "tools/call");
        Assert.Equal(4242L, call["params"]!["arguments"]!["body"]!["arguments"]!["employee"]!.GetValue<long>());
    }

    [Fact]
    public async Task Shift_details_are_bound_to_the_callers_email_and_stripped_of_other_selectors()
    {
        var handler = new FakeMcpServer { ExtraTools = ["ZohoPeople_getEmployeeShiftDetails"] };
        var client = CreateClient(handler, new FakeTokens());

        await client.CallToolAsync("getEmployeeShiftDetails", new JsonObject
        {
            ["emailId"] = "someone.else@example.com",
            ["erecno"] = 999,
            ["departmentId"] = 5,
        }, CancellationToken.None);

        var call = handler.Requests.Select(r => JsonNode.Parse(r.Body)!).Single(r => r["method"]?.GetValue<string>() == "tools/call");
        var args = call["params"]!["arguments"]!["body"]!["arguments"]!.AsObject();
        Assert.Equal("me@example.com", args["emailId"]!.GetValue<string>());
        Assert.False(args.ContainsKey("erecno"));
        Assert.False(args.ContainsKey("departmentId"));
    }

    [Fact]
    public void Model_sees_the_inner_schema_without_selectors_it_cannot_use()
    {
        var client = CreateClient(new FakeMcpServer(), new FakeTokens());
        var wire = JsonNode.Parse("""
            {"type":"object","properties":{"body":{"type":"object","properties":{"arguments":{"type":"object","description":"x",
              "properties":{"employee":{"type":"integer"},"leaveyear":{"type":"integer"}},"required":["employee"]}},"required":["arguments"]}},"required":["body"]}
            """)!.AsObject();

        var schema = client.ModelFacingSchema("getLeaveBalance", wire);

        Assert.Equal(["leaveyear"], schema["properties"]!.AsObject().Select(p => p.Key));
        Assert.Empty(schema["required"]!.AsArray());
    }

    [Theory]
    [InlineData("ZohoPeople_applyLeave")]      // the wire name of a write tool must not slip past the guard
    [InlineData("ZohoPeople_getLeaveBalance")] // the guard only knows bare names
    public async Task Prefixed_names_are_never_authorised(string toolName)
    {
        var handler = new FakeMcpServer();
        var client = CreateClient(handler, new FakeTokens());

        await Assert.ThrowsAsync<ToolBlockedException>(() => client.CallToolAsync(toolName, null, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Allowlisted_tool_missing_on_the_server_fails_instead_of_returning_server_text()
    {
        var handler = new FakeMcpServer();
        var client = CreateClient(handler, new FakeTokens());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.CallToolAsync("listholidays", null, CancellationToken.None));
        Assert.Contains("not enabled", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(handler.Requests, r => r.Body.Contains("tools/call", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unauthorized_refreshes_the_token_once_and_retries()
    {
        var handler = new FakeMcpServer { RejectToken = "token-1" };
        var tokens = new FakeTokens();
        var client = CreateClient(handler, tokens);

        var result = await client.CallToolAsync("getLeaveBalance", null, CancellationToken.None);

        Assert.Equal("ok", result.Text);
        Assert.Equal(1, tokens.ForcedRefreshes);
        Assert.Equal("Bearer token-2", handler.Requests[^1].Authorization);
    }

    [Fact]
    public async Task Second_unauthorized_means_the_employee_must_reconnect()
    {
        var handler = new FakeMcpServer { RejectAll = true };
        var client = CreateClient(handler, new FakeTokens());

        await Assert.ThrowsAsync<ZohoConnectionRequiredException>(() => client.CallToolAsync("getLeaveBalance", null, CancellationToken.None));
    }

    [Fact]
    public async Task Tool_list_is_filtered_to_the_allowlist()
    {
        var client = CreateClient(new FakeMcpServer(), new FakeTokens());

        var tools = await client.ListAllowedToolsAsync(CancellationToken.None);

        Assert.Equal(["getLeaveBalance", "getLeaveRecords"], tools.Select(t => t.Name).Order());
    }

    [Fact]
    public async Task Not_connected_still_offers_allowlisted_tools_so_the_assistant_can_prompt_to_connect()
    {
        var handler = new FakeMcpServer();
        var client = CreateClient(handler, new FakeTokens { Connected = false });

        var tools = await client.ListAllowedToolsAsync(CancellationToken.None);

        Assert.Equal(ToolAllowlistGuard.FromEmbeddedSpec().AllowedTools.Order(), tools.Select(t => t.Name).Order());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Parses_streamable_http_event_stream()
    {
        const string body = "event: message\ndata: {\"jsonrpc\":\"2.0\",\"id\":7,\"result\":{\"ok\":true}}\n\n";
        var message = McpResponseParser.FromEventStream(body, 7);
        Assert.True(McpResponseParser.ResultOrThrow(message, "x")["ok"]!.GetValue<bool>());
    }

    [Fact]
    public void Json_rpc_error_is_surfaced()
    {
        var message = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"error":{"code":-32601,"message":"nope"}}""") as JsonObject;
        var ex = Assert.Throws<InvalidOperationException>(() => McpResponseParser.ResultOrThrow(message, "tools/call"));
        Assert.Contains("nope", ex.Message, StringComparison.Ordinal);
    }

    private static ZohoMcpClient CreateClient(FakeMcpServer handler, FakeTokens tokens)
    {
        var options = Options.Create(new ZohoMcpOptions { Endpoint = "https://mcp.test/mcp/key/message" });
        return new ZohoMcpClient(
            new HttpClient(handler),
            ToolAllowlistGuard.FromEmbeddedSpec(),
            new FixedUser(),
            tokens,
            new FakeEmployees(),
            new ZohoToolListCache(options, TimeProvider.System),
            options);
    }

    private sealed class FakeEmployees : IZohoEmployeeDirectory
    {
        public Task<long?> FindErecnoAsync(string email, CancellationToken cancellationToken) =>
            Task.FromResult<long?>(email == "me@example.com" ? 4242L : null);
        public Task SetErecnoAsync(string email, long erecno, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedUser : IUserContextAccessor
    {
        public UserContext Current { get; } = new("oid-1", "me@example.com", "Me", "me@example.com", "token");
    }

    private sealed class FakeTokens : IZohoAccessTokenProvider
    {
        private int _version = 1;
        public bool Connected { get; init; } = true;
        public int ForcedRefreshes { get; private set; }

        public Task<string> GetAccessTokenAsync(string userId, bool forceRefresh, CancellationToken cancellationToken)
        {
            if (!Connected) throw new ZohoConnectionRequiredException("not connected");
            if (forceRefresh)
            {
                ForcedRefreshes++;
                _version++;
            }
            return Task.FromResult($"token-{_version}");
        }
    }

    /// <summary>Fake MCP server: answers initialize, tools/list (with a write tool mixed in) and tools/call.</summary>
    private sealed class FakeMcpServer : HttpMessageHandler
    {
        public List<(string Body, string? Authorization)> Requests { get; } = [];
        public string? RejectToken { get; init; }
        public bool RejectAll { get; init; }
        public string[] ExtraTools { get; init; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var auth = request.Headers.Authorization?.ToString();
            Requests.Add((body, auth));
            if (RejectAll || (RejectToken is not null && auth == $"Bearer {RejectToken}"))
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);

            var json = JsonNode.Parse(body)!;
            var id = json["id"];
            if (id is null) return new HttpResponseMessage(HttpStatusCode.Accepted);

            var result = json["method"]!.GetValue<string>() switch
            {
                "tools/list" => "{\"tools\":[" + string.Concat(ExtraTools.Select(t => $"{{\"name\":\"{t}\",\"inputSchema\":{{\"type\":\"object\"}}}},")) + """{"name":"ZohoPeople_getLeaveBalance","inputSchema":{"type":"object"}},{"name":"ZohoPeople_getLeaveRecords","inputSchema":{"type":"object"}},{"name":"ZohoPeople_applyLeave","inputSchema":{"type":"object"}},{"name":"getLeaveBalance","inputSchema":{"type":"object"}}]}""",
                "tools/call" => """{"content":[{"type":"text","text":"ok"}],"isError":false}""",
                _ => """{"protocolVersion":"2025-06-18","capabilities":{}}"""
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"jsonrpc":"2.0","id":{{id.ToJsonString()}},"result":{{result}}}""", Encoding.UTF8, "application/json")
            };
        }
    }
}
