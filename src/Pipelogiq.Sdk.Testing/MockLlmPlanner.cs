using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Agent.Services;

namespace PipelogiqSDK.Testing;

/// <summary>
/// A scripted LLM planner for unit testing agent flows without a real LLM or network call.
/// Configure expected responses via <see cref="AgentTestScenario"/>.
/// </summary>
public sealed class MockLlmPlanner : ILlmPlanner
{
    private readonly Queue<MockThinkStep> _thinkSteps = new();
    private string _synthesizeResponse = "Mock response.";
    private AgentPlan? _plan;

    internal void EnqueueThinkStep(MockThinkStep step) => _thinkSteps.Enqueue(step);
    internal void SetSynthesizeResponse(string response) => _synthesizeResponse = response;
    internal void SetPlan(AgentPlan plan) => _plan = plan;

    /// <inheritdoc />
    public Task<AgentPlan> PlanAsync(
        string userMessage,
        IReadOnlyList<AgentToolDefinition> tools,
        string? systemPrompt = null,
        CancellationToken ct = default)
    {
        var plan = _plan ?? new AgentPlan { DirectAnswer = "Mock direct answer." };
        return Task.FromResult(plan);
    }

    /// <inheritdoc />
    public Task<AgentThinkDecision> ThinkAsync(
        string originalMessage,
        IReadOnlyList<AgentConversationTurn> history,
        IReadOnlyList<AgentToolDefinition> tools,
        bool requireConfirmationForMutations,
        string? systemPrompt = null,
        IReadOnlyList<AgentAttachment>? attachments = null,
        CancellationToken ct = default)
    {
        if (_thinkSteps.TryDequeue(out var step))
            return Task.FromResult(step.Decision);

        // No more scripted steps → signal Done
        return Task.FromResult(new AgentThinkDecision
        {
            Action = AgentThinkAction.Done,
            FinalAnswer = "Mock agent completed all scripted steps.",
        });
    }

    /// <inheritdoc />
    public Task<AgentTextResult> SynthesizeAsync(
        string originalMessage,
        IReadOnlyList<AgentToolResult> results,
        CancellationToken ct = default)
        => Task.FromResult(new AgentTextResult { Text = _synthesizeResponse });
}

/// <summary>A single scripted think step.</summary>
internal sealed record MockThinkStep(AgentThinkDecision Decision);
