using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Agent.Configuration;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Agent.Services;
using PipelogiqSDK.Api;
using PipelogiqSDK.Contracts;
using PipelogiqSDK.StageHelper;
using System.Net;

namespace PipelogiqSDK.Agent.Handlers;

/// <summary>
/// Built-in handler for the AI agent orchestration stage.
/// Calls the LLM to plan tool calls, then dynamically appends stages to the pipeline.
/// </summary>
public class AgentOrchestratorHandler(
    ILlmPlanner llmPlanner,
    IAgentToolRegistry toolRegistry,
    AgentOptions agentOptions,
    PipelogiqApiClient apiClient) : IStageHandler<AgentOrchestratorInput>
{
    private const int RateLimitRetryIntervalSeconds = 120;
    private const int RateLimitMaxRetries = 30;

    /// <inheritdoc />
    public async Task<IStageResult> ExecuteAsync(AgentOrchestratorInput input, IStageContext? context = null)
    {
        var pipelineId = context?.PipelineId
            ?? throw new InvalidOperationException("PipelineId is required for agent orchestration.");

        // Store original message and session info in context for all subsequent stages
        SetContextValue(context, AgentConstants.OriginalMessage, input.Message);
        if (input.ReplyTo != null)
            SetContextValue(context, AgentConstants.ReplyTarget, input.ReplyTo);
        SetContextValue(context, AgentConstants.SessionId, input.SessionId ?? string.Empty);
        SetContextValue(context, AgentConstants.UserId, input.UserId ?? string.Empty);
        SetContextValue(context, AgentConstants.ToolResults, new List<AgentToolResult>());
        if (input.Attachments?.Count > 0)
            SetContextValue(context, AgentConstants.Attachments, input.Attachments);

        // ReAct mode: hand off to AgentThinkHandler loop — no upfront planning
        if (agentOptions.UseReActMode)
        {
            SetContextValue(context, AgentConstants.ConversationHistory, new List<AgentConversationTurn>());
            SetContextValue(context, AgentConstants.ThinkStepCount, 0);

            var thinkRequest = new AppendStagesRequest
            {
                Stages =
                [
                    new StageInfo
                    {
                        StageName = "agent:think",
                        StageHandlerName = AgentConstants.ThinkHandlerName,
                        Options = BuildRateLimitRetryOptions(),
                    }
                ]
            };
            await apiClient.AppendAgentStagesAsync(pipelineId, thinkRequest);

            return StageResult.Success("ReAct mode: think stage appended. Loop begins.");
        }

        // Plan-and-execute mode: LLM plans all tool calls upfront
        var tools = toolRegistry.GetAll();
        AgentPlan plan;
        try
        {
            plan = await llmPlanner.PlanAsync(input.Message, tools, agentOptions.SystemPrompt);
        }
        catch (HttpRequestException ex) when (IsAnthropicRateLimit(ex))
        {
            return StageResult.RateLimitExceeded(
                $"Anthropic rate limit reached. Retry scheduled in {RateLimitRetryIntervalSeconds} seconds.");
        }

        if (!plan.HasToolCalls)
        {
            if (!string.IsNullOrWhiteSpace(plan.DirectAnswer))
                SetContextValue(context, "agent:directAnswer", plan.DirectAnswer);

            await AppendStagesAsync(pipelineId, new List<AgentToolCall>(), context, ct: default);
            return StageResult.Success("No tools needed; responder appended.");
        }

        var readOnlyCalls = plan.ToolCalls.Where(c => !IsToolMutating(c.Tool)).ToList();
        var mutatingCalls = plan.ToolCalls.Where(c => IsToolMutating(c.Tool)).ToList();

        await AppendStagesAsync(pipelineId, readOnlyCalls, mutatingCalls, context, ct: default);

        return StageResult.Success($"Agent plan built: {plan.ToolCalls.Count} tool call(s) appended as stages.");
    }

    private bool IsToolMutating(string toolName)
    {
        var def = toolRegistry.Find(toolName);
        return def?.IsEffectivelyMutating ?? false;
    }

    private async Task AppendStagesAsync(
        int pipelineId,
        List<AgentToolCall> readOnlyCalls,
        List<AgentToolCall> mutatingCalls,
        IStageContext context,
        CancellationToken ct)
    {
        var stages = new List<StageInfo>();

        // Read-only tool stages
        foreach (var call in readOnlyCalls)
            stages.Add(BuildToolStage(call));

        // Confirmation stage before mutations (if policy requires it)
        if (mutatingCalls.Count > 0 && agentOptions.RequireConfirmationForMutations)
        {
            stages.Add(BuildConfirmationStage(mutatingCalls));
        }

        // Mutating tool stages
        foreach (var call in mutatingCalls)
            stages.Add(BuildToolStage(call));

        // Always add responder last
        stages.Add(BuildResponderStage());

        var request = new AppendStagesRequest { Stages = stages };
        var response = await apiClient.AppendAgentStagesAsync(pipelineId, request, ct);

        // Store responder stage ID in context so confirmation handler can jump to it on rejection
        var responderStage = response.Stages.LastOrDefault();
        if (responderStage != null)
            SetContextValue(context, AgentConstants.ResponderStageId, responderStage.Id);
    }

    private async Task AppendStagesAsync(
        int pipelineId,
        List<AgentToolCall> allCalls,
        IStageContext context,
        CancellationToken ct)
    {
        await AppendStagesAsync(pipelineId, allCalls, new List<AgentToolCall>(), context, ct);
    }

    private static StageInfo BuildToolStage(AgentToolCall call)
    {
        var input = new AgentToolCallInput
        {
            ToolName = call.Tool,
            Params = call.Params,
            ResultKey = call.EffectiveResultKey,
        };

        return new StageInfo
        {
            StageName = $"agent:tool:{call.Tool}",
            StageHandlerName = AgentConstants.ToolHandlerName,
            Input = input,
        };
    }

    private static StageInfo BuildConfirmationStage(List<AgentToolCall> pendingMutations)
    {
        var input = new AgentConfirmationInput { PendingMutations = pendingMutations };

        return new StageInfo
        {
            StageName = "agent:confirmation",
            StageHandlerName = AgentConstants.ConfirmationHandlerName,
            Input = input,
        };
    }

    private static StageInfo BuildResponderStage()
    {
        return new StageInfo
        {
            StageName = "agent:responder",
            StageHandlerName = AgentConstants.ResponderHandlerName,
        };
    }

    private static void SetContextValue(IStageContext context, string key, object value)
    {
        context.Payload ??= new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        context.Payload[key] = value;
    }

    private static StageOptions BuildRateLimitRetryOptions() => new()
    {
        RetryInterval = RateLimitRetryIntervalSeconds,
        MaxRetries = RateLimitMaxRetries,
    };

    private static bool IsAnthropicRateLimit(HttpRequestException ex)
        => ex.StatusCode == HttpStatusCode.TooManyRequests;
}
