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
}
