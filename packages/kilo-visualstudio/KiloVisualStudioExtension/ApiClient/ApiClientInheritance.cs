using KiloExtensionDTOs;
using KiloExtensionDTOs.ExtensionMessages;
using KiloExtensionDTOs.Parts;
using KiloExtensionDTOs.Questions;
using KiloExtensionDTOs.WebviewMessages;
using KiloVisualStudioExtension.ApiClient.Json;
using KiloVisualStudioExtension.Utils;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using Error = KiloVisualStudioExtension.ApiClient.Error;

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
    public string SessionID { get; set; }
    public string Agent { get; set; }

  }

  public partial class AssistantMessage : Message
  {
  }
  public partial class UserMessage : Message { }

  public partial class EventMessageUpdated : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {
      var message = this.Properties?.Info != null ? MessageConverter.Convert(this.Properties.Info) : null;
      return new MessageCreatedMessage
      {
        Message = message
      };
    }
  }

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


  public partial class EventMessageRemoved : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {
      return new MessageRemovedMessage
      {
        MessageID = this.Properties.MessageID,
        SessionID = this.Properties.SessionID
      };
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


  public partial class EventSessionUpdated : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {
      return new SessionUpdatedMessage
      {
        Session = EntityConverter.ConvertUpdate(this.Properties.Info)
      };
    }
  }

  public partial class SyncEventSessionUpdated : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {
      //      case "session.updated.1":
      //        return null
      // why ???
      return null;
    }
  }

  public partial class EventSessionDeleted : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {
      return new SessionDeletedMessage
      {
        SessionID = this.Properties.SessionID
      };
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
        SessionID = this.SyncEvent.Data.SessionID
      };
    }
  }

  public partial class EventSessionCreated : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {
      return new SessionCreatedMessage
      {
        Session = EntityConverter.Convert(Properties.Info)
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

  public partial class EventMessagePartUpdated : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {
      return new PartUpdate
      {
        SessionID = Properties.SessionID,
        //MessageID
        Part = Properties.Part
      };
    }
  }

  public partial class SyncEventMessagePartUpdated : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {
      return new PartUpdate
      {
        SessionID = SyncEvent.Data.SessionID,
        //MessageID
        Part = SyncEvent.Data.Part
      };
    }
  }

  public partial class EventMessagePartRemoved : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {
      return new PartRemove
      {
        SessionID = Properties.SessionID,
        MessageID = Properties.MessageID,
        PartID = Properties.PartID
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


  public partial class EventSessionTurnClose : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {
      //  case "session.turn.close":
      //    return {
      //    type: "sessionTurnClosed",
      //        sessionID: event.properties.sessionID,
      //        reason: event.properties.reason,
      //      }
      return new SessionTurnClosedMessage
      {
        SessionID = this.Properties.SessionID,
        Reason = EntityConverter.Convert(Properties.Reason)
      };
    }
  }


  public partial class EventPermissionAsked : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {
      //  case "permission.asked":
      //    return {
      //    type: "permissionRequest",
      //        permission:
      //      {
      //      id: event.properties.id,
      //          sessionID: event.properties.sessionID,
      //          toolName: event.properties.permission,
      //          patterns: event.properties.patterns ?? [],
      //          always: event.properties.always ?? [],
      //          args: event.properties.metadata,
      //          message: `Permission required: ${event.properties.permission}`,
      //          tool: event.properties.tool,
      //        },
      //      }

      return new PermissionRequestMessage
      {
        Permission = new KiloExtensionDTOs.Permissions.PermissionRequest
        {
          Id = Properties.Id,
          SessionID = Properties.SessionID,
          ToolName = Properties.Permission,
          Patterns = Properties.Patterns.ToList(),
          Always = Properties.Always.ToList(),
          Args = Properties.Metadata,
          Message = $"Permission required: {Properties.Permission}",
          Tool = EntityConverter.Convert(Properties.Tool)
        }
      };
    }
  }

  public partial class EventPermissionReplied : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {
      //  case "permission.replied":
      //    return {
      //    type: "permissionResolved",
      //        permissionID: event.properties.requestID,
      //      }
      return new PermissionResolvedMessage
      {
        PermissionID = this.Properties.RequestID
      };
    }
  }

  public partial class EventTodoUpdated : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {
      //  case "todo.updated":
      //    return {
      //    type: "todoUpdated",
      //        sessionID: event.properties.sessionID,
      //        items: event.properties.todos,
      //      }
      return new TodoUpdatedMessage
      {
        SessionID = Properties.SessionID,
        Items = EntityConverter.Convert(Properties.Todos)
      };
    }
  }

  public partial class EventQuestionAsked : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {
      //  case "question.asked":
      //    return {
      //    type: "questionRequest",
      //        question:
      //      {
      //      id: event.properties.id,
      //          sessionID: event.properties.sessionID,
      //          questions: event.properties.questions,
      //          blocking: event.properties.blocking,
      //          tool: event.properties.tool,
      //        },
      //      }
      return new QuestionRequestMessage
      {
        Question = new KiloExtensionDTOs.Questions.QuestionRequest
        {
          Id = Properties.Id,
          SessionID = Properties.SessionID,
          Questions = EntityConverter.Convert(Properties.Questions),
          Blocking = Properties.Blocking,
          Tool = EntityConverter.Convert(Properties.Tool),
        }
      };
    }
  }

  public partial class EventQuestionReplied : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {
      //  case "question.replied":
      //  case "question.rejected":
      //    return {
      //    type: "questionResolved",
      //        requestID: event.properties.requestID,
      //      }
      return new QuestionResolvedMessage
      {
        RequestID = Properties.RequestID
      };
    }
  }

  public partial class EventQuestionRejected : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {
      //  case "question.replied":
      //  case "question.rejected":
      //    return {
      //    type: "questionResolved",
      //        requestID: event.properties.requestID,
      //      }
      return new QuestionResolvedMessage
      {
        RequestID = Properties.RequestID
      };
    }
  }

  public partial class EventSuggestionShown : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {
      //  case "suggestion.shown":
      //    return {
      //    type: "suggestionRequest",
      //        suggestion:
      //      {
      //      id: event.properties.id,
      //          sessionID: event.properties.sessionID,
      //          text: event.properties.text,
      //          actions: event.properties.actions,
      //          blocking: event.properties.blocking,
      //          tool: event.properties.tool,
      //        },
      //      }
      return new SuggestionRequestMessage
      {
        Suggestion = new KiloExtensionDTOs.Questions.SuggestionRequest
        {
          Id = Properties.Id,
          SessionID = Properties.SessionID,
          Text = Properties.Text,
          Actions = EntityConverter.Convert(Properties.Actions),
          Blocking = Properties.Blocking,
          Tool = EntityConverter.Convert(Properties.Tool)
        }
      };
    }
  }

  public partial class EventSuggestionAccepted : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {
      //  case "suggestion.accepted":
      //  case "suggestion.dismissed":
      //    return {
      //    type: "suggestionResolved",
      //        requestID: event.properties.requestID,
      //      }
      return new SuggestionResolvedMessage
      {
        RequestID = Properties.RequestID
      };
    }
  }

  public partial class EventSuggestionDismissed : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {
      //  case "suggestion.accepted":
      //  case "suggestion.dismissed":
      //    return {
      //    type: "suggestionResolved",
      //        requestID: event.properties.requestID,
      //      }
      return new SuggestionResolvedMessage
      {
        RequestID = Properties.RequestID
      };
    }
  }

  public partial class EventSessionError : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {
      //  case "session.error":
      //    {
      //      return {
      //      type: "sessionError",
      //        sessionID: event.properties.sessionID,
      //        error: event.properties.error,
      //      }
      //    }
      return new SessionErrorMessage
      {
        SessionID = Properties.SessionID,
        Error = new KiloExtensionDTOs.Sessions.MessageErrorType
        {
          Name = Properties.Error.AdditionalProperties["name"].ToString(),
          Data = Properties.Error.AdditionalProperties["data"]
        }
      };
    }
  }

  public partial class EventSandboxStatusChanged : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {
      //  case "sandbox.status.changed":
      //    return {
      //    type: "sandboxStatus",
      //        sessionID: event.properties.sessionID,
      //        directory: event.properties.directory,
      //        enabled: event.properties.enabled,
      //        available: event.properties.available,
      //        reason: event.properties.reason,
      //        version: event.properties.version,
      //      }
      return new SandboxStatusMessage
      {
        SessionID = Properties.SessionID,
        Directory = Properties.Directory,
        Enabled = Properties.Enabled,
        Available = Properties.Available,
        Reason = Properties.Reason,
        Version = Properties.Version,
      };
    }
  }

  public partial class EventIndexingStatus : IWebviewMappable
  {
    public IWebviewMessage GetWebViewMessage(string sessionID)
    {

      //  case "indexing.status":
      //    return {
      //    type: "indexingStatusLoaded",
      //        status: event.properties.status,
      //      }
      //  default:
      //    return null
      //  }
      //}    }
      return new IndexingStatusLoadedMessage
      {
        Status = EntityConverter.Convert(Properties.Status)
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
          Status = KiloExtensionDTOs.Connection.SessionStatusEnum.Retry,
          Attempt = Properties.Status.Attempt,
          Message = Properties.Status.Message,
          Next = Properties.Status.Next
        },
        "offline" => new SessionStatusMessage
        {
          SessionID = Properties.SessionID,
          Status = KiloExtensionDTOs.Connection.SessionStatusEnum.Retry,
          Message = Properties.Status.Message,
        },
        _ => new SessionStatusMessage
        {
          SessionID = Properties.SessionID,
          Status = Properties.Status.Type.ToEnum(KiloExtensionDTOs.Connection.SessionStatusEnum.Idle),
        }
      };
    }
  }




  public partial class Error
  {
    public string Name { get; set; }

    public object Data { get; set; } = new object();
  }

  public interface IEvent
  {

  }

  public interface IEventSessionErrorError
  {

  }



  public partial class ProviderAuthError : Error, IEventSessionErrorError { }
  public partial class UnknownError : Error, IEventSessionErrorError { }
  public partial class MessageOutputLengthError : Error, IEventSessionErrorError { }
  public partial class MessageAbortedError : Error, IEventSessionErrorError { }
  public partial class StructuredOutputError : Error, IEventSessionErrorError { }
  public partial class ContextOverflowError : Error, IEventSessionErrorError { }
  public partial class ContentFilterError : Error, IEventSessionErrorError { }
  public partial class APIError : Error, IEventSessionErrorError { }
  public partial class AgentRequirementError : IEventSessionErrorError { }

  public partial class SyncEvent : IEvent { }
  public partial class SyncEventMessageUpdated : SyncEvent { }
  public partial class SyncEventMessageRemoved : SyncEvent { }
  public partial class SyncEventMessagePartUpdated : SyncEvent { }
  public partial class SyncEventMessagePartRemoved : SyncEvent { }
  public partial class SyncEventSessionCreated : SyncEvent { }
  public partial class SyncEventSessionUpdated : SyncEvent { }
  public partial class SyncEventSessionRemoved : SyncEvent { }


  public partial class Event : IEvent { }
  // SSE Event
  public partial class EventMessagePartUpdated : Event
  {
    /// <summary>
    /// Extracts the child ID from a Part if it's a tool task part.
    /// Returns sessionId from part.metadata.sessionId or part.state.metadata.sessionId
    /// </summary>
    public string? GetPartChildId()
    {
      if (Properties.Part == null) return null;

      var additional = Properties.Part.AdditionalProperties;

      // Check if type is "tool" and tool is "task"
      if (!additional.TryGetValue("type", out var typeObj) || typeObj?.ToString() != "tool")
        return null;

      if (!additional.TryGetValue("tool", out var toolObj) || toolObj?.ToString() != "task")
        return null;

      // Try metadata.sessionId first
      if (additional.TryGetValue("metadata", out var metadataObj) &&
          metadataObj is IDictionary<string, object> metadataDict)
      {
        if (metadataDict.TryGetValue("sessionId", out var sessionIdObj))
          return sessionIdObj?.ToString();
      }

      // Fallback to state.metadata.sessionId
      if (additional.TryGetValue("state", out var stateObj) &&
          stateObj is IDictionary<string, object> stateDict)
      {
        if (stateDict.TryGetValue("metadata", out var stateMetadataObj) &&
            stateMetadataObj is IDictionary<string, object> stateMetadataDict)
        {
          if (stateMetadataDict.TryGetValue("sessionId", out var stateSessionIdObj))
            return stateSessionIdObj?.ToString();
        }
      }

      return null;
    }

  }
  public partial class EventMemoryStatus : Event, MemoryEventConverter.IMemoryEvent { }
  public partial class EventMemoryUpdated : Event, MemoryEventConverter.IMemoryEvent { }
  public partial class EventMemoryError : Event, MemoryEventConverter.IMemoryEvent { }
  public partial class EventServerInstanceDisposed : Event { }
  public partial class EventSessionTurnOpen : Event { }
  public partial class EventSessionTurnClose : Event { }
  public partial class EventSessionQueueChanged : Event { }
  public partial class EventSessionNetworkAsked : Event { }
  public partial class EventSessionNetworkReplied : Event { }
  public partial class EventSessionNetworkRejected : Event { }
  public partial class EventSessionNetworkRestored : Event { }
  public partial class EventBackgroundProcessUpdated : Event { }
  public partial class EventBackgroundProcessDeleted : Event { }
  public partial class EventInteractiveTerminalUpdated : Event { }
  public partial class EventInteractiveTerminalData : Event { }
  public partial class EventInteractiveTerminalDeleted : Event { }
  public partial class EventSandboxStatusChanged : Event { }
  public partial class EventSuggestionShown : Event { }
  public partial class EventSuggestionAccepted : Event { }
  public partial class EventSuggestionDismissed : Event { }
  public partial class EventKilocodeAgentManagerStart : Event { }
  public partial class EventKilocodeAgentManagerRequested : Event { }
  public partial class EventKilocodeAgentManagerCancelled : Event { }
  public partial class EventKilocodeNotebookRequested : Event { }
  public partial class EventKilocodeNotebookCancelled : Event { }
  public partial class EventKiloSessionsRemoteStatusChanged : Event { }
  public partial class EventLspClientDiagnostics : Event { }
  public partial class EventMemoryStatus1 : Event { }
  public partial class EventMemoryUpdated1 : Event { }
  public partial class EventMemoryError1 : Event { }
  public partial class EventIndexingStatus : Event { }
  public partial class EventIndexingWarning : Event { }
  public partial class EventServerConnected : Event { }
  public partial class EventGlobalDisposed : Event { }
  public partial class EventGlobalConfigUpdated : Event { }
  public partial class EventPluginAdded : Event { }
  public partial class EventCatalogModelUpdated : Event { }
  public partial class EventSessionCreated : Event { }
  public partial class EventSessionUpdated : Event { }
  public partial class EventSessionDeleted : Event { }
  public partial class EventMessageUpdated : Event { }
  public partial class EventMessageRemoved : Event { }
  public partial class EventMessagePartRemoved : Event { }
  public partial class EventSessionNextAgentSwitched : Event { }
  public partial class EventSessionNextModelSwitched : Event { }
  public partial class EventSessionNextMoved : Event { }
  public partial class EventSessionNextPrompted : Event { }
  public partial class EventSessionNextPromptAdmitted : Event { }
  public partial class EventSessionNextPromptPromoted : Event { }
  public partial class EventSessionNextInterruptRequested : Event { }
  public partial class EventSessionNextContextUpdated : Event { }
  public partial class EventSessionNextSynthetic : Event { }
  public partial class EventSessionNextShellStarted : Event { }
  public partial class EventSessionNextShellEnded : Event { }
  public partial class EventSessionNextStepStarted : Event { }
  public partial class EventSessionNextStepEnded : Event { }
  public partial class EventSessionNextStepFailed : Event { }
  public partial class EventSessionNextTextStarted : Event { }
  public partial class EventSessionNextTextDelta : Event { }
  public partial class EventSessionNextTextEnded : Event { }
  public partial class EventSessionNextReasoningStarted : Event { }
  public partial class EventSessionNextReasoningDelta : Event { }
  public partial class EventSessionNextReasoningEnded : Event { }
  public partial class EventSessionNextToolInputStarted : Event { }
  public partial class EventSessionNextToolInputDelta : Event { }
  public partial class EventSessionNextToolInputEnded : Event { }
  public partial class EventSessionNextToolCalled : Event { }
  public partial class EventSessionNextToolProgress : Event { }
  public partial class EventSessionNextToolSuccess : Event { }
  public partial class EventSessionNextToolFailed : Event { }
  public partial class EventSessionNextRetried : Event { }
  public partial class EventSessionNextCompactionStarted : Event { }
  public partial class EventSessionNextCompactionDelta : Event { }
  public partial class EventSessionNextCompactionEnded : Event { }
  public partial class EventQuestionAsked : Event { }
  public partial class EventQuestionReplied : Event { }
  public partial class EventQuestionRejected : Event { }
  public partial class EventTuiPromptAppend : Event { }
  public partial class EventTuiCommandExecute : Event { }
  public partial class EventTuiToastShow1 : Event { }
  public partial class EventTuiSessionSelect : Event { }
  public partial class EventMcpToolsChanged : Event { }
  public partial class EventMcpBrowserOpenFailed : Event { }
  public partial class EventMessagePartDelta : Event { }
  public partial class EventSessionDiff : Event { }
  public partial class EventSessionError : Event { }
  public partial class EventModelsDevRefreshed : Event { }
  public partial class EventInstallationUpdated : Event { }
  public partial class EventInstallationUpdateAvailable : Event { }
  public partial class EventPermissionAsked : Event { }
  public partial class EventPermissionReplied : Event { }
  public partial class EventReferenceUpdated : Event { }
  public partial class EventIntegrationUpdated : Event { }
  public partial class EventPermissionV2Asked : Event { }
  public partial class EventPermissionV2Replied : Event { }
  public partial class EventProjectDirectoriesUpdated : Event { }
  public partial class EventFileEdited : Event { }
  public partial class EventFileWatcherUpdated : Event { }
  public partial class EventPtyCreated : Event { }
  public partial class EventPtyUpdated : Event { }
  public partial class EventPtyExited : Event { }
  public partial class EventPtyDeleted : Event { }
  public partial class EventQuestionV2Asked : Event { }
  public partial class EventQuestionV2Replied : Event { }
  public partial class EventQuestionV2Rejected : Event { }
  public partial class EventTodoUpdated : Event { }
  public partial class EventSessionStatus : Event { }
  public partial class EventSessionIdle : Event { }
  public partial class EventSessionCompacted : Event { }
  public partial class EventCommandExecuted : Event { }
  public partial class EventProjectUpdated : Event { }
  public partial class EventVcsBranchUpdated : Event { }
  public partial class EventLspUpdated : Event { }
  public partial class EventWorkspaceReady : Event { }
  public partial class EventWorkspaceFailed : Event { }
  public partial class EventWorkspaceStatus : Event { }
  public partial class EventWorktreeReady : Event { }
  public partial class EventWorktreeFailed : Event { }




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
  public partial class MemoryConfigureResponseSchema200 : IMemoryResponseBase { }
  public partial class MemoryStatusResponseSchema200 : IMemoryResponseBase { }
  public partial class MemoryShowResponseSchema200 : IMemoryResponseBase { }
  public partial class MemoryEnableResponseSchema200 : IMemoryResponseBase { }
  public partial class MemoryDisableResponseSchema200 : IMemoryResponseBase { }
  public partial class MemoryDisableResponseSchema200 : IMemoryResponseBase { }
  public partial class MemoryRebuildResponseSchema200 : IMemoryResponseBase { }
  public partial class MemoryRememberResponseSchema200 : IMemoryResponseBase
  {
    [Newtonsoft.Json.JsonProperty("root", Required = Newtonsoft.Json.Required.AllowNull, NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    public string Root { get; set; }
  }
  public partial class MemoryCorrectResponseSchema200 : IMemoryResponseBase
  {
    [Newtonsoft.Json.JsonProperty("root", Required = Newtonsoft.Json.Required.AllowNull, NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    public string Root { get; set; }
  }
  public partial class MemoryForgetResponseSchema200 : IMemoryResponseBase
  {
    [Newtonsoft.Json.JsonProperty("root", Required = Newtonsoft.Json.Required.AllowNull, NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    public string Root { get; set; }
  }
  public partial class MemoryPurgeResponseSchema200 : IMemoryResponseBase { }





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
