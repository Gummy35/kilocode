#nullable disable
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using KiloVisualStudioExtension.ApiClient;

namespace KiloVisualStudioExtension.ApiClient.Polymorphic
{
    /// <summary>
    /// JSON converter for Part polymorphic deserialization.
    /// Maps the 'type' discriminator to concrete Part types.
    /// </summary>
    public class PartConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Part);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            var jsonObject = JObject.Load(reader);
            var typeToken = jsonObject["type"];
            if (typeToken == null)
                throw new JsonSerializationException("Part JSON missing required 'type' field");

            var type = typeToken.Value<string>();
            if (string.IsNullOrEmpty(type))
                throw new JsonSerializationException("Part 'type' field is empty");

            return type switch
            {
                "text" => jsonObject.ToObject<TextPart>(serializer),
                "file" => jsonObject.ToObject<FilePart>(serializer),
                "tool" => jsonObject.ToObject<ToolPart>(serializer),
                "reasoning" => jsonObject.ToObject<ReasoningPart>(serializer),
                "step-start" => jsonObject.ToObject<StepStartPart>(serializer),
                "step-finish" => jsonObject.ToObject<StepFinishPart>(serializer),
                "snapshot" => jsonObject.ToObject<SnapshotPart>(serializer),
                "patch" => jsonObject.ToObject<PatchPart>(serializer),
                "agent" => jsonObject.ToObject<AgentPart>(serializer),
                "retry" => jsonObject.ToObject<RetryPart>(serializer),
                "compaction" => jsonObject.ToObject<CompactionPart>(serializer),
                "subtask" => jsonObject.ToObject<SubtaskPart>(serializer),
                _ => throw new JsonSerializationException($"Unknown Part type '{type}'")
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
                case TextPart text: serializer.Serialize(writer, text); break;
                case FilePart file: serializer.Serialize(writer, file); break;
                case ToolPart tool: serializer.Serialize(writer, tool); break;
                case ReasoningPart reasoning: serializer.Serialize(writer, reasoning); break;
                case StepStartPart stepStart: serializer.Serialize(writer, stepStart); break;
                case StepFinishPart stepFinish: serializer.Serialize(writer, stepFinish); break;
                case SnapshotPart snapshot: serializer.Serialize(writer, snapshot); break;
                case PatchPart patch: serializer.Serialize(writer, patch); break;
                case AgentPart agent: serializer.Serialize(writer, agent); break;
                case RetryPart retry: serializer.Serialize(writer, retry); break;
                case CompactionPart compaction: serializer.Serialize(writer, compaction); break;
                case SubtaskPart subtask: serializer.Serialize(writer, subtask); break;
                default: throw new JsonSerializationException($"Unknown Part type: {value.GetType().FullName}");
            }
        }
    }
}
