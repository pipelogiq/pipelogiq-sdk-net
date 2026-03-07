namespace PipelogiqSDK.Contracts;

/// <summary>
/// Optional stage execution settings.
/// </summary>
public class StageOptions
{
    /// <summary>
    /// Gets or sets whether next stage should run on failure.
    /// </summary>
    public bool? RunNextIfFailed { get; set; }

    /// <summary>
    /// Gets or sets retry interval in seconds.
    /// </summary>
    public int? RetryInterval { get; set; }

    /// <summary>
    /// Gets or sets timeout in seconds.
    /// </summary>
    public int? TimeOut { get; set; }

    /// <summary>
    /// Gets or sets maximum retry count.
    /// </summary>
    public int? MaxRetries { get; set; }

    /// <summary>
    /// Gets or sets dependency stage names.
    /// </summary>
    public List<string>? DependsOn { get; set; }

    /// <summary>
    /// Gets or sets stages that can run in parallel.
    /// </summary>
    public List<string>? RunInParallelWith { get; set; }

    /// <summary>
    /// Gets or sets whether empty output should fail stage.
    /// </summary>
    public bool? FailIfOutputEmpty { get; set; }

    /// <summary>
    /// Gets or sets whether failure notifications are enabled.
    /// </summary>
    public bool? NotifyOnFailure { get; set; }

    /// <summary>
    /// Gets or sets user identity for stage execution.
    /// </summary>
    public string? RunAsUser { get; set; }
}
