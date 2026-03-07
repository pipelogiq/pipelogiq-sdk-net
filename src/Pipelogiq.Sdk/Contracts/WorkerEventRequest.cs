using Newtonsoft.Json;

namespace PipelogiqSDK.Contracts;

/// <summary>
/// Worker event payload.
/// </summary>
public class WorkerEventRequest
{
    /// <summary>
    /// Gets or sets worker identifier.
    /// </summary>
    [JsonProperty("workerId")]
    public string WorkerId { get; set; } = null!;

    /// <summary>
    /// Gets or sets event type.
    /// </summary>
    [JsonProperty("type")]
    public string Type { get; set; } = null!;

    /// <summary>
    /// Gets or sets event message.
    /// </summary>
    [JsonProperty("message")]
    public string? Message { get; set; }

    /// <summary>
    /// Gets or sets event metadata.
    /// </summary>
    [JsonProperty("metadata")]
    public Dictionary<string, object?>? Metadata { get; set; }
}
