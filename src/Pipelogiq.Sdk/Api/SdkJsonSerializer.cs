using System.Text.Json;
using Newtonsoft.Json.Linq;
using NewtonsoftJsonSerializer = Newtonsoft.Json.JsonSerializer;
using NewtonsoftJsonConverter = Newtonsoft.Json.JsonConverter;
using NewtonsoftJsonReader = Newtonsoft.Json.JsonReader;
using NewtonsoftJsonWriter = Newtonsoft.Json.JsonWriter;
using NewtonsoftJsonSerializerSettings = Newtonsoft.Json.JsonSerializerSettings;

namespace PipelogiqSDK.Api;

internal static class SdkJsonSerializer
{
    private static readonly NewtonsoftJsonSerializerSettings Settings = new()
    {
        Converters =
        [
            new JsonElementNewtonsoftConverter(),
            new JsonDocumentNewtonsoftConverter()
        ]
    };

    public static string Serialize(object content) => Newtonsoft.Json.JsonConvert.SerializeObject(content, Settings);

    private sealed class JsonElementNewtonsoftConverter : NewtonsoftJsonConverter
    {
        public override bool CanRead => false;

        public override bool CanConvert(Type objectType) =>
            objectType == typeof(JsonElement) || objectType == typeof(JsonElement?);

        public override void WriteJson(NewtonsoftJsonWriter writer, object? value, NewtonsoftJsonSerializer serializer)
        {
            if (value is null)
            {
                writer.WriteNull();
                return;
            }

            var element = value is JsonElement jsonElement
                ? jsonElement
                : ((JsonElement?)value).GetValueOrDefault();

            WriteJsonElement(writer, element);
        }

        public override object? ReadJson(
            NewtonsoftJsonReader reader,
            Type objectType,
            object? existingValue,
            NewtonsoftJsonSerializer serializer) =>
            throw new NotSupportedException("JsonElementNewtonsoftConverter is write-only.");
    }

    private sealed class JsonDocumentNewtonsoftConverter : NewtonsoftJsonConverter
    {
        public override bool CanRead => false;

        public override bool CanConvert(Type objectType) =>
            objectType == typeof(JsonDocument);

        public override void WriteJson(NewtonsoftJsonWriter writer, object? value, NewtonsoftJsonSerializer serializer)
        {
            if (value is not JsonDocument document)
            {
                writer.WriteNull();
                return;
            }

            WriteJsonElement(writer, document.RootElement);
        }

        public override object? ReadJson(
            NewtonsoftJsonReader reader,
            Type objectType,
            object? existingValue,
            NewtonsoftJsonSerializer serializer) =>
            throw new NotSupportedException("JsonDocumentNewtonsoftConverter is write-only.");
    }

    private static void WriteJsonElement(NewtonsoftJsonWriter writer, JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            writer.WriteNull();
            return;
        }

        JToken.Parse(element.GetRawText()).WriteTo(writer);
    }
}
