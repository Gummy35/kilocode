using Common;
using EnvDTE;
using Extensibility;
using KiloExtensionDTOs;
using KiloExtensionDTOs.ExtensionMessages;
using KiloExtensionDTOs.KiloProviderUtils;
using KiloExtensionDTOs.Sessions;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.ApiClient.Sse;
using KiloVisualStudioExtension.Services;
using KiloVisualStudioExtension.Services.Handlers.Followup;
using KiloVisualStudioExtension.Services.Handlers.Indexing;
using KiloVisualStudioExtension.Services.Handlers.Mcp;
using KiloVisualStudioExtension.Services.Handlers.Memory;
using KiloVisualStudioExtension.Services.Handlers.Model;
using KiloVisualStudioExtension.Services.Handlers.Network;
using KiloVisualStudioExtension.Services.Handlers.Sandbox;
using KiloVisualStudioExtension.Services.Handlers.Session;
using KiloVisualStudioExtension.Utils;
using Microsoft.VisualStudio.Debugger.Interop;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using static KiloVisualStudioExtension.Services.MessagePageFetcher;
using static Microsoft.VisualStudio.Shell.ThreadedWaitDialogHelper;
using static System.Net.Mime.MediaTypeNames;
using Events = KiloVisualStudioExtension.ApiClient.Events;
using WebViewMessage = KiloExtensionDTOs.Sessions.Message;

namespace KiloVisualStudioExtension
{

  internal record NetworkWait
  {
    internal string Sid;
    internal long Refs;
  }

  public class SSEHandlerService : ServiceProviderServiceBase
  {
    private bool _disposed;

    private VSProvider Provider => _serviceProvider.GetService<VSProvider>()
        ?? throw new InvalidOperationException("VSProvider not registered in service provider");

    private MessageConfirmation Confirmations => ServiceProviderExtensions.GetService<MessageConfirmation>(_serviceProvider);

    private SessionHandlerService SessionHandler => ServiceProviderExtensions.GetService<SessionHandlerService>(_serviceProvider);

    private FollowupService FollowupHandler => ServiceProviderExtensions.GetService<FollowupService>(_serviceProvider);

    private IndexingService IndexingHandler => ServiceProviderExtensions.GetService<IndexingService>(_serviceProvider);

    private SandboxService SandboxHandler => ServiceProviderExtensions.GetService<SandboxService>(_serviceProvider);

    private NetworkService NetworkHandler => ServiceProviderExtensions.GetService<NetworkService>(_serviceProvider);
    private readonly HashSet<string> memoryEvents = ["memory.status", "memory.updated", "memory.error"];
    private readonly HashSet<string> sessionScopedPartEvents = ["message.part.updated", "message.part.delta", "message.part.removed"];
    private readonly Dictionary<string, NetworkWait> _networkWaits = new();

    // Session state moved to SessionHandlerService - commented out to preserve for potential future use
    // private readonly HashSet<string> _trackedSessionIds = new HashSet<string>();
    // private readonly Dictionary<string, string> _sessionStatusMap = new Dictionary<string, string>();
    // private readonly Dictionary<string, string> _sessionDirectories = new Dictionary<string, string>();
    // private readonly Dictionary<string, SessionRevision> _revisions = new Dictionary<string, SessionRevision>();
    // private readonly HashSet<string> _modelUsageSessionIds = new HashSet<string>();
    // private readonly Dictionary<string, MessageCost> _messageCosts = new Dictionary<string, MessageCost>();
    // private readonly Dictionary<string, string> _messageSessionIds = new Dictionary<string, string>();
    // private readonly Dictionary<string, string> _networkWaits = new Dictionary<string, string>();
    //private readonly HashSet<string> _trackedSessionIds = new HashSet<string>();
    //private readonly Dictionary<string, string> _sessionDirectories = new Dictionary<string, string>();
    private ProjectDirectoryProvider _projectDirectoryProvider => ServiceProviderExtensions.GetService<ProjectDirectoryProvider>(_serviceProvider);

    //private int _sandboxRevision = 0; // Commented out - moved to SandboxHandlerService

    //private Followup _pendingFollowup; // Commented out - moved to FollowupHandlerService
    //private string? _cachedIndexingStatusMessage = null; // Commented out - moved to IndexingHandlerService
    private string? _currentProjectID = null;

    private RemoteStatusService? _remoteService;


    public string? CurrentProjectID
    {
      get => _currentProjectID;
      set => _currentProjectID = value;
    }


    //internal string ResolveDirectory(string? sessionID = null)
    //{
    //  return _projectDirectoryProvider.GetWorkspaceDirectory(sessionID)
    //    ?? System.Environment.CurrentDirectory;
    //}

    private readonly Action<object> _postMessage;
    private readonly JsonSerializer _serializer;

    public SSEHandlerService(ServiceProvider serviceProvider, Action<object> postMessage/*, DTE dte = null*/) : base(serviceProvider)
    {
      _postMessage = postMessage;
      _serializer = KiloJsonSerializer.Create();

      //if (dte != null)
      //{
      //  var vsProvider = _serviceProvider.AddService(new VisualStudioDirectoryProvider(serviceProvider, dte));
      //  _projectDirectoryProvider = vsProvider.CreateProvider(
      //      projectDirectoryOverride: null // or specify a path like @"C:\MyProject"
      //                                     //sessionDirectories: _sessionDirectories
      //      );
      //  _serviceProvider.AddService(_projectDirectoryProvider);
      //}
    }

    /// <summary>
    /// Sets the RemoteStatusService for handling remote control state.
    /// </summary>
    public void SetRemoteStatusService(RemoteStatusService service)
    {
      _remoteService = service;
    }

    public void PostMessage(object message)
    {
      _postMessage(message);
    }

    public void HandleEvent(SseEventReceivedEventArgs raw)
    {
      try
      {
        var _sessionService = _serviceProvider.GetService<SessionHandlerService>();
//        System.Diagnostics.Debug.WriteLine($"SSE Event received : {raw.EventType}");

        var e = SseEventDeserializer.Deserialize(raw);
        if (e == null) return;
        System.Diagnostics.Debug.WriteLine($"SSE Event received : {e.Type}");

        var sessionId = ResolveEventSessionId(raw);

        var evt = (IEvent)e.Data;

        var directory = raw.Directory;

        // if (event.type === "kilo-sessions.remote-status-changed") {
        if (evt is EventKiloSessionsRemoteStatusChanged ev)
        {
          _remoteService?.UpdateFromEvent(new RemoteState { Enabled = ev.Properties.Enabled, Connected = ev.Properties.Connected });
          return;
        }

        // if (event.type === "memory.status" || event.type === "memory.updated" || event.type === "memory.error") {
        if (evt is EventMemoryStatus || evt is EventMemoryUpdated || evt is EventMemoryError)
        {
          // const props = event.properties as { sessionID?: unknown; detail?: unknown; reason?: unknown }
          var props = raw.Payload;
          // const eventSessionID = typeof props.sessionID === "string" ? props.sessionID : undefined
          // const active = this.currentSession?.id
          var active = Provider.GetCurrentSessionID();
          // const local =

          //   !directory || sameDirectory(directory, this.getProjectDirectory(active) ?? this.getWorkspaceDirectory(active))
          var local = string.IsNullOrEmpty(directory) ||
            PathUtils.SameDirectory(
              directory,
              _projectDirectoryProvider.GetProjectDirectory(active)
                ?? _projectDirectoryProvider.GetWorkspaceDirectory(active)
            );
          // const trackedById = Boolean(eventSessionID && this.trackedSessionIds.has(eventSessionID))
          var trackedById = !string.IsNullOrEmpty(sessionId) && SessionHandler.IsTrackedSession(sessionId);
          // Directory-scoped events (enable/disable/rebuild/configure/purge) carry no
          // sessionID, so also match any tracked session sharing the event directory —
          // e.g. a non-active Agent Manager tab on the same worktree.
          // const trackedByDir = directory
          //   ? [...this.sessionDirectories.entries()]
          //       .filter(([sid, dir]) => this.trackedSessionIds.has(sid) && sameDirectory(directory, dir))
          //       .map(([sid]) => sid)
          //   : []
          var trackedByDir = !string.IsNullOrEmpty(directory)
            ? _projectDirectoryProvider.GetSessionsByDirectory(directory)
            : new List<string>();
          // const tracked = trackedById || trackedByDir.length > 0
          var tracked = trackedById || trackedByDir.Count > 0;
          // if (!local && !tracked) return
          if (!local && !tracked) return;
          // if (trackedById && eventSessionID && directory) this.trackDirectory(eventSessionID, directory)
          if (trackedById && !string.IsNullOrEmpty(sessionId) && !string.IsNullOrEmpty(directory)) _projectDirectoryProvider.TrackDirectory(sessionId, directory);
          // const targets = new Set<string | undefined>()
          var targets = new HashSet<string>();
          // if (trackedById && eventSessionID) targets.add(eventSessionID)
          if (trackedById && !string.IsNullOrEmpty(sessionId)) targets.Add(sessionId);
          // for (const sid of trackedByDir) targets.add(sid)
          foreach (var sid in trackedByDir) targets.Add(sid);
          // if (local && active) targets.add(active)
          if (local && !string.IsNullOrEmpty(active)) targets.Add(active);
          // if (targets.size === 0 && local) targets.add(undefined)
          if (targets.Count == 0 && local) targets.Add(null);
          // const detail =
          //   props.detail && typeof props.detail === "object"
          //     ? props.detail
          //     : event.type === "memory.error" && typeof props.reason === "string"
          //       ? { type: "error", message: props.reason, reason: props.reason }
          //       : undefined

          KiloExtensionDTOs.Memory.MemoryEventDetail detail = null;
          var rawDetails = raw.Payload["properties"]?["detail"] ?? null;
          if (rawDetails != null)
          {
            detail = ((MemoryEventConverter.IMemoryEvent)evt).ToMemoryEventDetail();
          }
          else
          {
            if (evt is EventMemoryError)
            {
              var reason = raw.Payload["properties"]?["reason"]?.Value<string>() ?? null;
              if (reason != null)
              {
                //JToken detail = props["detail"]?.Type == JTokenType.Object
                //  ? props["detail"]
                //  : e.EventType == "memory.error" && props["reason"]?.Type == JTokenType.String
                //    ? new JObject { ["type"] = "error", ["message"] = props["reason"], ["reason"] = props["reason"] }
                //    : null;
                detail = new KiloExtensionDTOs.Memory.MemoryEventDetail
                {
                  Type = SkippedErrorSavedRecalledEnum.Error,
                  Message = reason,
                  Reason = reason
                };
              }
            }
          }
          // for (const sessionID of targets) {
          foreach (var target in targets)
          {
            // if (detail) {
            if (detail != null)
            {
              // this.postMessage({
              //   type: "memoryEvent",
              //   sessionID,
              //   detail,
              // })
              PostMessage(new KiloExtensionDTOs.Memory.MemoryEventMessage { SessionID = target, Detail = detail });
            }
            // void this.memory.fetch(sessionID)

            _serviceProvider.GetService<MemoryService>().Fetch(target);
          }
          // return
          return;
        }

        // Drop session events from other projects before any tracking logic.
        // This must come first: the trackedSessionIds guard below would otherwise
        // let a foreign session through if it was accidentally tracked.
        // if (!isLegacySyncEvent(event) && isEventFromForeignProject(event, this.projectID)) return

        if (!raw.IsLegacySyncEvent && IsEventFromForeignProject(e.Type, CurrentProjectID)) return;
        // if (
        //   this.projectID &&
        //   (event.type === "session.created" || event.type === "session.updated") &&
        //   event.properties.info.projectID !== undefined &&
        //   event.properties.info.projectID !== null &&
        //   event.properties.info.projectID !== this.projectID
        // ) {
        if (!string.IsNullOrEmpty(CurrentProjectID)
          && (evt is EventSessionCreated || evt is EventSessionUpdated))
        {
          var projectId = raw.Payload["properties"]?["info"]?["projectID"]?.Value<string>() ?? null;
          if (projectId != null && projectId != CurrentProjectID)
            return;
        }


        // if (event.type === "mcp.browser.open.failed") {
        if (evt is EventMcpBrowserOpenFailed)
        {
          var typedEvent = (EventMcpBrowserOpenFailed)evt;
          // McpOAuth.openMcpOAuthUrlOnce(event.properties.url)
          _serviceProvider.GetService<McpHandlerService>().OpenMcpOAuthUrlOnce(typedEvent.Properties.Url);
          // return
          return;
        }

        // if (event.type === "message.updated") {
        if (evt is EventMessageUpdated)
        {
          var typedEvent = (EventMessageUpdated)evt;
          // this.confirmations.confirm(event.properties.info.id)
          this.Confirmations.Confirm(typedEvent.Properties.Info.Id);
        }

        // session.status events pass the onEventFiltered pre-filter for all providers (see line 842),
        // so this runs on every KiloProvider instance — including the Settings panel which has no
        // tracked sessions. Update sessionStatusMap and forward to webview before the
        // trackedSessionIds guard so the Settings panel's allStatusMap stays current for the
        // busy-session warning on Save.
        // if (event.type === "session.status") {
        if (evt is EventSessionStatus)
        {
          var typedEvent = (EventSessionStatus)evt;
          // const sid = event.properties.sessionID
          var type = typedEvent.Properties.Status.Type;
          // const prev = this.sessionStatusMap.get(sid)
          var prev = SessionHandler.GetSessionStatus(sessionId);
          // if ((prev === undefined || prev === "idle") && event.properties.status.type !== "idle") {
          if ((prev == null || prev == "idle") && type != "idle")
          {
            // this.costs.rearm(sid)
            _serviceProvider.GetService<CostService>().Rearm(sessionId);
          }
          // this.sessionStatusMap.set(sid, event.properties.status.type)
          SessionHandler.SetSessionStatus(sessionId, type);
          // this.aborts.observe(sid, event.properties.status.type, directory)
          SessionHandler.Aborts.Observe(sessionId, type, directory);
          // const msg = mapSSEEventToWebviewMessage(event, sid)
          var msg = MapSSEEventToWebviewMessage((Event)e.Data, sessionId);
          // if (msg) {
          if (msg != null)
          {
            // this.streams.flush(sid)
            _serviceProvider.GetService<SessionStreamScheduler>().Flush(sessionId);
            // this.postMessage(msg)
            PostMessage(msg);
          }
          // return
          return;
        }

        // Extract sessionID from the event
        // if (event.type === "session.created" && this.adoptPendingFollowup(event.properties.info)) {
        if (evt is EventSessionCreated &&
          _serviceProvider.GetService<FollowupService>().AdoptPendingFollowup((evt as EventSessionCreated).Properties.Info))
        {
          // return
          return;
        }

        //// Events without sessionID (server.connected, server.heartbeat, indexing.status) → always forward
        //// Events with sessionID → only forward if this webview tracks that session
        //// message.part.* events are always session-scoped; drop if session unknown.
        // if (!sessionID && isSessionScopedPartEvent(event.type)) return
        if (string.IsNullOrEmpty(sessionId) && sessionScopedPartEvents.Contains(e.Type)) return;
        // if (this.postModelUsageChanged(event, sessionID)) return
        if (PostModelUsageChanged(evt, sessionId)) return;
        // if (
        //   event.type !== "indexing.status" &&
        //   event.type !== "session.deleted" &&
        //   sessionID &&
        //   !this.trackedSessionIds.has(sessionID)
        // )
        if (!(evt is EventIndexingStatus)
          && !(evt is EventSessionDeleted)
          && !string.IsNullOrEmpty(sessionId)
          && !SessionHandler.IsTrackedSession(sessionId))
          //   return
          return;

        // if (event.type === "session.updated" && typeof event.properties.info.cost === "number") {
        if (evt is EventSessionUpdated evt2 && !double.IsNaN(evt2.Properties.Info.Cost))
        {
          // const cost = this.costs.setSessionCost(event.properties.sessionID, event.properties.info.cost)
          var cost = ServiceProvider.GetService<CostService>().SetSessionCost(evt2.Properties.SessionID, evt2.Properties.Info.Cost);
          // this.requestCostAlert(event.properties.sessionID, cost)
          Provider.RequestCostAlert(sessionId, cost);
        }
        // if (event.type === "session.updated") {
        if (evt is EventSessionUpdated)
        {
          if (!raw.IsLegacySyncEvent) return;
          var typedEvent = (EventSessionUpdated)evt;
          if (!SessionHandler.UpdateSessionRevision(typedEvent.Properties.SessionID, typedEvent.Id, e.Seq)) return;
        }

        // Refresh provider and agent lists when the server signals a state disposal
        // if (event.type === "global.disposed") {
        if (evt is EventGlobalDisposed)
        {
          // void this.reloadAfterAuthChange()
          _ = Provider.ReloadAfterAuthChangeAsync();
          return;
        }

        // if (event.type === "server.instance.disposed") {
        if (evt is EventServerInstanceDisposed)
        {
          var typedEvent = (EventServerInstanceDisposed)evt;
          var dir = typedEvent.Properties.Directory;
          if (!string.IsNullOrEmpty(dir))
            foreach (var sid in SessionHandler.Aborts.Dispose(dir))
              _sessionService.SetSessionStatus(sid, "idle");
          if (!string.IsNullOrEmpty(dir)
            && !PathUtils.SameDirectory(dir, _serviceProvider.GetService<ProjectDirectoryProvider>().GetWorkspaceDirectory()))
            return;

          _ = Provider.ReloadAfterAuthChangeAsync();
          return;
        }

        // Config was updated without a full dispose (e.g. permission-only save).
        // Fetch and push the updated config + refresh agents and providers so the
        // Settings panel and mode/model pickers reflect the change.
        // if (event.type === "global.config.updated") {
        if (evt is EventGlobalConfigUpdated)
        {
          // this.requirements.clear()
          //   _requirements.Clear(); // TODO : Future impl
          // void Promise.all([this.fetchAndSendConfigUpdated(), this.fetchAndSendAgents(), this.fetchAndSendProviders()])
          _ = Task.WhenAll(Provider.FetchAndSendConfigUpdatedAsync(), Provider.FetchAndSendAgentsAsync(), Provider.FetchAndSendProvidersAsync());
          // return
          return;
        }

        // Forward relevant events to webview
        // Side effects that must happen before the webview message is sent
        // if (event.type === "message.updated") {
        if (evt is EventMessageUpdated)
        {
          var typedEvent = (EventMessageUpdated)evt;
          // const info = event.properties.info
          // const value = info.role === "assistant" ? info.cost : undefined
          double? value = typedEvent.Properties.Info is AssistantMessage assistantMessage ? assistantMessage.Cost : null;
          // const cost = this.updateMessageCost(event.properties.sessionID, info.id, info.role, value)
          var _costService = _serviceProvider.GetService<CostService>();
          var cost = _costService.UpdateMessageCost(sessionId, typedEvent.Properties.Info.Id, typedEvent.Properties.Info is AssistantMessage ? "assistant" : "user", value);
          // if (cost !== undefined) this.requestCostAlert(event.properties.sessionID, cost)
          if (cost != null) Provider.RequestCostAlert(sessionId, cost.Value);
        }
        // if (event.type === "message.removed") {
        if (evt is EventMessageRemoved)
        {
          var typedEvent = evt as EventMessageRemoved;
          var _costService = _serviceProvider.GetService<CostService>();
          _costService.RemoveMessageCost(typedEvent.Properties.MessageID);
        }
        // if (event.type === "session.created" && !this.currentsession) {
        if (evt is EventSessionCreated && Provider.GetCurrentSession() == null)
        {
          var typedEvt = (EventSessionCreated)evt;
          // this.setcurrentsession(event.properties.info)
          Provider.SetCurrentSession(typedEvt.Properties.Info);
          // this.contextsessionid = event.properties.info.id
          Provider.SetContextSessionID(typedEvt.Properties.Info.Id);
          // this.trackedsessionids.add(event.properties.info.id)
          _sessionService.TrackSession(typedEvt.Properties.Info.Id);
        }
        // if (event.type === "session.updated" && this.currentSession?.id === event.properties.sessionID) {
        if (evt is EventSessionUpdated && Provider.GetCurrentSessionID() == sessionId)
        {
          var typedEvt = (EventSessionUpdated)evt;
          // this.setCurrentSession(event.properties.info)
          Provider.SetCurrentSession(typedEvt.Properties.Info);
          // this.contextSessionID = event.properties.sessionID
          Provider.SetContextSessionID(sessionId);
        }
        // if (event.type === "session.deleted") {
        if (evt is EventSessionDeleted)
        {
          // const sid = event.properties.sessionID
          // this.trackedSessionIds.delete(sid)
          _sessionService.UntrackSession(sessionId);
          // this.modelUsageSessionIds.delete(sid)
          _sessionService.RemoveModelUsage(sessionId);
          // this.sessionDirectories.delete(sid)
          _projectDirectoryProvider.ClearSessionDirectory(sessionId);
          // this.connectionService.pruneSession(sid)
          _serviceProvider.GetService<KiloConnectionService>().PruneSession(sessionId);
          // this.costs.onSessionDeleted(sid)
          _serviceProvider.GetService<CostService>().OnSessionDeleted(sessionId);
        }

        // Auto-adopt child sessions as soon as the task tool part reveals their ID.
        // This means the child's permission/question events are tracked immediately —
        // before the webview renderer has a chance to call syncSession — eliminating
        // the race where the child blocks on a prompt that the UI never sees.
        // if (event.type === "message.part.updated") {
        if (evt is EventMessagePartUpdated)
        {
          // const part = event.properties.part as {
          //   type?: string
          //   tool?: string
          //   metadata?: { sessionId?: string }
          //   state?: { metadata?: { sessionId?: string } }
          //   sessionID?: string
          // }
          var typedEvt = (EventMessagePartUpdated)evt;
          var part = typedEvt.Properties.Part;
          // const childId = childID(part)
          var childId = typedEvt.GetPartChildId();
          // if (childId && !this.trackedSessionIds.has(childId)) {
          if (!string.IsNullOrEmpty(childId) && !_sessionService.IsTrackedSession(childId))
          {
            // console.log("[Kilo New] KiloProvider: 🔗 Auto-adopting child session from task tool", { childId })
            System.Diagnostics.Debug.WriteLine($"[Kilo New] KiloProvider: 🔗 Auto-adopting child session from task tool {childId}");
            // void this.handleSyncSession(childId, part.sessionID ?? sessionID)
            _ = HandleSyncSessionAsync(childId, part.AdditionalProperties?["sessionID"]?.ToString() ?? sessionId);
          }
        }

        // Drop the per-session caches for deleted sessions so a late
        // handleLoadMessages response (or any other guarded read) can't resurrect
        // transcript state for a session the webview just cleaned up. The
        // prefilter lets session.deleted through without re-tracking, and the
        // handleEvent guard does the same — this is the matching prune.
        // if (event.type === "session.deleted" && sessionID) {
        if (evt is EventSessionDeleted && !string.IsNullOrEmpty(sessionId))
        {
          // this.pruneDeletedSession(sessionID)
          Provider.PruneDeletedSession(sessionId);
        }

        // if (!isLegacySyncEvent(event)) {
        if (!raw.IsLegacySyncEvent)
        {
          //// const props = event.properties
          //var props = e.Payload;
          //// handleNetworkEvent(
          ////   event.type,
          ////   {
          ////     id: "id" in props && typeof props.id === "string" ? props.id : undefined,
          ////     sessionID: "sessionID" in props && typeof props.sessionID === "string" ? props.sessionID : undefined,
          ////     requestID: "requestID" in props && typeof props.requestID === "string" ? props.requestID : undefined,
          ////   },
          ////   this.client,
          ////   (s) => this.getWorkspaceDirectory(s),
          //// )
          _ = HandleNetworkEvent(evt);         
        }

        // if (event.type === "indexing.status" && directory) {
        if (evt is EventIndexingStatus && !string.IsNullOrEmpty(directory))
        {
          // if (!samedirectory(directory, this.getworkspacedirectory(this.currentsession?.id))) return
          if (!PathUtils.SameDirectory(directory, _projectDirectoryProvider.GetWorkspaceDirectory(Provider.GetCurrentSessionID()))) return;
        }

        // const msg = isLegacySyncEvent(event)
        //   ? this.mapSyncEventToWebviewMessage(event)
        //   : mapSSEEventToWebviewMessage(event, sessionID)

        if (evt is EventMessagePartUpdated)
        {
          var typedEvt = (EventMessagePartUpdated)evt;
          typedEvt.Properties.Part = SlimUtils.SlimPart(typedEvt.Properties.Part);
        } 
        if (evt is SyncEventMessagePartUpdated)
        {
          var typedEvt = (SyncEventMessagePartUpdated)evt;
          typedEvt.SyncEvent.Data.Part = SlimUtils.SlimPart(typedEvt.SyncEvent.Data.Part);
        }

        // const next = msg.type === "messageCreated" ? { ...msg, message: this.slimInfo(msg.message) } : msg
        if (evt is EventMessageUpdated)
        {
          var typedEvt = (EventMessageUpdated)evt;
          typedEvt.Properties.Info = SlimUtils.SlimInfo(typedEvt.Properties.Info);
        }
        if (evt is SyncEventMessageUpdated)
        {
          var typedEvt = (SyncEventMessageUpdated)evt;
          typedEvt.SyncEvent.Data.Info = SlimUtils.SlimInfo(typedEvt.SyncEvent.Data.Info);
        }

        var next = MapSSEEventToWebviewMessage((Event)e.Data, sessionId);
        // if (!msg) return
        if (next == null) return;
        // if (msg.type === "partUpdated") {
        if (next is KiloExtensionDTOs.PartUpdate)
        {
          var typedEvt = (KiloExtensionDTOs.PartUpdate)next;
          // this.streams.push({ ...msg, part: this.slimPart(msg.part) })        
          _serviceProvider.GetService<SessionStreamScheduler>().Push(typedEvt);
          // return
          return;
        }
        
        
        // if (next.type === "sandboxStatus") {
        if (next is SandboxStatusMessage)
        {
          var typedEvt = (SandboxStatusMessage)next;
          // if (!sameDirectory(next.directory, this.getWorkspaceDirectory(next.sessionID))) return
          if (!PathUtils.SameDirectory(typedEvt.Directory, _projectDirectoryProvider.GetWorkspaceDirectory(typedEvt.SessionID))) return;
          // this.postMessage({ ...next, revision: ++this.sandboxRevision })
          PostMessage(
            new SandboxStatusMessage
            {
              Available = typedEvt.Available,
              Directory = typedEvt.Directory,
              Enabled = typedEvt.Enabled,
              Reason = typedEvt.Reason,
              RequestID = typedEvt.RequestID,
              Revision = _serviceProvider.GetService<SandboxService>().IncrementSandboxRevision(),
              SessionID = typedEvt.SessionID,           
            });
          return;
        }
        // if (next.type === "indexingStatusLoaded") {
        if (next is IndexingStatusLoadedMessage)
        {
          // this.cachedIndexingStatusMessage = next

          _serviceProvider.GetService<ICacheService>().UpdateAsync("indexingStatusLoadedMessage", next);
        }
        // this.streams.flush(sessionID)
        _serviceProvider.GetService<SessionStreamScheduler>().Flush(sessionId);
        // this.postMessage(next)
        PostMessage(next);
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] SSEHelper: error handling SSE event: {ex.Message}");
      }
    }

    private async Task HandleNetworkEvent(IEvent evt)
    {
      if (evt is EventSessionNetworkAsked)
      {
        var typedEvt = (EventSessionNetworkAsked)evt;
        if (string.IsNullOrEmpty(typedEvt.Properties.Id) || string.IsNullOrEmpty(typedEvt.Properties.SessionID)) return;
        var existing = _networkWaits.ContainsKey(typedEvt.Properties.Id) ? _networkWaits[typedEvt.Properties.Id] : null;
        if (existing != null)
          existing.Refs++;
        else
          _networkWaits[typedEvt.Properties.Id] = new NetworkWait { Sid = typedEvt.Properties.SessionID, Refs = 1 };
      }
      else if (evt is EventSessionNetworkRestored)
      {
        var typedEvt = (EventSessionNetworkRestored)evt;
        if (string.IsNullOrEmpty(typedEvt.Properties.RequestID)) return;
        var entry = _networkWaits.ContainsKey(typedEvt.Properties.RequestID) ? _networkWaits[typedEvt.Properties.RequestID] : null;
        if (entry == null) return;
        System.Diagnostics.Debug.WriteLine($"[Kilo New] network: auto-replying to restore {typedEvt.Properties.RequestID}");
        var nswagClient = Provider.GetNswagClient();
        await nswagClient.Network_replyAsync(typedEvt.Properties.RequestID, _projectDirectoryProvider.GetWorkspaceDirectory(entry.Sid), "");
        _networkWaits.Remove(typedEvt.Properties.RequestID);
      }
      else if (evt is EventSessionNetworkReplied)
      {
        var typedEvt = (EventSessionNetworkReplied)evt;
        _networkWaits.Remove(typedEvt.Properties.RequestID);
      }
      else if (evt is EventSessionNetworkRejected)
      {
        var typedEvt = (EventSessionNetworkRejected)evt;
        _networkWaits.Remove(typedEvt.Properties.RequestID);
      }
    }


    /// <summary>
    /// Handles syncSession message - syncs a child session (e.g., spawned by task tool).
    /// Tracks the session for SSE events and fetches its messages.
    /// Matches VS Code's handleSyncSession pattern.
    /// </summary>
    public async Task HandleSyncSessionAsync(string sessionID, string? parentSessionID = null)
    {
      //    if (!this.client) return
      var nswagClient = Provider.GetNswagClient();
      if (nswagClient == null)
      {
        PostMessage(
          new ErrorMessage { Message = "Not connected to CLI backend" }
            );
        return;
      }


      //    if (this.syncedChildSessions.has(sessionID)) return
      if (string.IsNullOrEmpty(sessionID)) return;
      var sessionService = ServiceProvider.GetService<SessionHandlerService>();
      // Check if already synced
      if (sessionService.HasSyncedChildSession(sessionID)) return;
      //    this.syncedChildSessions.add(sessionID)
      sessionService.AddSyncedChildSession(sessionID);
      //    this.trackedSessionIds.add(sessionID)
      sessionService.TrackSession(sessionID);

      // Inherit the parent's worktree directory so permission responses use
      // the correct backend Instance. Without this, child sessions in Agent
      // Manager worktrees fall back to workspace root and fail to find the
      // pending permission request.
      if (!string.IsNullOrEmpty(parentSessionID))
      {
        var _sessionDirectories = _projectDirectoryProvider.GetSessionDirectories();
        var dir = _sessionDirectories[parentSessionID];
        if (dir != null && !_sessionDirectories.ContainsKey(sessionID))
        {
          _projectDirectoryProvider.SetSessionDirectory(sessionID, dir);
        }
      }


      try
      {
        //      const workspaceDir = this.getWorkspaceDirectory(sessionID)
        var workspaceDir = _projectDirectoryProvider.GetWorkspaceDirectory(sessionID);

        //      const [info, history] = await Promise.all([
        //        retry(() => this.client!.session.get({ sessionID, directory: workspaceDir }, { throwOnError: true })),
        //        retry(() => this.client!.session.messages({ sessionID, directory: workspaceDir }, { throwOnError: true })),
        //      ])
        // Fetch session info and messages in parallel
        var sessionTask = Retry.RetryAsync(() => nswagClient.Session_getAsync(sessionID, workspaceDir, ""));
        var messagesTask = Retry.RetryAsync(() => nswagClient.Session_messagesAsync(sessionID, workspaceDir, "", null, null));

        await Task.WhenAll(sessionTask, messagesTask);

        var session = sessionTask.Result;
        var messagesResponse = messagesTask.Result;

        //      this.postMessage({ type: "sessionUpdated", session: this.sessionToWebview(info.data) })
        // Post session updated message
        var sessionUpdated = new SessionUpdatedMessage
        {
          Session = EntityConverter.Convert(session)
        };
        PostMessage(sessionUpdated);

        //      const messages = history.data.map((m) => ({
        //        ...this.slimInfo(m.info),
        //        parts: this.slimParts(m.parts),
        //        createdAt: new Date(m.info.time.created).toISOString(),
        //      }))

        // Process messages
        var messages = messagesResponse.Select(Utils.SlimUtils.SlimMessage);
        //      for (const message of messages) {
        //        this.connectionService.recordMessageSessionId(message.id, message.sessionID)
        //      }
        //      this.resetMessageCosts(sessionID, messages)
        // Record message session IDs
        var _connectionService = _serviceProvider.GetService<KiloConnectionService>();
        foreach (var message in messages)
        {
          sessionService.MapMessageToSession(message.Id, message.SessionID);
        }

        // Reset message costs for this session
        _serviceProvider.GetService<CostService>().ResetMessageCosts(sessionID, messages);

        // Drop any queued deltas for this session

        _serviceProvider.GetService<SessionStreamScheduler>().Drop(sessionID);

        // Post messages loaded message
        var messagesLoaded = new MessagesLoadedMessage
        {
          SessionID = sessionID,
          Messages = messages.ToList(),
          Mode = ReplacePrependReconcileEnum.Replace,
          HasMore = false
        };
        PostMessage(messagesLoaded);

        // Recover any prompts emitted by the child before we started tracking it
        Provider.RecoverPendingPrompts();
      }
      catch (Exception ex)
      {
        sessionService.RemoveSyncedChildSession(sessionID);
        System.Diagnostics.Debug.WriteLine($"[Kilo New] KiloProvider: Failed to sync child session: {ex.Message}");
      }
    }

    public bool PostModelUsageChanged(IEvent e, string sessionID)
    {
      //  if (!sessionID || this.trackedSessionIds.has(sessionID)) return false
      if (string.IsNullOrEmpty(sessionID) || SessionHandler.IsTrackedSession(sessionID)) return false;
      //  if (event.type === "session.created") {
      if (e is EventSessionCreated evt)
      {
        //    const parent = event.properties.info.parentID
        var parent = evt.Properties.Info.ParentID;
        //    if (!parent || !this.modelUsageSessionIds.has(parent)) return false
        if (string.IsNullOrEmpty(parent) || SessionHandler.HasModelUsage(parent)) return false;
        //    this.modelUsageSessionIds.add(sessionID)
        SessionHandler.TrackModelUsage(sessionID);
        //    this.postMessage({ type: "sessionModelUsageChanged", sessionID })
        PostMessage(new SessionModelUsageChangedMessage
        {
          SessionID = sessionID
        });
        //    return true
        return true;
        //  }
      }


      //  if (!this.modelUsageSessionIds.has(sessionID)) return false
      if (!SessionHandler.HasModelUsage(sessionID)) return false;

      //  if (event.type === "message.part.updated") {
      if (e is EventMessagePartUpdated evt2)
      {
        //  const part = event.properties.part as {
        //    type ?: string
        //      tool ?: string
        //      metadata ?: { sessionId ?: string }
        //    state ?: { metadata ?: { sessionId ?: string } }
        //  }
        //  const child = childID(part)
        var child = evt2.GetPartChildId();
        //    if (child && !this.modelUsageSessionIds.has(child))
        //  {
        if (child != null && !SessionHandler.HasModelUsage(child))
        {
          //    this.modelUsageSessionIds.add(child)
          SessionHandler.TrackModelUsage(child);
          //      this.postMessage({ type: "sessionModelUsageChanged", sessionID: child })
          PostMessage(new SessionModelUsageChangedMessage { SessionID = child });
          return true;
          //      return true
          //    }
        }
        //}
      }
      //  const changed =
      //    event.type === "message.removed" ||
      //    event.type === "message.part.removed" ||
      //    event.type === "session.deleted" ||
      //    (event.type === "message.part.updated" && event.properties.part.type === "step-finish")
      var changed = e is EventMessageRemoved
        || e is EventMessagePartRemoved
        || e is EventSessionDeleted
        || (e is EventMessagePartUpdated evt3 && evt3.Properties.Part is StepFinishPart);
      //  if (!changed) return false
      if (!changed) return false;
      //  if (event.type === "session.deleted") this.modelUsageSessionIds.delete(sessionID)
      if (e is EventSessionDeleted) SessionHandler.RemoveModelUsage(sessionID);
      //  this.postMessage({ type: "sessionModelUsageChanged", sessionID })
      PostMessage(new SessionModelUsageChangedMessage { SessionID = sessionID });
      //  return true     
      //}
      return true;
    }


    internal IWebviewMessage MapSSEEventToWebviewMessage(Event evt, string sessionID)
    {
      return evt is IWebviewMappable ? ((IWebviewMappable)evt).GetWebViewMessage(sessionID) : null;
    }
    


    private static string? ResolveSyncSessionId(JToken payload, Action<string, string>? onMessageUpdated = null)
    {
      var id = payload["data"]?["info"]?["id"]?.Value<string>() ?? "";
      var sessionId = payload["data"]?["sessionID"]?.Value<string>() ?? "";
      if ((payload["type"]?.Value<string>() ?? "") == "message.updated.1")
      {
        onMessageUpdated?.Invoke(id, sessionId);
      }
      return sessionId;
    }

    private static string? ResolveTransientSessionId(JToken payload)
    {
      var evType = payload["type"]?.Value<string>() ?? "";
      switch (evType)
      {
        case "session.status":
        case "session.turn.open":
        case "session.turn.close":
        case "session.idle":
        case "session.error":
        case "todo.updated":
        case "message.part.delta":
        case "permission.asked":
        case "permission.replied":
        case "question.asked":
        case "question.replied":
        case "question.rejected":
        case "suggestion.shown":
        case "suggestion.accepted":
        case "suggestion.dismissed":
        case "session.network.asked":
        case "session.network.replied":
        case "session.network.rejected":
        case "session.network.restored":
          return payload["properties"]?["sessionID"]?.Value<string>() ?? "";
        default:
          return null;
      }
    }

    public void RecordMessageSessionId(string messageID, string sessionID)
    {
      SessionHandler.MapMessageToSession(messageID, sessionID);
    }

    public string? LookupMessageSessionId(string messageID)
    {
      return SessionHandler.GetSessionIdForMessage(messageID);
    }

    //public bool IsStaleEvent(string sessionID, string eventId, int seq)
    //{
    //  var revision = SessionHandler.GetRevision(sessionID);
    //  if (revision == null)
    //  {
    //    return false;
    //  }

    //  var versioned = seq > 0 || revision.Seq > 0;
    //  if (versioned)
    //  {
    //    return seq <= revision.Seq;
    //  }

    //  return eventId.CompareTo(revision.Id) <= 0;
    //}

    public bool IsEventFromForeignProject(string eventName, string? projectID)
    {
      if (string.IsNullOrEmpty(_currentProjectID) || string.IsNullOrEmpty(projectID))
      {
        return false;
      }

      if (eventName == "session.created.1" || eventName == "session.deleted.1")
      {
        return projectID != _currentProjectID;
      }

      if (eventName == "session.updated.1")
      {
        return projectID != _currentProjectID;
      }

      return false;
    }

    //public void SetProjectID(string? projectID)
    //{
    //  _currentProjectID = projectID;
    //}

    private static string? ResolveEventSessionIdPure(
      JToken payload,
      Func<string, string?> lookupMessageSessionId,
      Action<string, string>? onMessageUpdated = null)
    {
      var evType = payload["type"]?.Value<string>() ?? "";
      var properties = payload["properties"];

      if (evType == "sync")
      {
        return ResolveSyncSessionId(payload, onMessageUpdated);
      }

      // No need for "void" trick - C# doesn't warn about unused parameters by default
      // Or use underscore prefix to indicate intentional non-use:
      _ = lookupMessageSessionId;

      if (evType == "sandbox.status.changed")
        return properties["sessionID"]?.Value<string>() ?? "";

      return ResolveTransientSessionId(payload);
    }

    public string ResolveEventSessionId(SseEventReceivedEventArgs ev)
    {
      return ResolveEventSessionId(ev.Payload);
    }

    public string ResolveEventSessionId(JToken ev)
    {
      var evType = ev["type"]?.Value<string>() ?? "";
      var properties = ev["properties"];
      var sessionID = properties?["sessionID"]?.Value<string>() ?? "";
      switch (evType)
      {
        case "session.created":
        case "session.updated":
        case "session.deleted":
        case "message.removed":
        case "message.part.updated":
        case "message.part.removed":
          return sessionID;
        case "message.updated":
          RecordMessageSessionId(properties["info"]?["id"]?.Value<string>() ?? "", sessionID);
          return sessionID;
        default:
          return ResolveEventSessionIdPure(
            ev,
            (messageId) => LookupMessageSessionId(messageId),
            (messageId, sessionId) => RecordMessageSessionId(messageId, sessionId)
          );
      }
    }

    private bool MatchesPendingFollowup(ApiClient.Session session)
    {
      return FollowupHandler.MatchesPendingFollowup(session.Directory, DateTimeOffset.Now.ToUnixTimeMilliseconds(), session.ParentID);
    }


    public bool FilterSSEEvent(SseEventReceivedEventArgs e)
    {

      JObject ev = e.UnwrapSyncEvent();
      if (ev == null) return false;

      var evType = ev["type"]?.Value<string>() ?? "";

      // Remote status events are global and should always pass through
      if (evType == "kilo-sessions.remote-status-changed") return true;
      if (memoryEvents.Contains(evType))
        return true;

      var sessionId = ResolveEventSessionId(ev);
      // message.part.* events are always session-scoped; drop if session unknown.
      if (string.IsNullOrEmpty(sessionId)) return !sessionScopedPartEvents.Contains(evType);
      if (evType == "session.created" && MatchesPendingFollowup((SseEventDeserializer.Deserialize(ev).Data as EventSessionCreated).Properties.Info))
        return true;


      // session.status must always pass through — even for sessions not tracked by this
      // KiloProvider instance. The Settings panel is a separate provider with no tracked
      // sessions, but it needs session.status to populate sessionStatusMap and allStatusMap
      // for the busy-session warning on Save.
      if (evType == "session.status") return true;

      // session.deleted must always pass through so the webview can run its cleanup
      // (messages, parts, stash, todos, permissions, drafts, etc.) — including for
      // sessions that were never explicitly tracked here (e.g. child sessions
      // cascade-deleted with the parent, or external CLI deletions). We deliberately
      // do NOT re-track the deleted id: handleLoadMessages intentionally drops late
      // responses for sessions that have been pruned, and re-tracking would let an
      // in-flight messagesLoaded response resurrect transcript state for a session
      // the webview just cleaned up.
      if (evType == "session.deleted") return true;

      return SessionHandler.IsTrackedSession(sessionId);
    }

    public void Dispose()
    {
      if (_disposed) return;
      _disposed = true;
    }


    #region deprecated




    //private void HandleMessageUpdatedSync(EventMessageUpdated evt)
    //{
    //  var info = evt.Properties.Info;
    //  var infoJson = info.ToJson();
    //  var infoObj = JObject.Parse(infoJson);
    //  var messageID = infoObj["id"]?.Value<string>();
    //  var sessionID = evt.Properties.SessionID;

    //  RecordMessageSessionId(messageID, sessionID);

    //  if (infoObj["cost"]?.Type == JTokenType.Float && infoObj["role"]?.Value<string>() == "assistant")
    //  {
    //    //_serviceProvider.GetService<MaxCostNudgeService>().
    //    //SessionHandler.GetOrCreateMessageCost(messageID, sessionID).Cost = infoObj["cost"].Value<double>();
    //  }

    //  var timeObj = infoObj["time"];
    //  var createdAt = timeObj != null && timeObj["created"]?.Type == JTokenType.Integer
    //      ? DateTimeOffset.FromUnixTimeMilliseconds((long)timeObj["created"].Value<long>()).ToUniversalTime().ToString("o")
    //      : DateTime.UtcNow.ToString("o");

    //  var message = new WebViewMessage
    //  {
    //    Id = messageID,
    //    SessionID = sessionID,
    //    Role = infoObj["role"]?.Value<string>(),
    //    Content = infoObj["content"]?.ToString(),
    //    Parts = infoObj["parts"],
    //    CreatedAt = createdAt,
    //    Time = timeObj != null ? new TimeType { Created = (double)timeObj["created"]?.Value<long>(), Completed = timeObj["updated"]?.Value<long>() } : null,
    //    Agent = infoObj["agent"]?.ToString(),
    //    //Model = new ModelType { ModelID = } // infoObj["model"]?.ToString(),
    //    ProviderID = infoObj["providerID"]?.Value<string>(),
    //    ModelID = infoObj["modelID"]?.Value<string>()
    //  };

    //  PostMessage(new MessageCreatedMessage { Message = message });
    //}

    ////private void HandleMessageRemovedSync(SyncEvent evt)
    ////{
    ////  var data = (KiloVisualStudioExtension.ApiClient.EventMessageRemoved)evt.Data;

    ////  _messageCosts.Remove(data.Properties.MessageID);

    ////  PostMessage(new MessageRemovedMessage
    ////  {
    ////    SessionID = data.Properties.SessionID,
    ////    MessageID = data.Properties.MessageID
    ////  });
    ////}

    //private void HandlePartUpdatedSync(MessagePartUpdatedSyncEvent evt)
    //{
    //  var data = (KiloVisualStudioExtension.ApiClient.EventMessagePartUpdated)evt.Data;
    //  var part = data.Properties.Part;
    //  var sessionID = data.Properties.SessionID;
    //  var partJson = part.ToJson();
    //  var partObj = JObject.Parse(partJson);
    //  var messageID = partObj["messageID"]?.Value<string>();

    //  var metadata = partObj["metadata"];
    //  if (metadata != null && metadata is JObject metadataObj && metadataObj["sessionId"] != null)
    //  {
    //    var childId = metadataObj["sessionId"].Value<string>();
    //    if (!string.IsNullOrEmpty(childId) && !SessionHandler.IsTrackedSession(childId))
    //    {
    //      System.Diagnostics.Debug.WriteLine($"[Kilo] SSEHelper: Auto-adopting child session: {childId}");
    //      SessionHandler.TrackSession(childId);
    //    }
    //  }

    //  //PostMessage(new PartUpdate
    //  //{

    //  //  SessionID = sessionID,
    //  //  MessageID = messageID,
    //  //  Part = part,

    //  //});
    //}

    //private void HandlePartRemovedSync(SyncEvent evt)
    //{
    //  var data = (KiloVisualStudioExtension.ApiClient.EventMessagePartRemoved)evt.Data;

    //  PostMessage(new PartRemove
    //  {
    //    SessionID = data.Properties.SessionID,
    //    MessageID = data.Properties.MessageID,
    //    PartID = data.Properties.PartID
    //  });
    //}

    //private void HandleSessionCreatedSync(SessionCreatedSyncEvent evt)
    //{
    //  var data = (KiloVisualStudioExtension.ApiClient.EventSessionCreated)evt.Data;
    //  var info = data.Properties.Info;
    //  var sessionID = info.Id;

    //  if (string.IsNullOrEmpty(Provider.GetCurrentSessionID()))
    //  {
    //    Provider.SetCurrentSessionID(sessionID);
    //    SessionHandler.TrackSession(sessionID);
    //  }

    //  var createdAt = info.Time != null
    //      ? DateTimeOffset.FromUnixTimeMilliseconds((long)info.Time.Created).ToUniversalTime().ToString("o")
    //      : DateTime.UtcNow.ToString("o");
    //  var updatedAt = info.Time != null
    //      ? DateTimeOffset.FromUnixTimeMilliseconds((long)info.Time.Updated).ToUniversalTime().ToString("o")
    //      : DateTime.UtcNow.ToString("o");

    //  PostMessage(new SessionCreatedMessage
    //  {
    //    Session = new KiloExtensionDTOs.Sessions.SessionInfo
    //    {
    //      Id = sessionID,
    //      ParentID = info.ParentID,
    //      Title = info.Title,
    //      CreatedAt = createdAt,
    //      UpdatedAt = updatedAt,
    //      Revert = info.Revert,
    //      Summary = info.Summary
    //    }
    //  });
    //}

    //private void HandleSessionUpdatedSync(SessionUpdatedSyncEvent evt)
    //{
    //  var data = (ApiClient.EventSessionUpdated)evt.Data;
    //  var info = data.Properties.Info;
    //  var sessionID = data.Properties.SessionID;

    //  if (!string.IsNullOrEmpty(evt.Id) && evt.Seq > 0)
    //  {
    //    if (IsStaleEvent(sessionID, evt.Id, evt.Seq))
    //    {
    //      System.Diagnostics.Debug.WriteLine($"[Kilo] SSEHelper: Dropping stale session.updated event for {sessionID}");
    //      return;
    //    }
    //    SessionHandler.TrackRevision(sessionID, new SessionRevision
    //    {
    //      Id = evt.Id,
    //      Seq = evt.Seq
    //    });
    //  }

    //  if (Provider.GetCurrentSessionID() == sessionID)
    //  {
    //    Provider.SetCurrentSessionID(sessionID);
    //  }

    //  var createdAt = info.Time != null
    //      ? DateTimeOffset.FromUnixTimeMilliseconds((long)info.Time.Created).ToUniversalTime().ToString("o")
    //      : DateTime.UtcNow.ToString("o");
    //  var updatedAt = info.Time != null
    //      ? DateTimeOffset.FromUnixTimeMilliseconds((long)info.Time.Updated).ToUniversalTime().ToString("o")
    //      : DateTime.UtcNow.ToString("o");

    //  PostMessage(new SessionUpdatedMessage
    //  {
    //    Session = new SessionUpdate
    //    {
    //      Id = sessionID,
    //      ParentID = info.ParentID,
    //      Title = info.Title,
    //      CreatedAt = createdAt,
    //      UpdatedAt = updatedAt,
    //      Revert = info.Revert,
    //      Summary = info.Summary
    //    }
    //  });
    //}

    //private void HandleSessionDeletedSync(SyncEvent evt)
    //{
    //  var data = (KiloVisualStudioExtension.ApiClient.EventSessionDeleted)evt.Data;
    //  var sessionID = data.Properties.SessionID;

    //  if (!string.IsNullOrEmpty(sessionID))
    //  {
    //    _trackedSessionIds.Remove(sessionID);
    //    _modelUsageSessionIds.Remove(sessionID);
    //    _revisions.Remove(sessionID);
    //    _sessionStatusMap.Remove(sessionID);

    //    var costsToRemove = _messageCosts.Where(kvp => kvp.Value.SessionID == sessionID).Select(kvp => kvp.Key).ToArray();
    //    foreach (var costId in costsToRemove)
    //    {
    //      _messageCosts.Remove(costId);
    //    }
    //  }

    //  PostMessage(new SessionDeletedMessage { SessionID = sessionID });
    //}

    //private void HandleMemoryEvent(string type, JToken properties)
    //{
    //  var eventSessionID = properties["sessionID"]?.Value<string>();
    //  var active = CurrentSessionID;

    //  var local = string.IsNullOrEmpty(eventSessionID) || eventSessionID == active || _trackedSessionIds.Contains(eventSessionID);
    //  if (!local) return;

    //  object? detail = null;
    //  if (properties["detail"]?.Type == JTokenType.Object)
    //  {
    //    detail = properties["detail"];
    //  }
    //  else if (type == "memory.error" && properties["reason"]?.Type == JTokenType.String)
    //  {
    //    detail = JsonConvert.DeserializeObject<object>(JsonConvert.SerializeObject(new { type = "error", message = properties["reason"].Value<string>(), reason = properties["reason"].Value<string>() }));
    //  }

    //  if (detail != null)
    //  {
    //    PostMessage(new MemoryEventMessage
    //    {
    //      SessionID = eventSessionID,
    //      Detail = detail
    //    });
    //  }
    //}

    //private void HandleSessionStatus(JToken properties, string? sessionID)
    //{
    //  var status = properties["status"] ?? throw new JsonSerializationException("session.status missing 'status'");
    //  var statusType = status["type"]?.Value<string>();
    //  var sid = properties["sessionID"]?.Value<string>() ?? "";

    //  var prev = _sessionStatusMap.ContainsKey(sid) ? _sessionStatusMap[sid] : null;
    //  if ((prev == null || prev.Type == "idle") && statusType != "idle")
    //  {
    //    // costs.rearm(sid) - not implemented
    //  }

    //  _sessionStatusMap[sid] = new SessionStatus
    //  {
    //    Type = statusType ?? "",
    //    Attempt = status["attempt"]?.Value<int>() ?? 0,
    //    Message = status["message"]?.Value<string>(),
    //    Next = status["next"]?.Type == JTokenType.Float || status["next"]?.Type == JTokenType.Integer ? (long?)status["next"].Value<long>() : null
    //  };

    //  var sessionStatus = new KiloExtensionDTOs.ExtensionMessages.SessionStatusMessage
    //  {
    //    SessionID = sid,
    //    Status = MapSessionStatusEnum(statusType),
    //    Attempt = status["attempt"]?.Type == JTokenType.Integer || status["attempt"]?.Type == JTokenType.Float ? (double?)status["attempt"].Value<long>() : null,
    //    Message = status["message"]?.Value<string>(),
    //    Next = status["next"]?.Type == JTokenType.Float || status["next"]?.Type == JTokenType.Integer ? (double?)status["next"].Value<long>() : null
    //  };

    //  PostMessage(sessionStatus);
    //}

    //private KiloExtensionDTOs.Connection.SessionStatus MapSessionStatusEnum(string? type)
    //{
    //  return type switch
    //  {
    //    "idle" => KiloExtensionDTOs.Connection.SessionStatus.Idle,
    //    "busy" => KiloExtensionDTOs.Connection.SessionStatus.Busy,
    //    "retry" => KiloExtensionDTOs.Connection.SessionStatus.Retry,
    //    "offline" => KiloExtensionDTOs.Connection.SessionStatus.Offline,
    //    _ => KiloExtensionDTOs.Connection.SessionStatus.Idle
    //  };
    //}

    //private void HandlePartDelta(JToken properties)
    //{
    //  var partID = properties["partID"]?.Value<string>();
    //  var messageID = properties["messageID"]?.Value<string>();
    //  var sid = properties["sessionID"]?.Value<string>();
    //  var delta = properties["delta"]?.Value<string>();

    //  System.Diagnostics.Debug.WriteLine($"[Kilo] SSEHelper: HandlePartDelta - sid={sid}, tracked={_trackedSessionIds.Contains(sid ?? "")}, deltaLen={delta?.Length ?? 0}");

    //  if (!string.IsNullOrEmpty(sid) && !_trackedSessionIds.Contains(sid))
    //  {
    //    System.Diagnostics.Debug.WriteLine($"[Kilo] SSEHelper: Skipping part update - session not tracked");
    //    return;
    //  }

    //  PostMessage(new KiloExtensionDTOs.PartUpdate
    //  {
    //    SessionID = sid,
    //    MessageID = messageID,
    //    Part = new { id = partID, type = "text", messageID, text = delta },
    //    Delta = new { type = "text-delta", textDelta = delta }
    //  });
    //}

    //private void HandleSessionCreatedStream(JToken properties)
    //{
    //  var info = properties["info"] ?? throw new JsonSerializationException("session.created missing 'info'");
    //  var sessionID = info["id"]?.Value<string>() ?? "";

    //  if (string.IsNullOrEmpty(CurrentSessionID))
    //  {
    //    CurrentSessionID = sessionID;
    //    _trackedSessionIds.Add(sessionID);
    //  }

    //  var createdAt = info["time"]?["created"] != null
    //      ? DateTimeOffset.FromUnixTimeMilliseconds((long)info["time"]["created"].Value<double>()).ToUniversalTime().ToString("o")
    //      : DateTime.UtcNow.ToString("o");
    //  var updatedAt = info["time"]?["updated"] != null
    //      ? DateTimeOffset.FromUnixTimeMilliseconds((long)info["time"]["updated"].Value<double>()).ToUniversalTime().ToString("o")
    //      : DateTime.UtcNow.ToString("o");

    //  PostMessage(new
    //  {
    //    type = "sessionCreated",
    //    session = new
    //    {
    //      id = sessionID,
    //      parentID = info["parentID"]?.Type == JTokenType.String ? info["parentID"].Value<string>() : null,
    //      title = info["title"]?.Value<string>(),
    //      createdAt,
    //      updatedAt,
    //      revert = info["revert"]?.Type == JTokenType.Object ? info["revert"] : (object?)null,
    //      summary = info["summary"]?.Type == JTokenType.String ? info["summary"].Value<string>() : null
    //    }
    //  });
    //}

    //private void HandleSessionUpdatedStream(JToken properties)
    //{
    //  var sessionID = properties["sessionID"]?.Value<string>();
    //  var info = properties["info"] ?? throw new JsonSerializationException("session.updated missing 'info'");

    //  if (info["cost"] != null && info["cost"].Type == JTokenType.Float)
    //  {
    //    // requestCostAlert - not implemented
    //  }

    //  if (CurrentSessionID == sessionID)
    //  {
    //    CurrentSessionID = sessionID;
    //  }

    //  var createdAt = info["time"]?["created"] != null
    //      ? DateTimeOffset.FromUnixTimeMilliseconds((long)info["time"]["created"].Value<double>()).ToUniversalTime().ToString("o")
    //      : DateTime.UtcNow.ToString("o");
    //  var updatedAt = info["time"]?["updated"] != null
    //      ? DateTimeOffset.FromUnixTimeMilliseconds((long)info["time"]["updated"].Value<double>()).ToUniversalTime().ToString("o")
    //      : DateTime.UtcNow.ToString("o");

    //  PostMessage(new
    //  {
    //    type = "sessionUpdated",
    //    session = new
    //    {
    //      id = sessionID,
    //      parentID = info["parentID"]?.Type == JTokenType.String ? info["parentID"].Value<string>() : null,
    //      title = info["title"]?.Value<string>(),
    //      createdAt,
    //      updatedAt,
    //      revert = info["revert"]?.Type == JTokenType.Object ? info["revert"] : (object?)null,
    //      summary = info["summary"]?.Type == JTokenType.String ? info["summary"].Value<string>() : null
    //    }
    //  });
    //}

    //private void HandleSessionDeletedStream(JToken properties)
    //{
    //  var sessionID = properties["sessionID"]?.Value<string>();

    //  if (!string.IsNullOrEmpty(sessionID))
    //  {
    //    _trackedSessionIds.Remove(sessionID);
    //    _modelUsageSessionIds.Remove(sessionID);
    //    _revisions.Remove(sessionID);
    //    _sessionStatusMap.Remove(sessionID);

    //    var costsToRemove = _messageCosts.Where(kvp => kvp.Value.SessionID == sessionID).Select(kvp => kvp.Key).ToArray();
    //    foreach (var costId in costsToRemove)
    //    {
    //      _messageCosts.Remove(costId);
    //    }
    //  }

    //  PostMessage(new
    //  {
    //    type = "sessionDeleted",
    //    sessionID
    //  });
    //}

    //private void HandleMessageUpdatedStream(JToken properties)
    //{
    //  var info = properties["info"] ?? throw new JsonSerializationException("message.updated missing 'info'");
    //  var sessionID = info["sessionID"]?.Value<string>();
    //  var messageID = info["id"]?.Value<string>() ?? "";

    //  if (info["cost"] != null && info["cost"].Type == JTokenType.Float)
    //  {
    //    var cost = info["cost"].Value<double>();
    //    if (info["role"]?.Value<string>() == "assistant")
    //    {
    //      _messageCosts[messageID] = new MessageCost { SessionID = sessionID ?? "", MessageID = messageID, Cost = cost };
    //    }
    //  }

    //  var createdAt = info["time"]?["created"] != null
    //      ? DateTimeOffset.FromUnixTimeMilliseconds((long)info["time"]["created"].Value<long>()).ToUniversalTime().ToString("o")
    //      : DateTime.UtcNow.ToString("o");

    //  // Match TypeScript: { ...info, createdAt: new Date(info.time.created).toISOString() }
    //  var messageObj = new Dictionary<string, object?>();
    //  if (info is JObject infoObj)
    //  {
    //    foreach (var prop in infoObj)
    //    {
    //      messageObj[prop.Key] = prop.Value;
    //    }
    //  }
    //  messageObj["createdAt"] = createdAt;

    //  PostMessage(new
    //  {
    //    type = "messageCreated",
    //    message = messageObj
    //  });
    //}

    //private void HandleMessageRemovedStream(JToken properties)
    //{
    //  var messageID = properties["messageID"]?.Value<string>();
    //  _messageCosts.Remove(messageID ?? "");

    //  PostMessage(new
    //  {
    //    type = "messageRemoved",
    //    sessionID = properties["sessionID"]?.Value<string>(),
    //    messageID
    //  });
    //}

    //private void HandleGlobalDisposed()
    //{
    //  PostMessage(new { type = "global.disposed" });
    //}

    //private void HandleServerInstanceDisposed(JToken properties)
    //{
    //  var dir = properties["directory"]?.Value<string>();

    //  foreach (var sid in _sessionStatusMap.Keys.ToList())
    //  {
    //    _sessionStatusMap[sid] = new ApiClient.SessionStatus { Type = "idle" };
    //  }

    //  PostMessage(new { type = "server.instance.disposed", directory = dir });
    //}

    //private void HandleGlobalConfigUpdated()
    //{
    //  PostMessage(new { type = "globalConfigUpdated" });
    //}

    //private void HandlePartUpdatedStream(JToken properties)
    //{
    //  var part = properties["part"] ?? throw new JsonSerializationException("message.part.updated missing 'part'");
    //  var sessionID = properties["sessionID"]?.Value<string>();
    //  var messageID = part["messageID"]?.Value<string>();

    //  if (part["metadata"]?["sessionId"] != null)
    //  {
    //    var childId = part["metadata"]["sessionId"].Value<string>();
    //    if (!string.IsNullOrEmpty(childId) && !_trackedSessionIds.Contains(childId))
    //    {
    //      System.Diagnostics.Debug.WriteLine($"[Kilo] SSEHelper: Auto-adopting child session: {childId}");
    //      _trackedSessionIds.Add(childId);
    //    }
    //  }

    //  PostMessage(new
    //  {
    //    type = "partUpdated",
    //    sessionID,
    //    messageID,
    //    part
    //  });
    //}

    //private void HandleIndexingStatus(JToken properties)
    //{
    //  var status = properties["status"];
    //  _cachedIndexingStatusMessage = JsonConvert.SerializeObject(new { type = "indexingStatusLoaded", status });

    //  if (!string.IsNullOrEmpty(_cachedIndexingStatusMessage))
    //  {
    //    _postMessage(_cachedIndexingStatusMessage);
    //  }
    //}

    //private void HandleSessionTurnClosed(JToken properties)
    //{
    //  PostMessage(new
    //  {
    //    type = "sessionTurnClosed",
    //    sessionID = properties["sessionID"]?.Value<string>(),
    //    reason = properties["reason"]?.Value<string>()
    //  });
    //}

    //private void HandleSessionTurnOpen(JToken properties)
    //{
    //  var sessionID = properties["sessionID"]?.Value<string>();
    //  System.Diagnostics.Debug.WriteLine($"[Kilo] SSEHelper: session.turn.open for {sessionID}");
    //}

    //private void HandleNetworkEvent(string type, JToken properties)
    //{
    //  var requestID = properties["requestID"]?.Value<string>() ?? properties["id"]?.Value<string>();
    //  var sessionID = properties["sessionID"]?.Value<string>();

    //  System.Diagnostics.Debug.WriteLine($"[Kilo] SSEHelper: network event {type} for session {sessionID}, requestID {requestID}");

    //  if (type == "session.network.asked" && !string.IsNullOrEmpty(requestID))
    //  {
    //    _networkWaits[requestID] = sessionID ?? "";
    //  }
    //  else if (type == "session.network.restored" && !string.IsNullOrEmpty(requestID))
    //  {
    //    if (_networkWaits.TryGetValue(requestID, out var sid))
    //    {
    //      System.Diagnostics.Debug.WriteLine($"[Kilo] SSEHelper: Auto-replying to network restore for {requestID}");
    //      _networkWaits.Remove(requestID);
    //    }
    //  }
    //  else if (type == "session.network.replied" || type == "session.network.rejected")
    //  {
    //    _networkWaits.Remove(requestID ?? "");
    //  }
    //}

    //private void HandlePermissionAsked(JToken properties)
    //{
    //  var permission = properties["permission"]?.Value<string>();
    //  PostMessage(new
    //  {
    //    type = "permissionRequest",
    //    permission = new
    //    {
    //      id = properties["id"]?.Value<string>(),
    //      sessionID = properties["sessionID"]?.Value<string>(),
    //      toolName = permission,
    //      patterns = properties["patterns"] ?? JArray.Parse("[]"),
    //      always = properties["always"] ?? JArray.Parse("[]"),
    //      args = properties["metadata"] ?? JObject.Parse("{}"),
    //      message = $"Permission required: {permission}",
    //      tool = properties["tool"] ?? JObject.Parse("{}")
    //    }
    //  });
    //}

    //private void HandlePermissionReplied(JToken properties)
    //{
    //  PostMessage(new
    //  {
    //    type = "permissionResolved",
    //    permissionID = properties["requestID"]?.Value<string>()
    //  });
    //}

    //private void HandleTodoUpdated(JToken properties)
    //{
    //  PostMessage(new
    //  {
    //    type = "todoUpdated",
    //    sessionID = properties["sessionID"]?.Value<string>(),
    //    items = properties["todos"]
    //  });
    //}

    //private void HandleQuestionAsked(JToken properties)
    //{
    //  PostMessage(new
    //  {
    //    type = "questionRequest",
    //    question = new
    //    {
    //      id = properties["id"]?.Value<string>(),
    //      sessionID = properties["sessionID"]?.Value<string>(),
    //      questions = properties["questions"],
    //      blocking = properties["blocking"]?.Value<bool>() ?? false,
    //      tool = properties["tool"] ?? JObject.Parse("{}")
    //    }
    //  });
    //}

    //private void HandleQuestionResolved(JToken properties)
    //{
    //  PostMessage(new
    //  {
    //    type = "questionResolved",
    //    requestID = properties["requestID"]?.Value<string>()
    //  });
    //}

    //private void HandleSuggestionShown(JToken properties)
    //{
    //  PostMessage(new
    //  {
    //    type = "suggestionRequest",
    //    suggestion = new
    //    {
    //      id = properties["id"]?.Value<string>(),
    //      sessionID = properties["sessionID"]?.Value<string>(),
    //      text = properties["text"]?.Value<string>(),
    //      actions = properties["actions"],
    //      blocking = properties["blocking"]?.Value<bool>() ?? false,
    //      tool = properties["tool"] ?? JObject.Parse("{}")
    //    }
    //  });
    //}

    //private void HandleSuggestionResolved(JToken properties)
    //{
    //  PostMessage(new
    //  {
    //    type = "suggestionResolved",
    //    requestID = properties["requestID"]?.Value<string>()
    //  });
    //}

    //private void HandleSessionError(JToken properties)
    //{
    //  PostMessage(new
    //  {
    //    type = "sessionError",
    //    sessionID = properties["sessionID"]?.Value<string>(),
    //    error = properties["error"] ?? JObject.Parse("{}")
    //  });
    //}

    //private void HandleSandboxStatusChanged(JToken properties)
    //{
    //  _sandboxRevision++;
    //  PostMessage(new
    //  {
    //    type = "sandboxStatus",
    //    sessionID = properties["sessionID"]?.Value<string>(),
    //    directory = properties["directory"]?.Value<string>(),
    //    enabled = properties["enabled"]?.Value<bool>() ?? false,
    //    available = properties["available"]?.Value<bool>() ?? false,
    //    reason = properties["reason"]?.Value<string>(),
    //    version = properties["version"]?.Value<int>() ?? 0,
    //    revision = _sandboxRevision
    //  });
    //}

    //public void TrackSession(string sessionID)
    //{
    //  _trackedSessionIds.Add(sessionID);
    //}

    //public void UntrackSession(string sessionID)
    //{
    //  _trackedSessionIds.Remove(sessionID);
    //}

    //public bool IsSessionTracked(string sessionID)
    //{
    //  return _trackedSessionIds.Contains(sessionID);
    //}

    //public void SetCurrentSession(string? sessionID)
    //{
    //  CurrentSessionID = sessionID;
    //  if (!string.IsNullOrEmpty(sessionID))
    //  {
    //    _trackedSessionIds.Add(sessionID);
    //  }
    //}
    #endregion


  }
}
