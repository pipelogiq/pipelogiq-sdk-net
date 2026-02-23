using PipelogiqSDK.Abstractions;

namespace PipelogiqSDK.Contracts;

public class StageResultDto : IStageResult
{
    public int? PipelineId { get; set; }
    public int StageId { get; set; }
    public string Result { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public int? NextStageId { get; set; }
    public bool RunNextIfCurrentFailed { get; set; }
    public List<StageLogDto>? Logs { get; set; }
    public List<ContextItem>? ContextItems { get; set; }
}
