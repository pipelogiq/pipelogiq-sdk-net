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
