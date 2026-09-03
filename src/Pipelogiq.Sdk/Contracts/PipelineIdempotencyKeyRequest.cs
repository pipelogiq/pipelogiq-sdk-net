namespace PipelogiqSDK.Contracts;

/// <summary>
/// Request used to look up a pipeline without placing its idempotency key in the URL.
/// </summary>
public sealed class PipelineIdempotencyKeyRequest
{
    /// <summary>Client-provided pipeline idempotency key.</summary>
    [Newtonsoft.Json.JsonProperty("idempotencyKey")]
    public string IdempotencyKey { get; set; } = null!;
}
