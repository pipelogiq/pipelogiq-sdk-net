using PipelogiqSDK.Abstractions;

namespace PipelogiqSDK.Contracts;

/// <summary>
/// Default implementation of <see cref="IStageResult"/>.
/// </summary>
public class StageResultDto : IStageResult, IClassifiedStageResult
{
    /// <inheritdoc />
    public int? PipelineId { get; set; }

    /// <inheritdoc />
    public int StageId { get; set; }

    /// <inheritdoc />
    public string Result { get; set; } = string.Empty;

    /// <inheritdoc />
    public bool IsSuccess { get; set; }

    /// <inheritdoc />
    public string? ErrorCode { get; set; }

    /// <inheritdoc />
    [Newtonsoft.Json.JsonProperty("retryable")]
    public bool? Retryable { get; set; }

    /// <inheritdoc />
    public int? NextStageId { get; set; }

    /// <inheritdoc />
    public bool RunNextIfCurrentFailed { get; set; }

    /// <inheritdoc />
    public bool IsWaitingForApproval { get; set; }

    /// <inheritdoc />
    public List<StageLogDto>? Logs { get; set; }

    /// <inheritdoc />
    public List<ContextItem>? ContextItems { get; set; }

    /// <inheritdoc />
    public List<StageInfo>? AppendedStages { get; set; }

    /// <summary>Stable identifier for this stage execution delivery.</summary>
    public string? ExecutionId { get; set; }

    /// <summary>One-based stage execution attempt number.</summary>
    public int? Attempt { get; set; }

    /// <summary>Pipeline idempotency key associated with this execution, when available.</summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>Effective execution timeout in seconds, when available.</summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>W3C traceparent associated with this execution.</summary>
    public string? Traceparent { get; set; }

    /// <summary>W3C tracestate associated with this execution.</summary>
    public string? Tracestate { get; set; }

    /// <summary>Trace identifier associated with this execution.</summary>
    public string? TraceId { get; set; }

    /// <summary>Span identifier associated with this execution.</summary>
    public string? SpanId { get; set; }
}
