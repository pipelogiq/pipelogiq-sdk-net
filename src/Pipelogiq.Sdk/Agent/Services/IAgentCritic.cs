using PipelogiqSDK.Agent.Configuration;
using PipelogiqSDK.Agent.Models;

namespace PipelogiqSDK.Agent.Services;

/// <summary>
/// Second-model critic that reviews the primary think handler's proposed action.
/// Implementations call a different LLM (typically OpenAI while the thinker is Claude
/// or vice versa) to get an independent verdict on whether the agent is on track.
/// </summary>
public interface IAgentCritic
{
    /// <summary>
    /// Reviews the think handler's proposal and returns Approve or Reject with feedback.
    /// </summary>
    /// <param name="originalMessage">The original user request the agent is working on.</param>
    /// <param name="history">Conversation turns so far (tool calls, tool results, prior think decisions).</param>
    /// <param name="proposal">The decision the think handler produced that needs reviewing.</param>
    /// <param name="tools">Available tools — lets the critic judge tool choice and parameter plausibility.</param>
    /// <param name="overrides">Per-pipeline overrides providing the critic model, API key, and rubric.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<AgentCriticVerdict> ReviewAsync(
        string originalMessage,
        IReadOnlyList<AgentConversationTurn> history,
        AgentThinkDecision proposal,
        IReadOnlyList<AgentToolDefinition> tools,
        AgentRunOverrides overrides,
        CancellationToken ct = default);
}
