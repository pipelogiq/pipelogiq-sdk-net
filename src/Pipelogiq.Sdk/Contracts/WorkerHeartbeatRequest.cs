using Newtonsoft.Json;

namespace PipelogiqSDK.Contracts;

public class WorkerHeartbeatRequest
{
    [JsonProperty("workerId")]
    public string WorkerId { get; set; } = null!;

    [JsonProperty("state")]
    public string State { get; set; } = null!;

    [JsonProperty("uptimeSec")]
    public long UptimeSec { get; set; }

    [JsonProperty("brokerConnected")]
    public bool BrokerConnected { get; set; }

    [JsonProperty("inFlightJobs")]
    public long InFlightJobs { get; set; }

    [JsonProperty("jobsProcessed")]
    public long JobsProcessed { get; set; }

    [JsonProperty("jobsFailed")]
    public long JobsFailed { get; set; }

    [JsonProperty("queueLag")]
    public long QueueLag { get; set; }

    [JsonProperty("cpuPercent")]
    public double CpuPercent { get; set; }

    [JsonProperty("memoryMb")]
    public double MemoryMb { get; set; }

    [JsonProperty("lastError")]
    public string? LastError { get; set; }

    [JsonProperty("message")]
    public string? Message { get; set; }

    [JsonProperty("metadata")]
    public Dictionary<string, object?>? Metadata { get; set; }
}
