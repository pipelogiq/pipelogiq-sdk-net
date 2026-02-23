using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Contracts;
using PipelogiqSDK.StageHelper;

namespace PipelogiqSDK.Execution;

public class StageExecutionData
{
    public object? HandlerInstance { get; set; }
    public Type? HandlerType { get; set; }
    public Type? InputType { get; set; }
    public string? JsonInput { get; set; }
    public int? StageId { get; set; }
    public int? PipelineId { get; set; }
    public List<ContextItem>? ContextItems { get; set; }
    public PipelineLogger? Logger { get; set; }
}
