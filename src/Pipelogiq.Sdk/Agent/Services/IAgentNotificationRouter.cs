using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Agent.Models;

namespace PipelogiqSDK.Agent.Services;

/// <summary>
/// Resolves the destination channel for agent notifications from stage context.
/// </summary>
public interface IAgentNotificationRouter
{
    /// <summary>
    /// Attempts to deliver the notification using the reply route stored in stage context.
    /// </summary>
    Task<AgentNotificationDispatchResult> NotifyAsync(
        IStageContext? context,
        AgentNotification notification,
        CancellationToken ct = default);
}
