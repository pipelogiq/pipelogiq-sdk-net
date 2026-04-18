namespace PipelogiqSDK.Abstractions;

/// <summary>
/// Defines a stage handler with typed input.
/// </summary>
/// <typeparam name="TInput">Input payload type.</typeparam>
public interface IStageHandler<in TInput> where TInput : class
{
    /// <summary>
    /// Executes stage logic with typed input.
    /// </summary>
    /// <param name="input">Deserialized stage input.</param>
    /// <param name="context">Stage execution context.</param>
    /// <returns>Stage execution result.</returns>
    Task<IStageResult> ExecuteAsync(TInput input, IStageContext? context = null);
}

/// <summary>
/// Defines a stage handler without typed input.
/// </summary>
public interface IStageHandler
{
    /// <summary>
    /// Executes stage logic.
    /// </summary>
    /// <param name="context">Stage execution context.</param>
    /// <returns>Stage execution result.</returns>
    Task<IStageResult> ExecuteAsync(IStageContext? context = null);
}

/// <summary>
/// Represents the outcome of a stage execution.
/// </summary>
public interface IStageResult
{
    /// <summary>
    /// Gets or sets the pipeline identifier.
    /// </summary>
    int? PipelineId { get; set; }

    /// <summary>
    /// Gets or sets the stage identifier.
    /// </summary>
    int StageId { get; set; }

    /// <summary>
    /// Gets or sets the stage result message.
    /// </summary>
    string Result { get; set; }

    /// <summary>
    /// Gets or sets whether execution succeeded.
    /// </summary>
    bool IsSuccess { get; set; }

    /// <summary>
    /// Gets or sets an optional error code reported on failure (e.g. "RATE_LIMIT_EXCEEDED",
    /// "TIMEOUT", "UPSTREAM_ERROR"). Used by retry policies to decide whether to retry.
    /// </summary>
    string? ErrorCode { get; set; }

    /// <summary>
    /// Gets or sets the next stage identifier.
    /// </summary>
    int? NextStageId { get; set; }

    /// <summary>
    /// Gets or sets whether next stage should run when current stage fails.
    /// </summary>
    bool RunNextIfCurrentFailed { get; set; }

    /// <summary>
    /// Gets or sets whether this stage is waiting for external approval before continuing.
    /// When true, the pipeline pauses at this stage until resumed via ResumeStageApprovalAsync.
    /// </summary>
    bool IsWaitingForApproval { get; set; }

    /// <summary>
    /// Gets or sets logs produced by the stage.
    /// </summary>
    List<Contracts.StageLogDto>? Logs { get; set; }

    /// <summary>
    /// Gets or sets context items produced by the stage.
    /// </summary>
    List<Contracts.ContextItem>? ContextItems { get; set; }

    /// <summary>
    /// Gets or sets follow-up stages that should be appended to the pipeline
    /// transactionally together with this stage result.
    /// </summary>
    List<Contracts.StageInfo>? AppendedStages { get; set; }
}

/// <summary>
/// Represents mutable stage execution context.
/// </summary>
public interface IStageContext
{
    /// <summary>
    /// Gets or sets the pipeline identifier.
    /// </summary>
    int? PipelineId { get; set; }

    /// <summary>
    /// Gets or sets the stage identifier.
    /// </summary>
    int? StageId { get; set; }

    /// <summary>
    /// Gets or sets the context creation time in UTC.
    /// </summary>
    DateTime? CreatedTime { get; set; }

    /// <summary>
    /// Gets stage status when available.
    /// </summary>
    string? Status { get; }

    /// <summary>
    /// Gets or sets arbitrary context payload values.
    /// </summary>
    Dictionary<string, object>? Payload { get; set; }
}
