namespace PipelogiqSDK.Contracts;

/// <summary>
/// Pipeline response returned by the API after creating a pipeline.
/// </summary>
public class PipelineResponse
{
    /// <summary>Pipeline identifier.</summary>
    public int Id { get; set; }

    /// <summary>Pipeline name.</summary>
    public string Name { get; set; } = null!;

    /// <summary>W3C trace ID (32-char hex) associated with this pipeline.</summary>
    public string? TraceId { get; set; }

    /// <summary>Pipeline status.</summary>
    public string Status { get; set; } = null!;

    /// <summary>Pipeline creation time (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Pipeline completion time (UTC), if finished.</summary>
    public DateTime? FinishedAt { get; set; }

    /// <summary>Application identifier.</summary>
    public int? ApplicationId { get; set; }

    /// <summary>Ordered list of stage statuses.</summary>
    public List<string>? StageStatuses { get; set; }

    /// <summary>Pipeline stages with full detail.</summary>
    public List<StageDto>? Stages { get; set; }

    /// <summary>Pipeline context items.</summary>
    public List<ContextItem>? PipelineContextItems { get; set; }

    /// <summary>Pipeline keywords.</summary>
    public List<KeywordDto>? PipelineKeywords { get; set; }

    /// <summary>Whether this pipeline is an event pipeline.</summary>
    public bool? IsEvent { get; set; }
}
