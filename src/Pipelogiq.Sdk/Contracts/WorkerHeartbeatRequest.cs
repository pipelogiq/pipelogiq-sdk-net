using Newtonsoft.Json;

namespace PipelogiqSDK.Contracts;

/// <summary>
/// Worker heartbeat payload.
/// </summary>
public class WorkerHeartbeatRequest
{
    /// <summary>
    /// Gets or sets worker identifier.
    /// </summary>
    [JsonProperty("workerId")]
    public string WorkerId { get; set; } = null!;

    /// <summary>
    /// Gets or sets worker state.
    /// </summary>
    [JsonProperty("state")]
    public string State { get; set; } = null!;

    /// <summary>
    /// Gets or sets uptime in seconds.
    /// </summary>
    [JsonProperty("uptimeSec")]
    public long UptimeSec { get; set; }

    /// <summary>
    /// Gets or sets whether broker connection is active.
    /// </summary>
    [JsonProperty("brokerConnected")]
    public bool BrokerConnected { get; set; }

    /// <summary>
    /// Gets or sets in-flight jobs count.
    /// </summary>
    [JsonProperty("inFlightJobs")]
    public long InFlightJobs { get; set; }

    /// <summary>
    /// Gets or sets processed jobs count.
    /// </summary>
    [JsonProperty("jobsProcessed")]
    public long JobsProcessed { get; set; }

    /// <summary>
    /// Gets or sets failed jobs count.
    /// </summary>
    [JsonProperty("jobsFailed")]
    public long JobsFailed { get; set; }

    /// <summary>
    /// Gets or sets current queue lag.
    /// </summary>
    [JsonProperty("queueLag")]
    public long QueueLag { get; set; }

    /// <summary>
    /// Gets or sets CPU usage percentage.
    /// </summary>
    [JsonProperty("cpuPercent")]
    public double CpuPercent { get; set; }

    /// <summary>
    /// Gets or sets memory usage in megabytes.
    /// </summary>
    [JsonProperty("memoryMb")]
    public double MemoryMb { get; set; }

    /// <summary>
    /// Gets or sets last error text.
    /// </summary>
    [JsonProperty("lastError")]
    public string? LastError { get; set; }

    /// <summary>
    /// Gets or sets status message.
    /// </summary>
    [JsonProperty("message")]
    public string? Message { get; set; }

    /// <summary>
    /// Gets or sets heartbeat metadata.
    /// </summary>
    [JsonProperty("metadata")]
    public Dictionary<string, object?>? Metadata { get; set; }
}
