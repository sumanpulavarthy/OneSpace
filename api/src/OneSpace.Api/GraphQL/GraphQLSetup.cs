using System.Diagnostics;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using OneSpace.Application.Integrations;

namespace OneSpace.Api.GraphQL;

public static class GraphQLSetup
{
    public static IRequestExecutorBuilder AddOneSpaceGraphQL(this IServiceCollection services) =>
        services
            .AddGraphQLServer()
            .AddQueryType<Query>()
            .AddMutationType<Mutation>()
            .AddTypeExtension<ZohoConnectionQueries>()
            .AddTypeExtension<ZohoConnectionMutations>()
            // The schema has its own service container; expose the app's logger factory to the error filter.
            .AddApplicationService<ILoggerFactory>()
            .AddErrorFilter<OneSpaceErrorFilter>()
            .ModifyRequestOptions(o => o.ExecutionTimeout = TimeSpan.FromSeconds(90));
}

/// <summary>Maps exceptions to stable error codes without leaking internals to the client.</summary>
public sealed partial class OneSpaceErrorFilter(ILoggerFactory loggerFactory) : IErrorFilter
{
    private readonly ILogger logger = loggerFactory.CreateLogger<OneSpaceErrorFilter>();

    public IError OnError(IError error)
    {
        var traceId = Activity.Current?.TraceId.ToString();
        return error.Exception switch
        {
            null => error,
            ArgumentException ex => Build(error, UserMessage(ex), "BAD_USER_INPUT", traceId),
            UnauthorizedAccessException => Build(error, "You are not signed in.", "UNAUTHENTICATED", traceId),
            ToolBlockedException => Build(error, "That action is not available in OneSpace.", "FORBIDDEN_TOOL", traceId),
            ZohoConnectionRequiredException => Build(error, "Connect Zoho People to see this.", "ZOHO_CONNECTION_REQUIRED", traceId),
            var ex => LogAndBuild(error, ex, traceId)
        };
    }

    private IError LogAndBuild(IError error, Exception ex, string? traceId)
    {
        LogUnhandled(logger, ex, traceId);
        return Build(error, "Something went wrong. Try again in a moment.", "INTERNAL_ERROR", traceId);
    }

    /// <summary>ArgumentException appends " (Parameter 'x')"; employees should only see the sentence.</summary>
    private static string UserMessage(ArgumentException ex) =>
        ex.ParamName is null ? ex.Message : ex.Message.Replace($" (Parameter '{ex.ParamName}')", string.Empty, StringComparison.Ordinal);

    private static IError Build(IError error, string message, string code, string? traceId) =>
        ErrorBuilder.FromError(error)
            .SetMessage(message)
            .SetCode(code)
            .SetExtension("traceId", traceId)
            .SetException(null)
            .Build();

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled GraphQL error (trace {TraceId})")]
    private static partial void LogUnhandled(ILogger logger, Exception ex, string? traceId);
}
