using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Contracts;
using PipelogiqSDK.StageHelper;

namespace PipelogiqSDK.Execution;

/// <summary>
/// Input data used by <see cref="StageExecutor"/> to execute a stage handler.
/// </summary>
public class StageExecutionData
{
    /// <summary>
    /// Gets or sets explicit handler instance.
    /// </summary>
    public object? HandlerInstance { get; set; }

    /// <summary>
    /// Gets or sets handler type for DI resolution.
    /// </summary>
    public Type? HandlerType { get; set; }

    /// <summary>
    /// Gets or sets expected input type.
    /// </summary>
    public Type? InputType { get; set; }

    /// <summary>
    /// Gets or sets serialized JSON input payload.
    /// </summary>
    public string? JsonInput { get; set; }

    /// <summary>
    /// Gets or sets stage identifier.
    /// </summary>
    public int? StageId { get; set; }

    /// <summary>
    /// Gets or sets pipeline identifier.
    /// </summary>
    public int? PipelineId { get; set; }

    /// <summary>Gets or sets stable execution identifier.</summary>
    public string? ExecutionId { get; set; }

    /// <summary>Gets or sets one-based execution attempt number.</summary>
    public int? Attempt { get; set; }

    /// <summary>Gets or sets associated pipeline idempotency key.</summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>Gets or sets effective timeout in seconds.</summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>Gets or sets handler execution cancellation token.</summary>
    public CancellationToken CancellationToken { get; set; }

    /// <summary>Gets or sets W3C traceparent.</summary>
    public string? Traceparent { get; set; }

    /// <summary>Gets or sets W3C tracestate.</summary>
    public string? Tracestate { get; set; }

    /// <summary>Gets or sets trace identifier.</summary>
    public string? TraceId { get; set; }

    /// <summary>Gets or sets span identifier.</summary>
    public string? SpanId { get; set; }

    /// <summary>
    /// Gets or sets incoming context items.
    /// </summary>
    public List<ContextItem>? ContextItems { get; set; }

    /// <summary>
    /// Gets or sets stage logger.
    /// </summary>
    public PipelineLogger? Logger { get; set; }
}
