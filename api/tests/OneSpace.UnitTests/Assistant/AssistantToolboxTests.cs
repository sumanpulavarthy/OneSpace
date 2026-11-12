using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using OneSpace.Application.Abstractions;
using OneSpace.Application.Integrations;
using OneSpace.Assistant.Engines;
using OneSpace.Assistant.Guard;
using OneSpace.Assistant.Presentation;
using OneSpace.Assistant.Tools;
using OneSpace.Domain.Assistant;
using OneSpace.Domain.Policies;

namespace OneSpace.UnitTests.Assistant;

public class AssistantToolboxTests
{
    private readonly FakeZoho _zoho = new();

    [Theory]
    [InlineData("zoho_applyLeave")]
    [InlineData("applyLeave")]
    [InlineData("zoho_fetchLeaveTypeDetails")]
    public async Task Model_requested_write_or_admin_tool_is_blocked_and_recorded(string tool)
    {
        var execution = await Toolbox().ExecuteAsync(tool, null, CancellationToken.None);

        Assert.True(execution.IsError);
        Assert.Equal(ToolInvocationOutcome.Blocked, execution.Invocation.Outcome);
        Assert.Equal(0, _zoho.Calls);
    }

    [Fact]
    public async Task Allowlisted_zoho_tool_runs_and_is_attributed()
    {
        var execution = await Toolbox().ExecuteAsync("zoho_getLeaveBalance", null, CancellationToken.None);

        Assert.False(execution.IsError);
        Assert.Equal(1, _zoho.Calls);
        Assert.Equal("From Zoho People", Assert.Single(execution.Sources).Label);
    }

    [Fact]
    public async Task Zoho_mcp_disabled_is_reported_as_an_admin_setting_not_an_outage()
    {
        _zoho.NextResult = new McpToolResult("""{"status":"failure","data":{"error":{"code":"MCP_DISABLED","message":"MCP is disabled"}}}""", true);

        var execution = await Toolbox().ExecuteAsync("zoho_getLeaveBalance", null, CancellationToken.None);

        Assert.Equal(ToolInvocationOutcome.Failed, execution.Invocation.Outcome);
        Assert.Contains("enable Zia", execution.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Calendar_range_over_31_days_is_rejected()
    {
        var input = new JsonObject { ["start"] = "2026-01-01T00:00:00Z", ["end"] = "2026-03-01T00:00:00Z" };
        var execution = await Toolbox().ExecuteAsync(AssistantToolbox.GetMyCalendar, input, CancellationToken.None);

        Assert.Equal(ToolInvocationOutcome.Failed, execution.Invocation.Outcome);
    }

    [Theory]
    [InlineData("What's my leave balance?", "zoho_getLeaveBalance")]
    [InlineData("Show me the holiday list", "zoho_listholidays")]
    [InlineData("Where's the WFH policy?", "search_policies")]
    [InlineData("Am I free tomorrow? check my calendar", "get_my_calendar")]
    [InlineData("What are my shift details", "zoho_getEmployeeShiftDetails")]
    public void Rule_based_router_picks_the_expected_tool(string question, string tool) =>
        Assert.Equal(tool, RuleBasedAssistantEngine.Route(question, DateTimeOffset.UtcNow)?.Tool);

    [Theory]
    [InlineData("Please apply leave for me tomorrow", true)]
    [InlineData("Cancel my leave on Friday", true)]
    [InlineData("Raise a ticket with IT", true)]
    [InlineData("How do I apply for leave?", false)]
    [InlineData("What's my leave balance?", false)]
    public void Rule_based_engine_declines_action_requests(string question, bool isAction) =>
        Assert.Equal(isAction, RuleBasedAssistantEngine.IsActionRequest(question));

    [Theory]
    [InlineData("Where's the WFH policy?", "the WFH")]
    [InlineData("travel policy", "travel")]
    [InlineData("policies", "policies")]
    public void Generic_policy_words_are_removed_from_search(string query, string expected) =>
        Assert.Equal(expected, AssistantToolbox.SearchTerms(query));

    [Fact]
    public void Intent_is_derived_from_tool_calls()
    {
        var calls = new List<ToolInvocation>
        {
            new("zoho_getLeaveBalance", Domain.SourceSystem.ZohoPeople, ToolInvocationOutcome.Succeeded, 1),
            new(AssistantToolbox.SearchPolicies, Domain.SourceSystem.ComplyKaro, ToolInvocationOutcome.Succeeded, 1)
        };
        Assert.Equal("leave_lookup+policy_lookup", IntentClassifier.For(calls));
    }

    private AssistantToolbox Toolbox() => new(_zoho, new NoM365(), new NoPolicies(), ToolAllowlistGuard.FromEmbeddedSpec(),
        new ToolResultPresenter(PresentationSpec.LoadEmbedded()), NullLogger<AssistantToolbox>.Instance);

    private sealed class FakeZoho : IZohoPeopleClient
    {
        public int Calls { get; private set; }
        public McpToolResult NextResult { get; set; } = new("{\"balance\":12}", false);

        public Task<IReadOnlyList<McpToolDescriptor>> ListAllowedToolsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<McpToolDescriptor>>([]);

        public Task<McpToolResult> CallToolAsync(string toolName, JsonObject? arguments, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(NextResult);
        }
    }

    private sealed class NoM365 : IMicrosoft365Client
    {
        public Task<EmployeeProfile> GetMyProfileAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("not configured");

        public Task<IReadOnlyList<CalendarEntry>> GetMyCalendarAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CalendarEntry>>([]);

        public Task<EmployeeProfile?> GetMyManagerAsync(CancellationToken cancellationToken) => Task.FromResult<EmployeeProfile?>(null);
        public Task<IReadOnlyList<EmployeeProfile>> GetMyDirectReportsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<EmployeeProfile>>([]);
        public Task<PresenceStatus> GetMyPresenceAsync(CancellationToken cancellationToken) => Task.FromResult(new PresenceStatus("Available", "Available"));
        public Task<IReadOnlyList<MailMessageSummary>> GetMyMessagesAsync(string? search, int top, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MailMessageSummary>>([]);
        public Task<IReadOnlyList<DriveItemSummary>> GetMyFilesAsync(string? search, int top, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DriveItemSummary>>([]);
        public Task<IReadOnlyList<TeamSummary>> GetMyTeamsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TeamSummary>>([]);
        public Task<IReadOnlyList<ChatSummary>> GetMyChatsAsync(int top, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ChatSummary>>([]);
        public Task<IReadOnlyList<TodoTaskSummary>> GetMyTasksAsync(int top, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TodoTaskSummary>>([]);
    }

    private sealed class NoPolicies : IPolicyDocumentRepository, IPolicySearch
    {
        public Task UpsertManyAsync(IReadOnlyCollection<PolicyDocument> documents, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<long> DeleteMissingAsync(IReadOnlyCollection<string> keepIds, CancellationToken cancellationToken) => Task.FromResult(0L);
        public Task<IReadOnlyList<PolicySearchResult>> SearchAsync(string text, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PolicySearchResult>>([]);
        public Task<PolicyDocument?> GetAsync(string id, CancellationToken cancellationToken) => Task.FromResult<PolicyDocument?>(null);
        public Task<IReadOnlyList<PolicyDocument>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PolicyDocument>>([]);
    }
}
