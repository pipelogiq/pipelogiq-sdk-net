namespace PipelogiqSDK.Agent.Models;

/// <summary>
/// Structured stage output shown in Pipelogiq UI for agent stages.
/// </summary>
public class AgentStageOutput
{
    /// <summary>Human-readable stage summary.</summary>
    public string Raw { get; set; } = string.Empty;

    /// <summary>Logical LLM operation that produced the usage (think, critic, plan, synthesize).</summary>
    public string? LlmOperation { get; set; }

    /// <summary>Usage for the current LLM call, when available.</summary>
    public AgentLlmUsage? LlmUsage { get; set; }

    /// <summary>Aggregate usage for the whole pipeline/session so far.</summary>
    public AgentLlmUsageSummary? SessionUsage { get; set; }
}
