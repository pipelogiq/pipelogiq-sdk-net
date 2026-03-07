namespace PipelogiqSDK.Contracts;

/// <summary>
/// RabbitMQ connection response DTO.
/// </summary>
public class RabbitConnectionResponse
{
    /// <summary>
    /// Gets or sets RabbitMQ connection string.
    /// </summary>
    public string? ConnectionString { get; set; }
}
