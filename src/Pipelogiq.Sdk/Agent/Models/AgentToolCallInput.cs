namespace PipelogiqSDK.Agent.Models;

/// <summary>
/// Input payload for a single AgentToolHandler stage.
/// </summary>
public class AgentToolCallInput
{
    /// <summary>Name of the tool to execute.</summary>
    public string ToolName { get; set; } = null!;

    /// <summary>Parameters for the API call (may contain {{ref:key}} references).</summary>
    public Dictionary<string, object?> Params { get; set; } = new();

    /// <summary>Context key under which to store the result.</summary>
    public string ResultKey { get; set; } = null!;
}
