namespace PipelogiqSDK.Contracts;

public class PipelineDto
{
    public int Id { get; set; }
    public string? ApiKey { get; set; }
    public string Name { get; set; } = null!;
    public DateTime? CreatedAt { get; set; }
    public List<StageInfo>? Stages { get; set; }
    public List<KeywordDto>? PipelineKeywords { get; set; }
    public List<ContextItem>? PipelineContextItems { get; set; }
}
