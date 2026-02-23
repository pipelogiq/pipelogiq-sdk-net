using Microsoft.Extensions.Logging;
using PipelogiqSDK.Contracts;
using PipelogiqSDK.Execution;

namespace PipelogiqSDK.StageHelper;

public class PipelineLogger
{
    public List<StageLogDto> Logs = new();
    private readonly ReaderWriterLockSlim DictLock = new();

    public void Info(string message) => Log(message, LogLevel.Information);

    public void Warning(string message) => Log(message, LogLevel.Warning);

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
