using Newtonsoft.Json;

namespace PipelogiqSDK.Contracts;

/// <summary>
/// Worker bootstrap request payload.
/// </summary>
public class WorkerBootstrapRequest
{
    /// <summary>
    /// Gets or sets worker name.
    /// </summary>
    [JsonProperty("workerName")]
    public string WorkerName { get; set; } = null!;

    /// <summary>
    /// Gets or sets worker instance identifier.
    /// </summary>
    [JsonProperty("instanceId")]
    public string? InstanceId { get; set; }

    /// <summary>
    /// Gets or sets worker version.
    /// </summary>
    [JsonProperty("workerVersion")]
    public string? WorkerVersion { get; set; }

    /// <summary>
    /// Gets or sets SDK version.
    /// </summary>
    [JsonProperty("sdkVersion")]
    public string? SdkVersion { get; set; }

    /// <summary>
    /// Gets or sets environment name.
    /// </summary>
    [JsonProperty("environment")]
    public string? Environment { get; set; }

    /// <summary>
    /// Gets or sets host name.
    /// </summary>
    [JsonProperty("hostName")]
    public string? HostName { get; set; }

    /// <summary>
    /// Gets or sets worker process identifier.
    /// </summary>
    [JsonProperty("pid")]
    public int Pid { get; set; }

    /// <summary>
    /// Gets or sets supported handler names.
    /// </summary>
    [JsonProperty("supportedHandlers")]
    public List<string> SupportedHandlers { get; set; } = new();

    /// <summary>
    /// Gets or sets capability flags.
    /// </summary>
    [JsonProperty("capabilities")]
    public Dictionary<string, bool>? Capabilities { get; set; }

    /// <summary>
    /// Gets or sets custom worker metadata.
    /// </summary>
    [JsonProperty("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }
}
