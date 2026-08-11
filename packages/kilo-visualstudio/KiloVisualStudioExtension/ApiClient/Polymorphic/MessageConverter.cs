#nullable disable
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using KiloVisualStudioExtension.ApiClient;

namespace KiloVisualStudioExtension.ApiClient.Polymorphic
{
    /// <summary>
    /// JSON converter for Message polymorphic deserialization.
    /// Maps the 'role' discriminator to concrete Message types.
    /// </summary>
    public class MessageConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Message);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            var jsonObject = JObject.Load(reader);
            var roleToken = jsonObject["role"];
            if (roleToken == null)
                throw new JsonSerializationException("Message JSON missing required 'role' field");

            var role = roleToken.Value<string>();
            if (string.IsNullOrEmpty(role))
                throw new JsonSerializationException("Message 'role' field is empty");

            return role switch
            {
                "user" => jsonObject.ToObject<UserMessage>(serializer),
                "assistant" => jsonObject.ToObject<AssistantMessage>(serializer),
                _ => throw new JsonSerializationException($"Unknown Message role '{role}'")
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
                case UserMessage user: serializer.Serialize(writer, user); break;
                case AssistantMessage assistant: serializer.Serialize(writer, assistant); break;
                default: throw new JsonSerializationException($"Unknown Message type: {value.GetType().FullName}");
            }
        }
    }
}
