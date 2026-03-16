namespace PipelogiqSDK.Agent.Models;

/// <summary>
/// Notification sent to the client during agent execution.
/// </summary>
public class AgentNotification
{
    /// <summary>Notification type: "response", "confirmation_required", "progress".</summary>
    public string Type { get; set; } = null!;

    /// <summary>Human-readable message text.</summary>
    public string Message { get; set; } = null!;

    /// <summary>Stage ID of the confirmation stage (for confirmation_required type).</summary>
    public int? ConfirmationStageId { get; set; }

    /// <summary>Pipeline ID for tracking.</summary>
    public int? PipelineId { get; set; }

    /// <summary>Additional metadata.</summary>
    public Dictionary<string, object?>? Metadata { get; set; }
}
