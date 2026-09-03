namespace PipelogiqSDK.Contracts;

/// <summary>
/// Response envelope returned by the fail-safe idempotent pipeline creation endpoint.
/// </summary>
public sealed class IdempotentPipelineCreateResponse
{
    /// <summary>The created or previously existing pipeline.</summary>
    public PipelineResponse Pipeline { get; set; } = null!;

    /// <summary>Whether this request created the pipeline.</summary>
    public bool Created { get; set; }

    /// <summary>Whether this request returned a previously existing pipeline.</summary>
    public bool WasExisting { get; set; }
}
