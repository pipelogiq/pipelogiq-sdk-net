using System.Text.Json;
using PipelogiqSDK.Contracts;

namespace PipelogiqSDK.Execution;

internal static class PayloadConverter
{
    public static Dictionary<string, object> FromContextItems(IEnumerable<ContextItem> contextItems)
    {
        var payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var contextItem in contextItems)
        {
            if (string.IsNullOrWhiteSpace(contextItem.Key))
                continue;

            payload[contextItem.Key] = DeserializeContextItem(contextItem)!;
        }

        return payload;
    }

    public static List<ContextItem> ToContextItems(Dictionary<string, object>? payload)
    {
        if (payload == null || payload.Count == 0)
            return new List<ContextItem>();

        var contextItems = new List<ContextItem>(payload.Count);

        foreach (var (key, value) in payload)
        {
            var valueType = value?.GetType() ?? typeof(object);
            var valueTypeName = valueType.AssemblyQualifiedName ?? valueType.FullName ?? typeof(object).FullName!;

            contextItems.Add(new ContextItem
            {
                Key = key,
                Value = JsonSerializer.Serialize(value, valueType),
                ValueType = valueTypeName
            });
        }

        return contextItems;
    }

    public static object? DeserializeJson(string json, Type targetType)
    {
        try
        {
            return JsonSerializer.Deserialize(json, targetType);
        }
        catch
        {
            return null;
        }
    }

    private static object? DeserializeContextItem(ContextItem contextItem)
    {
        var type = ResolveType(contextItem.ValueType);

        if (type != null && TryDeserialize(contextItem.Value, type, out var typedValue))
            return typedValue;

        if (TryDeserialize(contextItem.Value, typeof(object), out var genericValue))
            return genericValue;

        // Backward-compatible fallback for legacy payloads that were not JSON-serialized.
        return contextItem.Value;
    }

    private static Type? ResolveType(string? valueType)
    {
        if (string.IsNullOrWhiteSpace(valueType))
            return null;

        return Type.GetType(valueType);
    }

    private static bool TryDeserialize(string? json, Type targetType, out object? value)
    {
        try
        {
            value = JsonSerializer.Deserialize(json ?? "null", targetType);
            return true;
        }
        catch
        {
            value = null;
            return false;
        }
    }
}
