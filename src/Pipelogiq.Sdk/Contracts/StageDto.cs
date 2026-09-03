namespace PipelogiqSDK.Contracts;

/// <summary>
/// Stage metadata DTO.
/// </summary>
public class StageDto
{
    /// <summary>Stage identifier.</summary>
    public int Id { get; set; }

    /// <summary>Pipeline identifier.</summary>
    public int PipelineId { get; set; }

    /// <summary>OTel span ID (16-char hex).</summary>
    public string? SpanId { get; set; }

    /// <summary>Stage name.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Stage handler name (used for routing to worker queue).</summary>
    public string? StageHandlerName { get; set; }

    /// <summary>Stage description.</summary>
    public string? Description { get; set; }

    /// <summary>Stage status.</summary>
    public string? Status { get; set; }

    /// <summary>Stage creation time (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Stage start time (UTC).</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>Stage completion time (UTC).</summary>
    public DateTime? FinishedAt { get; set; }

    /// <summary>Time at which a delayed retry becomes eligible, if one is scheduled.</summary>
    public DateTime? NextRetryAt { get; set; }

    /// <summary>Stage input payload (JSON).</summary>
    public string? Input { get; set; }

    /// <summary>Stage output (result from handler).</summary>
    public string? Output { get; set; }

    /// <summary>Next stage identifier.</summary>
    public int? NextStageId { get; set; }

    /// <summary>Whether the stage was skipped.</summary>
    public bool? IsSkipped { get; set; }

    /// <summary>Whether this stage is an event stage.</summary>
    public bool? IsEvent { get; set; }

    /// <summary>Whether next stage runs if this stage fails.</summary>
    public bool RunNextIfCurrentFailed { get; set; }

    /// <summary>Total number of execution attempts recorded for the stage.</summary>
    [Newtonsoft.Json.JsonProperty("attempt")]
    public int? Attempts { get; set; }

    /// <summary>Number of automatic retries already scheduled for the stage.</summary>
    public int? RetryAttempt { get; set; }

    /// <summary>Error code reported by the most recent failed attempt.</summary>
    public string? LastErrorCode { get; set; }

    /// <summary>Server classification of the latest failure: retryable or terminal.</summary>
    public string? FailureDisposition { get; set; }

    /// <summary>Whether the server considers this stage terminal.</summary>
    public bool? IsTerminal { get; set; }

    /// <summary>Effective stage execution options when returned by the server.</summary>
    public StageOptions? Options { get; set; }

    /// <summary>Stage log entries when requested by the status endpoint.</summary>
    public List<StageLogDto>? Logs { get; set; }

    /// <summary>Number of historical terminal failures retained by the server.</summary>
    public int FailureCount { get; set; }

    /// <summary>Timestamp of the most recent historical terminal failure.</summary>
    public DateTime? LastFailedAt { get; set; }

    /// <summary>Whether the stage has any retained terminal failure history.</summary>
    public bool HasFailureHistory { get; set; }

    /// <summary>Current or most recently recorded stage execution identifier.</summary>
    public string? ExecutionId { get; set; }
}
