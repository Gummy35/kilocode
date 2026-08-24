using KiloExtensionDTOs.ExtensionMessages;
using KiloExtensionDTOs.WebviewMessages;
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
    public WebviewMessage GetWebViewMessage(string sessionID);
  }

  public partial class Message
  {
    public string Id { get; set; }
  }


  public partial class AssistantMessage : Message {

    [Newtonsoft.Json.JsonProperty("error", Required = Newtonsoft.Json.Required.DisallowNull, NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.IsoDateTimeConverter))]
    public Error Error { get; set; }
  }
  public partial class UserMessage : Message { }

  public partial class SyncEventMessageUpdated : IWebviewMappable
  {
    public KiloExtensionDTOs.WebviewMessages.WebviewMessage GetWebViewMessage(string sessionID)
    {

      return MessageConverter.Convert(this.SyncEvent.Data.Info);
    //  this.SyncEvent.Data.Info.
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

  
  public partial class ProviderAuthError : Error { }
  public partial class UnknownError : Error { }
  public partial class MessageOutputLengthError : Error { }
  public partial class MessageAbortedError : Error { }
  public partial class StructuredOutputError : Error { }
  public partial class ContextOverflowError : Error { }
  public partial class ContentFilterError : Error { }
  public partial class APIError : Error { }
  

  public partial class SyncEventMessageUpdated : SyncEvent { }
  public partial class SyncEventMessageRemoved : SyncEvent { }
  public partial class SyncEventMessagePartUpdated : SyncEvent { }
  public partial class SyncEventMessagePartRemoved : SyncEvent { }
  public partial class SyncEventSessionCreated : SyncEvent { }
  public partial class SyncEventSessionUpdated : SyncEvent { }
  public partial class SyncEventSessionRemoved : SyncEvent { }

  // SSE Event
  public partial class EventMessageUpdated : Event { }
  public partial class EventMessageRemoved : Event { }
  public partial class EventMessagePartUpdated : Event { }
  public partial class EventMessagePartRemoved : Event { }
  public partial class EventSessionCreated: Event { }
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
  public partial class Response48 : IMemoryResponseBase {
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
