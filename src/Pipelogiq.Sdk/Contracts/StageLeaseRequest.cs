namespace PipelogiqSDK.Contracts;

/// <summary>
/// Identifies the worker claiming or renewing one dispatched stage execution.
/// </summary>
public sealed class StageLeaseRequest
{
    /// <summary>Stable execution identifier from <see cref="StageNextDto"/>.</summary>
    public string ExecutionId { get; set; } = string.Empty;

    /// <summary>Worker identifier returned by bootstrap.</summary>
    public string WorkerId { get; set; } = string.Empty;
}
