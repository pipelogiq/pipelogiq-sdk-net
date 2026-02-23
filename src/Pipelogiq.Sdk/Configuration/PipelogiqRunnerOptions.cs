namespace PipelogiqSDK.Configuration;

public class PipelogiqRunnerOptions
{
    public string? ApiKey { get; set; }

    public string ApiUrl { get; set; } = "http://localhost:8081";

    public string? WorkerName { get; set; }

    public string? InstanceId { get; set; }

    public string? WorkerVersion { get; set; }

    public string? Environment { get; set; }

    public Dictionary<string, bool>? Capabilities { get; set; }

    public Dictionary<string, string>? Metadata { get; set; }

    [Obsolete("AppId is server-provided from /workers/bootstrap and no longer required in runner options.")]
    public string? AppId { get; set; }
}
