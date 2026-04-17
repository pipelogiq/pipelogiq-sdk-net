using PipelogiqSDK.Agent.Configuration;

namespace PipelogiqSDK.Agent.Services;

/// <summary>
/// Resolves the correct <see cref="IAgentCritic"/> implementation for the provider
/// specified in <see cref="AgentCriticOptions.Provider"/>.
/// Kept as a separate service so the critic handler does not need to know about
/// every built-in provider implementation directly.
/// </summary>
public interface IAgentCriticResolver
{
    /// <summary>Returns the critic implementation bound to the given provider.</summary>
    IAgentCritic Resolve(AgentLlmProvider provider);
}

/// <summary>
/// Default resolver. Uses <see cref="OpenAiCritic"/> for <see cref="AgentLlmProvider.OpenAI"/>
/// and <see cref="ClaudeCritic"/> for <see cref="AgentLlmProvider.Anthropic"/>.
/// Ollama currently has no dedicated critic implementation — falls back to Claude critic
/// semantics only if the caller points <see cref="AgentCriticOptions.ApiBaseUrl"/> at an
/// Anthropic-compatible endpoint.
/// </summary>
public class DefaultAgentCriticResolver(OpenAiCritic openAiCritic, ClaudeCritic claudeCritic) : IAgentCriticResolver
{
    /// <inheritdoc />
    public IAgentCritic Resolve(AgentLlmProvider provider) => provider switch
    {
        AgentLlmProvider.OpenAI => openAiCritic,
        AgentLlmProvider.Anthropic => claudeCritic,
        _ => throw new NotSupportedException(
            $"Provider {provider} does not have a built-in critic implementation. " +
            "Register a custom IAgentCritic or pick OpenAI/Anthropic for the critic provider."),
    };
}
