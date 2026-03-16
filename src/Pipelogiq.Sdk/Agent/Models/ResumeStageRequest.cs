namespace PipelogiqSDK.Agent.Models;

/// <summary>
/// Request to resume a stage waiting for external approval.
/// </summary>
public class ResumeStageRequest
{
    /// <summary>Whether the pending action was approved.</summary>
    public bool Approved { get; set; }

    /// <summary>Optional reason for rejection.</summary>
    public string? RejectionReason { get; set; }
}
