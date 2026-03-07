using Microsoft.Extensions.Logging;
using PipelogiqSDK.Configuration;
using PipelogiqSDK.Contracts;

namespace PipelogiqSDK.Builders;

/// <summary>
/// Builder for log payloads.
/// </summary>
public class LogBuilder : BaseBuilder<LogBuilder>
{
    private readonly LogLevel _logLevel;
    private readonly string _message;

    private LogBuilder(LogLevel logLevel, string message, PipelogiqRunnerOptions? options = null) : base(options)
    {
        _logLevel = logLevel;
        _message = message;
    }

    /// <summary>
    /// Creates log builder.
    /// </summary>
    /// <param name="logLevel">Log level.</param>
    /// <param name="message">Log message.</param>
    /// <param name="options">Optional runner options.</param>
    /// <returns>Configured log builder.</returns>
    public static LogBuilder Create(LogLevel logLevel, string message, PipelogiqRunnerOptions? options = null)
    {
        return new LogBuilder(logLevel, message, options);
    }

    /// <summary>
    /// Builds log payload.
    /// </summary>
    /// <returns>Log DTO payload.</returns>
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

    /// <summary>
    /// Sends log payload to API.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task SendAsync(CancellationToken ct = default)
    {
        await ApiClient.PostLogAsync(Build(), ct);
    }
}
