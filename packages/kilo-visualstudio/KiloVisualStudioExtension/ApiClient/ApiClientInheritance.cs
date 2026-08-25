using KiloExtensionDTOs;
using KiloExtensionDTOs.ExtensionMessages;
using KiloExtensionDTOs.KiloProviderUtils;
using KiloExtensionDTOs.Parts;
using KiloExtensionDTOs.WebviewMessages;
using KiloVisualStudioExtension.ApiClient.Json;
using KiloVisualStudioExtension.Utils;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace KiloVisualStudioExtension.ApiClient
{
  /// <summary>
  /// Inheritance declarations for polymorphic types defined in the OpenAPI spec.
  /// NSwag generates independent classes for polymorphic schemas (anyOf/oneOf),
  /// but the OpenAPI spec defines inheritance relationships that should be restored
  /// here for proper polymorphic behavior.
  /// </summary>

  public interface IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID);
  }

  public partial class Message
  {
    public string Id { get; set; }
  }

  public partial class AssistantMessage : Message
  {
  }
  public partial class UserMessage : Message { }

  public partial class SyncEventMessageUpdated : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {
      var message = this.SyncEvent?.Data?.Info != null ? MessageConverter.Convert(this.SyncEvent.Data.Info) : null;
      return new MessageCreatedMessage
      {
        Message = message
      };
      ////  this.SyncEvent.Data.Info.
      //  return new MessageCreatedMessage
      //  {
      //    Message = 
      //    {

      //    }
      //  }
      //  const info = event.data.info
      //    return {
      //type: "messageCreated",
      //      message:
      //  {
      //    ...info,
      //        createdAt: new Date(info.time.created).toISOString(),
      //      },
      //    }
    }
  }

  public partial class SyncEventMessageRemoved : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {
      //      case "message.removed.1":
      //        return {
      //          type: "messageRemoved",
      //          sessionID: event.data.sessionID,
      //          messageID: event.data.messageID,
      //        }
      return new MessageRemovedMessage
      {
        MessageID = this.Data.MessageID,
        SessionID = this.Data.SessionID
      };
    }
  }

  public partial class SyncEventSessionUpdated : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {
      //      case "session.updated.1":
      //        return null
      return null;
    }
  }

  public partial class SyncEventSessionDeleted : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {
      //      case "session.deleted.1":
      //        return {
      //          type: "sessionDeleted",
      //          sessionID: event.data.sessionID,
      //        }
      return new SessionDeletedMessage
      {
        SessionID = sessionID
      };
    }
  }

  public partial class SyncEventSessionCreated : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {
      return new SessionCreatedMessage
      {
        //      case "session.created.1":
        //        return {
        //    type: "sessionCreated",
        //          session: sessionToWebview(event.data.info),
        //        }
        Session = EntityConverter.Convert(this.SyncEvent.Data.Info)
      };
    }
  }

  public partial class SyncEventMessagePartUpdated : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {
      return new PartUpdate
      {
        Part = SyncEvent.Data.Part
      };
    }
  }

  public partial class SyncEventMessagePartRemoved : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {
      return new PartRemove
      {
        SessionID = SyncEvent.Data.SessionID,
        MessageID = SyncEvent.Data.MessageID,
        PartID = SyncEvent.Data.PartID
      };
    }
  }

  public partial class EventMessagePartDelta : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {
      if (sessionID == null) return null;
      return new PartUpdate
      {
        SessionID = Properties.SessionID,
        MessageID = Properties.MessageID,
        Part = new TextPart
        {
          Id = Properties.PartID,
          MessageID = Properties.MessageID,
          Text = Properties.Delta
        },
        Delta = new PartDelta { TextDelta = Properties.Delta }
      };
    }
  }

  public partial class EventSessionStatus : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {

      //function statusExtra(info: Extract < Event, { type: "session.status" }> ["properties"]["status"]) {
      //  if (info.type === "retry") return { attempt: info.attempt, message: info.message, next: info.next }
      //  if (info.type === "offline") return { message: info.message }
      //  return { }
      //}

      //    case "session.status":
      //    {
      //      const info = event.properties.status
      //      const status = info.type
      //      const extra = statusExtra(info)
      //      return {
      //      type: "sessionStatus" as const,
      //      sessionID: event.properties.sessionID,
      //        status,
      //        ...extra,
      //      }
      //    }
      return Properties.Status.Type switch
      {
        "retry" => new SessionStatusMessage
        {
          SessionID = Properties.SessionID,
          Status = KiloExtensionDTOs.Connection.SessionStatus.Retry,
          Attempt = Properties.Status.Attempt,
          Message = Properties.Status.Message,
          Next = Properties.Status.Next
        },
        "offline" => new SessionStatusMessage
        {
          SessionID = Properties.SessionID,
          Status = KiloExtensionDTOs.Connection.SessionStatus.Retry,
          Message = Properties.Status.Message,
        },
        _ => new SessionStatusMessage
        {
          SessionID = Properties.SessionID,
          Status = Properties.Status.Type.ToEnum(KiloExtensionDTOs.Connection.SessionStatus.Idle),
        }
      };
    }
  }


 

  public partial class Error
  {
    public string Name { get; set; }

    public object Data { get; set; } = new object();
  }

  public partial class ProviderAuthError : Error { }
  public partial class UnknownError : Error { }
  public partial class MessageOutputLengthError : Error { }
  public partial class MessageAbortedError : Error { }
  public partial class StructuredOutputError : Error { }
  public partial class ContextOverflowError : Error { }
  public partial class ContentFilterError : Error { }
  public partial class APIError : Error { }


  public partial class SyncEvent : Events { }
  public partial class SyncEventMessageUpdated : SyncEvent { }
  public partial class SyncEventMessageRemoved : SyncEvent { }
  public partial class SyncEventMessagePartUpdated : SyncEvent { }
  public partial class SyncEventMessagePartRemoved : SyncEvent { }
  public partial class SyncEventSessionCreated : SyncEvent { }
  public partial class SyncEventSessionUpdated : SyncEvent { }
  public partial class SyncEventSessionRemoved : SyncEvent { }

  public partial class Event : Events { }
  // SSE Event
  public partial class EventMessageUpdated : Event { }
  public partial class EventMessageRemoved : Event { }
  public partial class EventMessagePartUpdated : Event { }
  public partial class EventMessagePartRemoved : Event { }
  public partial class EventMessagePartDelta : Event { }
  public partial class EventSessionCreated : Event { }
  public partial class EventSessionUpdated : Event { }
  public partial class EventSessionDeleted : Event { }
  public partial class EventKiloSessionsRemoteStatusChanged : Event { }
  public partial class EventMemoryStatus : Event, MemoryEventConverter.IMemoryEvent { }
  public partial class EventMemoryUpdated : Event, MemoryEventConverter.IMemoryEvent { }
  public partial class EventMemoryError : Event, MemoryEventConverter.IMemoryEvent { }

  public partial class EventMcpBrowserOpenFailed : Event { }

  public partial class TextPartInput : Parts { }
  public partial class FilePartInput : Parts { }
  public partial class AgentPartInput : Parts { }
  public partial class SubtaskPartInput : Parts { }


  // Interface for all memory API responses
  public interface IMemoryResponseBase
  {
    string? Root { get; }
  }

  // Response classes inheriting from IMemoryResponseBase
  public partial class Response42 : IMemoryResponseBase { }
  public partial class Response43 : IMemoryResponseBase { }
  public partial class Response44 : IMemoryResponseBase { }
  public partial class Response45 : IMemoryResponseBase { }
  public partial class Response46 : IMemoryResponseBase { }
  public partial class Response47 : IMemoryResponseBase { }
  public partial class Response48 : IMemoryResponseBase
  {
    [Newtonsoft.Json.JsonProperty("root", Required = Newtonsoft.Json.Required.AllowNull, NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    public string Root { get; set; }
  }
  public partial class Response49 : IMemoryResponseBase
  {
    [Newtonsoft.Json.JsonProperty("root", Required = Newtonsoft.Json.Required.AllowNull, NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    public string Root { get; set; }
  }
  public partial class Response50 : IMemoryResponseBase
  {
    [Newtonsoft.Json.JsonProperty("root", Required = Newtonsoft.Json.Required.AllowNull, NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    public string Root { get; set; }
  }
  public partial class Response51 : IMemoryResponseBase { }





  public partial class Balance
  {
    [JsonProperty("balance", Required = Required.Always)]
    public double balance { get; set; }
  }

  public partial class KiloPass
  {
    [JsonProperty("currentPeriodBaseCreditsUsd", Required = Required.Always)]
    public double CurrentPeriodBaseCreditsUsd { get; set; }

    [JsonProperty("currentPeriodUsageUsd", Required = Required.Always)]
    public double CurrentPeriodUsageUsd { get; set; }

    [JsonProperty("currentPeriodBonusCreditsUsd", Required = Required.Always)]
    public double CurrentPeriodBonusCreditsUsd { get; set; }

    [JsonProperty("nextBillingAt", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
    public string NextBillingAt { get; set; }
  }

  public partial class CurrentOrgId
  {
    [JsonProperty("$value", Required = Required.Always)]
    public string Value { get; set; }
  }
}
