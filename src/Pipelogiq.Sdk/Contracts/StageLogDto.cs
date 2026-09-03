namespace PipelogiqSDK.Contracts;

/// <summary>
/// Stage log entry DTO.
/// </summary>
public class StageLogDto
{
    /// <summary>Gets or sets log entry identifier.</summary>
    public int? Id { get; set; }

    /// <summary>Gets or sets owning stage identifier.</summary>
    public int? StageId { get; set; }

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
}
