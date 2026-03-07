namespace PipelogiqSDK.Configuration;

/// <summary>
/// Runner options for Pipelogiq worker and builders.
/// </summary>
public class PipelogiqRunnerOptions
{
    /// <summary>
    /// Gets or sets API key used for authenticated API calls.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets API base URL.
    /// </summary>
    public string ApiUrl { get; set; } = "http://localhost:8081";

    /// <summary>
    /// Gets or sets worker name reported to control plane.
    /// </summary>
    public string? WorkerName { get; set; }

    /// <summary>
    /// Gets or sets worker instance identifier.
    /// </summary>
    public string? InstanceId { get; set; }

    /// <summary>
    /// Gets or sets worker version.
    /// </summary>
    public string? WorkerVersion { get; set; }

    /// <summary>
    /// Gets or sets deployment environment name.
    /// </summary>
    public string? Environment { get; set; }

    /// <summary>
    /// Gets or sets capability flags advertised during bootstrap.
    /// </summary>
    public Dictionary<string, bool>? Capabilities { get; set; }

    /// <summary>
    /// Gets or sets worker metadata sent during bootstrap.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>
    /// Gets or sets queue provisioning behavior.
    /// </summary>
    public QueueProvisioningMode QueueProvisioningMode { get; set; } = QueueProvisioningMode.AssertOnly;

    /// <summary>
    /// Gets or sets legacy application identifier.
    /// </summary>
    [Obsolete("AppId is server-provided from /workers/bootstrap and no longer required in runner options.")]
    public string? AppId { get; set; }
}
