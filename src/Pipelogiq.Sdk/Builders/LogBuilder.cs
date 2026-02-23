using Microsoft.Extensions.Logging;
using PipelogiqSDK.Configuration;
using PipelogiqSDK.Contracts;

namespace PipelogiqSDK.Builders;

public class LogBuilder : BaseBuilder<LogBuilder>
{
    private readonly LogLevel _logLevel;
    private readonly string _message;

    private LogBuilder(LogLevel logLevel, string message, PipelogiqRunnerOptions? options = null) : base(options)
    {
        _logLevel = logLevel;
        _message = message;
    }

    public static LogBuilder Create(LogLevel logLevel, string message, PipelogiqRunnerOptions? options = null)
    {
        return new LogBuilder(logLevel, message, options);
    }

    public LogDto Build()
    {
        var key = RequireApiKey();
        return new LogDto
        {
            ApiKey = key,
            Message = _message,
            LogLevel = _logLevel.ToString(),
            Keywords = Keywords,
            Created = DateTime.UtcNow,
        };
    }

    public async Task SendAsync(CancellationToken ct = default)
    {
        await ApiClient.PostLogAsync(Build(), ct);
    }
}
