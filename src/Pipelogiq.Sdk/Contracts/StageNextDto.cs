namespace PipelogiqSDK.Contracts;

/// <summary>
/// Stage dispatch payload consumed by worker.
/// </summary>
public class StageNextDto
{
    /// <summary>
    /// Gets or sets stage identifier.
    /// </summary>
    public int StageId { get; set; }

    /// <summary>
    /// Gets or sets pipeline identifier.
    /// </summary>
    public int? PipelineId { get; set; }

    /// <summary>Stable identifier for this stage execution delivery.</summary>
    public string? ExecutionId { get; set; }

    /// <summary>One-based stage execution attempt number.</summary>
    public int? Attempt { get; set; }

    /// <summary>Pipeline idempotency key associated with this execution.</summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>Effective stage timeout in seconds.</summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>W3C trace identifier associated with this execution.</summary>
    public string? TraceId { get; set; }

    /// <summary>W3C span identifier associated with this execution.</summary>
    public string? SpanId { get; set; }

    /// <summary>W3C traceparent associated with this execution.</summary>
    public string? Traceparent { get; set; }

    /// <summary>W3C tracestate associated with this execution.</summary>
    public string? Tracestate { get; set; }

    /// <summary>
    /// Gets or sets stage handler name.
    /// </summary>
    public string? StageHandlerName { get; set; }

    /// <summary>
    /// Gets or sets serialized input payload.
    /// </summary>
    public string? Input { get; set; }

    /// <summary>
    /// Gets or sets context items for handler execution.
    /// </summary>
    public List<ContextItem>? ContextItems { get; set; }
}
