using Newtonsoft.Json;

namespace PipelogiqSDK.Contracts;

public class WorkerBootstrapResponse
{
    [JsonProperty("workerId")]
    public string? WorkerId { get; set; }

    [JsonProperty("workerSessionToken")]
    public string? WorkerSessionToken { get; set; }

    [JsonProperty("configVersion")]
    public string? ConfigVersion { get; set; }

    [JsonProperty("application")]
    public WorkerApplicationConfig Application { get; set; } = new();

    [JsonProperty("messageBroker")]
    public WorkerMessageBrokerConfig MessageBroker { get; set; } = new();

    [JsonProperty("queues")]
    public WorkerQueuesConfig Queues { get; set; } = new();

    [JsonProperty("heartbeat")]
    public WorkerHeartbeatConfig Heartbeat { get; set; } = new();

    [JsonProperty("observability")]
    public WorkerObservabilityConfig? Observability { get; set; }
}

public class WorkerApplicationConfig
{
    [JsonProperty("applicationId")]
    public int ApplicationId { get; set; }

    [JsonProperty("applicationName")]
    public string? ApplicationName { get; set; }

    [JsonProperty("appId")]
    public string? AppId { get; set; }
}

public class WorkerMessageBrokerConfig
{
    [JsonProperty("type")]
    public string? Type { get; set; }

    [JsonProperty("connectionString")]
    public string? ConnectionString { get; set; }

    [JsonProperty("prefetch")]
    public int Prefetch { get; set; } = 1;

    [JsonProperty("dlqEnabled")]
    public bool DlqEnabled { get; set; }

    [JsonProperty("dlqTtlSec")]
    public int DlqTtlSec { get; set; }
}

public class WorkerQueuesConfig
{
    [JsonProperty("stageResult")]
    public string? StageResult { get; set; }

    [JsonProperty("stageSetStatus")]
    public string? StageSetStatus { get; set; }

    [JsonProperty("stageUpdatedFanout")]
    public string? StageUpdatedFanout { get; set; }

    [JsonProperty("stageNextPattern")]
    public string? StageNextPattern { get; set; }
}

public class WorkerHeartbeatConfig
{
    [JsonProperty("intervalSec")]
    public int IntervalSec { get; set; } = 15;

    [JsonProperty("offlineAfterSec")]
    public int OfflineAfterSec { get; set; } = 45;
}

public class WorkerObservabilityConfig
{
    [JsonProperty("traceLinkTemplate")]
    public string? TraceLinkTemplate { get; set; }

    [JsonProperty("logsLinkTemplate")]
    public string? LogsLinkTemplate { get; set; }
}
