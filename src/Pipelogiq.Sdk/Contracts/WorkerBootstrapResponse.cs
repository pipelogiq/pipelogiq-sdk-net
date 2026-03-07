using Newtonsoft.Json;

namespace PipelogiqSDK.Contracts;

/// <summary>
/// Worker bootstrap response payload.
/// </summary>
public class WorkerBootstrapResponse
{
    /// <summary>
    /// Gets or sets worker identifier.
    /// </summary>
    [JsonProperty("workerId")]
    public string? WorkerId { get; set; }

    /// <summary>
    /// Gets or sets worker session token.
    /// </summary>
    [JsonProperty("workerSessionToken")]
    public string? WorkerSessionToken { get; set; }

    /// <summary>
    /// Gets or sets server config version.
    /// </summary>
    [JsonProperty("configVersion")]
    public string? ConfigVersion { get; set; }

    /// <summary>
    /// Gets or sets application configuration.
    /// </summary>
    [JsonProperty("application")]
    public WorkerApplicationConfig Application { get; set; } = new();

    /// <summary>
    /// Gets or sets message broker configuration.
    /// </summary>
    [JsonProperty("messageBroker")]
    public WorkerMessageBrokerConfig MessageBroker { get; set; } = new();

    /// <summary>
    /// Gets or sets queue configuration.
    /// </summary>
    [JsonProperty("queues")]
    public WorkerQueuesConfig Queues { get; set; } = new();

    /// <summary>
    /// Gets or sets heartbeat configuration.
    /// </summary>
    [JsonProperty("heartbeat")]
    public WorkerHeartbeatConfig Heartbeat { get; set; } = new();

    /// <summary>
    /// Gets or sets observability configuration.
    /// </summary>
    [JsonProperty("observability")]
    public WorkerObservabilityConfig? Observability { get; set; }
}

/// <summary>
/// Application-specific worker bootstrap settings.
/// </summary>
public class WorkerApplicationConfig
{
    /// <summary>
    /// Gets or sets numeric application identifier.
    /// </summary>
    [JsonProperty("applicationId")]
    public int ApplicationId { get; set; }

    /// <summary>
    /// Gets or sets application name.
    /// </summary>
    [JsonProperty("applicationName")]
    public string? ApplicationName { get; set; }

    /// <summary>
    /// Gets or sets app identifier used in queue naming.
    /// </summary>
    [JsonProperty("appId")]
    public string? AppId { get; set; }
}

/// <summary>
/// Message broker settings for worker runtime.
/// </summary>
public class WorkerMessageBrokerConfig
{
    /// <summary>
    /// Gets or sets broker type.
    /// </summary>
    [JsonProperty("type")]
    public string? Type { get; set; }

    /// <summary>
    /// Gets or sets broker connection string.
    /// </summary>
    [JsonProperty("connectionString")]
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets consumer prefetch count.
    /// </summary>
    [JsonProperty("prefetch")]
    public int Prefetch { get; set; } = 1;

    /// <summary>
    /// Gets or sets whether DLQ mode is enabled.
    /// </summary>
    [JsonProperty("dlqEnabled")]
    public bool DlqEnabled { get; set; }

    /// <summary>
    /// Gets or sets DLQ TTL in seconds.
    /// </summary>
    [JsonProperty("dlqTtlSec")]
    public int DlqTtlSec { get; set; }
}

/// <summary>
/// Queue names and patterns provided by control plane.
/// </summary>
public class WorkerQueuesConfig
{
    /// <summary>
    /// Gets or sets stage result queue name.
    /// </summary>
    [JsonProperty("stageResult")]
    public string? StageResult { get; set; }

    /// <summary>
    /// Gets or sets stage status queue name.
    /// </summary>
    [JsonProperty("stageSetStatus")]
    public string? StageSetStatus { get; set; }

    /// <summary>
    /// Gets or sets fanout exchange for stage updates.
    /// </summary>
    [JsonProperty("stageUpdatedFanout")]
    public string? StageUpdatedFanout { get; set; }

    /// <summary>
    /// Gets or sets StageNext queue naming pattern.
    /// </summary>
    [JsonProperty("stageNextPattern")]
    public string? StageNextPattern { get; set; }
}

/// <summary>
/// Heartbeat timing settings.
/// </summary>
public class WorkerHeartbeatConfig
{
    /// <summary>
    /// Gets or sets heartbeat interval in seconds.
    /// </summary>
    [JsonProperty("intervalSec")]
    public int IntervalSec { get; set; } = 15;

    /// <summary>
    /// Gets or sets offline threshold in seconds.
    /// </summary>
    [JsonProperty("offlineAfterSec")]
    public int OfflineAfterSec { get; set; } = 45;
}

/// <summary>
/// Observability links configuration.
/// </summary>
public class WorkerObservabilityConfig
{
    /// <summary>
    /// Gets or sets trace link template.
    /// </summary>
    [JsonProperty("traceLinkTemplate")]
    public string? TraceLinkTemplate { get; set; }

    /// <summary>
    /// Gets or sets logs link template.
    /// </summary>
    [JsonProperty("logsLinkTemplate")]
    public string? LogsLinkTemplate { get; set; }
}
