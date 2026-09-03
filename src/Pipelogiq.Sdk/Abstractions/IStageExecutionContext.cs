namespace PipelogiqSDK.Abstractions;

/// <summary>
/// Optional extension of <see cref="IStageContext"/> with technical execution metadata.
/// Handlers that only depend on <see cref="IStageContext"/> remain source and binary compatible.
/// </summary>
public interface IStageExecutionContext : IStageContext
{
    /// <summary>Stable identifier for the current stage execution delivery.</summary>
    string? ExecutionId { get; }

    /// <summary>One-based stage execution attempt number.</summary>
    int? Attempt { get; }

    /// <summary>Pipeline idempotency key associated with the execution.</summary>
    string? IdempotencyKey { get; }

    /// <summary>Effective stage timeout in seconds.</summary>
    int? TimeoutSeconds { get; }

    /// <summary>Cancellation token associated with handler execution.</summary>
    CancellationToken CancellationToken { get; }

    /// <summary>W3C traceparent associated with the execution.</summary>
    string? Traceparent { get; }

    /// <summary>W3C tracestate associated with the execution.</summary>
    string? Tracestate { get; }

    /// <summary>Trace identifier associated with the execution.</summary>
    string? TraceId { get; }

    /// <summary>Span identifier associated with the execution.</summary>
    string? SpanId { get; }
}
