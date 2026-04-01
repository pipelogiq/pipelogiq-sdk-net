namespace PipelogiqSDK.Agent.Models;

/// <summary>
/// Token usage reported by the LLM for a single call.
/// </summary>
public class AgentLlmUsage
{
    /// <summary>Total input tokens consumed.</summary>
    public int InputTokens { get; set; }

    /// <summary>Total output tokens generated.</summary>
    public int OutputTokens { get; set; }

    /// <summary>Input tokens written to the prompt cache (Anthropic only).</summary>
    public int CacheCreationTokens { get; set; }

    /// <summary>Input tokens read from the prompt cache instead of being re-processed (Anthropic only).</summary>
    public int CacheReadTokens { get; set; }

    /// <summary>Estimated cost in USD based on the model pricing table.</summary>
    public decimal EstimatedCostUsd { get; set; }
}
