using EnvDTE;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.ApiClient.Json;
using KiloVisualStudioExtension.ApiClient.Sse;
using KiloVisualStudioExtension.Services;
using KiloVisualStudioExtension.Utils;
using KiloExtensionDTOs;
using KiloExtensionDTOs.ExtensionMessages;
using KiloExtensionDTOs.Parts;
using KiloExtensionDTOs.Sessions;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Telemetry;
using Microsoft.VisualStudio.Text.Editor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO.Packaging;
using System.Linq;
using System.Threading.Tasks;
using System.Web.UI.Design;
using Common;
using static KiloVisualStudioExtension.Services.MessagePageFetcher;
using ApiMessage = KiloVisualStudioExtension.ApiClient.Message;
using WebViewMessage = KiloExtensionDTOs.Sessions.Message;
using KiloVisualStudioExtension.Utils;
using KiloExtensionDTOs.Memory;
using KiloExtensionDTOs.Connection;
using KiloVisualStudioExtension.Services.Handlers.Memory;

namespace KiloVisualStudioExtension
{
  using KiloVisualStudioExtension.Services;
  using KiloVisualStudioExtension.Services.Handlers.Session;
  using KiloVisualStudioExtension.Services.Handlers.Followup;
  using KiloVisualStudioExtension.Services.Handlers.Indexing;
  using KiloVisualStudioExtension.Services.Handlers.Sandbox;
  using KiloVisualStudioExtension.Services.Handlers.Network;
  
  public class SSEHelper: ServiceProviderServiceBase
  {
    private bool _disposed;

    private VSProvider Provider => _serviceProvider.GetService<VSProvider>()
        ?? throw new InvalidOperationException("VSProvider not registered in service provider");

    private MessageConfirmation Confirmations => ServiceProviderExtensions.GetService<MessageConfirmation>(_serviceProvider);

    private SessionHandlerService SessionHandler => ServiceProviderExtensions.GetService<SessionHandlerService>(_serviceProvider);

    private FollowupHandlerService FollowupHandler => ServiceProviderExtensions.GetService<FollowupHandlerService>(_serviceProvider);

    private IndexingHandlerService IndexingHandler => ServiceProviderExtensions.GetService<IndexingHandlerService>(_serviceProvider);

    private SandboxHandlerService SandboxHandler => ServiceProviderExtensions.GetService<SandboxHandlerService>(_serviceProvider);

    private NetworkHandlerService NetworkHandler => ServiceProviderExtensions.GetService<NetworkHandlerService>(_serviceProvider);

    // Session state moved to SessionHandlerService - commented out to preserve for potential future use
    // private readonly HashSet<string> _trackedSessionIds = new HashSet<string>();
    // private readonly Dictionary<string, string> _sessionStatusMap = new Dictionary<string, string>();
    // private readonly Dictionary<string, string> _sessionDirectories = new Dictionary<string, string>();
    // private readonly Dictionary<string, SessionRevision> _revisions = new Dictionary<string, SessionRevision>();
    // private readonly HashSet<string> _modelUsageSessionIds = new HashSet<string>();
    // private readonly Dictionary<string, MessageCost> _messageCosts = new Dictionary<string, MessageCost>();
    // private readonly Dictionary<string, string> _messageSessionIds = new Dictionary<string, string>();
    // private readonly Dictionary<string, string> _networkWaits = new Dictionary<string, string>();
    private readonly HashSet<string> _trackedSessionIds = new HashSet<string>();
    private readonly Dictionary<string, string> _sessionDirectories = new Dictionary<string, string>();
    private readonly ProjectDirectoryProvider _projectDirectoryProvider;

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


    internal string ResolveDirectory(string? sessionID = null)
    {
      return _projectDirectoryProvider.GetWorkspaceDirectory(sessionID) 
        ?? System.Environment.CurrentDirectory;
    }

    private readonly Action<string> _postMessage;
    private readonly JsonSerializer _serializer;

    public SSEHelper(ServiceProvider serviceProvider, Action<string> postMessage, DTE dte = null): base(serviceProvider)
    {
      _postMessage = postMessage;
      _serializer = KiloJsonSerializer.Create();

      if (dte != null)
      {
        var vsProvider = _serviceProvider.AddService(new VisualStudioDirectoryProvider(dte));
        _projectDirectoryProvider = vsProvider.CreateProvider(
            projectDirectoryOverride: null, // or specify a path like @"C:\MyProject"
            sessionDirectories: _sessionDirectories);
        _serviceProvider.AddService(_projectDirectoryProvider);
      }
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
      _postMessage(JsonConvert.SerializeObject(message));
    }

    public void HandleEvent(SseEventReceivedEventArgs raw)
    {
      try
      {
        System.Diagnostics.Debug.WriteLine($"SSE Event received : {raw.EventType}");
        
        var e = SseEventDeserializer.Deserialize(raw);
        if (e == null) return;

        var sessionId = ResolveEventSessionId(raw);

        var evt = e.Data;
        
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
            ? _sessionDirectories.Where(kvp => SessionHandler.IsTrackedSession(kvp.Key) && PathUtils.SameDirectory(directory, kvp.Value)).Select(kvp => kvp.Key).ToList()
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
                detail = new KiloExtensionDTOs.Memory.MemoryEventDetail
                {
                  Type = SkippedErrorSavedRecalledEnum.Error,
                  Message = reason,
                  Reason = reason
                };
              }
            }
          }
          //JToken detail = props["detail"]?.Type == JTokenType.Object
          //  ? props["detail"]
          //  : e.EventType == "memory.error" && props["reason"]?.Type == JTokenType.String
          //    ? new JObject { ["type"] = "error", ["message"] = props["reason"], ["reason"] = props["reason"] }
          //    : null;
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
            
            _serviceProvider.GetService<MemoryHandlerService>().Fetch(target);
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
        

        ////////// if (event.type === "mcp.browser.open.failed") {
        ////////if (evt is EventMcpBrowserOpenFailed)
        ////////{
        ////////  var typedEvent = (EventMcpBrowserOpenFailed)evt;
        ////////  // McpOAuth.openMcpOAuthUrlOnce(event.properties.url)
        ////////  McpOAuth.OpenMcpOAuthUrlOnce(typedEvent.Properties.Url);
        ////////  // return
        ////////  return;
        ////////}

        ////////// if (event.type === "message.updated") {
        ////////if (evt is EventMessageUpdated)
        ////////{
        ////////  var typedEvent = (EventMessageUpdated)evt;
        ////////  // this.confirmations.confirm(event.properties.info.id)
        ////////  _confirmations.Confirm(typedEvent.Properties.Info.Id);
        ////////}

        ////////// session.status events pass the onEventFiltered pre-filter for all providers (see line 842),
        ////////// so this runs on every KiloProvider instance — including the Settings panel which has no
        ////////// tracked sessions. Update sessionStatusMap and forward to webview before the
        ////////// trackedSessionIds guard so the Settings panel's allStatusMap stays current for the
        ////////// busy-session warning on Save.
        ////////// if (event.type === "session.status") {
        ////////if (evt is EventSessionStatus)
        ////////{
        ////////  var typedEvent = (EventSessionStatus)evt;
        ////////  // const sid = event.properties.sessionID
        ////////  var type = typedEvent.Properties.Status.Type;
        ////////  // const prev = this.sessionStatusMap.get(sid)
        ////////  var prev = _sessionStatusMap.TryGetValue(sessionId, out var prevVal) ? prevVal : null;
        ////////  // if ((prev === undefined || prev === "idle") && event.properties.status.type !== "idle") {
        ////////  if ((prev == null || prev == "idle") && type != "idle")
        ////////  {
        ////////    // this.costs.rearm(sid)
        ////////    _costs.Rearm(sessionId);
        ////////  }
        ////////  // this.sessionStatusMap.set(sid, event.properties.status.type)
        ////////  _sessionStatusMap[sessionId] = type;
        ////////  // this.aborts.observe(sid, event.properties.status.type, directory)
        ////////  _aborts.Observe(sessionId, type, directory);
        ////////  // const msg = mapSSEEventToWebviewMessage(event, sid)
        ////////  var msg = MapSseEventToWebviewMessage(e, sessionId);
        ////////  // if (msg) {
        ////////  if (msg != null)
        ////////  {
        ////////    // this.streams.flush(sid)
        ////////    _streams.Flush(sessionId);
        ////////    // this.postMessage(msg)
        ////////    PostMessage(msg);
        ////////  }
        ////////  // return
        ////////  return;
        ////////}

        ////////// Extract sessionID from the event
        ////////// if (event.type === "session.created" && this.adoptPendingFollowup(event.properties.info)) {
        ////////if (evt is EventSessionCreated && AdoptPendingFollowup(raw.Payload["properties"]["info"]))
        ////////{
        ////////  // return
        ////////  return;
        ////////}

        ////////// const sessionID = this.resolveEventSessionId(event)
        
        ////////// Events without sessionID (server.connected, server.heartbeat, indexing.status) → always forward
        ////////// Events with sessionID → only forward if this webview tracks that session
        ////////// message.part.* events are always session-scoped; drop if session unknown.
        ////////// if (!sessionID && isSessionScopedPartEvent(event.type)) return
        ////////if (string.IsNullOrEmpty(sessionId) && sessionScopedPartEvents.Contains(e.EventType)) return;
        ////////// if (this.postModelUsageChanged(event, sessionID)) return
        ////////if (PostModelUsageChanged(e, sessionId)) return;
        ////////// if (
        //////////   event.type !== "indexing.status" &&
        //////////   event.type !== "session.deleted" &&
        //////////   sessionID &&
        //////////   !this.trackedSessionIds.has(sessionID)
        ////////// )
        ////////if (!(evt is EventIndexingStatus) && !(evt is EventSessionDeleted) && !string.IsNullOrEmpty(sessionId) && !IsSessionTracked(sessionId))
        ////////  //   return
        ////////  return;

        ////////// if (event.type === "session.updated" && typeof event.properties.info.cost === "number") {
        ////////if (evt is EventSessionUpdated && raw.Payload["info"]?["cost"]?.Type == JTokenType.Float)
        ////////{
        ////////  // const cost = this.costs.setSessionCost(event.properties.sessionID, event.properties.info.cost)
        ////////  var cost = _costs.SetSessionCost(sessionId, e.Payload["info"]?["cost"]?.Value<double>());
        ////////  // this.requestCostAlert(event.properties.sessionID, cost)
        ////////  RequestCostAlert(sessionId, cost);
        ////////}

        ////////// if (event.type === "session.updated") {
        ////////if (evt is EventSessionUpdated)
        ////////{
        ////////  // Full bus snapshots duplicate sync patches with the same event ID but no sequence metadata.
        ////////  // if (!isLegacySyncEvent(event)) return
        ////////  if (!raw.IsLegacySyncEvent) return;
        ////////  // const sid = event.properties.sessionID
        ////////  // const revision = this.revisions.get(sid)
        ////////  var revision = _revisions.TryGetValue(sessionId, out var revVal) ? revVal : null;
        ////////  // const versioned = event.seq > 0 || (revision?.seq ?? 0) > 0
        ////////  var versioned = e.Seq > 0 || (revision?.Seq ?? 0) > 0;
        ////////  // if (revision && (versioned ? event.seq <= revision.seq : event.id <= revision.id)) return
        ////////  if (revision != null && (versioned ? e.Seq <= revision.Seq : e.Payload["id"]?.Value<string>() <= revision.Id)) return;
        ////////  // this.revisions.set(sid, { id: event.id, seq: event.seq })
        ////////  _revisions[sessionId] = new { Id = e.Payload["id"]?.Value<string>(), Seq = e.Seq};
        ////////}

        ////////// Refresh provider and agent lists when the server signals a state disposal
        ////////// if (event.type === "global.disposed") {
        ////////if (evt is EventGlobalDisposed)
        ////////{
        ////////  // void this.reloadAfterAuthChange()
        ////////  ReloadAfterAuthChange();
        ////////  // return
        ////////  return;
        ////////}

        ////////// if (event.type === "server.instance.disposed") {
        ////////if (evt is EventServerInstanceDisposed)
        ////////{
        ////////  // const props = event.properties as Record<string, unknown> | null
        ////////  var props = e.Payload;
        ////////  // const dir = typeof props?.directory === "string" ? props.directory : undefined
        ////////  var dir = props?["directory"]?.Type == JTokenType.String ? props["directory"]?.Value<string>() : null;
        ////////  // if (dir) for (const sid of this.aborts.dispose(dir)) this.sessionStatusMap.set(sid, "idle")
        ////////  if (!string.IsNullOrEmpty(dir))
        ////////    foreach (var sid in _aborts.Dispose(dir))
        ////////      _sessionStatusMap[sid] = "idle";
        ////////  // if (dir && !sameDirectory(dir, this.getWorkspaceDirectory())) return
        ////////  if (!string.IsNullOrEmpty(dir) && !PathUtils.SameDirectory(dir, GetWorkspaceDirectory())) return;
        ////////  // void this.reloadAfterAuthChange()
        ////////  ReloadAfterAuthChange();
        ////////  // return
        ////////  return;
        ////////}

        ////////// Config was updated without a full dispose (e.g. permission-only save).
        ////////// Fetch and push the updated config + refresh agents and providers so the
        ////////// Settings panel and mode/model pickers reflect the change.
        ////////// if (event.type === "global.config.updated") {
        ////////if (evt is EventGlobalConfigUpdated)
        ////////{
        ////////  // this.requirements.clear()
        ////////  _requirements.Clear();
        ////////  // void Promise.all([this.fetchAndSendConfigUpdated(), this.fetchAndSendAgents(), this.fetchAndSendProviders()])
        ////////  _ = Task.WhenAll(FetchAndSendConfigUpdated(), FetchAndSendAgents(), FetchAndSendProviders());
        ////////  // return
        ////////  return;
        ////////}

        ////////// Forward relevant events to webview
        ////////// Side effects that must happen before the webview message is sent
        ////////// if (event.type === "message.updated") {
        ////////if (evt is EventMessageUpdated)
        ////////{
        ////////  // const info = event.properties.info
        ////////  var info = e.Payload["info"];
        ////////  // const value = info.role === "assistant" ? info.cost : undefined
        ////////  var value = info?["role"]?.Value<string>() == "assistant" ? info?["cost"]?.Value<double>() : (double?)null;
        ////////  // const cost = this.updateMessageCost(event.properties.sessionID, info.id, info.role, value)
        ////////  var cost = UpdateMessageCost(sessionId, info?["id"]?.Value<string>(), info?["role"]?.Value<string>(), value);
        ////////  // if (cost !== undefined) this.requestCostAlert(event.properties.sessionID, cost)
        ////////  if (cost != null) RequestCostAlert(sessionId, cost);
        ////////}
        ////////// if (event.type === "message.removed") {
        ////////if (evt is EventMessageRemoved)
        ////////{
        ////////  // this.removeMessageCost(event.properties.messageID)
        ////////  RemoveMessageCost(e.Payload["messageID"]?.Value<string>());
        ////////}
        ////////// if (event.type === "session.created" && !this.currentSession) {
        ////////if (evt is EventSessionCreated && _currentSession == null)
        ////////{
        ////////  // this.setCurrentSession(event.properties.info)
        ////////  SetCurrentSession(e.Payload["info"]);
        ////////  // this.contextSessionID = event.properties.info.id
        ////////  _contextSessionID = e.Payload["info"]?["id"]?.Value<string>();
        ////////  // this.trackedSessionIds.add(event.properties.info.id)
        ////////  _trackedSessionIds.Add(e.Payload["info"]?["id"]?.Value<string>());
        ////////}
        ////////// if (event.type === "session.updated" && this.currentSession?.id === event.properties.sessionID) {
        ////////if (evt is EventSessionUpdated && _currentSession?.Id == sessionId)
        ////////{
        ////////  // this.setCurrentSession(event.properties.info)
        ////////  SetCurrentSession(e.Payload["info"]);
        ////////  // this.contextSessionID = event.properties.sessionID
        ////////  _contextSessionID = sessionId;
        ////////}
        ////////// if (event.type === "session.deleted") {
        ////////if (evt is EventSessionDeleted)
        ////////{
        ////////  // const sid = event.properties.sessionID
        ////////  // this.trackedSessionIds.delete(sid)
        ////////  _trackedSessionIds.Remove(sessionId);
        ////////  // this.modelUsageSessionIds.delete(sid)
        ////////  _modelUsageSessionIds.Remove(sessionId);
        ////////  // this.sessionDirectories.delete(sid)
        ////////  _sessionDirectories.Remove(sessionId);
        ////////  // this.connectionService.pruneSession(sid)
        ////////  _connectionService.PruneSession(sessionId);
        ////////  // this.costs.onSessionDeleted(sid)
        ////////  _costs.OnSessionDeleted(sessionId);
        ////////}

        ////////// Auto-adopt child sessions as soon as the task tool part reveals their ID.
        ////////// This means the child's permission/question events are tracked immediately —
        ////////// before the webview renderer has a chance to call syncSession — eliminating
        ////////// the race where the child blocks on a prompt that the UI never sees.
        ////////// if (event.type === "message.part.updated") {
        ////////if (evt is EventMessagePartUpdated)
        ////////{
        ////////  // const part = event.properties.part as {
        ////////  //   type?: string
        ////////  //   tool?: string
        ////////  //   metadata?: { sessionId?: string }
        ////////  //   state?: { metadata?: { sessionId?: string } }
        ////////  //   sessionID?: string
        ////////  // }
        ////////  var part = e.Payload["part"];
        ////////  // const childId = childID(part)
        ////////  var childId = ChildId(part);
        ////////  // if (childId && !this.trackedSessionIds.has(childId)) {
        ////////  if (!string.IsNullOrEmpty(childId) && !_trackedSessionIds.Contains(childId))
        ////////  {
        ////////    // console.log("[Kilo New] KiloProvider: 🔗 Auto-adopting child session from task tool", { childId })
        ////////    Console.WriteLine($"[Kilo New] KiloProvider: 🔗 Auto-adopting child session from task tool {{ childId: {childId} }}");
        ////////    // void this.handleSyncSession(childId, part.sessionID ?? sessionID)
        ////////    _ = HandleSyncSession(childId, part?["sessionID"]?.Value<string>() ?? sessionId);
        ////////  }
        ////////}

        ////////// Drop the per-session caches for deleted sessions so a late
        ////////// handleLoadMessages response (or any other guarded read) can't resurrect
        ////////// transcript state for a session the webview just cleaned up. The
        ////////// prefilter lets session.deleted through without re-tracking, and the
        ////////// handleEvent guard does the same — this is the matching prune.
        ////////// if (event.type === "session.deleted" && sessionID) {
        ////////if (evt is EventSessionDeleted && !string.IsNullOrEmpty(sessionId))
        ////////{
        ////////  // this.pruneDeletedSession(sessionID)
        ////////  PruneDeletedSession(sessionId);
        ////////}

        ////////// if (!isLegacySyncEvent(event)) {
        ////////if (!raw.IsLegacySyncEvent)
        ////////{
        ////////  // const props = event.properties
        ////////  var props = e.Payload;
        ////////  // handleNetworkEvent(
        ////////  //   event.type,
        ////////  //   {
        ////////  //     id: "id" in props && typeof props.id === "string" ? props.id : undefined,
        ////////  //     sessionID: "sessionID" in props && typeof props.sessionID === "string" ? props.sessionID : undefined,
        ////////  //     requestID: "requestID" in props && typeof props.requestID === "string" ? props.requestID : undefined,
        ////////  //   },
        ////////  //   this.client,
        ////////  //   (s) => this.getWorkspaceDirectory(s),
        ////////  // )
        ////////  HandleNetworkEvent(
        ////////    e.EventType,
        ////////    new
        ////////    {
        ////////      Id = props["id"]?.Type == JTokenType.String ? props["id"]?.Value<string>() : null,
        ////////      SessionID = sessionId,
        ////////      RequestID = props["requestID"]?.Type == JTokenType.String ? props["requestID"]?.Value<string>() : null
        ////////    },
        ////////    Client,
        ////////    (s) => GetWorkspaceDirectory(s)
        ////////  );
        ////////}

        ////////// if (event.type === "indexing.status" && directory) {
        ////////if (evt is EventIndexingStatus && !string.IsNullOrEmpty(directory))
        ////////{
        ////////  // if (!sameDirectory(directory, this.getWorkspaceDirectory(this.currentSession?.id))) return
        ////////  if (!PathUtils.SameDirectory(directory, GetWorkspaceDirectory(_currentSession?.Id))) return;
        ////////}

        ////////// const msg = isLegacySyncEvent(event)
        //////////   ? this.mapSyncEventToWebviewMessage(event)
        //////////   : mapSSEEventToWebviewMessage(event, sessionID)
        ////////var msg = raw.IsLegacySyncEvent
        ////////  ? MapSyncEventToWebviewMessage(e)
        ////////  : MapSseEventToWebviewMessage(e, sessionId);
        ////////// if (!msg) return
        ////////if (msg == null) return;
        ////////// if (msg.type === "partUpdated") {
        ////////if (msg.type == "partUpdated")
        ////////{
        ////////  // this.streams.push({ ...msg, part: this.slimPart(msg.part) })
        ////////  _streams.Push(new { msg.type, part = SlimPart(msg.part) });
        ////////  // return
        ////////  return;
        ////////}
        ////////// const next = msg.type === "messageCreated" ? { ...msg, message: this.slimInfo(msg.message) } : msg
        ////////var next = msg.type == "messageCreated" ? new { msg.type, message = SlimInfo(msg.message) } : msg;
        ////////// if (next.type === "sandboxStatus") {
        ////////if (next.type == "sandboxStatus")
        ////////{
        ////////  // if (!sameDirectory(next.directory, this.getWorkspaceDirectory(next.sessionID))) return
        ////////  if (!PathUtils.SameDirectory(next.directory, GetWorkspaceDirectory(next.sessionID))) return;
        ////////  // this.postMessage({ ...next, revision: ++this.sandboxRevision })
        ////////  PostMessage(new { next.type, next.sessionID, next.directory, revision = ++_sandboxRevision });
        ////////  // return
        ////////  return;
        ////////}
        ////////// if (next.type === "indexingStatusLoaded") {
        ////////if (next.type == "indexingStatusLoaded")
        ////////{
        ////////  // this.cachedIndexingStatusMessage = next
        ////////  _cachedIndexingStatusMessage = next;
        ////////}
        ////////// this.streams.flush(sessionID)
        ////////_streams.Flush(sessionId);
        ////////// this.postMessage(next)
        ////////PostMessage(next);


      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] SSEHelper: error handling SSE event: {ex.Message}");
      }
    }

    //private void HandleSyncEvent(SyncEvent syncEvent)
    //{
    //  var name = syncEvent.Name;

    //  switch (name)
    //  {
    //    case "message.updated.1":
    //      HandleMessageUpdatedSync((MessageUpdatedSyncEvent)syncEvent);
    //      break;
    //    case "message.removed.1":
    //      HandleMessageRemovedSync(syncEvent);
    //      break;
    //    case "message.part.updated.1":
    //      HandlePartUpdatedSync((MessagePartUpdatedSyncEvent)syncEvent);
    //      break;
    //    case "message.part.removed.1":
    //      HandlePartRemovedSync(syncEvent);
    //      break;
    //    case "session.created.1":
    //      HandleSessionCreatedSync((SessionCreatedSyncEvent)syncEvent);
    //      break;
    //    case "session.updated.1":
    //      HandleSessionUpdatedSync((SessionUpdatedSyncEvent)syncEvent);
    //      break;
    //    case "session.deleted.1":
    //      HandleSessionDeletedSync(syncEvent);
    //      break;
    //  }
    //}

    //private void HandleStreamEvent(StreamEvent streamEvent)
    //{
    //  var type = streamEvent.EventType;
    //  var properties = streamEvent.Properties;
    //  var sessionID = streamEvent.SessionID;
    //  var directory = streamEvent.Directory;

    //  switch (type)
    //  {
    //    case "kilo-sessions.remote-status-changed":
    //      return;

    //    case "memory.status":
    //    case "memory.updated":
    //    case "memory.error":
    //      HandleMemoryEvent(type, properties);
    //      return;

    //    case "session.status":
    //      HandleSessionStatus(properties, sessionID);
    //      return;

    //    case "message.part.delta":
    //      HandlePartDelta(properties);
    //      return;

    //    case "session.created":
    //      //    HandleSessionCreatedStream(properties);
    //      break;

    //    case "session.updated":
    //      HandleSessionUpdatedStream(properties);
    //      break;

    //    case "session.deleted":
    //      //    HandleSessionDeletedStream(properties);
    //      break;

    //    case "message.updated":
    //      HandleMessageUpdatedStream(properties);
    //      break;

    //    case "message.removed":
    //      //    HandleMessageRemovedStream(properties);
    //      break;

    //    case "global.disposed":
    //      HandleGlobalDisposed();
    //      return;

    //    case "server.instance.disposed":
    //      HandleServerInstanceDisposed(properties);
    //      return;

    //    case "global.config.updated":
    //      HandleGlobalConfigUpdated();
    //      return;

    //    case "message.part.updated":
    //      HandlePartUpdatedStream(properties);
    //      break;

    //    case "indexing.status":
    //      HandleIndexingStatus(properties);
    //      break;

    //    case "session.turn.close":
    //      HandleSessionTurnClosed(properties);
    //      break;

    //    case "session.turn.open":
    //      HandleSessionTurnOpen(properties);
    //      break;

    //    case "session.network.asked":
    //    case "session.network.replied":
    //    case "session.network.rejected":
    //    case "session.network.restored":
    //      HandleNetworkEvent(type, properties);
    //      break;

    //    case "permission.asked":
    //      HandlePermissionAsked(properties);
    //      break;

    //    case "permission.replied":
    //      HandlePermissionReplied(properties);
    //      break;

    //    case "todo.updated":
    //      HandleTodoUpdated(properties);
    //      break;

    //    case "question.asked":
    //      HandleQuestionAsked(properties);
    //      break;

    //    case "question.replied":
    //    case "question.rejected":
    //      HandleQuestionResolved(properties);
    //      break;

    //    case "suggestion.shown":
    //      HandleSuggestionShown(properties);
    //      break;

    //    case "suggestion.accepted":
    //    case "suggestion.dismissed":
    //      HandleSuggestionResolved(properties);
    //      break;

    //    case "session.error":
    //      HandleSessionError(properties);
    //      break;

    //    case "sandbox.status.changed":
    //      HandleSandboxStatusChanged(properties);
    //      break;
    //  }
    //}

    private void HandleMessageUpdatedSync(MessageUpdatedSyncEvent evt)
    {
      var data = (ApiClient.EventMessageUpdated)evt.Data;
      var info = data.Properties.Info;
      var infoJson = info.ToJson();
      var infoObj = JObject.Parse(infoJson);
      var messageID = infoObj["id"]?.Value<string>();
      var sessionID = data.Properties.SessionID;

      RecordMessageSessionId(messageID, sessionID);

      if (infoObj["cost"]?.Type == JTokenType.Float && infoObj["role"]?.Value<string>() == "assistant")
      {
        SessionHandler.GetOrCreateMessageCost(messageID, sessionID).Cost = infoObj["cost"].Value<double>();
      }

      var timeObj = infoObj["time"];
      var createdAt = timeObj != null && timeObj["created"]?.Type == JTokenType.Integer
          ? DateTimeOffset.FromUnixTimeMilliseconds((long)timeObj["created"].Value<long>()).ToUniversalTime().ToString("o")
          : DateTime.UtcNow.ToString("o");

      var message = new WebViewMessage
      {
        Id = messageID,
        SessionID = sessionID,
        Role = infoObj["role"]?.Value<string>(),
        Content = infoObj["content"]?.ToString(),
        Parts = infoObj["parts"],
        CreatedAt = createdAt,
        Time = timeObj != null ? new TimeType { Created = (double)timeObj["created"]?.Value<long>(), Completed = timeObj["updated"]?.Value<long>() } : null,
        Agent = infoObj["agent"]?.ToString(),
        //Model = new ModelType { ModelID = } // infoObj["model"]?.ToString(),
        ProviderID = infoObj["providerID"]?.Value<string>(),
        ModelID = infoObj["modelID"]?.Value<string>()
      };

      PostMessage(new MessageCreatedMessage { Message = message });
    }

    //private void HandleMessageRemovedSync(SyncEvent evt)
    //{
    //  var data = (KiloVisualStudioExtension.ApiClient.EventMessageRemoved)evt.Data;

    //  _messageCosts.Remove(data.Properties.MessageID);

    //  PostMessage(new MessageRemovedMessage
    //  {
    //    SessionID = data.Properties.SessionID,
    //    MessageID = data.Properties.MessageID
    //  });
    //}

    private void HandlePartUpdatedSync(MessagePartUpdatedSyncEvent evt)
    {
      var data = (KiloVisualStudioExtension.ApiClient.EventMessagePartUpdated)evt.Data;
      var part = data.Properties.Part;
      var sessionID = data.Properties.SessionID;
      var partJson = part.ToJson();
      var partObj = JObject.Parse(partJson);
      var messageID = partObj["messageID"]?.Value<string>();

      var metadata = partObj["metadata"];
      if (metadata != null && metadata is JObject metadataObj && metadataObj["sessionId"] != null)
      {
        var childId = metadataObj["sessionId"].Value<string>();
        if (!string.IsNullOrEmpty(childId) && !SessionHandler.IsTrackedSession(childId))
        {
          System.Diagnostics.Debug.WriteLine($"[Kilo] SSEHelper: Auto-adopting child session: {childId}");
          SessionHandler.TrackSession(childId);
        }
      }

      PostMessage(new PartUpdatedMessage
      {
        SessionID = sessionID,
        MessageID = messageID,
        Part = part,

      });
    }

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

    private void HandleSessionCreatedSync(SessionCreatedSyncEvent evt)
    {
      var data = (KiloVisualStudioExtension.ApiClient.EventSessionCreated)evt.Data;
      var info = data.Properties.Info;
      var sessionID = info.Id;

      if (string.IsNullOrEmpty(Provider.GetCurrentSessionID()))
      {
        Provider.SetCurrentSessionID(sessionID);
        SessionHandler.TrackSession(sessionID);
      }

      var createdAt = info.Time != null
          ? DateTimeOffset.FromUnixTimeMilliseconds((long)info.Time.Created).ToUniversalTime().ToString("o")
          : DateTime.UtcNow.ToString("o");
      var updatedAt = info.Time != null
          ? DateTimeOffset.FromUnixTimeMilliseconds((long)info.Time.Updated).ToUniversalTime().ToString("o")
          : DateTime.UtcNow.ToString("o");

      PostMessage(new SessionCreatedMessage
      {
        Session = new KiloExtensionDTOs.Sessions.SessionInfo
        {
          Id = sessionID,
          ParentID = info.ParentID,
          Title = info.Title,
          CreatedAt = createdAt,
          UpdatedAt = updatedAt,
          Revert = info.Revert,
          Summary = info.Summary
        }
      });
    }

    private void HandleSessionUpdatedSync(SessionUpdatedSyncEvent evt)
    {
      var data = (ApiClient.EventSessionUpdated)evt.Data;
      var info = data.Properties.Info;
      var sessionID = data.Properties.SessionID;

      if (!string.IsNullOrEmpty(evt.Id) && evt.Seq > 0)
      {
        if (IsStaleEvent(sessionID, evt.Id, evt.Seq))
        {
          System.Diagnostics.Debug.WriteLine($"[Kilo] SSEHelper: Dropping stale session.updated event for {sessionID}");
          return;
        }
        UpdateRevision(sessionID, evt.Id, evt.Seq);
      }

      if (Provider.GetCurrentSessionID() == sessionID)
      {
        Provider.SetCurrentSessionID(sessionID);
      }

      var createdAt = info.Time != null
          ? DateTimeOffset.FromUnixTimeMilliseconds((long)info.Time.Created).ToUniversalTime().ToString("o")
          : DateTime.UtcNow.ToString("o");
      var updatedAt = info.Time != null
          ? DateTimeOffset.FromUnixTimeMilliseconds((long)info.Time.Updated).ToUniversalTime().ToString("o")
          : DateTime.UtcNow.ToString("o");

      PostMessage(new SessionUpdatedMessage
      {
        Session = new SessionUpdate
        {
          Id = sessionID,
          ParentID = info.ParentID,
          Title = info.Title,
          CreatedAt = createdAt,
          UpdatedAt = updatedAt,
          Revert = info.Revert,
          Summary = info.Summary
        }
      });
    }

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

    public bool IsStaleEvent(string sessionID, string eventId, int seq)
    {
      var revision = SessionHandler.GetRevision(sessionID);
      if (revision == null)
      {
        return false;
      }

      var versioned = seq > 0 || revision.Seq > 0;
      if (versioned)
      {
        return seq <= revision.Seq;
      }

      return long.Parse(eventId) <= revision.Id;
    }

    public void UpdateRevision(string sessionID, string eventId, int seq)
    {
      SessionHandler.TrackRevision(sessionID, new SessionRevision
      {
        Id = long.Parse(eventId),
        Seq = seq
      });
    }

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

    public void SetProjectID(string? projectID)
    {
      _currentProjectID = projectID;
    }

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

    private readonly HashSet<string> memoryEvents = ["memory.status", "memory.updated", "memory.error"];
    private readonly HashSet<string> sessionScopedPartEvents = ["message.part.updated", "message.part.delta", "message.part.removed"];

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

  }

  public partial class SessionRevision
  {
    public long Id { get; set; }
    public int Seq { get; set; }
  }

  public partial class MessageCost
  {
    public string SessionID { get; set; } = "";
    public string MessageID { get; set; } = "";
    public double Cost { get; set; }
  }
}
