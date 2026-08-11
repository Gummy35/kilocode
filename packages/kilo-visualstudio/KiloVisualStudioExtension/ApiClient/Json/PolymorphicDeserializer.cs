using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using KiloVisualStudioExtension.ApiClient;

namespace KiloVisualStudioExtension.ApiClient.Json
{
    /// <summary>
    /// Explicit discriminator-based deserialization for polymorphic NSwag models.
    /// Uses JObject/JToken for discriminator inspection, then delegates to Newtonsoft
    /// for actual deserialization of concrete types.
    /// 
    /// This approach avoids JsonConverter inheritance issues with C# 14 + Newtonsoft 13.0.3
    /// while providing strongly-typed deserialization for Part, ToolState, and Message.
    /// </summary>
    public static class PolymorphicDeserializer
    {
    /// <summary>
    /// Deserializes a Part token to the appropriate concrete type based on the 'type' discriminator.
    /// Returns object because NSwag generates independent classes (no common base).
    /// Caller should cast to the expected concrete type (TextPart, ToolPart, etc.).
    /// </summary>
    public static object DeserializePart(JToken token, JsonSerializer serializer)
        {
            if (token == null)
                throw new ArgumentNullException(nameof(token));

            var obj = token as JObject ?? throw new JsonSerializationException("Expected JObject for Part");
            var typeToken = obj["type"];
            if (typeToken == null)
                throw new JsonSerializationException("Part JSON missing required 'type' field");

            var type = typeToken.Value<string>();
            if (string.IsNullOrEmpty(type))
                throw new JsonSerializationException("Part 'type' field is empty");

            return type switch
            {
                "text" => obj.ToObject<TextPart>(serializer)!,
                "reasoning" => obj.ToObject<ReasoningPart>(serializer)!,
                "file" => obj.ToObject<FilePart>(serializer)!,
                "tool" => obj.ToObject<ToolPart>(serializer)!,
                "step-start" => obj.ToObject<StepStartPart>(serializer)!,
                "step-finish" => obj.ToObject<StepFinishPart>(serializer)!,
                "snapshot" => obj.ToObject<SnapshotPart>(serializer)!,
                "patch" => obj.ToObject<PatchPart>(serializer)!,
                "agent" => obj.ToObject<AgentPart>(serializer)!,
                "retry" => obj.ToObject<RetryPart>(serializer)!,
                "compaction" => obj.ToObject<CompactionPart>(serializer)!,
                "subtask" => obj.ToObject<SubtaskPart>(serializer)!,
                _ => throw new JsonSerializationException($"Unknown Part type '{type}'")
            };
        }

    /// <summary>
    /// Deserializes a ToolState token to the appropriate concrete type based on the 'status' discriminator.
    /// Returns object because NSwag generates independent classes (no common base).
    /// Caller should cast to the expected concrete type (ToolStatePending, ToolStateRunning, etc.).
    /// </summary>
    public static object DeserializeToolState(JToken token, JsonSerializer serializer)
        {
            if (token == null)
                throw new ArgumentNullException(nameof(token));

            var obj = token as JObject ?? throw new JsonSerializationException("Expected JObject for ToolState");
            var statusToken = obj["status"];
            if (statusToken == null)
                throw new JsonSerializationException("ToolState JSON missing required 'status' field");

            var status = statusToken.Value<string>();
            if (string.IsNullOrEmpty(status))
                throw new JsonSerializationException("ToolState 'status' field is empty");

            return status switch
            {
                "pending" => obj.ToObject<ToolStatePending>(serializer)!,
                "running" => obj.ToObject<ToolStateRunning>(serializer)!,
                "completed" => obj.ToObject<ToolStateCompleted>(serializer)!,
                "error" => obj.ToObject<ToolStateError>(serializer)!,
                _ => throw new JsonSerializationException($"Unknown ToolState status '{status}'")
            };
        }

    /// <summary>
    /// Deserializes a Message token to the appropriate concrete type based on the 'role' discriminator.
    /// Returns object because NSwag generates independent classes (no common base).
    /// Caller should cast to the expected concrete type (UserMessage, AssistantMessage).
    /// </summary>
    public static object DeserializeMessage(JToken token, JsonSerializer serializer)
        {
            if (token == null)
                throw new ArgumentNullException(nameof(token));

            var obj = token as JObject ?? throw new JsonSerializationException("Expected JObject for Message");
            var roleToken = obj["role"];
            if (roleToken == null)
                throw new JsonSerializationException("Message JSON missing required 'role' field");

            var role = roleToken.Value<string>();
            if (string.IsNullOrEmpty(role))
                throw new JsonSerializationException("Message 'role' field is empty");

            return role switch
            {
                "user" => obj.ToObject<UserMessage>(serializer)!,
                "assistant" => obj.ToObject<AssistantMessage>(serializer)!,
                _ => throw new JsonSerializationException($"Unknown Message role '{role}'")
            };
        }

        /// <summary>
        /// Deserializes a FilePartSource token to the appropriate concrete type based on the 'type' discriminator.
        /// </summary>
        public static object DeserializeFilePartSource(JToken token, JsonSerializer serializer)
        {
            if (token == null)
                throw new ArgumentNullException(nameof(token));

            var obj = token as JObject ?? throw new JsonSerializationException("Expected JObject for FilePartSource");
            var typeToken = obj["type"];
            if (typeToken == null)
                throw new JsonSerializationException("FilePartSource JSON missing required 'type' field");

            var type = typeToken.Value<string>();
            if (string.IsNullOrEmpty(type))
                throw new JsonSerializationException("FilePartSource 'type' field is empty");

            return type switch
            {
                "file" => obj.ToObject<FilePartSource>(serializer)!,
                "symbol" => obj.ToObject<SymbolSource>(serializer)!,
                _ => throw new JsonSerializationException($"Unknown FilePartSource type '{type}'")
            };
        }
    }
}
