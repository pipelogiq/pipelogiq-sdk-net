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

    /// <summary>
    /// Gets or sets incoming context items.
    /// </summary>
    public List<ContextItem>? ContextItems { get; set; }

    /// <summary>
    /// Gets or sets stage logger.
    /// </summary>
    public PipelineLogger? Logger { get; set; }
}
