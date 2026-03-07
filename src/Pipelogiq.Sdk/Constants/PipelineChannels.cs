namespace PipelogiqSDK.Constants;

/// <summary>
/// Default RabbitMQ channel and queue names used by Pipelogiq.
/// </summary>
public static class PipelineChannels
{
    /// <summary>
    /// Queue for stage execution results.
    /// </summary>
    public const string StageResult = "StageResult";

    /// <summary>
    /// Queue prefix for next stage dispatch.
    /// </summary>
    public const string StageNext = "StageNext";

    /// <summary>
    /// Queue for stage status updates.
    /// </summary>
    public const string StageSetStatus = "StageSetStatus";

    /// <summary>
    /// Queue for stop-stage signals.
    /// </summary>
    public const string StageStop = "StageStop";

    /// <summary>
    /// Queue for start-pipeline commands.
    /// </summary>
    public const string StartPipeline = "StartPipeline";

    /// <summary>
    /// Queue for stop-pipeline commands.
    /// </summary>
    public const string StopPipeline = "StopPipeline";
}
