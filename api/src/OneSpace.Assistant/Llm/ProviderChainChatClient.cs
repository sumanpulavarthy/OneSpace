using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OneSpace.Assistant.Telemetry;

namespace OneSpace.Assistant.Llm;

/// <summary>What happened on one engine call: who answered, whether failover was needed, tokens used.</summary>
public sealed class LlmCallInfo
{
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public bool FailedOver { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
}

/// <summary>
/// One <see cref="IChatClient"/> over the provider chain: picks the model for the requested tier, adapts options
/// to the model's capabilities, and fails over to the next provider when one errors (with a circuit breaker per
/// provider). A streamed response fails over only before its first update; after that the error propagates.
/// </summary>
public sealed partial class ProviderChainChatClient(LlmProviderChain chain, ILogger<ProviderChainChatClient> logger) : IChatClient
{
    public const string TierKey = "onespace.tier";
    public const string CallInfoKey = "onespace.call";

    public LlmProviderChain Chain => chain;

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var (tier, info) = Read(options);
        var history = messages as IList<ChatMessage> ?? messages.ToList();
        Exception? last = null;
        var attempts = 0;

        foreach (var provider in Candidates())
        {
            var model = provider.For(tier);
            try
            {
                var response = await model.Client.GetResponseAsync(history, Adapt(options, provider, model), cancellationToken);
                provider.Breaker.RecordSuccess();
                Record(info, provider, model, attempts > 0, response.Usage);
                return response;
            }
            catch (Exception ex) when (IsFailover(ex, cancellationToken))
            {
                last = ex;
                attempts++;
                Failed(provider, ex);
            }
        }
        throw new LlmUnavailableException(last);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (tier, info) = Read(options);
        var history = messages as IList<ChatMessage> ?? messages.ToList();
        Exception? last = null;
        var attempts = 0;

        foreach (var provider in Candidates())
        {
            var model = provider.For(tier);
            IAsyncEnumerator<ChatResponseUpdate>? stream = null;
            bool hasFirst;
            try
            {
                stream = model.Client.GetStreamingResponseAsync(history, Adapt(options, provider, model), cancellationToken).GetAsyncEnumerator(cancellationToken);
                hasFirst = await stream.MoveNextAsync();
            }
            catch (Exception ex) when (IsFailover(ex, cancellationToken))
            {
                if (stream is not null) await stream.DisposeAsync();
                last = ex;
                attempts++;
                Failed(provider, ex);
                continue;
            }

            await using (stream)
            {
                Record(info, provider, model, attempts > 0, null);
                if (!hasFirst)
                {
                    provider.Breaker.RecordSuccess();
                    yield break;
                }

                yield return Track(info, stream.Current);
                while (true)
                {
                    try
                    {
                        if (!await stream.MoveNextAsync()) break;
                    }
                    catch (Exception ex) when (IsFailover(ex, cancellationToken))
                    {
                        provider.Breaker.RecordFailure();
                        throw;
                    }
                    yield return Track(info, stream.Current);
                }
                provider.Breaker.RecordSuccess();
                yield break;
            }
        }
        throw new LlmUnavailableException(last);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
        // The chain (a singleton) owns the provider clients.
    }

    private IEnumerable<LlmProvider> Candidates()
    {
        var available = chain.Providers.Where(p => p.Breaker.IsAvailable).ToList();
        // Every breaker open: try them all anyway rather than fail without trying.
        return available.Count > 0 ? available : chain.Providers;
    }

    private static (ModelTier Tier, LlmCallInfo? Info) Read(ChatOptions? options)
    {
        var tier = options?.AdditionalProperties?.TryGetValue(TierKey, out var t) == true && t is ModelTier requested ? requested : ModelTier.Primary;
        var info = options?.AdditionalProperties?.TryGetValue(CallInfoKey, out var i) == true ? i as LlmCallInfo : null;
        return (tier, info);
    }

    /// <summary>Per-model options: our bookkeeping keys removed, unsupported settings dropped.</summary>
    internal static ChatOptions Adapt(ChatOptions? options, LlmProvider provider, LlmModel model)
    {
        var adapted = options?.Clone() ?? new ChatOptions();
        if (adapted.AdditionalProperties is { } extra)
        {
            var kept = new AdditionalPropertiesDictionary();
            foreach (var (key, value) in extra)
                if (key is not (TierKey or CallInfoKey)) kept[key] = value;
            adapted.AdditionalProperties = kept.Count > 0 ? kept : null;
        }
        adapted.ModelId = model.ModelId;
        if (!model.SupportsReasoning(provider.Options.Type)) adapted.Reasoning = null;
        if (model.Capabilities is { ParallelToolCalls: false }) adapted.AllowMultipleToolCalls = false;
        return adapted;
    }

    private static void Record(LlmCallInfo? info, LlmProvider provider, LlmModel model, bool failedOver, UsageDetails? usage)
    {
        if (info is null) return;
        info.Provider = provider.Name;
        info.Model = model.ModelId;
        info.FailedOver |= failedOver;
        AddUsage(info, usage);
    }

    private static ChatResponseUpdate Track(LlmCallInfo? info, ChatResponseUpdate update)
    {
        if (info is not null)
            foreach (var usage in update.Contents.OfType<UsageContent>())
                AddUsage(info, usage.Details);
        return update;
    }

    private static void AddUsage(LlmCallInfo info, UsageDetails? usage)
    {
        info.InputTokens += usage?.InputTokenCount ?? 0;
        info.OutputTokens += usage?.OutputTokenCount ?? 0;
    }

    private void Failed(LlmProvider provider, Exception ex)
    {
        provider.Breaker.RecordFailure();
        AssistantTelemetry.Failovers.Add(1, new KeyValuePair<string, object?>("provider", provider.Name));
        LogProviderFailed(logger, ex, provider.Name);
    }

    private static bool IsFailover(Exception ex, CancellationToken cancellationToken) =>
        !(ex is OperationCanceledException && cancellationToken.IsCancellationRequested);

    [LoggerMessage(Level = LogLevel.Warning, Message = "LLM provider {Provider} failed; trying the next provider")]
    private static partial void LogProviderFailed(ILogger logger, Exception ex, string provider);
}

/// <summary>Every provider in the chain failed.</summary>
public sealed class LlmUnavailableException(Exception? inner)
    : Exception("No LLM provider could answer.", inner);
