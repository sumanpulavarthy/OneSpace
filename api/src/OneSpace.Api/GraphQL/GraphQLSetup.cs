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
        if (error.Exception is null) return error;

        var (message, code) = ErrorMapping.Map(error.Exception);
        if (code == ErrorMapping.InternalError) LogUnhandled(logger, error.Exception, traceId);
        return Build(error, message, code, traceId);
    }

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
