using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Agent.Services;
using PipelogiqSDK.StageHelper;

namespace PipelogiqSDK.Agent.Handlers;

/// <summary>
/// Built-in handler that synthesizes the final response and notifies the client.
/// </summary>
public class AgentResponderHandler(
    ILlmPlanner llmPlanner,
    IAgentNotificationRouter? notificationRouter = null,
    IAgentSessionStore? sessionStore = null,
    IAgentLifecycleObserver? lifecycleObserver = null) : IStageHandler
{
    /// <inheritdoc />
    public async Task<IStageResult> ExecuteAsync(IStageContext? context = null)
    {
        var originalMessage = context.TryGetValue<string>(AgentConstants.OriginalMessage) ?? string.Empty;

        // Check if there's a direct answer (no tool calls needed or rejection message)
        var directAnswer = context.TryGetValue<string>("agent:directAnswer");
        string responseText;
        if (!string.IsNullOrWhiteSpace(directAnswer))
        {
            responseText = directAnswer;
            context.LogInfo($"Responder using direct answer [{responseText.ToLogPreview(800)}]");
        }
        else
        {
            // Synthesize response from tool results
            var toolResults = context.TryGetValue<List<AgentToolResult>>(AgentConstants.ToolResults)
                              ?? new List<AgentToolResult>();

            context.LogInfo($"Responder synthesizing final message from {toolResults.Count} tool result(s).");
            responseText = toolResults.Count > 0
                ? await llmPlanner.SynthesizeAsync(originalMessage, toolResults)
                : "The request has been processed.";
        }

        var result = await SendOrFallbackAsync(context, responseText);

        // Save conversation history for multi-turn continuity.
        // The next user message from the same session will load this history
        // and the agent will understand the context without starting over.
        await SaveSessionHistoryAsync(context, originalMessage, responseText);

        // Emit session-completed lifecycle event
        if (lifecycleObserver != null)
        {
            try
            {
                var toolResults = context.TryGetValue<List<AgentToolResult>>(AgentConstants.ToolResults);
                var costUsd = (decimal?)context.TryGetValue<decimal>(AgentConstants.SessionEstimatedCostUsd);
                await lifecycleObserver.OnSessionCompletedAsync(new AgentSessionLifecycleEvent
                {
                    SessionId = context.TryGetValue<string>(AgentConstants.SessionId),
                    OriginalMessage = originalMessage,
                    FinalResponse = responseText.Length > 500 ? responseText[..500] + "…" : responseText,
                    ToolCallCount = toolResults?.Count ?? 0,
                    EstimatedCostUsd = costUsd,
                });
            }
            catch
            {
                // Observer errors are intentionally swallowed — they must never affect agent flow
            }
        }

        return result;
    }

    private async Task SaveSessionHistoryAsync(IStageContext? context, string originalMessage, string finalResponse)
    {
        if (sessionStore == null) return;

        var sessionId = context.TryGetValue<string>(AgentConstants.SessionId);
        if (string.IsNullOrEmpty(sessionId)) return;

        // awaitingReply = true  → agent asked a question, keep session so the
        //                          next user message continues the conversation.
        // awaitingReply = false → task completed (or not set), clear the session
        //                          so the next message starts completely fresh.
        var history = context.TryGetValue<List<AgentConversationTurn>>(AgentConstants.ConversationHistory)
                      ?? new List<AgentConversationTurn>();

        var toSave = new List<AgentConversationTurn>();

        // If history doesn't start with user_message turns, this was a fresh pipeline —
        // wrap the think-loop turns with proper user_message / assistant_response bookends.
        if (history.Count == 0 || history[0].Type != "user_message")
            toSave.Add(new AgentConversationTurn { Type = "user_message", Content = originalMessage });

        toSave.AddRange(history);
        toSave.Add(new AgentConversationTurn { Type = "assistant_response", Content = finalResponse });

        await sessionStore.SaveAsync(sessionId, toSave);
    }

    private async Task<IStageResult> SendOrFallbackAsync(IStageContext? context, string message)
    {
        if (notificationRouter == null)
        {
            StoreFallbackResponse(context, message);
            context.LogWarning("Notification router unavailable. Final response stored in context.");
            return StageResult.Success(
                $"Notification router unavailable. Final response stored in context key '{AgentConstants.FinalResponse}'.");
        }

        var dispatch = await notificationRouter.NotifyAsync(context, new AgentNotification
        {
            Type = "response",
            Message = message,
            PipelineId = context?.PipelineId,
        });

        if (dispatch.Delivered)
        {
            context.LogInfo($"Final response delivered via channel '{dispatch.Channel}'.");
            return StageResult.Success($"Response sent via channel '{dispatch.Channel}'.");
        }

        StoreFallbackResponse(context, message);
        context.LogWarning($"Notification delivery failed [{dispatch.FailureReason}]. Final response stored in context.");
        return StageResult.Success(
            $"Notification delivery failed ({dispatch.FailureReason}). Final response stored in context key '{AgentConstants.FinalResponse}'.");
    }

    private static void StoreFallbackResponse(IStageContext? context, string response)
    {
        if (context == null)
            return;

        context.Payload ??= new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        context.Payload[AgentConstants.FinalResponse] = response;
    }
}
