using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using KiloVisualStudioExtension.ApiClient.Json;

namespace KiloVisualStudioExtension.ApiClient.Sse
{
    /// <summary>
    /// Typed representation of SSE events from the Kilo backend.
    /// Distinguishes between sync events (name-based) and stream events (type-based).
    /// </summary>
    public abstract class SseEvent
    {
        public string EventType { get; set; } = "";
    }

    /// <summary>
    /// Sync event format: {name, id, seq, data}
    /// Used for: message.updated.1, session.created.1, etc.
    /// </summary>
    public class SyncEvent : SseEvent
    {
        public string Name { get; set; } = "";
        public string Id { get; set; } = "";
        public int Seq { get; set; }
        public JToken Data { get; set; } = null!;
    }

    /// <summary>
    /// Stream event format: {type, properties, sessionID, directory}
    /// Used for: message.part.updated, session.status, etc.
    /// </summary>
    public class StreamEvent : SseEvent
    {
        public string SessionID { get; set; } = "";
        public string Directory { get; set; } = "";
        public JToken Properties { get; set; } = null!;
    }

    /// <summary>
    /// Deserializes SSE event JSON into typed SseEvent objects.
    /// Uses Newtonsoft.Json with shared settings from KiloJsonSerializer.
    /// </summary>
    public static class SseEventDeserializer
    {
        /// <summary>
        /// Deserializes an SSE event from raw JSON string.
        /// </summary>
        public static SseEvent Deserialize(string eventType, string data)
        {
            var serializer = KiloJsonSerializer.Create();
            var obj = JObject.Parse(data);

            // Check for "payload" wrapper
            if (obj.TryGetValue("payload", out var payload))
                obj = (JObject)payload;

            // Route based on structure
            if (obj.TryGetValue("name", out var nameToken))
            {
                // Sync event
                return DeserializeSyncEvent(obj, serializer);
            }
            else if (obj.TryGetValue("type", out var typeToken))
            {
                // Stream event
                return DeserializeStreamEvent(obj, serializer);
            }

            throw new JsonSerializationException($"Unknown SSE event structure: {data}");
        }

        private static SseEvent DeserializeSyncEvent(JObject obj, JsonSerializer serializer)
        {
            var name = obj["name"]?.Value<string>() ?? "";
            var id = obj["id"]?.Value<string>() ?? "";
            var seq = obj["seq"]?.Value<int>() ?? 0;
            var data = obj["data"] ?? throw new JsonSerializationException("Sync event missing 'data'");

            return name switch
            {
                "message.updated.1" => new MessageUpdatedSyncEvent
                {
                    EventType = "sync",
                    Name = name,
                    Id = id,
                    Seq = seq,
                    Data = data
                },
                "message.part.updated.1" => new MessagePartUpdatedSyncEvent
                {
                    EventType = "sync",
                    Name = name,
                    Id = id,
                    Seq = seq,
                    Data = data
                },
                "session.created.1" => new SessionCreatedSyncEvent
                {
                    EventType = "sync",
                    Name = name,
                    Id = id,
                    Seq = seq,
                    Data = data
                },
                "session.updated.1" => new SessionUpdatedSyncEvent
                {
                    EventType = "sync",
                    Name = name,
                    Id = id,
                    Seq = seq,
                    Data = data
                },
                _ => new GenericSyncEvent
                {
                    EventType = "sync",
                    Name = name,
                    Id = id,
                    Seq = seq,
                    Data = data
                }
            };
        }

        private static SseEvent DeserializeStreamEvent(JObject obj, JsonSerializer serializer)
        {
            var type = obj["type"]?.Value<string>() ?? "";
            var sessionID = obj["sessionID"]?.Value<string>() ?? "";
            var directory = obj["directory"]?.Value<string>() ?? "";
            var properties = obj["properties"] ?? throw new JsonSerializationException("Stream event missing 'properties'");

            return type switch
            {
                "message.part.updated" => new MessagePartUpdatedStreamEvent
                {
                    EventType = type,
                    SessionID = sessionID,
                    Directory = directory,
                    Properties = properties
                },
                "message.updated" => new MessageUpdatedStreamEvent
                {
                    EventType = type,
                    SessionID = sessionID,
                    Directory = directory,
                    Properties = properties
                },
                "session.status" => new SessionStatusStreamEvent
                {
                    EventType = type,
                    SessionID = sessionID,
                    Directory = directory,
                    Properties = properties
                },
                "permission.asked" => new PermissionAskedStreamEvent
                {
                    EventType = type,
                    SessionID = sessionID,
                    Directory = directory,
                    Properties = properties
                },
                "question.asked" => new QuestionAskedStreamEvent
                {
                    EventType = type,
                    SessionID = sessionID,
                    Directory = directory,
                    Properties = properties
                },
                "suggestion.shown" => new SuggestionShownStreamEvent
                {
                    EventType = type,
                    SessionID = sessionID,
                    Directory = directory,
                    Properties = properties
                },
                "session.error" => new SessionErrorStreamEvent
                {
                    EventType = type,
                    SessionID = sessionID,
                    Directory = directory,
                    Properties = properties
                },
                _ => new GenericStreamEvent
                {
                    EventType = type,
                    SessionID = sessionID,
                    Directory = directory,
                    Properties = properties
                }
            };
        }
    }

    // Concrete event types for better type safety
    public class MessageUpdatedSyncEvent : SyncEvent { }
    public class MessagePartUpdatedSyncEvent : SyncEvent { }
    public class SessionCreatedSyncEvent : SyncEvent { }
    public class SessionUpdatedSyncEvent : SyncEvent { }
    public class GenericSyncEvent : SyncEvent { }

    public class MessagePartUpdatedStreamEvent : StreamEvent { }
    public class MessageUpdatedStreamEvent : StreamEvent { }
    public class SessionStatusStreamEvent : StreamEvent { }
    public class PermissionAskedStreamEvent : StreamEvent { }
    public class QuestionAskedStreamEvent : StreamEvent { }
    public class SuggestionShownStreamEvent : StreamEvent { }
    public class SessionErrorStreamEvent : StreamEvent { }
    public class GenericStreamEvent : StreamEvent { }
}
