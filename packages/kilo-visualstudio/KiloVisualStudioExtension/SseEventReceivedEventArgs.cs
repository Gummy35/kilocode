using Newtonsoft.Json.Linq;
using System;

namespace KiloVisualStudioExtension
{
  /// <summary>
  /// Event arguments for SSE (Server-Sent Events) received from the backend.
  /// </summary>
  public class SseEventReceivedEventArgs : EventArgs
  {
    /// <summary>
    /// The type of SSE event (e.g., "session.created", "message.part.delta").
    /// Used for unit tests
    /// </summary>
    public string EventType { get; }

    /// <summary>
    /// The event data payload as a JSON object.
    /// </summary>
    public JObject Payload { get; }

    public string Directory { get; }

    public string Project { get; }

    public string SessionID { get; }

    private bool? isLegacy = null; 

    public bool IsLegacySyncEvent { 
      get
      {
        if (isLegacy == null)
          isLegacy = this.isLegacySyncEvent();
        return isLegacy.Value;
      } 
    }

    /// <summary>
    /// Creates a new instance of SseEventReceivedEventArgs.
    /// </summary>
    /// <param name="eventType">The event type.</param>
    /// <param name="data">The event data.</param>
    public SseEventReceivedEventArgs(string eventType, string data)
    {
      EventType = eventType;
      var Data = JObject.Parse(data);
      Directory = Data["directory"]?.Value<string>() ?? "";
      Project = Data["project"]?.Value<string>() ?? "";
      Payload = (JObject)Data["payload"] ?? null;
    }

    public JToken NormalizeEvent()
    {
      var type = Payload["type"]?.Value<string>() ?? "";
      if (type != "sync") return Payload;
      var ev = Payload["syncEvent"];
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

    public JObject UnwrapSyncEvent()
    {
      var type = Payload["type"]?.Value<string>() ?? "";
      if (type != "sync") return Payload;
      var payload = Payload["syncEvent"] != null ? NormalizeEvent() : Payload;
      var name = payload["name"]?.Value<string>() ?? "";

      return name switch
      {
        "message.updated.1" => JObject.FromObject(new { id = payload["id"], type = "message.updated", properties = payload["data"] }),
        "message.removed.1" => JObject.FromObject(new { id = payload["id"], type = "message.removed", properties = payload["data"] }),
        "message.part.updated.1" => JObject.FromObject(new { id = payload["id"], type = "message.part.updated", properties = payload["data"] }),
        "message.part.removed.1" => JObject.FromObject(new { id = payload["id"], type = "message.part.removed", properties = payload["data"] }),
        "session.created.1" => JObject.FromObject(new { id = payload["id"], type = "session.created", properties = payload["data"] }),
        "session.updated.1" => JObject.FromObject(new { source = "sync", id = payload["id"], seq = payload["seq"], type = "session.updated", properties = payload["data"] }),
        "message.deleted.1" => JObject.FromObject(new { id = payload["id"], type = "session.deleted", properties = payload["data"] }),
        _ => null
      };
    }

    protected bool isLegacySyncEvent()
    {
      var obj = UnwrapSyncEvent();
      var type = obj["type"]?.Value<string>() ?? "";
      
      if (type == "session.updated") 
        return (obj["source"]?.Value<string>() ?? "") == "sync";

      return
        (type == "message.updated") ||
        (type == "message.removed") ||
        (type == "message.part.updated") ||
        (type == "message.part.removed") ||
        (type == "session.created") ||
        (type == "session.deleted");
    }
  }
}
