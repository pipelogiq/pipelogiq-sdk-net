using PipelogiqSDK.Abstractions;
using PipelogiqSDK.StageHelper;

namespace PipelogiqSDK.Contracts;

public class StageContext : IStageContext
{
    public int? PipelineId { get; set; }
    public int? StageId { get; set; }
    public DateTime? CreatedTime { get; set; }
    public string? Status { get; init; }
    public Dictionary<string, object>? Payload { get; set; }
    public PipelineLogger? Logger { get; set; }
}
