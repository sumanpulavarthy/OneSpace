using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OneSpace.Application.Assistant;
using OneSpace.Assistant;
using OneSpace.Assistant.Engines;
using OneSpace.Assistant.Llm;
using OneSpace.Assistant.Presentation;
using OneSpace.Assistant.Tools;
using OneSpace.Domain;
using OneSpace.Domain.Assistant;
using OneSpace.Domain.Users;
using OneSpace.UnitTests.Llm;

namespace OneSpace.UnitTests.Engines;

public class LlmAssistantEngineTests
{
    private static readonly UserContext User = new("oid", "asha@contoso.com", "Asha", "asha@contoso.com", null);
    private readonly ToolResultPresenter _presenter = new(PresentationSpec.LoadEmbedded());
    private readonly FakeToolbox _toolbox;

    public LlmAssistantEngineTests() => _toolbox = new FakeToolbox(_presenter);

    [Fact]
    public async Task Shown_result_becomes_a_server_built_block_and_the_marker_is_removed()
    {
        var llm = new ScriptedChatClient()
            .CallTool("zoho_getLeaveBalance")
            .Reply("You have 4.5 days of casual leave left (Zoho People).\n{{show:t1}}");

        var result = await Engine(llm).AnswerAsync(User, AssistantRequest.Single("What's my leave balance?"), null, CancellationToken.None);

        Assert.Equal("You have 4.5 days of casual leave left (Zoho People).", result.Answer);
        var block = Assert.Single(result.Blocks);
        Assert.Equal("t1", block.Id);
        Assert.Equal(["Casual Leave", "4.5"], block.Rows[0]);
        Assert.True(result.Grounding is { Checked: true, Grounded: true });
        Assert.Equal("leave_lookup", result.Intent);
        Assert.Equal("test", result.Usage!.Provider);
    }

    [Fact]
    public async Task Invented_figure_fails_grounding()
    {
        var llm = new ScriptedChatClient().CallTool("zoho_getLeaveBalance").Reply("You have 9 days left.");

        var result = await Engine(llm).AnswerAsync(User, AssistantRequest.Single("leave balance?"), null, CancellationToken.None);

        Assert.False(result.Grounding.Grounded);
        Assert.Equal(["9"], result.Grounding.UnverifiedValues);
        Assert.Single(result.Blocks); // no marker: the data the answer used is still shown
    }

    [Fact]
    public async Task Ungrounded_fast_answer_is_escalated_to_the_primary_model()
    {
        var fast = new ScriptedChatClient().CallTool("zoho_getLeaveBalance").Reply("You have 9 days left.");
        var primary = new ScriptedChatClient().CallTool("zoho_getLeaveBalance").Reply("You have 4.5 days left. {{show:t1}}");
        var events = new RecordingSink();

        var result = await Engine(primary, fast).AnswerAsync(User, AssistantRequest.Single("What's my leave balance?"), events, CancellationToken.None);

        Assert.True(result.Grounding.Grounded);
        Assert.True(result.Usage!.Escalated);
        Assert.Contains(events.Events, e => e is RestartEvent);
        Assert.Equal(2, result.ToolCalls.Count);
    }

    [Fact]
    public async Task Tools_above_the_chain_data_class_are_not_offered_or_executed()
    {
        var llm = new ScriptedChatClient().CallTool("get_my_mail").Reply("I can't see your mail.");

        var result = await Engine(llm, maxDataClass: DataClass.Internal)
            .AnswerAsync(User, AssistantRequest.Single("Any mail from HR?"), null, CancellationToken.None);

        var offered = llm.Calls[0].Options!.Tools!.Select(t => t.Name).ToList();
        Assert.DoesNotContain("get_my_mail", offered);
        Assert.DoesNotContain("zoho_getLeaveBalance", offered); // Personal
        Assert.Equal(ToolInvocationOutcome.Blocked, Assert.Single(result.ToolCalls).Outcome);
        Assert.Equal(0, _toolbox.Executions);
    }

    [Fact]
    public async Task Tool_results_reach_the_model_masked_and_enveloped()
    {
        var llm = new ScriptedChatClient().CallTool("get_my_mail").Reply("You have one unread message. {{show:t1}}");

        await Engine(llm).AnswerAsync(User, AssistantRequest.Single("Any new mail?"), null, CancellationToken.None);

        var toolResult = llm.Calls[1].Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single();
        var text = toolResult.Result!.ToString()!;
        Assert.StartsWith("<tool_result id=\"t1\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Salary", text, StringComparison.Ordinal);
        Assert.Contains("[withheld]", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Streaming_publishes_text_without_markers_and_tool_activity()
    {
        var llm = new ScriptedChatClient().CallTool("zoho_getLeaveBalance").Reply("Balance: 4.5 days. {{show:t1}}");
        var events = new RecordingSink();

        await Engine(llm).AnswerAsync(User, AssistantRequest.Single("leave balance"), events, CancellationToken.None);

        var text = string.Concat(events.Events.OfType<TextDeltaEvent>().Select(e => e.Text));
        Assert.Equal("Balance: 4.5 days. ", text);
        Assert.Equal(["started", "succeeded"], events.Events.OfType<ToolActivityEvent>().Select(e => e.Status));
    }

    [Fact]
    public async Task Follow_up_includes_history_and_previous_tools()
    {
        var llm = new ScriptedChatClient().Reply("Same as before.");
        var history = new List<ConversationTurn>
        {
            new() { Question = "What's on my calendar tomorrow?", Answer = "Two meetings.", ToolNames = [AssistantToolbox.GetMyCalendar] }
        };

        await Engine(llm).AnswerAsync(User, new AssistantRequest("and my leave balance?", history), null, CancellationToken.None);

        var (messages, options) = llm.Calls[0];
        Assert.Contains(messages, m => m.Role == ChatRole.Assistant && m.Text == "Two meetings.");
        Assert.Contains(options!.Tools!, t => t.Name == AssistantToolbox.GetMyCalendar);
        Assert.Contains(options.Tools!, t => t.Name == "zoho_getLeaveBalance");
        Assert.DoesNotContain(options.Tools!, t => t.Name == AssistantToolbox.GetMyFiles);
    }

    [Fact]
    public void History_is_trimmed_to_the_newest_turns_within_budget()
    {
        var turns = Enumerable.Range(1, 5).Select(i => new ConversationTurn { Question = new string('q', 40), Answer = $"a{i}" + new string('a', 58) }).ToList();

        var kept = LlmAssistantEngine.TrimHistory(turns, 250);

        Assert.Equal(["a4", "a5"], kept.Select(t => t.Answer[..2]));
    }

    [Fact]
    public async Task Refusal_is_reported_without_an_answer_from_the_model()
    {
        var llm = new ScriptedChatClient().Then(_ =>
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "")) { FinishReason = ChatFinishReason.ContentFilter });

        var result = await Engine(llm).AnswerAsync(User, AssistantRequest.Single("something bad"), null, CancellationToken.None);

        Assert.Equal("refused", result.Intent);
    }

    private LlmAssistantEngine Engine(ScriptedChatClient primary, ScriptedChatClient? fast = null, DataClass maxDataClass = DataClass.Personal)
    {
        var chain = new LlmProviderChain([ProviderChainTests.Provider("test", primary, fast, maxDataClass)]);
        var client = new ProviderChainChatClient(chain, NullLogger<ProviderChainChatClient>.Instance);
        return new LlmAssistantEngine(client, _toolbox, _presenter, Options.Create(new AssistantOptions()), TimeProvider.System,
            NullLogger<LlmAssistantEngine>.Instance);
    }

    private sealed class RecordingSink : IAssistantEventSink
    {
        public List<AssistantStreamEvent> Events { get; } = [];

        public ValueTask PublishAsync(AssistantStreamEvent streamEvent, CancellationToken cancellationToken)
        {
            Events.Add(streamEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeToolbox(ToolResultPresenter presenter) : IAssistantToolbox
    {
        private static readonly Dictionary<string, string> Data = new()
        {
            ["zoho_getLeaveBalance"] = """{"response":{"status":0,"result":[{"leavetype":"Casual Leave","available":4.5}]}}""",
            [AssistantToolbox.GetMyMail] = """[{"Subject":"Pay letter","From":"HR","ReceivedAt":"2026-11-10T09:30:00Z","Preview":"Salary revision","IsRead":false}]""",
            [AssistantToolbox.GetMyCalendar] = "[]",
            [AssistantToolbox.GetMyFiles] = "[]",
            [AssistantToolbox.SearchPolicies] = "[]"
        };

        public int Executions { get; private set; }

        public Task<IReadOnlyList<AssistantToolDefinition>> GetDefinitionsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AssistantToolDefinition>>(
                Data.Keys.Select(k => new AssistantToolDefinition(k, k, new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() })).ToList());

        public Task<ToolExecution> ExecuteAsync(string toolName, JsonObject? input, string resultId, CancellationToken cancellationToken)
        {
            Executions++;
            var source = new SourceAttribution(AssistantToolbox.SystemFor(toolName), "From test");
            var presentation = presenter.Present(toolName, resultId, null, Data[toolName], source);
            return Task.FromResult(new ToolExecution(Data[toolName], false, [source],
                new ToolInvocation(toolName, AssistantToolbox.SystemFor(toolName), ToolInvocationOutcome.Succeeded, 1), presentation));
        }
    }
}
