using Newtonsoft.Json;

namespace PipelogiqSDK.Contracts;

public class WorkerBootstrapRequest
{
    [JsonProperty("workerName")]
    public string WorkerName { get; set; } = null!;

    [JsonProperty("instanceId")]
    public string? InstanceId { get; set; }

    [JsonProperty("workerVersion")]
    public string? WorkerVersion { get; set; }

    [JsonProperty("sdkVersion")]
    public string? SdkVersion { get; set; }

    [JsonProperty("environment")]
    public string? Environment { get; set; }

    [JsonProperty("hostName")]
    public string? HostName { get; set; }

    [JsonProperty("pid")]
    public int Pid { get; set; }

    [JsonProperty("supportedHandlers")]
    public List<string> SupportedHandlers { get; set; } = new();

    [JsonProperty("capabilities")]
    public Dictionary<string, bool>? Capabilities { get; set; }

    [JsonProperty("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }
}
