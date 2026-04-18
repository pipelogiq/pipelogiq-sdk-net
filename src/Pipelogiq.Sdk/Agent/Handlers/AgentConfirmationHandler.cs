using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Agent.Services;
using PipelogiqSDK.Contracts;
using PipelogiqSDK.StageHelper;

namespace PipelogiqSDK.Agent.Handlers;

/// <summary>
/// Built-in handler that pauses the pipeline to request user confirmation for mutations.
/// Returns IsWaitingForApproval = true on first run.
/// On resume, proceeds or skips mutations based on approval decision.
/// </summary>
public class AgentConfirmationHandler(IAgentNotificationRouter? notificationRouter = null) : IStageHandler<AgentConfirmationInput>
{
    /// <inheritdoc />
    public async Task<IStageResult> ExecuteAsync(AgentConfirmationInput input, IStageContext? context = null)
    {
        // Check if this is a resume (approval decision present in context)
        // TryGetValue<bool?> returns null if key missing, true/false if set
        var approved = context.TryGetValue<bool?>(AgentConstants.ApprovalDecision);

        if (approved.HasValue)
        {
            if (approved.Value)
            {
                context.LogInfo($"Confirmation approved [mutations={input.PendingMutations.Count}]");
                if (context != null)
                {
                    context.Payload ??= new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    context.Payload[AgentConstants.ApprovedMutations] = input.PendingMutations;
                }

                return StageResult.Success("User approved. Proceeding with changes.");
            }

            // Rejected: store rejection message for the responder
            var reason = context.TryGetValue<string>(AgentConstants.RejectionReason) ?? "User rejected the changes.";
            context!.Payload ??= new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            context.Payload["agent:directAnswer"] = reason;
            context.LogWarning($"Confirmation rejected [mutations={input.PendingMutations.Count}, reason={reason}]");

            // Plan-and-execute mode: jump directly to responder stage (already exists)
            var responderStageId = context.TryGetValue<int?>(AgentConstants.ResponderStageId);
            if (!responderStageId.HasValue)
            {
                var appendedStageIds = context.TryGetValue<Dictionary<string, int>>(AgentConstants.AppendedStageIds);
                if (appendedStageIds != null &&
                    appendedStageIds.TryGetValue("agent:responder", out var mappedResponderStageId))
                {
                    responderStageId = mappedResponderStageId;
                }
            }
            if (responderStageId.HasValue)
            {
                return new StageResultDto
                {
                    Result = "User rejected changes. Jumping to responder.",
                    IsSuccess = true,
                    NextStageId = responderStageId.Value,
                };
            }

            // ReAct mode: no responder stage ID — the next AgentThinkHandler will finalize
            return StageResult.Success("User rejected changes. Think stage will finalize.");
        }

        // First run — send confirmation request to client
        var pipelineId = context?.PipelineId;
        var stageId = context?.StageId;

        var description = BuildMutationDescription(input.PendingMutations);
        context.LogInfo($"Requesting confirmation [mutations={input.PendingMutations.Count}, details={description}]");
        var notification = new AgentNotification
        {
            Type = "confirmation_required",
            Message = $"I am about to make the following changes:\n{description}\n\nDo you want to proceed?",
            ConfirmationStageId = stageId,
            PipelineId = pipelineId,
            Metadata = new Dictionary<string, object?> { ["pendingMutations"] = input.PendingMutations.Count },
        };

        if (notificationRouter == null)
        {
            StorePendingNotification(context, notification);
            context.LogWarning("Confirmation notification router unavailable. Stored pending notification in context.");
            return StageResult.Error("Unable to deliver confirmation request because the notification router is unavailable.");
        }

        var dispatch = await notificationRouter.NotifyAsync(context, notification);
        if (!dispatch.Delivered)
        {
            StorePendingNotification(context, notification);
            context.LogWarning($"Confirmation delivery failed [{dispatch.FailureReason}]");
            return StageResult.Error($"Unable to deliver confirmation request. {dispatch.FailureReason}");
        }

        // Return waiting state — pipeline pauses here
        context.LogInfo($"Confirmation delivered via channel '{dispatch.Channel}'. Waiting for approval.");
        return new StageResultDto
        {
            Result = "Waiting for user confirmation.",
            IsSuccess = true,
            IsWaitingForApproval = true,
        };
    }

    private static void StorePendingNotification(IStageContext? context, AgentNotification notification)
    {
        if (context == null)
            return;

        context.Payload ??= new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        context.Payload[AgentConstants.PendingNotification] = notification;
    }

    private static string BuildMutationDescription(List<AgentToolCall> mutations) =>
        string.Join("\n", mutations.Select((m, i) =>
        {
            var paramStr = string.Join(", ", m.Params.Select(kv => $"{kv.Key}={kv.Value}"));
            return $"{i + 1}. {m.Tool}({paramStr})";
        }));
}
