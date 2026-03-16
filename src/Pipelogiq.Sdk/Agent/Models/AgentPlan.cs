namespace PipelogiqSDK.Agent.Models;

/// <summary>
/// The LLM's execution plan: a list of tool calls to make.
/// </summary>
public class AgentPlan
{
    /// <summary>Tool calls to execute in order.</summary>
    public List<AgentToolCall> ToolCalls { get; set; } = new();

    /// <summary>
    /// Direct answer without tool calls (when the LLM can answer without external data).
    /// </summary>
    public string? DirectAnswer { get; set; }

    /// <summary>Whether the plan requires any tool calls.</summary>
    public bool HasToolCalls => ToolCalls.Count > 0;
}
