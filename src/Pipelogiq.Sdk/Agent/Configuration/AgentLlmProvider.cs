namespace PipelogiqSDK.Agent.Configuration;

/// <summary>
/// Built-in LLM provider types supported by the SDK.
/// </summary>
public enum AgentLlmProvider
{
    /// <summary>Anthropic Messages API.</summary>
    Anthropic = 0,

    /// <summary>Ollama local chat API.</summary>
    Ollama = 1,

    /// <summary>OpenAI chat completions API.</summary>
    OpenAI = 2,
}
