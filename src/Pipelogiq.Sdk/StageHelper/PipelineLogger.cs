using Microsoft.Extensions.Logging;
using PipelogiqSDK.Contracts;
using PipelogiqSDK.Execution;

namespace PipelogiqSDK.StageHelper;

/// <summary>
/// Collects stage logs to include in stage result payload.
/// </summary>
public class PipelineLogger
{
    /// <summary>
    /// Gets collected log entries.
    /// </summary>
    public List<StageLogDto> Logs = new();
    private readonly ReaderWriterLockSlim DictLock = new();

    /// <summary>
    /// Adds information log message.
    /// </summary>
    /// <param name="message">Log message.</param>
    public void Info(string message) => Log(message, LogLevel.Information);

    /// <summary>
    /// Adds warning log message.
    /// </summary>
    /// <param name="message">Log message.</param>
    public void Warning(string message) => Log(message, LogLevel.Warning);

    /// <summary>
    /// Adds error log message.
    /// </summary>
    /// <param name="message">Log message.</param>
    public void Error(string message) => Log(message, LogLevel.Error);
    

    private void Log(string message, LogLevel level)
    {
        Logs.Add(new StageLogDto
        {
            Created = DateTime.UtcNow,
            Message = message,
            LogLevel = level.ToString(),
        });
    }
}
