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


  public partial class Message
  {
    public string Id { get; set; }
  }

 
  public partial class AssistantMessage : Message { }
  public partial class UserMessage : Message { }


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
