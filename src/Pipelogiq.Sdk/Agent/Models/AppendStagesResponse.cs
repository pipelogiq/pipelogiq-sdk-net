using PipelogiqSDK.Contracts;

namespace PipelogiqSDK.Agent.Models;

/// <summary>
/// Response from appending stages to a pipeline.
/// </summary>
public class AppendStagesResponse
{
    /// <summary>The appended stages with their assigned IDs.</summary>
    public List<StageDto> Stages { get; set; } = new();
}
