using Newtonsoft.Json;

namespace PipelogiqSDK.Contracts;

public class WorkerShutdownRequest
{
    [JsonProperty("workerId")]
    public string WorkerId { get; set; } = null!;

    [JsonProperty("state")]
    public string? State { get; set; }

    [JsonProperty("message")]
    public string? Message { get; set; }

    [JsonProperty("metadata")]
    public Dictionary<string, object?>? Metadata { get; set; }
}
