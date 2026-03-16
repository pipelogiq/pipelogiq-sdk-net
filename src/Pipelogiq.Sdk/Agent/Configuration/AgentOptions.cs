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

    /// <summary>Registered tool definitions (populated via AddTool).</summary>
    internal List<AgentToolDefinition> Tools { get; } = new();
}
