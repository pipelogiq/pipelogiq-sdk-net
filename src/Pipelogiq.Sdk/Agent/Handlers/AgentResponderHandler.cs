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
    IAgentNotificationRouter? notificationRouter = null) : IStageHandler
{
    /// <inheritdoc />
    public async Task<IStageResult> ExecuteAsync(IStageContext? context = null)
    {
        var originalMessage = context.TryGetValue<string>(AgentConstants.OriginalMessage) ?? string.Empty;

        // Check if there's a direct answer (no tool calls needed or rejection message)
        var directAnswer = context.TryGetValue<string>("agent:directAnswer");
        if (!string.IsNullOrWhiteSpace(directAnswer))
        {
            return await SendOrFallbackAsync(context, directAnswer);
        }

        // Synthesize response from tool results
        var toolResults = context.TryGetValue<List<AgentToolResult>>(AgentConstants.ToolResults)
                          ?? new List<AgentToolResult>();

        var responseText = toolResults.Count > 0
            ? await llmPlanner.SynthesizeAsync(originalMessage, toolResults)
            : "The request has been processed.";

        return await SendOrFallbackAsync(context, responseText);
    }

    private async Task<IStageResult> SendOrFallbackAsync(IStageContext? context, string message)
    {
        if (notificationRouter == null)
        {
            StoreFallbackResponse(context, message);
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
            return StageResult.Success($"Response sent via channel '{dispatch.Channel}'.");

        StoreFallbackResponse(context, message);
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
