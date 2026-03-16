using PipelogiqSDK.Abstractions;

namespace PipelogiqSDK.Contracts;

/// <summary>
/// Default implementation of <see cref="IStageResult"/>.
/// </summary>
public class StageResultDto : IStageResult
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
    public int? NextStageId { get; set; }

    /// <inheritdoc />
    public bool RunNextIfCurrentFailed { get; set; }

    /// <inheritdoc />
    public bool IsWaitingForApproval { get; set; }

    /// <inheritdoc />
    public List<StageLogDto>? Logs { get; set; }

    /// <inheritdoc />
    public List<ContextItem>? ContextItems { get; set; }
}
