namespace PipelogiqSDK.Abstractions;

public interface IStageHandler<in TInput> where TInput : class
{
    Task<IStageResult> ExecuteAsync(TInput input, IStageContext? context = null);
}

public interface IStageHandler
{
    Task<IStageResult> ExecuteAsync(IStageContext? context = null);
}

public interface IStageResult
{
    int? PipelineId { get; set; }
    int StageId { get; set; }
    string Result { get; set; }
    bool IsSuccess { get; set; }
    int? NextStageId { get; set; }
    bool RunNextIfCurrentFailed { get; set; }
    List<Contracts.StageLogDto>? Logs { get; set; }
    List<Contracts.ContextItem>? ContextItems { get; set; }
}

public interface IStageContext
{
    int? PipelineId { get; set; }
    int? StageId { get; set; }
    DateTime? CreatedTime { get; set; }
    string? Status { get; }
    Dictionary<string, object>? Payload { get; set; }
}
