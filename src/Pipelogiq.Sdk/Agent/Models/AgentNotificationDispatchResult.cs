namespace PipelogiqSDK.Agent.Models;

/// <summary>
/// Result of attempting to dispatch an agent notification through a reply channel.
/// </summary>
public class AgentNotificationDispatchResult
{
    /// <summary>
    /// Whether the notification was delivered to a channel.
    /// </summary>
    public bool Delivered { get; set; }

    /// <summary>
    /// Channel name used for delivery when available.
    /// </summary>
    public string? Channel { get; set; }

    /// <summary>
    /// Failure reason when the notification could not be delivered.
    /// </summary>
    public string? FailureReason { get; set; }
}
