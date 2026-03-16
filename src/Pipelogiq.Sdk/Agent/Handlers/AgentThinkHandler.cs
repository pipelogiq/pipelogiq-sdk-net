using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Agent.Configuration;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Agent.Services;
using PipelogiqSDK.Api;
using PipelogiqSDK.Contracts;
using PipelogiqSDK.StageHelper;
using System.Text.Json;

namespace PipelogiqSDK.Agent.Handlers;

/// <summary>
/// Built-in handler for the ReAct (Reason + Act) loop.
/// On each execution, reads the conversation history, calls the LLM for the next
/// single decision, and dynamically appends the appropriate stage(s) to the pipeline.
///
/// Flow per iteration:
///   think → [tool] → think → [tool] → think → [confirmation?] → think → ... → think → done → responder
/// </summary>
public class AgentThinkHandler(
    ILlmPlanner llmPlanner,
    IAgentToolRegistry toolRegistry,
    AgentOptions agentOptions,
    PipelogiqApiClient apiClient) : IStageHandler
{
    /// <inheritdoc />
    public async Task<IStageResult> ExecuteAsync(IStageContext? context = null)
    {
        var pipelineId = context?.PipelineId
            ?? throw new InvalidOperationException("PipelineId is required for AgentThinkHandler.");

        // Guard: prevent infinite loops
        var stepCount = context.TryGetValue<int>(AgentConstants.ThinkStepCount);
        stepCount++;
        SetContextValue(context, AgentConstants.ThinkStepCount, stepCount);

        if (stepCount > agentOptions.MaxThinkSteps)
        {
            SetContextValue(context, "agent:directAnswer",
                $"Agent reached the maximum number of reasoning steps ({agentOptions.MaxThinkSteps}).");
            await AppendStagesAsync(pipelineId, [BuildResponderStage()]);
            return StageResult.Success($"Max think steps ({agentOptions.MaxThinkSteps}) reached — responder appended.");
        }

        var history = GetOrCreateHistory(context);

        // Handle post-confirmation resume: approved/rejected decision is in context
        var approved = context.TryGetValue<bool?>(AgentConstants.ApprovalDecision);
        if (approved.HasValue)
        {
            history.Add(new AgentConversationTurn
            {
                Type = "confirmation_result",
                Approved = approved.Value,
                Content = approved.Value ? "User confirmed the action." : "User rejected the action.",
            });

            if (!approved.Value)
            {
                // Rejected — store rejection message and go straight to responder
                SetContextValue(context, "agent:directAnswer",
                    context.TryGetValue<string>(AgentConstants.RejectionReason) ?? "The action was cancelled by the user.");
                SetContextValue(context, AgentConstants.ConversationHistory, history);
                await AppendStagesAsync(pipelineId, [BuildResponderStage()]);
                return StageResult.Success("Confirmation rejected — responder appended.");
            }

            // Clear approval flag so next think step starts fresh
            context.Payload!.Remove(AgentConstants.ApprovalDecision);
        }

        // Ask LLM for next action
        var originalMessage = context.TryGetValue<string>(AgentConstants.OriginalMessage) ?? string.Empty;
        var tools = toolRegistry.GetAll();

        var decision = await llmPlanner.ThinkAsync(
            originalMessage,
            history,
            tools,
            agentOptions.RequireConfirmationForMutations,
            agentOptions.SystemPrompt);

        if (decision.Action == AgentThinkAction.CallTool &&
            decision.ToolCall != null &&
            agentOptions.RequireConfirmationForMutations &&
            IsMutatingTool(decision.ToolCall.Tool) &&
            !TryConsumeApprovedMutation(context, decision.ToolCall))
        {
            var forcedDecision = new AgentThinkDecision
            {
                Action = AgentThinkAction.NeedConfirmation,
                MutationsToConfirm = [decision.ToolCall],
                Reasoning = "Mutation requires explicit user confirmation by runtime policy.",
                RawDecisionJson = decision.RawDecisionJson ?? JsonSerializer.Serialize(new
                {
                    action = "need_confirmation",
                    mutations = new[]
                    {
                        new
                        {
                            tool = decision.ToolCall.Tool,
                            @params = decision.ToolCall.Params,
                            resultKey = decision.ToolCall.ResultKey
                        }
                    },
                    reasoning = "Mutation requires explicit user confirmation by runtime policy."
                }),
            };

            SetContextValue(context, AgentConstants.ConversationHistory, history);
            return await HandleNeedConfirmationAsync(pipelineId, forcedDecision, history, context);
        }

        SetContextValue(context, AgentConstants.ConversationHistory, history);

        return decision.Action switch
        {
            AgentThinkAction.CallTool when decision.ToolCall != null
                => await HandleCallToolAsync(pipelineId, decision, history, context),

            AgentThinkAction.NeedConfirmation when decision.MutationsToConfirm?.Count > 0
                => await HandleNeedConfirmationAsync(pipelineId, decision, history, context),

            _ => await HandleDoneAsync(pipelineId, decision, context),
        };
    }

    // ── Decision handlers ────────────────────────────────────────────────────

    private async Task<IStageResult> HandleCallToolAsync(
        int pipelineId,
        AgentThinkDecision decision,
        List<AgentConversationTurn> history,
        IStageContext context)
    {
        var call = decision.ToolCall!;

        // Record this decision in history as an assistant turn (raw JSON)
        history.Add(new AgentConversationTurn
        {
            Type = "tool_call",
            ToolName = call.Tool,
            Content = decision.RawDecisionJson,
        });
        SetContextValue(context, AgentConstants.ConversationHistory, history);

        await AppendStagesAsync(pipelineId, [BuildToolStage(call), BuildThinkStage()]);

        return StageResult.Success($"Think step {GetStep(context)}: calling tool '{call.Tool}'.");
    }

    private async Task<IStageResult> HandleNeedConfirmationAsync(
        int pipelineId,
        AgentThinkDecision decision,
        List<AgentConversationTurn> history,
        IStageContext context)
    {
        history.Add(new AgentConversationTurn
        {
            Type = "confirmation_requested",
            Content = decision.RawDecisionJson,
        });
        SetContextValue(context, AgentConstants.ConversationHistory, history);

        // After confirmation, the think stage resumes the loop
        await AppendStagesAsync(pipelineId, [
            BuildConfirmationStage(decision.MutationsToConfirm!),
            BuildThinkStage(),
        ]);

        return StageResult.Success($"Think step {GetStep(context)}: confirmation requested for {decision.MutationsToConfirm!.Count} mutation(s).");
    }

    private async Task<IStageResult> HandleDoneAsync(
        int pipelineId,
        AgentThinkDecision decision,
        IStageContext context)
    {
        if (!string.IsNullOrWhiteSpace(decision.FinalAnswer))
            SetContextValue(context, "agent:directAnswer", decision.FinalAnswer);

        await AppendStagesAsync(pipelineId, [BuildResponderStage()]);

        return StageResult.Success($"Think step {GetStep(context)}: reasoning complete — responder appended.");
    }

    // ── Stage builders ───────────────────────────────────────────────────────

    private static StageInfo BuildToolStage(AgentToolCall call) => new()
    {
        StageName = $"agent:tool:{call.Tool}",
        StageHandlerName = AgentConstants.ToolHandlerName,
        Input = new AgentToolCallInput
        {
            ToolName = call.Tool,
            Params = call.Params,
            ResultKey = call.EffectiveResultKey,
        },
    };

    private static StageInfo BuildThinkStage() => new()
    {
        StageName = "agent:think",
        StageHandlerName = AgentConstants.ThinkHandlerName,
    };

    private static StageInfo BuildConfirmationStage(List<AgentToolCall> mutations) => new()
    {
        StageName = "agent:confirmation",
        StageHandlerName = AgentConstants.ConfirmationHandlerName,
        Input = new AgentConfirmationInput { PendingMutations = mutations },
    };

    private static StageInfo BuildResponderStage() => new()
    {
        StageName = "agent:responder",
        StageHandlerName = AgentConstants.ResponderHandlerName,
    };

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Appends follow-up stages to the current pipeline.
    /// Overridable for tests that need to capture stage scheduling without making API calls.
    /// </summary>
    protected virtual async Task AppendStagesAsync(int pipelineId, IEnumerable<StageInfo> stages)
    {
        var request = new AppendStagesRequest { Stages = stages.ToList() };
        await apiClient.AppendAgentStagesAsync(pipelineId, request);
    }

    private bool IsMutatingTool(string toolName)
    {
        var tool = toolRegistry.Find(toolName);
        return tool?.IsEffectivelyMutating ?? false;
    }

    private static bool TryConsumeApprovedMutation(IStageContext? context, AgentToolCall call)
    {
        var approvedMutations = context.TryGetValue<List<AgentToolCall>>(AgentConstants.ApprovedMutations);
        if (approvedMutations == null || approvedMutations.Count == 0)
            return false;

        var index = approvedMutations.FindIndex(candidate => ToolCallsEqual(candidate, call));
        if (index < 0)
            return false;

        approvedMutations.RemoveAt(index);

        if (context?.Payload == null)
            return true;

        if (approvedMutations.Count == 0)
            context.Payload.Remove(AgentConstants.ApprovedMutations);
        else
            context.Payload[AgentConstants.ApprovedMutations] = approvedMutations;

        return true;
    }

    private static bool ToolCallsEqual(AgentToolCall left, AgentToolCall right)
    {
        if (!string.Equals(left.Tool, right.Tool, StringComparison.OrdinalIgnoreCase))
            return false;

        if (left.Params.Count != right.Params.Count)
            return false;

        foreach (var (key, value) in left.Params)
        {
            if (!right.Params.TryGetValue(key, out var rightValue))
                return false;

            var leftSerialized = JsonSerializer.Serialize(value);
            var rightSerialized = JsonSerializer.Serialize(rightValue);
            if (!string.Equals(leftSerialized, rightSerialized, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static List<AgentConversationTurn> GetOrCreateHistory(IStageContext? context)
    {
        var existing = context.TryGetValue<List<AgentConversationTurn>>(AgentConstants.ConversationHistory);
        return existing ?? new List<AgentConversationTurn>();
    }

    private static void SetContextValue(IStageContext? context, string key, object value)
    {
        if (context == null) return;
        context.Payload ??= new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        context.Payload[key] = value;
    }

    private static int GetStep(IStageContext? context) =>
        context.TryGetValue<int>(AgentConstants.ThinkStepCount);
}
