using PipelogiqSDK.Agent.Models;

namespace PipelogiqSDK.Agent.Services;

/// <summary>
/// Plans tool calls based on a user message and available tools.
/// Register your own implementation or use the built-in ClaudeLlmPlanner.
/// </summary>
public interface ILlmPlanner
{
    /// <summary>
    /// Analyzes the user message and returns a plan of tool calls to execute.
    /// </summary>
    Task<AgentPlan> PlanAsync(
        string userMessage,
        IReadOnlyList<AgentToolDefinition> tools,
        string? systemPrompt = null,
        CancellationToken ct = default);

    /// <summary>
    /// Synthesizes a final user-facing response from the original message and tool results.
    /// </summary>
    Task<AgentTextResult> SynthesizeAsync(
        string originalMessage,
        IReadOnlyList<AgentToolResult> results,
        CancellationToken ct = default);

    /// <summary>
    /// ReAct think step: given the conversation history so far, decides the next single action.
    /// Called by AgentThinkHandler on every iteration of the ReAct loop.
    /// </summary>
    /// <param name="originalMessage">The original user message.</param>
    /// <param name="history">All turns executed so far (tool calls, results, confirmations).</param>
    /// <param name="tools">Available tool definitions.</param>
    /// <param name="requireConfirmationForMutations">Whether to ask for confirmation before mutations.</param>
    /// <param name="systemPrompt">Optional domain-specific system prompt.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<AgentThinkDecision> ThinkAsync(
        string originalMessage,
        IReadOnlyList<AgentConversationTurn> history,
        IReadOnlyList<AgentToolDefinition> tools,
        bool requireConfirmationForMutations,
        string? systemPrompt = null,
        IReadOnlyList<AgentAttachment>? attachments = null,
        CancellationToken ct = default);
}
