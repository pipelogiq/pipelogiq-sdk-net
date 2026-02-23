namespace PipelogiqSDK.Contracts;

public class StageOptions
{
    public bool? RunNextIfFailed { get; set; }
    public int? RetryInterval { get; set; }
    public int? TimeOut { get; set; }
    public int? MaxRetries { get; set; }
    public List<string>? DependsOn { get; set; }
    public List<string>? RunInParallelWith { get; set; }
    public bool? FailIfOutputEmpty { get; set; }
    public bool? NotifyOnFailure { get; set; }
    public string? RunAsUser { get; set; }
}
