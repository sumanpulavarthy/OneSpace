using System.Text.Json.Nodes;

namespace OneSpace.Assistant.Tools;

/// <summary>The tools available to an engine. Implemented by <see cref="AssistantToolbox"/>; replaceable in tests and evals.</summary>
public interface IAssistantToolbox
{
    Task<IReadOnlyList<AssistantToolDefinition>> GetDefinitionsAsync(CancellationToken cancellationToken);

    /// <param name="resultId">Id the result's blocks get (e.g. <c>t1</c>), which the LLM uses to show them.</param>
    Task<ToolExecution> ExecuteAsync(string toolName, JsonObject? input, string resultId, CancellationToken cancellationToken);
}
