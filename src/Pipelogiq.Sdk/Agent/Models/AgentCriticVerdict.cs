namespace PipelogiqSDK.Agent.Models;

/// <summary>
/// The decision the critic returned after reviewing a proposal from the think handler.
/// </summary>
public enum AgentCriticDecision
{
    /// <summary>Proposal passes the rubric — let the tool call or final answer proceed.</summary>
    Approve = 0,

    /// <summary>Proposal fails the rubric — send it back to the think handler with feedback.</summary>
    Reject = 1,
}

/// <summary>
/// Structured response from the second-model critic.
/// </summary>
public class AgentCriticVerdict
{
    /// <summary>Approve or Reject.</summary>
    public AgentCriticDecision Decision { get; set; }

    /// <summary>
    /// Short free-text feedback explaining the verdict.
    /// On Reject this is injected into the conversation history so the next think step can react.
    /// </summary>
    public string? Feedback { get; set; }

    /// <summary>
    /// Specific concerns listed one-per-item — better for structured display and for the thinker
    /// to address point-by-point than a single blob of prose.
    /// </summary>
    public List<string>? Concerns { get; set; }

    /// <summary>Critic's self-reported confidence in the verdict, in [0,1].</summary>
    public decimal? Confidence { get; set; }

    /// <summary>Raw JSON the critic returned. Stored for audit/debugging.</summary>
    public string? RawVerdictJson { get; set; }

    /// <summary>Token usage for this critic call. Null if provider did not report it.</summary>
    public AgentLlmUsage? TokenUsage { get; set; }
}
