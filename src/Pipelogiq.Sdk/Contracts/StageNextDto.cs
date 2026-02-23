namespace PipelogiqSDK.Contracts;

public class StageNextDto
{
    public int StageId { get; set; }
    public int? PipelineId { get; set; }
    public string? StageHandlerName { get; set; }
    public string? Input { get; set; }
    public List<ContextItem>? ContextItems { get; set; }
}
