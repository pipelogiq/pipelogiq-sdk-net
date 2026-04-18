using System.Net;
using System.Text.Json;
using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Agent;
using PipelogiqSDK.Agent.Configuration;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Agent.Services;
using PipelogiqSDK.Api;
using PipelogiqSDK.Contracts;
using PipelogiqSDK.StageHelper;

namespace PipelogiqSDK.Agent.Handlers;

/// <summary>
/// Reviews the think handler's pending proposal with a second-model critic.
/// On approve: schedules the originally-planned follow-up stages (tool, confirmation, or responder).
/// On reject: appends a critic_feedback turn to history and loops back to think for another attempt,
/// up to <see cref="AgentCriticOptions.MaxRejectionsPerStep"/> rejections per single proposal.
/// </summary>
public class AgentCriticHandler(
    IAgentCriticResolver criticResolver,
    IAgentToolRegistry toolRegistry,
    AgentOptions agentOptions,
    PipelogiqApiClient apiClient) : IStageHandler
{
    private const int RateLimitRetryIntervalSeconds = 120;
    private const int RateLimitMaxRetries = 30;

    /// <inheritdoc />
    public async Task<IStageResult> ExecuteAsync(IStageContext? context = null)
    {
        var pipelineId = context?.PipelineId
            ?? throw new InvalidOperationException("PipelineId is required for AgentCriticHandler.");

        var proposal = context.TryGetValue<AgentThinkDecision>(AgentConstants.PendingProposal);
        if (proposal == null)
        {
            context.LogWarning("Critic invoked but no pending proposal found in context — falling through to think.");
            await AppendStagesAsync(pipelineId, [BuildThinkStage()]);
            return StageResult.Success("No pending proposal — think re-scheduled.");
        }

        var criticMode = AgentCriticRuntime.ResolveMode(context, agentOptions);
        var criticSettings = agentOptions.Critic;
        var history = context.TryGetValue<List<AgentConversationTurn>>(AgentConstants.ConversationHistory) ?? new();
        var originalMessage = context.TryGetValue<string>(AgentConstants.OriginalMessage) ?? string.Empty;
        var tools = toolRegistry.GetAll();
        var rejectionCount = context.TryGetValue<int>(AgentConstants.CriticRejectionCount);

        context.LogInfo(
            $"Critic review started [mode={criticMode}, provider={criticSettings.Provider}, model={criticSettings.Model ?? "default"}, action={proposal.Action}, tool={proposal.ToolCall?.Tool ?? "-"}, rejectionsSoFar={rejectionCount}]");

        IAgentCritic critic;
        try
        {
            critic = criticResolver.Resolve(criticSettings.Provider);
        }
        catch (NotSupportedException ex)
        {
            context.LogWarning($"Critic provider unavailable — approving by default [{ex.Message}]");
            return await ApproveAndScheduleAsync(pipelineId, proposal, context);
        }

        AgentCriticVerdict verdict;
        try
        {
            verdict = await critic.ReviewAsync(originalMessage, history, proposal, tools, criticSettings);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            context.LogWarning($"Critic hit rate limit [{ex.Message.ToLogPreview(300)}]");
            return StageResult.RateLimitExceeded(
                $"Critic provider rate limit reached. Retry scheduled in {RateLimitRetryIntervalSeconds} seconds.");
        }
        catch (Exception ex)
        {
            context.LogWarning($"Critic call failed — approving by default to keep loop moving [{ex.Message.ToLogPreview(400)}]");
            return await ApproveAndScheduleAsync(pipelineId, proposal, context);
        }

        AccumulateTokenUsage(context, verdict.TokenUsage);
        context.LogInfo(
            $"Critic verdict [decision={verdict.Decision}, confidence={verdict.Confidence?.ToString("0.00") ?? "-"}, concerns={verdict.Concerns?.Count ?? 0}, feedback={verdict.Feedback.ToLogPreview(400)}]");

        if (verdict.Decision == AgentCriticDecision.Approve)
            return await ApproveAndScheduleAsync(pipelineId, proposal, context);

        var nextRejectionCount = rejectionCount + 1;
        if (nextRejectionCount > criticSettings.MaxRejectionsPerStep)
        {
            context.LogWarning(
                $"Critic rejection cap reached [cap={criticSettings.MaxRejectionsPerStep}] — approving proposal to make forward progress.");
            return await ApproveAndScheduleAsync(pipelineId, proposal, context);
        }

        return await RejectAndLoopAsync(pipelineId, proposal, verdict, nextRejectionCount, history, context);
    }

    // ── Approve path ─────────────────────────────────────────────────────────

    private async Task<IStageResult> ApproveAndScheduleAsync(
        int pipelineId,
        AgentThinkDecision proposal,
        IStageContext context)
    {
        ClearCriticState(context);

        var history = context.TryGetValue<List<AgentConversationTurn>>(AgentConstants.ConversationHistory) ?? new();

        switch (proposal.Action)
        {
            case AgentThinkAction.CallTool when proposal.ToolCall != null:
                history.Add(new AgentConversationTurn
                {
                    Type = "tool_call",
                    ToolName = proposal.ToolCall.Tool,
                    Content = proposal.RawDecisionJson,
                });
                SetContextValue(context, AgentConstants.ConversationHistory, history);
                await AppendStagesAsync(pipelineId, [BuildToolStage(proposal.ToolCall), BuildThinkStage()]);
                return StageResult.Success($"Critic approved — tool '{proposal.ToolCall.Tool}' scheduled.");

            case AgentThinkAction.NeedConfirmation when proposal.MutationsToConfirm?.Count > 0:
                history.Add(new AgentConversationTurn
                {
                    Type = "confirmation_requested",
                    Content = proposal.RawDecisionJson,
                });
                SetContextValue(context, AgentConstants.ConversationHistory, history);
                await AppendStagesAsync(pipelineId, [
                    BuildConfirmationStage(proposal.MutationsToConfirm!),
                    BuildThinkStage(),
                ]);
                return StageResult.Success(
                    $"Critic approved — confirmation scheduled for {proposal.MutationsToConfirm!.Count} mutation(s).");

            default:
                if (!string.IsNullOrWhiteSpace(proposal.FinalAnswer))
                    SetContextValue(context, "agent:directAnswer", proposal.FinalAnswer!);
                await AppendStagesAsync(pipelineId, [BuildResponderStage()]);
                SetContextValue(context, AgentConstants.ResponderAppended, true);
                return StageResult.Success("Critic approved — responder scheduled.");
        }
    }

    // ── Reject path ──────────────────────────────────────────────────────────

    private async Task<IStageResult> RejectAndLoopAsync(
        int pipelineId,
        AgentThinkDecision proposal,
        AgentCriticVerdict verdict,
        int nextRejectionCount,
        List<AgentConversationTurn> history,
        IStageContext context)
    {
        SetContextValue(context, AgentConstants.CriticRejectionCount, nextRejectionCount);
        SetContextValue(context, AgentConstants.LastCriticFeedback, verdict.Feedback ?? string.Empty);

        // Drop the pending proposal so the think handler does not re-review the same decision
        if (context.Payload != null)
            context.Payload.Remove(AgentConstants.PendingProposal);

        var feedbackBody = BuildCriticFeedbackBody(proposal, verdict);
        history.Add(new AgentConversationTurn
        {
            Type = "critic_feedback",
            Content = feedbackBody,
        });
        SetContextValue(context, AgentConstants.ConversationHistory, history);

        await AppendStagesAsync(pipelineId, [BuildThinkStage()]);
        return StageResult.Success(
            $"Critic rejected (rejection {nextRejectionCount}) — looping back to think with feedback.");
    }

    private static string BuildCriticFeedbackBody(AgentThinkDecision proposal, AgentCriticVerdict verdict)
    {
        var feedback = JsonSerializer.Serialize(new
        {
            rejectedAction = proposal.Action.ToString(),
            rejectedTool = proposal.ToolCall?.Tool,
            feedback = verdict.Feedback,
            concerns = verdict.Concerns,
            confidence = verdict.Confidence,
            instruction = "A second-model reviewer rejected your previous proposal. Read the feedback and concerns, revise your plan, and emit a new decision. Do not repeat the rejected action without addressing every concern.",
        });
        return feedback;
    }

    // ── Stage builders ───────────────────────────────────────────────────────

    private static StageInfo BuildThinkStage() => new()
    {
        StageName = "agent:think",
        StageHandlerName = AgentConstants.ThinkHandlerName,
        Options = new StageOptions
        {
            RetryInterval = RateLimitRetryIntervalSeconds,
            MaxRetries = RateLimitMaxRetries,
        },
    };

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
        Options = new StageOptions { RunNextIfFailed = true },
    };

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Appends follow-up stages to the current pipeline. Overridable for tests.</summary>
    protected virtual async Task AppendStagesAsync(int pipelineId, IEnumerable<StageInfo> stages)
    {
        var request = new AppendStagesRequest { Stages = stages.ToList() };
        await apiClient.AppendAgentStagesAsync(pipelineId, request);
    }

    private static void ClearCriticState(IStageContext context)
    {
        if (context.Payload == null) return;
        context.Payload.Remove(AgentConstants.PendingProposal);
        context.Payload.Remove(AgentConstants.CriticRejectionCount);
        context.Payload.Remove(AgentConstants.LastCriticFeedback);
    }

    private static void SetContextValue(IStageContext? context, string key, object value)
    {
        if (context == null) return;
        context.Payload ??= new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        context.Payload[key] = value;
    }

    private static void AccumulateTokenUsage(IStageContext? context, AgentLlmUsage? usage)
    {
        if (context == null || usage == null) return;

        context.Payload ??= new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        void Add(string key, long value)
        {
            var current = context.TryGetValue<long>(key);
            context.Payload[key] = current + value;
        }

        void AddDecimal(string key, decimal value)
        {
            var current = context.TryGetValue<decimal>(key);
            context.Payload[key] = current + value;
        }

        Add(AgentConstants.SessionTotalInputTokens, usage.InputTokens);
        Add(AgentConstants.SessionTotalOutputTokens, usage.OutputTokens);
        AddDecimal(AgentConstants.SessionEstimatedCostUsd, usage.EstimatedCostUsd);
    }
}
