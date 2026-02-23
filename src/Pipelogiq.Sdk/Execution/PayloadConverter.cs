using System.Text.Json;
using PipelogiqSDK.Contracts;

namespace PipelogiqSDK.Execution;

internal static class PayloadConverter
{
    public static Dictionary<string, object> FromContextItems(IEnumerable<ContextItem> contextItems)
    {
        var payload = new Dictionary<string, object>();

        foreach (var contextItem in contextItems)
        {
            if (string.IsNullOrWhiteSpace(contextItem.Key))
                continue;
            
            payload[contextItem.Key] = contextItem.Value.ToString();

            if (string.IsNullOrWhiteSpace(contextItem.ValueType))
                continue;
            
            var type = Type.GetType(contextItem.ValueType);
            
            if (type == null)
                continue;

            var convertedValue = ConvertToType(contextItem.Value, type);
            if (convertedValue != null)
            {
                payload[contextItem.Key] = convertedValue;
            }
        
        }

        return payload;
    }

    public static List<ContextItem> ToContextItems(Dictionary<string, object>? payload)
    {
        if (payload == null || payload.Count == 0)
            return new List<ContextItem>();

        return payload
            .Select(x => new ContextItem
            {
                Key = x.Key,
                Value = x.Value.ToString() ?? string.Empty,
                ValueType = x.Value.GetType().AssemblyQualifiedName ?? x.Value.GetType().FullName ?? string.Empty
            })
            .ToList();
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

    public static object? ConvertToType(object rawValue, Type targetType)
    {
        try
        {
            if (rawValue == null)
                return null;

            if (targetType.IsInstanceOfType(rawValue))
                return rawValue;

            if (rawValue is JsonElement jsonElement)
                return jsonElement.Deserialize(targetType);

            if (rawValue is string str && targetType != typeof(string))
                return JsonSerializer.Deserialize(str, targetType);

            return Convert.ChangeType(rawValue, targetType);
        }
        catch
        {
            return null;
        }
    }
}
