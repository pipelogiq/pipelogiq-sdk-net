using Newtonsoft.Json;

namespace PipelogiqSDK.Contracts;

/// <summary>
/// Worker shutdown payload.
/// </summary>
public class WorkerShutdownRequest
{
    /// <summary>
    /// Gets or sets worker identifier.
    /// </summary>
    [JsonProperty("workerId")]
    public string WorkerId { get; set; } = null!;

    /// <summary>
    /// Gets or sets final worker state.
    /// </summary>
    [JsonProperty("state")]
    public string? State { get; set; }

    /// <summary>
    /// Gets or sets shutdown message.
    /// </summary>
    [JsonProperty("message")]
    public string? Message { get; set; }

    /// <summary>
    /// Gets or sets shutdown metadata.
    /// </summary>
    [JsonProperty("metadata")]
    public Dictionary<string, object?>? Metadata { get; set; }
}
