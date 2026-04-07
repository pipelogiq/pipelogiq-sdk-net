using PipelogiqSDK.Agent.Models;

namespace PipelogiqSDK.Testing;

/// <summary>
/// Fluent builder for scripting agent think-step sequences in tests.
/// </summary>
public sealed class AgentTestScenario
{
    private readonly MockLlmPlanner _planner;

    internal AgentTestScenario(MockLlmPlanner planner)
    {
        _planner = planner;
    }

    /// <summary>
    /// Adds a think step that calls the specified tool with the given parameters.
    /// </summary>
    public AgentTestScenario ThenCallTool(string toolName, object? parameters = null)
    {
        var paramDict = parameters == null
            ? new Dictionary<string, object?>()
            : System.Text.Json.JsonSerializer
                .Deserialize<Dictionary<string, object?>>(
                    System.Text.Json.JsonSerializer.Serialize(parameters)) ?? new();

        _planner.EnqueueThinkStep(new MockThinkStep(new AgentThinkDecision
        {
            Action = AgentThinkAction.CallTool,
            ToolCall = new AgentToolCall
            {
                Tool = toolName,
                Params = paramDict,
                ResultKey = toolName,
            },
        }));
        return this;
    }

    /// <summary>
    /// Adds a think step that signals the agent is done with the given final answer.
    /// </summary>
    public AgentTestScenario ThenFinishWith(string finalAnswer)
    {
        _planner.EnqueueThinkStep(new MockThinkStep(new AgentThinkDecision
        {
            Action = AgentThinkAction.Done,
            FinalAnswer = finalAnswer,
        }));
        return this;
    }

    /// <summary>
    /// Adds a think step that requests confirmation before executing mutations.
    /// </summary>
    public AgentTestScenario ThenRequestConfirmation(params AgentToolCall[] mutations)
    {
        _planner.EnqueueThinkStep(new MockThinkStep(new AgentThinkDecision
        {
            Action = AgentThinkAction.NeedConfirmation,
            MutationsToConfirm = [.. mutations],
        }));
        return this;
    }

    /// <summary>
    /// Overrides the synthesized final response text.
    /// </summary>
    public AgentTestScenario WithSynthesizedResponse(string response)
    {
        _planner.SetSynthesizeResponse(response);
        return this;
    }
}
