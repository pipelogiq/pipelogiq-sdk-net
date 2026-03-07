namespace PipelogiqSDK.Contracts;

/// <summary>
/// Pipeline payload DTO.
/// </summary>
public class PipelineDto
{
    /// <summary>
    /// Gets or sets pipeline identifier.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets API key.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets pipeline name.
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Gets or sets creation timestamp in UTC.
    /// </summary>
    public DateTime? CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets stages.
    /// </summary>
    public List<StageInfo>? Stages { get; set; }

    /// <summary>
    /// Gets or sets pipeline keywords.
    /// </summary>
    public List<KeywordDto>? PipelineKeywords { get; set; }

    /// <summary>
    /// Gets or sets pipeline context items.
    /// </summary>
    public List<ContextItem>? PipelineContextItems { get; set; }
}
