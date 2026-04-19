namespace PipelogiqSDK.Agent.Configuration;

/// <summary>
/// Logical LLM step types used by the built-in agent runtime.
/// </summary>
public enum AgentLlmStep
{
    /// <summary>Initial planning step in plan-and-execute mode.</summary>
    Plan = 0,

    /// <summary>Single ReAct reasoning step.</summary>
    Think = 1,

    /// <summary>Final synthesis / summarisation step.</summary>
    Synthesize = 2,

    /// <summary>Optional second-model critic review step.</summary>
    Critic = 3,
}

/// <summary>
/// Per-step routing configuration for the built-in planner and critic.
/// </summary>
public class AgentLlmStepRouter
{
    /// <summary>Route used for the upfront plan step in plan-and-execute mode.</summary>
    public AgentLlmStepRoute? Plan { get; set; }

    /// <summary>Route used for each ReAct think step.</summary>
    public AgentLlmStepRoute? Think { get; set; }

    /// <summary>Route used for the final synthesis / summarisation step.</summary>
    public AgentLlmStepRoute? Synthesize { get; set; }

    /// <summary>Route used for the optional critic review step.</summary>
    public AgentLlmStepRoute? Critic { get; set; }
}

/// <summary>
/// Provider/model selection for a single logical LLM step.
/// </summary>
public class AgentLlmStepRoute
{
    /// <summary>
    /// Optional provider override for this step.
    /// Leave null to fall back to the step default / global provider.
    /// </summary>
    public AgentLlmProvider? Provider { get; set; }

    /// <summary>
    /// Optional model override for this step.
    /// Leave null to fall back to the step default / global model.
    /// </summary>
    public string? Model { get; set; }
}

/// <summary>
/// Worker-owned provider credentials and base URLs for built-in LLM providers.
/// Use this when different steps are routed to different vendors.
/// </summary>
public class AgentProviderCatalog
{
    /// <summary>Anthropic credentials and endpoint overrides.</summary>
    public AgentProviderConnection Anthropic { get; set; } = new();

    /// <summary>OpenAI credentials and endpoint overrides.</summary>
    public AgentProviderConnection OpenAI { get; set; } = new();

    /// <summary>Ollama endpoint overrides.</summary>
    public AgentProviderConnection Ollama { get; set; } = new();
}

/// <summary>
/// Connection details for a single provider.
/// Secrets stay worker-owned and should not be placed in per-run overrides.
/// </summary>
public class AgentProviderConnection
{
    /// <summary>Provider API key when required.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Optional API base URL override.</summary>
    public string? ApiBaseUrl { get; set; }
}
