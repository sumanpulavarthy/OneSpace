using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace OneSpace.UnitTests.Llm;

/// <summary>An IChatClient that replies from a script and records what it was sent.</summary>
internal sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<Func<IReadOnlyList<ChatMessage>, ChatResponse>> _script = new();

    public List<(List<ChatMessage> Messages, ChatOptions? Options)> Calls { get; } = [];

    public ScriptedChatClient Reply(string text) =>
        Then(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, text)) { FinishReason = ChatFinishReason.Stop });

    public ScriptedChatClient CallTool(string name, Dictionary<string, object?>? arguments = null) =>
        Then(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent($"call_{name}", name, arguments)]))
        {
            FinishReason = ChatFinishReason.ToolCalls
        });

    public ScriptedChatClient Fail(Exception exception) => Then(_ => throw exception);

    public ScriptedChatClient Then(Func<IReadOnlyList<ChatMessage>, ChatResponse> step)
    {
        _script.Enqueue(step);
        return this;
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        Calls.Add((list, options));
        return Task.FromResult(_script.Dequeue()(list));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var update in response.ToChatResponseUpdates()) yield return update;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
