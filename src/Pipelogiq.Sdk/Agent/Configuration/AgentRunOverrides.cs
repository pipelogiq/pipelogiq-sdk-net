namespace PipelogiqSDK.Agent.Configuration;

/// <summary>
/// Per-pipeline overrides that attach to a single agent invocation without mutating
/// the global <see cref="AgentOptions"/> singleton. Pass one of these to
/// <c>AgentPipelineBuilderExtensions.CreateAiAgent(...)</c> to turn features like
/// the second-model critic on or off for a specific flow (e.g. only for budget
/// generation, not for every agent call).
/// </summary>
public class AgentRunOverrides
{
    /// <summary>Critic mode for this pipeline. <see cref="AgentCriticMode.Off"/> keeps the single-model loop.</summary>
    public AgentCriticMode CriticMode { get; set; } = AgentCriticMode.Off;

    /// <summary>
    /// LLM provider used for the critic. When not set, falls back to <see cref="AgentLlmProvider.OpenAI"/>
    /// so the critic is a genuine second opinion from a different vendor than the primary think model.
    /// </summary>
    public AgentLlmProvider CriticProvider { get; set; } = AgentLlmProvider.OpenAI;

    /// <summary>
    /// Model identifier for the critic, e.g. "gpt-4.1" or "gpt-5".
    /// If null, the critic implementation uses its own default.
    /// </summary>
    public string? CriticModel { get; set; }

    /// <summary>API key for the critic provider. Required unless the provider does not need one (e.g. local Ollama).</summary>
    public string? CriticApiKey { get; set; }

    /// <summary>Base URL of the critic provider. When null, the implementation uses the provider default.</summary>
    public string? CriticApiBaseUrl { get; set; }

    /// <summary>
    /// Domain-specific rubric injected into the critic's system prompt.
    /// Use this to turn the critic from "general reviewer" into a structured checklist —
    /// e.g. for a budget flow: "(a) does lineItems cover every firm SoW row? (b) do amounts sum to totals?".
    /// </summary>
    public string? CriticRubric { get; set; }

    /// <summary>
    /// Maximum number of critic rejections permitted per single think decision.
    /// Prevents oscillation when critic and thinker disagree forever. After this cap
    /// is reached the critic approves with a warning so the loop makes forward progress.
    /// Default: 2.
    /// </summary>
    public int MaxRejectionsPerStep { get; set; } = 2;
}
