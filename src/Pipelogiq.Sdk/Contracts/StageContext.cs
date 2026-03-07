using PipelogiqSDK.Abstractions;
using PipelogiqSDK.StageHelper;

namespace PipelogiqSDK.Contracts;

/// <summary>
/// Default implementation of <see cref="IStageContext"/>.
/// </summary>
public class StageContext : IStageContext
{
    /// <inheritdoc />
    public int? PipelineId { get; set; }

    /// <inheritdoc />
    public int? StageId { get; set; }

    /// <inheritdoc />
    public DateTime? CreatedTime { get; set; }

    /// <inheritdoc />
    public string? Status { get; init; }

    /// <inheritdoc />
    public Dictionary<string, object>? Payload { get; set; }

    /// <summary>
    /// Gets or sets stage logger used during execution.
    /// </summary>
    public PipelineLogger? Logger { get; set; }
}
