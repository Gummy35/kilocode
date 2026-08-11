using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using KiloVisualStudioExtension.ApiClient;

namespace KiloVisualStudioExtension.ApiClient.Polymorphic
{
    /// <summary>
    /// JSON converter for ToolState polymorphic deserialization.
    /// </summary>
    public class ToolStateConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(ToolState);

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null!;

            var jsonObject = JObject.Load(reader);
            var statusToken = jsonObject["status"];
            if (statusToken == null)
                throw new JsonSerializationException("ToolState JSON missing required 'status' field");

            var status = statusToken.Value<string>();
            if (string.IsNullOrEmpty(status))
                throw new JsonSerializationException("ToolState 'status' field is empty");

            return status switch
            {
                "pending" => jsonObject.ToObject<ToolStatePending>(serializer)!,
                "running" => jsonObject.ToObject<ToolStateRunning>(serializer)!,
                "completed" => jsonObject.ToObject<ToolStateCompleted>(serializer)!,
                "error" => jsonObject.ToObject<ToolStateError>(serializer)!,
                _ => throw new JsonSerializationException($"Unknown ToolState status '{status}'")
            };
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            switch (value)
            {
                case ToolStatePending pending:
                    serializer.Serialize(writer, pending);
                    break;
                case ToolStateRunning running:
                    serializer.Serialize(writer, running);
                    break;
                case ToolStateCompleted completed:
                    serializer.Serialize(writer, completed);
                    break;
                case ToolStateError error:
                    serializer.Serialize(writer, error);
                    break;
                default:
                    throw new JsonSerializationException($"Unknown ToolState type: {value.GetType().FullName}");
            }
        }
    }
}
