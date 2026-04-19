using PipelogiqSDK.Agent.Models;

namespace PipelogiqSDK.Agent.Configuration;

/// <summary>
/// Configuration for the built-in AI agent.
/// </summary>
public class AgentOptions
{
    /// <summary>Built-in LLM provider. Defaults to Anthropic.</summary>
    public AgentLlmProvider LlmProvider { get; set; } = AgentLlmProvider.Anthropic;

    /// <summary>LLM model identifier, e.g. "claude-opus-4-6" or "gemma3".</summary>
    public string LlmModel { get; set; } = "claude-opus-4-6";

    /// <summary>
    /// Maximum output tokens requested from Anthropic.
    /// Default is 4096.
    /// </summary>
    public int AnthropicMaxTokens { get; set; } = 4096;

    /// <summary>API key for the LLM provider when required (e.g. Anthropic API key).</summary>
    public string? LlmApiKey { get; set; }

    /// <summary>LLM API base URL. Defaults depend on the selected provider.</summary>
    public string LlmApiBaseUrl { get; set; } = "https://api.anthropic.com";

    /// <summary>
    /// System prompt prepended to every LLM conversation.
    /// Describe the domain and any rules the agent should follow.
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Whether mutations (POST/PATCH/PUT/DELETE) require explicit user confirmation.
    /// </summary>
    public bool RequireConfirmationForMutations { get; set; } = false;

    /// <summary>Base URL of the target API to call (your domain API).</summary>
    public string? TargetApiBaseUrl { get; set; }

    /// <summary>
    /// Ollama context window sent as <c>options.num_ctx</c>.
    /// Increase this to prevent prompt truncation on long conversations.
    /// </summary>
    public int? OllamaContextWindow { get; set; }

    /// <summary>
    /// Ollama maximum generated tokens sent as <c>options.num_predict</c>.
    /// Leave null to use the model default.
    /// </summary>
    public int? OllamaMaxOutputTokens { get; set; }

    /// <summary>Bearer token for the target API.</summary>
    public string? TargetApiBearerToken { get; set; }

    /// <summary>Static headers added to all target API requests.</summary>
    public Dictionary<string, string>? TargetApiHeaders { get; set; }

    /// <summary>
    /// Named target APIs available to tools.
    /// Use these when the agent must call multiple external APIs.
    /// </summary>
    internal Dictionary<string, AgentTargetApiDefinition> TargetApis { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Enables ReAct (Reason + Act) mode: the LLM reasons step-by-step, calling one tool
    /// at a time and seeing each result before deciding the next action.
    /// When false (default), the LLM plans all tool calls upfront (plan-and-execute).
    /// </summary>
    public bool UseReActMode { get; set; } = false;

    /// <summary>
    /// Maximum number of think steps in ReAct mode before forcing completion.
    /// Prevents infinite loops. Default is 20.
    /// </summary>
    public int MaxThinkSteps { get; set; } = 20;

    /// <summary>
    /// Per-operation model routing. Lets you use a cheaper model for planning and synthesis
    /// while keeping a more capable model for reasoning steps.
    /// </summary>
    public AgentModelRouter? ModelRouter { get; set; }

    /// <summary>
    /// Per-step provider/model routing. Use this when different logical agent steps
    /// should run on different vendors or models.
    /// </summary>
    public AgentLlmStepRouter? StepRouter { get; set; }

    /// <summary>
    /// Worker-owned provider credentials and endpoint overrides for additional providers.
    /// This is especially useful when step routing mixes Anthropic, OpenAI, and Ollama
    /// within the same agent.
    /// </summary>
    public AgentProviderCatalog Providers { get; set; } = new();

    /// <summary>
    /// Token budget and cost controls. Enables prompt caching and optional per-session limits.
    /// </summary>
    public AgentTokenBudget TokenBudget { get; set; } = new();

    /// <summary>
    /// Pipeline context keys whose values are automatically injected into the system prompt.
    /// The LLM can then use these values directly without asking the user.
    ///
    /// Key   = pipeline context key (e.g. "agent:userId")
    /// Value = human-readable label shown to the LLM (e.g. "Employee ID")
    ///
    /// Example:
    /// <code>
    /// agent.ContextInjectionKeys["agent:userId"] = "Employee ID";
    /// </code>
    /// This appends the following to the system prompt at runtime:
    ///   Available context: Employee ID = anna.ivanova
    /// </summary>
    public Dictionary<string, string> ContextInjectionKeys { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Controls progress notifications sent to the user during agent execution.
    /// </summary>
    public AgentProgressOptions Progress { get; set; } = new();

    /// <summary>
    /// Critic configuration owned by the worker/runtime. Per-pipeline runs may override
    /// only <see cref="AgentCriticOptions.Mode"/> through <see cref="AgentRunOverrides"/>.
    /// Secrets and provider settings live here and stay out of pipeline context.
    /// </summary>
    public AgentCriticOptions Critic { get; set; } = new();

    /// <summary>Registered tool definitions (populated via AddTool / AddNativeTool).</summary>
    internal List<AgentToolDefinition> Tools { get; } = new();

    /// <summary>
    /// Native tool handlers keyed by tool name (populated via AddNativeTool).
    /// Stored here so they survive between DI registrations and can be transferred
    /// into the <c>AgentToolRegistry</c> singleton at startup.
    /// </summary>
    internal Dictionary<string, Services.IAgentToolHandler> NativeHandlers { get; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Worker-owned configuration for the second-model critic.
/// </summary>
public class AgentCriticOptions
{
    /// <summary>
    /// Default critic mode for agent runs when no per-pipeline override is supplied.
    /// </summary>
    public AgentCriticMode Mode { get; set; } = AgentCriticMode.Off;

    /// <summary>
    /// LLM provider used for the critic. Defaults to OpenAI.
    /// </summary>
    public AgentLlmProvider Provider { get; set; } = AgentLlmProvider.OpenAI;

    /// <summary>
    /// Model identifier for the critic, e.g. "gpt-4.1" or "claude-sonnet-4-6".
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// API key for the critic provider when required.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Optional base URL override for the critic provider.
    /// </summary>
    public string? ApiBaseUrl { get; set; }

    /// <summary>
    /// Domain-specific rubric injected into the critic's system prompt.
    /// </summary>
    public string? Rubric { get; set; }

    /// <summary>
    /// Maximum number of critic rejections permitted per single think decision.
    /// </summary>
    public int MaxRejectionsPerStep { get; set; } = 2;
}

/// <summary>
/// Routes different LLM operations to specific model IDs.
/// Falls back to <see cref="AgentOptions.LlmModel"/> for any unset operation.
/// </summary>
public class AgentModelRouter
{
    /// <summary>
    /// Model used for the upfront planning step (plan-and-execute mode).
    /// A fast, cheaper model works well here. Example: "claude-haiku-4-5-20251001".
    /// </summary>
    public string? PlanModel { get; set; }

    /// <summary>
    /// Model used for each ReAct think step.
    /// Should be the most capable model in your setup. Example: "claude-opus-4-6".
    /// </summary>
    public string? ThinkModel { get; set; }

    /// <summary>
    /// Model used for the final synthesis/summarisation step.
    /// A cheaper model is usually sufficient. Example: "claude-haiku-4-5-20251001".
    /// </summary>
    public string? SynthesizeModel { get; set; }

    /// <summary>
    /// Model used for the optional critic step.
    /// Falls back to <see cref="AgentCriticOptions.Model"/> when unset.
    /// </summary>
    public string? CriticModel { get; set; }
}

/// <summary>
/// Controls how progress notifications look during agent execution.
/// Progress messages are sent to the user while the agent thinks and calls tools.
/// </summary>
public class AgentProgressOptions
{
    /// <summary>
    /// Whether to send progress notifications at all.
    /// Set to false to hide all intermediate status messages. Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Text shown while the LLM is reasoning. Step number is appended unless
    /// <see cref="ShowStepNumber"/> is false.
    /// Default: "Thinking…" → produces "Thinking… (step 2)"
    /// Ignored when <see cref="ThinkingMessages"/> is set.
    /// </summary>
    public string ThinkingMessage { get; set; } = "Thinking…";

    /// <summary>
    /// Rotating list of thinking messages — shown in sequence across reasoning steps.
    /// When set, overrides <see cref="ThinkingMessage"/>; the step index cycles through the array.
    /// </summary>
    public string[]? ThinkingMessages { get; set; }

    /// <summary>
    /// Whether to append the current step number to <see cref="ThinkingMessage"/>.
    /// Has no effect when <see cref="ThinkingMessages"/> is set. Default: true.
    /// </summary>
    public bool ShowStepNumber { get; set; } = true;

    /// <summary>
    /// Text prefix shown when a tool is about to be called.
    /// The tool name is appended unless <see cref="ShowToolName"/> is false.
    /// Set to null or empty to suppress tool-call notifications entirely.
    /// Ignored for tools that have an entry in <see cref="ToolCallMessages"/>.
    /// Default: null (no tool-call notification sent).
    /// </summary>
    public string? ToolCallMessage { get; set; } = null;

    /// <summary>
    /// Per-tool notification text, keyed by tool name.
    /// When a tool name matches an entry here, that text is shown instead of
    /// <see cref="ToolCallMessage"/>. Useful for human-friendly labels per action.
    /// </summary>
    public Dictionary<string, string>? ToolCallMessages { get; set; }

    /// <summary>
    /// Whether to append the tool name to <see cref="ToolCallMessage"/>.
    /// Only relevant when <see cref="ToolCallMessage"/> is set and no per-tool entry
    /// exists in <see cref="ToolCallMessages"/>. Default: true.
    /// </summary>
    public bool ShowToolName { get; set; } = true;
}

/// <summary>
/// Controls token consumption and enables Anthropic prompt caching.
/// </summary>
public class AgentTokenBudget
{
    /// <summary>
    /// Enable Anthropic prompt caching (cache_control breakpoints on system prompt and tools).
    /// Cached tokens cost ~10% of normal input tokens. Default: true.
    /// </summary>
    public bool EnablePromptCaching { get; set; } = true;

    /// <summary>
    /// Maximum total input tokens (including cache reads) allowed per agent session.
    /// When exceeded the agent returns an error instead of making another LLM call.
    /// Null means unlimited.
    /// </summary>
    public int? MaxInputTokensPerSession { get; set; }

    /// <summary>
    /// Maximum estimated cost in USD allowed per agent session.
    /// Null means unlimited.
    /// </summary>
    public decimal? MaxCostUsdPerSession { get; set; }
}
