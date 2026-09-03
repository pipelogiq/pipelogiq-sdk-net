using System.Text.Json;
using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Contracts;

namespace PipelogiqSDK.StageHelper;

/// <summary>
/// Extensions for reading typed values from stage context payload.
/// </summary>
public static class StageContextExtension
{
    /// <summary>
    /// Returns optional technical execution metadata when supplied by the worker runtime.
    /// </summary>
    public static IStageExecutionContext? AsExecutionContext(this IStageContext? stageContext)
        => stageContext as IStageExecutionContext;

    /// <summary>
    /// Returns the handler cancellation token, or <see cref="CancellationToken.None"/> for a legacy context.
    /// </summary>
    public static CancellationToken GetCancellationToken(this IStageContext? stageContext)
        => (stageContext as IStageExecutionContext)?.CancellationToken ?? CancellationToken.None;

    /// <summary>Returns the one-based execution attempt number when available.</summary>
    public static int? GetAttempt(this IStageContext? stageContext)
        => (stageContext as IStageExecutionContext)?.Attempt;

    /// <summary>Returns the stable execution identifier when available.</summary>
    public static string? GetExecutionId(this IStageContext? stageContext)
        => (stageContext as IStageExecutionContext)?.ExecutionId;

    /// <summary>Returns the pipeline idempotency key when available.</summary>
    public static string? GetIdempotencyKey(this IStageContext? stageContext)
        => (stageContext as IStageExecutionContext)?.IdempotencyKey;

    /// <summary>
    /// Attempts to read and convert payload value by key.
    /// </summary>
    /// <typeparam name="T">Expected value type.</typeparam>
    /// <param name="stageContext">Stage context.</param>
    /// <param name="key">Payload key.</param>
    /// <returns>Converted value or default.</returns>
    public static T? TryGetValue<T>(this IStageContext? stageContext, string key)
    {
        var dict = stageContext?.Payload;
        if (dict == null || !dict.TryGetValue(key, out var value))
            return default;

        try
        {
            if (value is T typedValue)
                return typedValue;

            if (value is JsonElement element)
            {
                var result = JsonSerializer.Deserialize<T>(element.GetRawText());
                if (result != null)
                    return result;
            }

            if (value is IConvertible)
                return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            // swallow conversion errors and return default
        }

        return default;
    }

    /// <summary>
    /// Writes an information log entry when the execution context has a stage logger attached.
    /// </summary>
    public static void LogInfo(this IStageContext? stageContext, string message)
        => Log(stageContext, message, "info");

    /// <summary>
    /// Writes a warning log entry when the execution context has a stage logger attached.
    /// </summary>
    public static void LogWarning(this IStageContext? stageContext, string message)
        => Log(stageContext, message, "warning");

    /// <summary>
    /// Writes an error log entry when the execution context has a stage logger attached.
    /// </summary>
    public static void LogError(this IStageContext? stageContext, string message)
        => Log(stageContext, message, "error");

    /// <summary>
    /// Serializes an arbitrary value into a compact preview suitable for stage logs.
    /// </summary>
    public static string ToLogPreview(this object? value, int maxLength = 1200)
    {
        if (value == null)
            return "null";

        string text;
        try
        {
            text = value switch
            {
                string s => s,
                JsonElement element => element.GetRawText(),
                _ => JsonSerializer.Serialize(value),
            };
        }
        catch
        {
            text = value.ToString() ?? string.Empty;
        }

        text = text.Replace("\r", " ").Replace("\n", " ").Trim();
        if (text.Length <= maxLength)
            return text;

        return text[..maxLength] + "...";
    }

    private static void Log(IStageContext? stageContext, string message, string level)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        if (stageContext is not StageContext concreteContext || concreteContext.Logger == null)
            return;

        switch (level)
        {
            case "warning":
                concreteContext.Logger.Warning(message);
                break;
            case "error":
                concreteContext.Logger.Error(message);
                break;
            default:
                concreteContext.Logger.Info(message);
                break;
        }
    }
}
