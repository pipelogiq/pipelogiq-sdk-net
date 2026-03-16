namespace PipelogiqSDK.Agent.Models;

/// <summary>
/// One tool call planned by the LLM.
/// </summary>
public class AgentToolCall
{
    /// <summary>Name of the tool to call (matches AgentToolDefinition.Name).</summary>
    public string Tool { get; set; } = null!;

    /// <summary>
    /// Parameters for the call. Values may contain {{ref:key.path}} for referencing
    /// previous tool results stored in context.
    /// </summary>
    public Dictionary<string, object?> Params { get; set; } = new();

    /// <summary>
    /// Key under which the result is stored in context as "agent:result:{ResultKey}".
    /// Defaults to tool name if not set.
    /// </summary>
    public string? ResultKey { get; set; }

    /// <summary>Gets the effective result key.</summary>
    public string EffectiveResultKey => ResultKey ?? Tool;
}
