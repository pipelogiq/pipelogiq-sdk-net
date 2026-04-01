namespace PipelogiqSDK.Agent.Models;

/// <summary>
/// The action the LLM decided to take in a ReAct think step.
/// </summary>
public enum AgentThinkAction
{
    /// <summary>Call one specific tool.</summary>
    CallTool,

    /// <summary>Request user confirmation before executing mutations.</summary>
    NeedConfirmation,

    /// <summary>All work is done; provide final answer to user.</summary>
    Done,
}

/// <summary>
/// Result of a single ReAct think step returned by <see cref="Services.ILlmPlanner.ThinkAsync"/>.
/// </summary>
public class AgentThinkDecision
{
    /// <summary>What the LLM decided to do next.</summary>
    public AgentThinkAction Action { get; set; }

    /// <summary>The tool call to execute (populated when Action is CallTool).</summary>
    public AgentToolCall? ToolCall { get; set; }

    /// <summary>Mutations requiring confirmation (populated when Action is NeedConfirmation).</summary>
    public List<AgentToolCall>? MutationsToConfirm { get; set; }

    /// <summary>LLM's chain-of-thought reasoning for this step.</summary>
    public string? Reasoning { get; set; }

    /// <summary>Final user-facing answer (populated when Action is Done).</summary>
    public string? FinalAnswer { get; set; }

    /// <summary>
    /// The raw JSON string the LLM responded with.
    /// Stored in conversation history so subsequent think steps can replay it accurately.
    /// </summary>
    public string? RawDecisionJson { get; set; }

    /// <summary>Token usage for this think step (null if provider does not report usage).</summary>
    public AgentLlmUsage? TokenUsage { get; set; }
}
