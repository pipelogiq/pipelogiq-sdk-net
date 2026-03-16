using PipelogiqSDK.Agent.Models;

namespace PipelogiqSDK.Agent.Services;

/// <summary>
/// Channel-specific delivery adapter used by the agent notification router.
/// </summary>
public interface IAgentNotificationChannel
{
    /// <summary>
    /// Human-readable channel name used for diagnostics.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Returns true when this channel can deliver to the specified reply target.
    /// </summary>
    bool CanHandle(AgentReplyTarget replyTarget);

    /// <summary>
    /// Delivers the notification to the specified reply target.
    /// </summary>
    Task NotifyAsync(AgentReplyTarget replyTarget, AgentNotification notification, CancellationToken ct = default);
}
