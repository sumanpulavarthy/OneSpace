using OneSpace.Application.Integrations;
using OneSpace.Assistant.Llm;

namespace OneSpace.Api.GraphQL;

/// <summary>Exception → employee-safe message and stable code. Shared by GraphQL and the streaming endpoint.</summary>
public static class ErrorMapping
{
    public const string InternalError = "INTERNAL_ERROR";

    public static (string Message, string Code) Map(Exception exception) => exception switch
    {
        ArgumentException ex => (UserMessage(ex), "BAD_USER_INPUT"),
        UnauthorizedAccessException => ("You are not signed in.", "UNAUTHENTICATED"),
        ToolBlockedException => ("That action is not available in OneSpace.", "FORBIDDEN_TOOL"),
        ZohoConnectionRequiredException => ("Connect Zoho People to see this.", "ZOHO_CONNECTION_REQUIRED"),
        LlmUnavailableException => ("The assistant is temporarily unavailable. Try again in a moment.", "ASSISTANT_UNAVAILABLE"),
        _ => ("Something went wrong. Try again in a moment.", InternalError)
    };

    /// <summary>ArgumentException appends " (Parameter 'x')"; employees should only see the sentence.</summary>
    private static string UserMessage(ArgumentException ex) =>
        ex.ParamName is null ? ex.Message : ex.Message.Replace($" (Parameter '{ex.ParamName}')", string.Empty, StringComparison.Ordinal);
}
