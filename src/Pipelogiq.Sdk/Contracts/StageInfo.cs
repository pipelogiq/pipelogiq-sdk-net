namespace PipelogiqSDK.Contracts;

/// <summary>
/// Stage definition used in pipeline payload.
/// </summary>
public class StageInfo
{
    /// <summary>
    /// Gets or sets stage identifier.
    /// </summary>
    public int? StageId { get; set; }

    /// <summary>
    /// Gets or sets pipeline identifier.
    /// </summary>
    public int? PipelineId { get; set; }

    /// <summary>
    /// Gets or sets stage name.
    /// </summary>
    public string? StageName { get; set; }

    /// <summary>
    /// Gets or sets registered handler name.
    /// </summary>
    public string? StageHandlerName { get; set; }

    /// <summary>
    /// Gets or sets serialized input payload.
    /// </summary>
    public object? Input { get; set; }

    /// <summary>
    /// Gets or sets stage options.
    /// </summary>
    public StageOptions? Options { get; set; }

    /// <summary>
    /// Gets or sets whether stage is an event stage.
    /// </summary>
    public bool? IsEvent { get; set; }
}
