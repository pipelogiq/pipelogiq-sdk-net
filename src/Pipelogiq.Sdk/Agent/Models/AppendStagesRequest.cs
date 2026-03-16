using PipelogiqSDK.Contracts;

namespace PipelogiqSDK.Agent.Models;

/// <summary>
/// Request to append stages to a running pipeline.
/// </summary>
public class AppendStagesRequest
{
    /// <summary>Stages to append, in execution order.</summary>
    public List<StageInfo> Stages { get; set; } = new();
}
