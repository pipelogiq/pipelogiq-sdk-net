namespace PipelogiqSDK.Contracts;

public class StageDto
{
    public int Id { get; set; }
    public int PipelineId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public int? NextStageId { get; set; }
    public string? FileName { get; set; }
    public bool RunNextIfCurrentFailed { get; set; }
}
