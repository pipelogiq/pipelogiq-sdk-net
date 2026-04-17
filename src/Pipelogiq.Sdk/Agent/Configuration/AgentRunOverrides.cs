namespace PipelogiqSDK.Agent.Configuration;

/// <summary>
/// Per-pipeline overrides that attach to a single agent invocation without mutating
/// the global <see cref="AgentOptions"/> singleton. This object is intentionally
/// minimal because it is serialized into pipeline input and may be visible in
/// stage context. Keep secrets and provider-specific critic settings on the worker
/// in <see cref="AgentOptions.Critic"/>.
/// </summary>
public class AgentRunOverrides
{
    /// <summary>Critic mode for this pipeline. <see cref="AgentCriticMode.Off"/> keeps the single-model loop.</summary>
    public AgentCriticMode CriticMode { get; set; } = AgentCriticMode.Off;
}
