namespace PipelogiqSDK.Agent.Models;

/// <summary>
/// Text response returned by an LLM together with optional token usage.
/// </summary>
public class AgentTextResult
{
    /// <summary>User-facing text returned by the model.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Token usage for this call. Null when the provider does not report usage.</summary>
    public AgentLlmUsage? TokenUsage { get; set; }
}
