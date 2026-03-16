using PipelogiqSDK.Agent.Models;

namespace PipelogiqSDK.Agent.Services;

/// <summary>
/// Legacy catch-all notifier abstraction.
/// Prefer channel-specific transports via <see cref="IAgentNotificationChannel"/> for new integrations.
/// </summary>
public interface IAgentNotifier
{
    /// <summary>
    /// Sends a notification to the specified session.
    /// </summary>
    /// <param name="sessionId">Client session identifier.</param>
    /// <param name="notification">Notification payload.</param>
    /// <param name="ct">Cancellation token.</param>
    Task NotifyAsync(string sessionId, AgentNotification notification, CancellationToken ct = default);
}
