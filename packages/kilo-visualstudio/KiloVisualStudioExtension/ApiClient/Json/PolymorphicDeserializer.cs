using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.ApiClient.Sse;

namespace KiloVisualStudioExtension.ApiClient.Json
{
    /// <summary>
    /// Explicit discriminator-based deserialization for polymorphic NSwag models.
    /// Uses JObject/JToken for discriminator inspection, then delegates to Newtonsoft
    /// for actual deserialization of concrete types.
    /// 
    /// This approach avoids JsonConverter inheritance issues with C# 14 + Newtonsoft 13.0.3
    /// while providing strongly-typed deserialization for Part, ToolState, Message, and nested polymorphic properties.
    /// </summary>
    public static class PolymorphicDeserializer
    {
    /// <summary>
    /// Deserializes a Part token to the appropriate concrete type based on the 'type' discriminator.
    /// Also handles nested polymorphic properties like ToolPart.State.
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

            object result = type switch
            {
                "text" => obj.ToObject<TextPart>(serializer)!,
                "reasoning" => obj.ToObject<ReasoningPart>(serializer)!,
                "file" => DeserializeFilePart(obj, serializer),
                "tool" => DeserializeToolPart(obj, serializer),
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

            return result;
        }

    /// <summary>
    /// Deserializes a ToolPart with proper handling of the nested State polymorphic property.
    /// </summary>
    private static object DeserializeToolPart(JObject obj, JsonSerializer serializer)
        {
            var toolPart = obj.ToObject<ToolPart>(serializer);
            if (toolPart == null)
                throw new JsonSerializationException("Failed to deserialize ToolPart");

            // Deserialize nested State polymorphically
            var stateToken = obj["state"];
            if (stateToken != null && stateToken.Type != JTokenType.Null)
            {
                toolPart.State = (ToolState)DeserializeToolState(stateToken, serializer);
            }

            return toolPart;
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
                "file" => obj.ToObject<FileSource>(serializer)!,
                "symbol" => obj.ToObject<SymbolSource>(serializer)!,
                "resource" => obj.ToObject<ResourceSource>(serializer)!,
                _ => throw new JsonSerializationException($"Unknown FilePartSource type '{type}'")
            };
        }

        /// <summary>
        /// Deserializes a FilePart with proper handling of the nested Source polymorphic property.
        /// </summary>
        public static object DeserializeFilePart(JToken token, JsonSerializer serializer)
        {
            if (token == null)
                throw new ArgumentNullException(nameof(token));

            var obj = token as JObject ?? throw new JsonSerializationException("Expected JObject for FilePart");
            var filePart = obj.ToObject<FilePart>(serializer);
            if (filePart == null)
                throw new JsonSerializationException("Failed to deserialize FilePart");

            // Deserialize nested Source polymorphically
            var sourceToken = obj["source"];
            if (sourceToken != null && sourceToken.Type != JTokenType.Null)
            {
                filePart.Source = (FilePartSource)DeserializeFilePartSource(sourceToken, serializer);
            }

            return filePart;
        }

        /// <summary>
        /// Deserializes EventMessagePartUpdated with proper handling of the nested Part polymorphic property.
        /// </summary>
        public static EventMessagePartUpdated DeserializeEventMessagePartUpdated(JToken token, JsonSerializer serializer)
        {
            if (token == null)
                throw new ArgumentNullException(nameof(token));

            var obj = token as JObject ?? throw new JsonSerializationException("Expected JObject for EventMessagePartUpdated");
            var evt = obj.ToObject<EventMessagePartUpdated>(serializer);
            if (evt == null)
                throw new JsonSerializationException("Failed to deserialize EventMessagePartUpdated");

            // Deserialize nested Part polymorphically
            var partToken = obj["properties"]?["part"];
            if (partToken != null && partToken.Type != JTokenType.Null)
            {
                evt.Properties.Part = (Part)DeserializePart(partToken, serializer);
            }

            return evt;
        }

        /// <summary>
        /// Deserializes EventMessagePartRemoved.
        /// </summary>
        public static EventMessagePartRemoved DeserializeEventMessagePartRemoved(JToken token, JsonSerializer serializer)
        {
            if (token == null)
                throw new ArgumentNullException(nameof(token));

            var obj = token as JObject ?? throw new JsonSerializationException("Expected JObject for EventMessagePartRemoved");
            var evt = obj.ToObject<EventMessagePartRemoved>(serializer);
            if (evt == null)
                throw new JsonSerializationException("Failed to deserialize EventMessagePartRemoved");

            return evt;
        }



        /// <summary>
        /// Deserializes EventMessageUpdated with proper handling of the nested Info polymorphic property.
        /// The Info field uses 'role' discriminator to determine SessionMessageAssistant vs SessionMessageUser, etc.
        /// </summary>
        public static EventMessageUpdated DeserializeEventMessageUpdated(JToken token, JsonSerializer serializer)
        {
            if (token == null)
                throw new ArgumentNullException(nameof(token));

            var obj = token as JObject ?? throw new JsonSerializationException("Expected JObject for EventMessageUpdated");
            var evt = obj.ToObject<EventMessageUpdated>(serializer);
            if (evt == null)
                throw new JsonSerializationException("Failed to deserialize EventMessageUpdated");

            // Deserialize nested Info polymorphically based on 'role' discriminator
            var infoToken = obj["properties"]?["info"];
            if (infoToken != null && infoToken.Type != JTokenType.Null)
            {
                evt.Properties.Info = (ApiClient.Message)DeserializeSessionMessage(infoToken, serializer);
            }

            return evt;
        }

        /// <summary>
        /// Deserializes a SessionMessage token to the appropriate concrete type based on the 'role' or 'type' discriminator.
        /// Returns ApiClient.Message (base class) but actually contains SessionMessageAssistant, SessionMessageUser, etc.
        /// </summary>
        public static object DeserializeSessionMessage(JToken token, JsonSerializer serializer)
        {
            if (token == null)
                throw new ArgumentNullException(nameof(token));

            var obj = token as JObject ?? throw new JsonSerializationException("Expected JObject for SessionMessage");
            
            // Check for 'role' discriminator first (AssistantMessage, UserMessage pattern)
            var roleToken = obj["role"];
            if (roleToken != null && roleToken.Type != JTokenType.Null)
            {
                var role = roleToken.Value<string>();
                return role switch
                {
                    "user" => obj.ToObject<SessionMessageUser>(serializer)!,
                    "assistant" => obj.ToObject<SessionMessageAssistant>(serializer)!,
                    _ => obj.ToObject<SessionMessage>(serializer)!
                };
            }
            
            // Fall back to 'type' discriminator
            var typeToken = obj["type"];
            if (typeToken != null && typeToken.Type != JTokenType.Null)
            {
                var type = typeToken.Value<string>();
                return type switch
                {
                    "user" => obj.ToObject<SessionMessageUser>(serializer)!,
                    "assistant" => obj.ToObject<SessionMessageAssistant>(serializer)!,
                    "system" => obj.ToObject<SessionMessageSystem>(serializer)!,
                    "synthetic" => obj.ToObject<SessionMessageSynthetic>(serializer)!,
                    "shell" => obj.ToObject<SessionMessageShell>(serializer)!,
                    "compaction" => obj.ToObject<SessionMessageCompaction>(serializer)!,
                    "model-switched" => obj.ToObject<SessionMessageModelSwitched>(serializer)!,
                    "agent-switched" => obj.ToObject<SessionMessageAgentSwitched>(serializer)!,
                    _ => obj.ToObject<SessionMessage>(serializer)!
                };
            }

            return obj.ToObject<SessionMessage>(serializer)!;
        }

        /// <summary>
        /// Deserializes EventSessionCreated with proper handling of the nested Info property.
        /// </summary>
        public static EventSessionCreated DeserializeEventSessionCreated(JToken token, JsonSerializer serializer)
        {
            if (token == null)
                throw new ArgumentNullException(nameof(token));

            var obj = token as JObject ?? throw new JsonSerializationException("Expected JObject for EventSessionCreated");
            var evt = obj.ToObject<EventSessionCreated>(serializer);
            if (evt == null)
                throw new JsonSerializationException("Failed to deserialize EventSessionCreated");

            return evt;
        }

        /// <summary>
        /// Deserializes EventSessionUpdated with proper handling of the nested Info property.
        /// </summary>
        public static EventSessionUpdated DeserializeEventSessionUpdated(JToken token, JsonSerializer serializer)
        {
            if (token == null)
                throw new ArgumentNullException(nameof(token));

            var obj = token as JObject ?? throw new JsonSerializationException("Expected JObject for EventSessionUpdated");
            var evt = obj.ToObject<EventSessionUpdated>(serializer);
            if (evt == null)
                throw new JsonSerializationException("Failed to deserialize EventSessionUpdated");

            return evt;
        }

        /// <summary>
        /// Deserializes EventSessionDeleted.
        /// </summary>
        public static EventSessionDeleted DeserializeEventSessionDeleted(JToken token, JsonSerializer serializer)
        {
            if (token == null)
                throw new ArgumentNullException(nameof(token));

            var obj = token as JObject ?? throw new JsonSerializationException("Expected JObject for EventSessionDeleted");
            var evt = obj.ToObject<EventSessionDeleted>(serializer);
            if (evt == null)
                throw new JsonSerializationException("Failed to deserialize EventSessionDeleted");

            return evt;
        }

        /// <summary>
        /// Deserializes EventMessageRemoved.
        /// </summary>
        public static EventMessageRemoved DeserializeEventMessageRemoved(JToken token, JsonSerializer serializer)
        {
            if (token == null)
                throw new ArgumentNullException(nameof(token));

            var obj = token as JObject ?? throw new JsonSerializationException("Expected JObject for EventMessageRemoved");
            var evt = obj.ToObject<EventMessageRemoved>(serializer);
            if (evt == null)
                throw new JsonSerializationException("Failed to deserialize EventMessageRemoved");

            return evt;
        }
    }
}
