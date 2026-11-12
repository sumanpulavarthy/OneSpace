using System.Diagnostics;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using OneSpace.Api.GraphQL;
using OneSpace.Application.Assistant;

namespace OneSpace.Api.Assistant;

/// <summary>
/// Streaming variant of the <c>ask</c> mutation over Server-Sent Events: narrative deltas and tool activity as
/// they happen, then one <c>completed</c> event whose payload has exactly the GraphQL <c>AssistantAnswer</c> shape
/// (same field names and enum values), so the UI reuses the generated types. Authenticated like /graphql.
/// </summary>
public static class AssistantStreamEndpoint
{
    public const string Path = "/assistant/stream";

    public static void MapAssistantStream(this WebApplication app) =>
        app.MapPost(Path, (AskInput input, AskAssistantUseCase useCase, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
            TypedResults.ServerSentEvents(Stream(input, useCase, loggerFactory.CreateLogger(typeof(AssistantStreamEndpoint)), cancellationToken)));

    private static async IAsyncEnumerable<SseItem<string>> Stream(
        AskInput input, AskAssistantUseCase useCase, ILogger logger, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<AssistantStreamEvent>(new UnboundedChannelOptions { SingleReader = true });
        var sink = new ChannelSink(channel.Writer);
        var work = RunAsync();

        await foreach (var streamEvent in channel.Reader.ReadAllAsync(cancellationToken))
            yield return new SseItem<string>(JsonSerializer.Serialize(streamEvent, streamEvent.GetType(), JsonOptions), streamEvent.Type);
        await work;

        async Task RunAsync()
        {
            try
            {
                var answer = await useCase.ExecuteAsync(input.Question, input.SessionId, sink, cancellationToken);
                channel.Writer.TryWrite(new CompletedEvent(answer));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The client went away; nothing to send.
            }
            catch (Exception ex)
            {
                var (message, code) = ErrorMapping.Map(ex);
                var traceId = Activity.Current?.TraceId.ToString();
                if (code == ErrorMapping.InternalError) logger.LogError(ex, "Streaming answer failed (trace {TraceId})", traceId);
                channel.Writer.TryWrite(new ErrorEvent(message, code, traceId));
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }
    }

    /// <summary>camelCase fields and GraphQL-style enum values (ZOHO_PEOPLE, KEY_VALUE) to match the schema.</summary>
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(new GraphQLEnumNamingPolicy()) },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private sealed class ChannelSink(ChannelWriter<AssistantStreamEvent> writer) : IAssistantEventSink
    {
        public ValueTask PublishAsync(AssistantStreamEvent streamEvent, CancellationToken cancellationToken) =>
            writer.WriteAsync(streamEvent, cancellationToken);
    }
}

/// <summary>HotChocolate's enum value naming: "ZohoPeople" → "ZOHO_PEOPLE", "Microsoft365" → "MICROSOFT365".</summary>
public sealed class GraphQLEnumNamingPolicy : JsonNamingPolicy
{
    public override string ConvertName(string name)
    {
        var result = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]) && char.IsUpper(name[i - 1]))))
                result.Append('_');
            result.Append(char.ToUpperInvariant(name[i]));
        }
        return result.ToString();
    }
}
