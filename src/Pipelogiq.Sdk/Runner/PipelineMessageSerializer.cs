using System.Text.Json;

namespace PipelogiqSDK.Runner;

/// <summary>
/// Serializes worker queue payloads with a stable JSON contract.
/// </summary>
internal static class PipelineMessageSerializer
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static T? Deserialize<T>(string payload)
    {
        return JsonSerializer.Deserialize<T>(payload, ReadOptions);
    }

    public static string Serialize<T>(T payload)
    {
        return JsonSerializer.Serialize(payload, WriteOptions);
    }
}
