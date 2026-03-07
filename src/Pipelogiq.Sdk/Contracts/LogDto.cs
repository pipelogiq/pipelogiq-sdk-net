namespace PipelogiqSDK.Contracts;

/// <summary>
/// Log payload DTO.
/// </summary>
public class LogDto
{
    /// <summary>
    /// Gets or sets API key.
    /// </summary>
    public string ApiKey { get; set; } = null!;

    /// <summary>
    /// Gets or sets creation timestamp in UTC.
    /// </summary>
    public DateTime? Created { get; set; }

    /// <summary>
    /// Gets or sets log message.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Gets or sets log level.
    /// </summary>
    public string? LogLevel { get; set; }

    /// <summary>
    /// Gets or sets attached keywords.
    /// </summary>
    public List<KeywordDto>? Keywords { get; set; }
}
