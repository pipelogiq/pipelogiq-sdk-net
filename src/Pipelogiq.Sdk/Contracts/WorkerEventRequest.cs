using Newtonsoft.Json;

namespace PipelogiqSDK.Contracts;

/// <summary>
/// Worker event batch payload.
/// </summary>
public class WorkerEventRequest
{
    /// <summary>
    /// Gets or sets worker identifier.
    /// </summary>
    [JsonProperty("workerId")]
    public string WorkerId { get; set; } = null!;

    /// <summary>
    /// Gets or sets worker events.
    /// </summary>
    [JsonProperty("events")]
    public List<WorkerEventItem> Events { get; set; } = [];
}

/// <summary>
/// Worker diagnostic event entry.
/// </summary>
public class WorkerEventItem
{
    /// <summary>
    /// Gets or sets event timestamp in UTC.
    /// </summary>
    [JsonProperty("ts")]
    public DateTime? Timestamp { get; set; }

    /// <summary>
    /// Gets or sets event log level.
    /// </summary>
    [JsonProperty("level")]
    public string Level { get; set; } = "INFO";

    /// <summary>
    /// Gets or sets event type.
    /// </summary>
    [JsonProperty("eventType")]
    public string EventType { get; set; } = null!;

    /// <summary>
    /// Gets or sets event message.
    /// </summary>
    [JsonProperty("message")]
    public string Message { get; set; } = null!;

    /// <summary>
    /// Gets or sets event details.
    /// </summary>
    [JsonProperty("details")]
    public Dictionary<string, object?>? Details { get; set; }
}
