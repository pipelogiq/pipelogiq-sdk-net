namespace PipelogiqSDK.Contracts;

/// <summary>
/// Result of a stage execution lease operation.
/// </summary>
public sealed class StageLeaseResponse
{
    /// <summary>Whether this worker owns the current execution lease.</summary>
    public bool Acquired { get; set; }

    /// <summary>One-based execution attempt number.</summary>
    public int? Attempt { get; set; }

    /// <summary>UTC lease expiry returned by the control plane.</summary>
    public DateTimeOffset? LeaseExpiresAt { get; set; }

    /// <summary>Machine-readable denial reason.</summary>
    public string? Reason { get; set; }
}
