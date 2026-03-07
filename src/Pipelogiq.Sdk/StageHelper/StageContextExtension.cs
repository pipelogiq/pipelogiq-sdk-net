using System.Text.Json;
using PipelogiqSDK.Abstractions;

namespace PipelogiqSDK.StageHelper;

/// <summary>
/// Extensions for reading typed values from stage context payload.
/// </summary>
public static class StageContextExtension
{
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
}
