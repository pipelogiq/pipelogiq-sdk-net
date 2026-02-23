namespace PipelogiqSDK.Contracts;

public class StageInfo
{
    public int? StageId { get; set; }
    public int? PipelineId { get; set; }
    public string? StageName { get; set; }
    public string? StageHandlerName { get; set; }
    public object? Input { get; set; }
    public StageOptions? Options { get; set; }
    public bool? IsEvent { get; set; }
}
