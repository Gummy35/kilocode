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




}
