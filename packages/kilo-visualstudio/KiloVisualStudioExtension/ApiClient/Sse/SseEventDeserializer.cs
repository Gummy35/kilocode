using Common;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.ApiClient.Json;
using Microsoft.VisualStudio.Shell.Interop;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;

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
    public object Data { get; set; } = null!;
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

    public static JObject NormalizeEvent(JObject obj)
    {
      var type = obj["type"]?.Value<string>() ?? "";
      if (type != "sync") return obj;
      var ev = obj["syncEvent"];
      return JObject.FromObject(new
      {
        type = "sync",
        name = ev["type"],
        id = ev["id"],
        seq = ev["seq"],
        aggregateID = ev["aggregateID"],
        data = ev["data"]
      });
    }

    //public static JObject UnwrapSyncEvent(JObject obj)
    //{
    //  var type = obj["type"]?.Value<string>() ?? "";
    //  if (type != "sync") return obj;

    //  var payload = obj.ContainsKey("syncEvent") ? NormalizeEvent(obj) : obj;
    //  var name = payload["name"]?.Value<string>() ?? "";

    //  return name switch
    //  {
    //    "message.updated.1" => JObject.FromObject(new { id = payload["id"], type = "message.updated", properties = payload["data"] }),
    //    "message.removed.1" => JObject.FromObject(new { id = payload["id"], type = "message.removed", properties = payload["data"] }),
    //    "message.part.updated.1" => JObject.FromObject(new { id = payload["id"], type = "message.part.updated", properties = payload["data"] }),
    //    "message.part.removed.1" => JObject.FromObject(new { id = payload["id"], type = "message.part.removed", properties = payload["data"] }),
    //    "message.created.1" => JObject.FromObject(new { id = payload["id"], type = "message.created", properties = payload["data"] }),
    //    "session.updated.1" => JObject.FromObject(new { source = "sync", id = payload["id"], seq = payload["seq"], type = "session.updated", properties = payload["data"] }),
    //    "message.deleted.1" => JObject.FromObject(new { id = payload["id"], type = "session.deleted", properties = payload["data"] }),
    //    _ => null
    //  };
    //}


    public static Events Deserialize(JToken obj)
    {
      var serializer = KiloJsonSerializer.Create();
      if (obj == null) return null;
      //obj = UnwrapSyncEvent(obj);

      var v = PolymorphicDeserializer.DeserializeSSEEvent(obj, serializer);

      string type = obj["type"].Value<string>();
      string id = obj["id"].Value<string>();
      int seq = 0;
      string aggregateId = "";
      if (type == "sync" && obj["syncEvent"] != null)
      {
        var inner = obj["syncEvent"];
        type = inner["type"].Value<string>() ?? "";
        id = inner["id"].Value<string>() ?? "";
        aggregateId = inner["aggregateID"].Value<string>() ?? "";
        seq = inner["seq"].Value<int>();
      }

      return new Events { AggregateID = aggregateId, Id = id, Seq = seq, Type = type, Data = v };
    }



    /// <summary>
    /// Deserializes an SSE event from raw JSON string.
    /// </summary>
    public static Events Deserialize(SseEventReceivedEventArgs ev)
    {


      //var obj = JObject.Parse(data);

      //var directory = obj["directory"]?.Value<string>() ?? "";
      //var project = obj["project"]?.Value<string>() ?? "";

      //// Check for "payload" wrapper
      //if (obj.TryGetValue("payload", out var payload))
      //    obj = (JObject)payload;

      var obj = ev.UnwrapSyncEvent();
      return Deserialize(obj);
      //            throw new JsonSerializationException($"Unknown SSE event structure");
    }

    //private static SseEvent DeserializeSyncEvent(JObject obj, JsonSerializer serializer)
    //{
    //  var name = obj["name"]?.Value<string>() ?? "";
    //  var id = obj["id"]?.Value<string>() ?? "";
    //  var seq = obj["seq"]?.Value<int>() ?? 0;
    //  var data = obj["data"];

    //  if (data == null)
    //    throw new JsonSerializationException("Sync event missing 'data'");

    //  // var rebuild data compatible with nswag generated types

    //  var d = new { id = id, type = name.Replace(".1", ""), properties = data };
    //  var s = JObject.FromObject(d);

    //  return name switch
    //  {
    //    "message.updated.1" => new MessageUpdatedSyncEvent
    //    {
    //      EventType = "sync",
    //      Name = name,
    //      Id = id,
    //      Seq = seq,
    //      Data = PolymorphicDeserializer.DeserializeEventMessageUpdated(data, serializer)
    //    },
    //    "message.removed.1" => new GenericSyncEvent
    //    {
    //      EventType = "sync",
    //      Name = name,
    //      Id = id,
    //      Seq = seq,
    //      Data = PolymorphicDeserializer.DeserializeEventMessageRemoved(data, serializer)
    //    },
    //    "message.part.updated.1" => new MessagePartUpdatedSyncEvent
    //    {
    //      EventType = "sync",
    //      Name = name,
    //      Id = id,
    //      Seq = seq,
    //      Data = PolymorphicDeserializer.DeserializeEventMessagePartUpdated(data, serializer)
    //    },
    //    "message.part.removed.1" => new GenericSyncEvent
    //    {
    //      EventType = "sync",
    //      Name = name,
    //      Id = id,
    //      Seq = seq,
    //      Data = PolymorphicDeserializer.DeserializeEventMessagePartRemoved(data, serializer)
    //    },
    //    "session.created.1" => new SessionCreatedSyncEvent
    //    {
    //      EventType = "sync",
    //      Name = name,
    //      Id = id,
    //      Seq = seq,
    //      Data = PolymorphicDeserializer.DeserializeEventSessionCreated(s, serializer)
    //    },
    //    "session.updated.1" => new SessionUpdatedSyncEvent
    //    {
    //      EventType = "sync",
    //      Name = name,
    //      Id = id,
    //      Seq = seq,
    //      Data = PolymorphicDeserializer.DeserializeEventSessionUpdated(data, serializer)
    //    },
    //    "session.deleted.1" => new GenericSyncEvent
    //    {
    //      EventType = "sync",
    //      Name = name,
    //      Id = id,
    //      Seq = seq,
    //      Data = PolymorphicDeserializer.DeserializeEventSessionDeleted(data, serializer)
    //    },
    //    _ => new GenericSyncEvent
    //    {
    //      EventType = "sync",
    //      Name = name,
    //      Id = id,
    //      Seq = seq,
    //      Data = data
    //    }
    //  };
    //}

    //private static SseEvent DeserializeStreamEvent(JObject obj, JsonSerializer serializer)
    //{
    //  var type = obj["type"]?.Value<string>() ?? "";
    //  var sessionID = obj["sessionID"]?.Value<string>() ?? "";
    //  var directory = obj["directory"]?.Value<string>() ?? "";
    //  var properties = obj["properties"] ?? throw new JsonSerializationException("Stream event missing 'properties'");

    //  return type switch
    //  {
    //    "message.part.updated" => //PolymorphicDeserializer.DeserializeEventMessagePartUpdated(obj, serializer)
    //    new MessagePartUpdatedStreamEvent
    //    {
    //      EventType = type,
    //      SessionID = sessionID,
    //      Directory = directory,
    //      Properties = properties
    //    }
    //      ,
    //    "message.updated" => new MessageUpdatedStreamEvent
    //    {
    //      EventType = type,
    //      SessionID = sessionID,
    //      Directory = directory,
    //      Properties = properties
    //    },
    //    "session.status" => new SessionStatusStreamEvent
    //    {
    //      EventType = type,
    //      SessionID = sessionID,
    //      Directory = directory,
    //      Properties = properties
    //    },
    //    "permission.asked" => new PermissionAskedStreamEvent
    //    {
    //      EventType = type,
    //      SessionID = sessionID,
    //      Directory = directory,
    //      Properties = properties
    //    },
    //    "question.asked" => new QuestionAskedStreamEvent
    //    {
    //      EventType = type,
    //      SessionID = sessionID,
    //      Directory = directory,
    //      Properties = properties
    //    },
    //    "suggestion.shown" => new SuggestionShownStreamEvent
    //    {
    //      EventType = type,
    //      SessionID = sessionID,
    //      Directory = directory,
    //      Properties = properties
    //    },
    //    "session.error" => new SessionErrorStreamEvent
    //    {
    //      EventType = type,
    //      SessionID = sessionID,
    //      Directory = directory,
    //      Properties = properties
    //    },
    //    _ => new GenericStreamEvent
    //    {
    //      EventType = type,
    //      SessionID = sessionID,
    //      Directory = directory,
    //      Properties = properties
    //    }
    //  };
    //}
  }

  // Concrete event types for better type safety
  // These extend SyncEvent/StreamEvent but use the NSwag-generated event classes for Data/Properties
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
