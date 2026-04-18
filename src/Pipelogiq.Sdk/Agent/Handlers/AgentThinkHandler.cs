using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Agent.Configuration;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Agent.Services;
using PipelogiqSDK.Api;
using PipelogiqSDK.Contracts;
using PipelogiqSDK.StageHelper;
using System.Net;
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
    PipelogiqApiClient apiClient,
    IAgentNotificationRouter? notificationRouter = null,
    IAgentMemoryStore? memoryStore = null,
    IAgentSessionStore? sessionStore = null) : IStageHandler
{
    private const int RateLimitRetryIntervalSeconds = 120;
    private const int RateLimitMaxRetries = 30;

    /// <inheritdoc />
    public async Task<IStageResult> ExecuteAsync(IStageContext? context = null)
    {
        var pipelineId = context?.PipelineId
            ?? throw new InvalidOperationException("PipelineId is required for AgentThinkHandler.");
        var sessionId = context.TryGetValue<string>(AgentConstants.SessionId);

        // Guard: prevent infinite loops
        var stepCount = context.TryGetValue<int>(AgentConstants.ThinkStepCount);
        stepCount++;
        SetContextValue(context, AgentConstants.ThinkStepCount, stepCount);
        context.LogInfo($"Think step started [step={stepCount}, pipelineId={pipelineId}, sessionId={sessionId ?? "-"}]");

        if (stepCount > agentOptions.MaxThinkSteps)
        {
            SetContextValue(context, "agent:directAnswer",
                $"Agent reached the maximum number of reasoning steps ({agentOptions.MaxThinkSteps}).");
            context.LogWarning($"Think limit reached [step={stepCount}, max={agentOptions.MaxThinkSteps}]");
            var appendedStages = await EnsureResponderStageAppendedAsync(pipelineId, context);
            return StageResult.Success(
                $"Max think steps ({agentOptions.MaxThinkSteps}) reached — responder appended.",
                appendedStages);
        }

        if (await HasFollowUpStagesAsync(pipelineId, context))
        {
            context.LogWarning(
                $"Retry detected — follow-up stages already exist after stage {context.StageId}. Skipping re-execution to prevent duplicates.");
            return StageResult.Success($"Think step {stepCount}: retry detected — follow-up stages already appended.");
        }

        // Ask LLM for next action — declare early so it's available to session loading below
        var originalMessage = context.TryGetValue<string>(AgentConstants.OriginalMessage) ?? string.Empty;

        var history = GetOrCreateHistory(context);

        // On the very first think step of a fresh pipeline, check whether there is
        // saved conversation history from a previous pipeline in the same session.
        // If found, inject it so the LLM sees the full multi-turn context.
        if (stepCount == 1 && history.Count == 0)
        {
            history = await LoadSessionHistoryAsync(originalMessage, context) ?? history;
            SetContextValue(context, AgentConstants.ConversationHistory, history);
            context.LogInfo(
                $"Session history bootstrap completed [sessionId={sessionId ?? "-"}, loadedTurns={history.Count}, historyTypes={SummarizeHistoryTypes(history)}]");
        }

        var tools = toolRegistry.GetAll();
        context.LogInfo(
            $"Think context prepared [step={stepCount}, sessionId={sessionId ?? "-"}, tools={tools.Count}, historyTurns={history.Count}, toolResults={GetToolResultCount(context)}, payloadKeys={context.Payload?.Count ?? 0}, messagePreview={originalMessage.ToLogPreview(400)}]");

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
                context.LogWarning("Approval rejected. Switching directly to responder.");
                var appendedStages = await EnsureResponderStageAppendedAsync(pipelineId, context);
                return StageResult.Success("Confirmation rejected — responder appended.", appendedStages);
            }

            // Approved — extract ApprovedMutations from the last confirmation_requested turn
            // so TryConsumeApprovedMutation can match them and skip re-confirmation.
            ExtractAndSetApprovedMutations(history, context);

            // Clear approval flag so next think step starts fresh
            context.Payload!.Remove(AgentConstants.ApprovalDecision);
        }

        // Check for tool loop: if any single tool has failed 3 times in a row,
        // stop reasoning and let the agent apologise to the user instead of looping.
        var loopingTool = DetectToolLoop(context);
        if (loopingTool != null)
        {
            SetContextValue(context, "agent:directAnswer",
                $"I'm sorry, I was unable to complete this step — the tool '{loopingTool}' " +
                "failed repeatedly. Please try again or rephrase your request.");
            context.LogWarning($"Tool loop detected [tool={loopingTool}, consecutiveFailures>={MaxConsecutiveToolFailures}]");
            var appendedStages = await EnsureResponderStageAppendedAsync(pipelineId, context);
            return new StageResultDto
            {
                Result = $"Tool loop detected for '{loopingTool}' — responder appended.",
                IsSuccess = true,
                ErrorCode = "TOOL_LOOP",
                AppendedStages = appendedStages,
            };
        }

        // Check token budget before calling the LLM
        if (IsTokenBudgetExceeded(context))
        {
            SetContextValue(context, "agent:directAnswer",
                "The agent stopped because the token or cost budget for this session was exceeded.");
            context.LogWarning("Token or cost budget exceeded. Switching to responder.");
            var appendedStages = await EnsureResponderStageAppendedAsync(pipelineId, context);
            return new StageResultDto
            {
                Result = "Token budget exceeded — responder appended.",
                IsSuccess = true,
                ErrorCode = "BUDGET_EXCEEDED",
                AppendedStages = appendedStages,
            };
        }

        // Emit progress notification
        await SendThinkingProgressAsync(context, stepCount);

        // Recall long-term memories and inject context values into system prompt
        var systemPrompt = await BuildSystemPromptWithMemoryAsync(originalMessage, context);

        // Attachments (images, PDFs) are only relevant on the first think step.
        // On subsequent steps the conversation history already carries full context.
        var attachments = stepCount == 1
            ? context.TryGetValue<List<AgentAttachment>>(AgentConstants.Attachments)
            : null;

        AgentThinkDecision decision;
        try
        {
            decision = await llmPlanner.ThinkAsync(
                originalMessage,
                history,
                tools,
                agentOptions.RequireConfirmationForMutations,
                systemPrompt,
                attachments);
        }
        catch (HttpRequestException ex) when (IsAnthropicRateLimit(ex))
        {
            context.LogWarning($"LLM planner hit rate limit [{ex.Message.ToLogPreview(300)}]");
            return StageResult.RateLimitExceeded(
                $"Anthropic rate limit reached. Retry scheduled in {RateLimitRetryIntervalSeconds} seconds.");
        }

        // Accumulate token usage in context
        AccumulateTokenUsage(context, decision.TokenUsage);
        context.LogInfo(
            $"Think decision received [action={decision.Action}, tool={(decision.ToolCall?.Tool ?? "-")}, confirmations={decision.MutationsToConfirm?.Count ?? 0}, hasFinalAnswer={!string.IsNullOrWhiteSpace(decision.FinalAnswer)}, decisionPreview={decision.RawDecisionJson.ToLogPreview(800)}]");

        if (decision.Action == AgentThinkAction.CallTool && decision.ToolCall != null)
        {
            context.LogInfo(
                $"Think selected tool [tool={decision.ToolCall.Tool}, resultKey={decision.ToolCall.EffectiveResultKey}, paramKeys={string.Join(",", decision.ToolCall.Params.Keys.OrderBy(x => x))}]");
        }

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

        var criticMode = AgentCriticRuntime.ResolveMode(context, agentOptions);
        if (ShouldRouteToCritic(decision, criticMode))
            return await RouteToCriticAsync(pipelineId, decision, context);

        return decision.Action switch
        {
            AgentThinkAction.CallTool when decision.ToolCall != null
                => await HandleCallToolAsync(pipelineId, decision, history, context),

            AgentThinkAction.NeedConfirmation when decision.MutationsToConfirm?.Count > 0
                => await HandleNeedConfirmationAsync(pipelineId, decision, history, context),

            _ => await HandleDoneAsync(pipelineId, decision, context),
        };
    }

    private bool ShouldRouteToCritic(AgentThinkDecision decision, AgentCriticMode criticMode)
    {
        if (criticMode == AgentCriticMode.Off)
            return false;

        return criticMode switch
        {
            AgentCriticMode.CriticOnEveryStep => true,
            AgentCriticMode.CriticOnFinal => decision.Action == AgentThinkAction.Done,
            AgentCriticMode.CriticOnMutating => decision.Action switch
            {
                AgentThinkAction.Done => true,
                AgentThinkAction.NeedConfirmation => true,
                AgentThinkAction.CallTool when decision.ToolCall != null => IsMutatingTool(decision.ToolCall.Tool),
                _ => false,
            },
            _ => false,
        };
    }

    private async Task<IStageResult> RouteToCriticAsync(int pipelineId, AgentThinkDecision decision, IStageContext context)
    {
        SetContextValue(context, AgentConstants.PendingProposal, decision);
        context.LogInfo(
            $"Think proposal routed to critic [action={decision.Action}, tool={(decision.ToolCall?.Tool ?? "-")}]");
        var appendedStages = await AppendStagesAsync(pipelineId, [BuildCriticStage()]);
        return StageResult.Success(
            $"Think step {GetStep(context)}: proposal sent to critic for review.",
            appendedStages);
    }

    private static StageInfo BuildCriticStage() => new()
    {
        StageName = "agent:critic",
        StageHandlerName = AgentConstants.CriticHandlerName,
        Options = new StageOptions
        {
            RetryInterval = RateLimitRetryIntervalSeconds,
            MaxRetries = RateLimitMaxRetries,
        },
    };

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

        await SendToolCallProgressAsync(context, call.Tool);
        context.LogInfo(
            $"Scheduling tool call [tool={call.Tool}, resultKey={call.EffectiveResultKey}, params={call.Params.ToLogPreview(800)}]");
        var appendedStages = await AppendStagesAsync(pipelineId, [BuildToolStage(call), BuildThinkStage()]);

        return StageResult.Success($"Think step {GetStep(context)}: calling tool '{call.Tool}'.", appendedStages);
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
        context.LogInfo(
            $"Scheduling confirmation flow [mutations={decision.MutationsToConfirm!.Count}, tools={string.Join(", ", decision.MutationsToConfirm!.Select(x => x.Tool))}]");
        var appendedStages = await AppendStagesAsync(pipelineId, [
            BuildConfirmationStage(decision.MutationsToConfirm!),
            BuildThinkStage(),
        ]);

        return StageResult.Success(
            $"Think step {GetStep(context)}: confirmation requested for {decision.MutationsToConfirm!.Count} mutation(s).",
            appendedStages);
    }

    private async Task<IStageResult> HandleDoneAsync(
        int pipelineId,
        AgentThinkDecision decision,
        IStageContext context)
    {
        if (!string.IsNullOrWhiteSpace(decision.FinalAnswer))
            SetContextValue(context, "agent:directAnswer", decision.FinalAnswer);

        context.LogInfo($"Think loop completed. Final answer preview: {decision.FinalAnswer.ToLogPreview(800)}");
        var appendedStages = await EnsureResponderStageAppendedAsync(pipelineId, context);

        return StageResult.Success(
            $"Think step {GetStep(context)}: reasoning complete — responder appended.",
            appendedStages);
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
        Options = new StageOptions
        {
            RetryInterval = RateLimitRetryIntervalSeconds,
            MaxRetries = RateLimitMaxRetries,
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
        Options = new StageOptions
        {
            RunNextIfFailed = true,
        },
    };

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Appends follow-up stages to the current pipeline.
    /// Overridable for tests that need to capture stage scheduling without making API calls.
    /// </summary>
    protected virtual Task<List<StageInfo>> AppendStagesAsync(int pipelineId, IEnumerable<StageInfo> stages)
    {
        return Task.FromResult(stages.ToList());
    }

    private async Task<List<StageInfo>> EnsureResponderStageAppendedAsync(int pipelineId, IStageContext? context)
    {
        if (context.TryGetValue<bool>(AgentConstants.ResponderAppended))
        {
            context.LogInfo("Responder stage already appended earlier. Skipping duplicate append.");
            return [];
        }

        var appendedStages = await AppendStagesAsync(pipelineId, [BuildResponderStage()]);
        SetContextValue(context, AgentConstants.ResponderAppended, true);
        context.LogInfo("Responder stage appended.");
        return appendedStages;
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

    private static void ExtractAndSetApprovedMutations(List<AgentConversationTurn> history, IStageContext? context)
    {
        var confirmTurn = history.LastOrDefault(t => t.Type == "confirmation_requested");
        if (confirmTurn?.Content == null) return;

        try
        {
            var start = confirmTurn.Content.IndexOf('{');
            var end = confirmTurn.Content.LastIndexOf('}');
            if (start < 0 || end < 0) return;

            using var doc = JsonDocument.Parse(confirmTurn.Content[start..(end + 1)]);
            var root = doc.RootElement;
            var mutations = new List<AgentToolCall>();

            // need_confirmation format: { action: "need_confirmation", mutations: [...] }
            if (root.TryGetProperty("mutations", out var mutEl) && mutEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in mutEl.EnumerateArray())
                {
                    if (!item.TryGetProperty("tool", out var toolEl)) continue;
                    var name = toolEl.GetString();
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var call = new AgentToolCall
                    {
                        Tool = name!,
                        ResultKey = item.TryGetProperty("resultKey", out var rkEl) ? rkEl.GetString() : null,
                    };
                    if (item.TryGetProperty("params", out var paramsEl) && paramsEl.ValueKind == JsonValueKind.Object)
                        foreach (var p in paramsEl.EnumerateObject())
                            call.Params[p.Name] = p.Value.ValueKind == JsonValueKind.String
                                ? p.Value.GetString()
                                : p.Value.GetRawText();
                    mutations.Add(call);
                }
            }
            // call_tool format: { action: "call_tool", tool: "...", params: {...} }
            else if (root.TryGetProperty("tool", out var toolEl2))
            {
                var name = toolEl2.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var call = new AgentToolCall
                    {
                        Tool = name!,
                        ResultKey = root.TryGetProperty("resultKey", out var rkEl) ? rkEl.GetString() : null,
                    };
                    if (root.TryGetProperty("params", out var paramsEl) && paramsEl.ValueKind == JsonValueKind.Object)
                        foreach (var p in paramsEl.EnumerateObject())
                            call.Params[p.Name] = p.Value.ValueKind == JsonValueKind.String
                                ? p.Value.GetString()
                                : p.Value.GetRawText();
                    mutations.Add(call);
                }
            }

            if (mutations.Count > 0)
                SetContextValue(context, AgentConstants.ApprovedMutations, mutations);
        }
        catch { /* ignore parse errors */ }
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

    private static bool IsAnthropicRateLimit(HttpRequestException ex)
        => ex.StatusCode == HttpStatusCode.TooManyRequests;

    // ── Loop detection ───────────────────────────────────────────────────────

    /// <summary>Maximum consecutive failures for a single tool before the agent stops.</summary>
    private const int MaxConsecutiveToolFailures = 3;

    private static string? DetectToolLoop(IStageContext? context)
    {
        if (context?.Payload == null) return null;

        foreach (var (key, value) in context.Payload)
        {
            if (!key.StartsWith(AgentConstants.ToolFailureCountPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var count = 0;
            if (value is int i) count = i;
            else if (value is long l) count = (int)l;
            else if (value is System.Text.Json.JsonElement el && el.TryGetInt32(out var j)) count = j;

            if (count >= MaxConsecutiveToolFailures)
                return key[AgentConstants.ToolFailureCountPrefix.Length..];
        }

        return null;
    }

    // ── Token budget ─────────────────────────────────────────────────────────

    private bool IsTokenBudgetExceeded(IStageContext? context)
    {
        var budget = agentOptions.TokenBudget;

        if (budget.MaxInputTokensPerSession.HasValue)
        {
            var totalInput = context.TryGetValue<int>(AgentConstants.SessionTotalInputTokens);
            if (totalInput >= budget.MaxInputTokensPerSession.Value)
                return true;
        }

        if (budget.MaxCostUsdPerSession.HasValue)
        {
            var totalCost = context.TryGetValue<decimal>(AgentConstants.SessionEstimatedCostUsd);
            if (totalCost >= budget.MaxCostUsdPerSession.Value)
                return true;
        }

        return false;
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
        Add(AgentConstants.SessionCacheReadTokens, usage.CacheReadTokens);
        Add(AgentConstants.SessionCacheCreationTokens, usage.CacheCreationTokens);
        AddDecimal(AgentConstants.SessionEstimatedCostUsd, usage.EstimatedCostUsd);
    }

    // ── Session history (cross-pipeline continuity) ──────────────────────────

    /// <summary>Maximum previous turns to carry into the new pipeline to stay within token budget.</summary>
    private const int MaxSessionTurns = 24;

    private async Task<List<AgentConversationTurn>?> LoadSessionHistoryAsync(
        string originalMessage,
        IStageContext? context)
    {
        if (sessionStore == null) return null;

        var sessionId = context.TryGetValue<string>(AgentConstants.SessionId);
        if (string.IsNullOrEmpty(sessionId)) return null;

        var previous = await sessionStore.LoadAsync(sessionId);
        if (previous == null || previous.Count == 0)
        {
            context?.LogInfo($"No prior session history found [sessionId={sessionId}]");
            return null;
        }

        // Cap to avoid token explosion on very long sessions
        var recent = previous.Count > MaxSessionTurns
            ? previous.GetRange(previous.Count - MaxSessionTurns, MaxSessionTurns)
            : new List<AgentConversationTurn>(previous);

        // Append the current user message so the LLM sees it as a reply
        recent.Add(new AgentConversationTurn
        {
            Type = "user_message",
            Content = originalMessage,
        });

        context?.LogInfo(
            $"Loaded prior session history [sessionId={sessionId}, storedTurns={previous.Count}, carriedTurns={recent.Count}, historyTypes={SummarizeHistoryTypes(recent)}]");

        return recent;
    }

    private static int GetToolResultCount(IStageContext? context) =>
        context?.TryGetValue<List<AgentToolResult>>(AgentConstants.ToolResults)?.Count ?? 0;

    private static string SummarizeHistoryTypes(IReadOnlyList<AgentConversationTurn> history)
    {
        if (history.Count == 0)
            return "-";

        return string.Join(">", history.Select(x => x.Type).TakeLast(8));
    }

    // ── Memory recall + context injection ────────────────────────────────────

    private async Task<string?> BuildSystemPromptWithMemoryAsync(string userMessage, IStageContext? context)
    {
        var sb = new System.Text.StringBuilder(agentOptions.SystemPrompt ?? string.Empty);

        // Inject pipeline context values the LLM should know about
        if (agentOptions.ContextInjectionKeys.Count > 0 && context?.Payload != null)
        {
            var injected = new List<string>();
            foreach (var (contextKey, label) in agentOptions.ContextInjectionKeys)
            {
                var value = context.TryGetValue<string>(contextKey);
                if (!string.IsNullOrEmpty(value))
                    injected.Add($"{label} = {value}");
            }

            if (injected.Count > 0)
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.AppendLine();
                sb.AppendLine("Available context (use these values directly, do not ask the user for them):");
                foreach (var line in injected)
                    sb.AppendLine($"- {line}");
            }
        }

        // Recall long-term memories
        if (memoryStore != null)
        {
            var sessionId = context.TryGetValue<string>(AgentConstants.SessionId);
            if (!string.IsNullOrEmpty(sessionId))
            {
                var memories = await memoryStore.RecallAsync(sessionId, userMessage);
                if (memories.Count > 0)
                {
                    if (sb.Length > 0) sb.AppendLine();
                    sb.AppendLine();
                    sb.AppendLine("Relevant memories from previous interactions:");
                    foreach (var m in memories)
                        sb.AppendLine($"- [{m.Category}] {m.Content}");
                }
            }
        }

        var result = sb.ToString().TrimEnd();
        return result.Length > 0 ? result : null;
    }

    // ── Progress notifications ────────────────────────────────────────────────

    private async Task SendThinkingProgressAsync(IStageContext? context, int stepCount)
    {
        var opts = agentOptions.Progress;
        if (!opts.Enabled || notificationRouter == null) return;

        string message;
        if (opts.ThinkingMessages is { Length: > 0 })
        {
            message = opts.ThinkingMessages[(stepCount - 1) % opts.ThinkingMessages.Length];
        }
        else
        {
            message = opts.ShowStepNumber
                ? $"{opts.ThinkingMessage} (step {stepCount})"
                : opts.ThinkingMessage;
        }

        await SendProgressAsync(context, message);
    }

    private async Task SendToolCallProgressAsync(IStageContext? context, string toolName)
    {
        var opts = agentOptions.Progress;
        if (!opts.Enabled || notificationRouter == null) return;

        // Tool-level ProgressMessage takes priority
        var toolDef = toolRegistry.Find(toolName);
        if (!string.IsNullOrEmpty(toolDef?.ProgressMessage))
        {
            await SendProgressAsync(context, toolDef.ProgressMessage);
            return;
        }

        if (opts.ToolCallMessages != null && opts.ToolCallMessages.TryGetValue(toolName, out var perToolMessage))
        {
            await SendProgressAsync(context, perToolMessage);
            return;
        }

        if (string.IsNullOrEmpty(opts.ToolCallMessage)) return;

        var message = opts.ShowToolName
            ? $"{opts.ToolCallMessage}: {toolName}…"
            : opts.ToolCallMessage;

        await SendProgressAsync(context, message);
    }

    private async Task SendProgressAsync(IStageContext? context, string message, CancellationToken cancellationToken = default)
    {
        if (notificationRouter == null) return;

        var notification = new AgentNotification
        {
            Type = "progress",
            Message = message,
            PipelineId = context?.PipelineId,
        };

        await notificationRouter.NotifyAsync(context, notification, cancellationToken);
    }

    // ── Retry deduplication ─────────────────────────────────────────────────

    private async Task<bool> HasFollowUpStagesAsync(int pipelineId, IStageContext? context)
    {
        var currentStageId = context?.StageId;
        if (!currentStageId.HasValue || currentStageId.Value <= 0)
            return false;

        try
        {
            var pipeline = await apiClient.GetPipelineAsync(pipelineId);
            return pipeline?.Stages?.Any(s =>
                s.Id > currentStageId.Value &&
                (s.Status is "NotStarted" or "Pending") &&
                (s.Name?.StartsWith("agent:", StringComparison.OrdinalIgnoreCase) ?? false)) ?? false;
        }
        catch
        {
            return false;
        }
    }
}
