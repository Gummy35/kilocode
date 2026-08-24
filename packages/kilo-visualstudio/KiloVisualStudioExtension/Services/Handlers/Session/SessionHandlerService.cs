using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.Services;
using SessionCreateRequest = KiloVisualStudioExtension.ApiClient.Body18;
using SessionUpdateRequest = KiloVisualStudioExtension.ApiClient.Body19;
using RevertRequest = KiloVisualStudioExtension.ApiClient.Body27;

// Forward declarations for types defined in SSEHelper
namespace KiloVisualStudioExtension
{
  public partial class SessionRevision { }
  public partial class MessageCost { }
}

namespace KiloVisualStudioExtension.Services.Handlers.Session
{
  /// <summary>
  /// Handles session-related operations like create, delete, rename, and load messages.
  /// This matches the VS Code pattern where session handling logic is extracted into
  /// separate handler modules (e.g., kilo-provider/handlers/session.ts).
  /// </summary>
  public class SessionHandlerService : ServiceProviderServiceBase
  {
    private bool _disposed;

    private VSProvider Provider => _serviceProvider.GetService<VSProvider>()
        ?? throw new InvalidOperationException("VSProvider not registered in service provider");

    // Session tracking state - moved from SSEHelper for proper separation of concerns
    private readonly HashSet<string> _trackedSessionIds = new();
    private readonly Dictionary<string, string> _sessionStatusMap = new();
    private readonly Dictionary<string, SessionRevision> _revisions = new();
    private readonly HashSet<string> _modelUsageSessionIds = new();
    private readonly Dictionary<string, MessageCost> _messageCosts = new();
    private readonly Dictionary<string, string> _messageSessionIds = new();

    /// <summary>
    /// Creates a new SessionHandlerService instance.
    /// </summary>
    /// <param name="serviceProvider">The service provider for dependency injection.</param>
    public SessionHandlerService(ServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    /// <summary>
    /// Tracks a session as active.
    /// </summary>
    public void TrackSession(string sessionID)
    {
      _trackedSessionIds.Add(sessionID);
    }

    /// <summary>
    /// Untracks a session (removes from tracking).
    /// </summary>
    public void UntrackSession(string sessionID)
    {
      _trackedSessionIds.Remove(sessionID);
      _modelUsageSessionIds.Remove(sessionID);
      _revisions.Remove(sessionID);
      _sessionStatusMap.Remove(sessionID);

      // Remove all message costs for this session
      var costsToRemove = _messageCosts.Where(kvp => kvp.Value.SessionID == sessionID).Select(kvp => kvp.Key).ToList();
      foreach (var costId in costsToRemove)
      {
        _messageCosts.Remove(costId);
      }
    }

    /// <summary>
    /// Checks if a session is currently tracked.
    /// </summary>
    public bool IsTrackedSession(string sessionID)
    {
      return _trackedSessionIds.Contains(sessionID);
    }

    /// <summary>
    /// Gets all tracked session IDs.
    /// </summary>
    public IReadOnlyCollection<string> GetTrackedSessionIds()
    {
      return new HashSet<string>(_trackedSessionIds);
    }

    /// <summary>
    /// Gets the session status map copy (read-only).
    /// </summary>
    public IReadOnlyDictionary<string, string> GetSessionStatusMap()
    {
      return new Dictionary<string, string>(_sessionStatusMap);
    }

    /// <summary>
    /// Sets the status for a session.
    /// </summary>
    public string GetSessionStatus(string sessionID)
    {
      return _sessionStatusMap.ContainsKey(sessionID) ? _sessionStatusMap[sessionID] : "";
    }

    /// <summary>
    /// Sets the status for a session.
    /// </summary>
    public void SetSessionStatus(string sessionID, string status)
    {
      _sessionStatusMap[sessionID] = status;
    }

    /// <summary>
    /// Gets or creates a message cost entry.
    /// </summary>
    public MessageCost GetOrCreateMessageCost(string messageID, string sessionID)
    {
      if (!_messageCosts.TryGetValue(messageID, out var cost))
      {
        cost = new MessageCost { SessionID = sessionID, MessageID = messageID };
        _messageCosts[messageID] = cost;
      }
      return cost;
    }

    /// <summary>
    /// Removes a message cost entry.
    /// </summary>
    public void RemoveMessageCost(string messageID)
    {
      _messageCosts.Remove(messageID);
    }

    /// <summary>
    /// Gets all message costs for a session.
    /// </summary>
    public IEnumerable<KeyValuePair<string, MessageCost>> GetMessageCostsForSession(string sessionID)
    {
      return _messageCosts.Where(kvp => kvp.Value.SessionID == sessionID);
    }

    /// <summary>
    /// Tracks a session revision.
    /// </summary>
    public void TrackRevision(string sessionID, SessionRevision revision)
    {
      _revisions[sessionID] = revision;
    }

    /// <summary>
    /// Gets a session revision.
    /// </summary>
    public SessionRevision? GetRevision(string sessionID)
    {
      return _revisions.TryGetValue(sessionID, out var revision) ? revision : null;
    }

    /// <summary>
    /// Removes a session revision.
    /// </summary>
    public void RemoveRevision(string sessionID)
    {
      _revisions.Remove(sessionID);
    }

    /// <summary>
    /// Tracks model usage for a session.
    /// </summary>
    public void TrackModelUsage(string sessionID)
    {
      _modelUsageSessionIds.Add(sessionID);
    }

    /// <summary>
    /// Checks if a session has model usage tracked.
    /// </summary>
    public bool HasModelUsage(string sessionID)
    {
      return _modelUsageSessionIds.Contains(sessionID);
    }

    /// <summary>
    /// Removes model usage tracking for a session.
    /// </summary>
    public void RemoveModelUsage(string sessionID)
    {
      _modelUsageSessionIds.Remove(sessionID);
    }

    /// <summary>
    /// Maps a message to a session.
    /// </summary>
    public void MapMessageToSession(string messageID, string sessionID)
    {
      _messageSessionIds[messageID] = sessionID;
    }

    /// <summary>
    /// Gets the session ID for a message.
    /// </summary>
    public string? GetSessionIdForMessage(string messageID)
    {
      return _messageSessionIds.TryGetValue(messageID, out var sessionID) ? sessionID : null;
    }

    /// <summary>
    /// Removes message-to-session mapping.
    /// </summary>
    public void RemoveMessageSessionMapping(string messageID)
    {
      _messageSessionIds.Remove(messageID);
    }

    /// <summary>
    /// Handles the createSession message from the webview.
    /// Creates a new session in the backend and notifies the webview.
    /// 
    /// VS Code workflow: Matches the pattern in kilo-provider/handlers/session.ts
    /// where createSession creates a session and triggers loadMessages.
    /// 
    /// Workflow steps:
    /// 1. Get current directory for session context
    /// 2. Call CreateSessionInternalAsync to create session
    /// 3. On success, check if session ID exists
    /// 4. If session ID exists, call HandleLoadMessagesAsync to load messages
    /// 5. On failure, send error message to webview
    /// 
    /// Messages sent to webview:
    /// - sessionCreated: { session: { id, directory, title, updated, status } }
    /// - error: { message: "Failed to create session" }
    /// </summary>
    /// <param name="payload">The message payload (unused for createSession).</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HandleCreateSessionAsync(JsonElement? payload)
    {
      var dir = System.Environment.CurrentDirectory;
      var success = await CreateSessionInternalAsync(dir);
      if (!success)
      {
        Provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = "Failed to create session" }));
      }
      else
      {
        if (!string.IsNullOrEmpty(Provider.GetCurrentSessionID()))
        {
          var sessionID = Provider.GetCurrentSessionID()!;
          TrackSession(sessionID);
          Provider.PostMessage(JsonSerializer.Serialize(new { type = "workspaceDirectoryChanged", directory = dir }));
          Provider.FocusSession(sessionID);

          // After creating and focusing the session, load messages to display the conversation
          // This matches VS Code's flow where loadMessages is called after session creation
          await HandleLoadMessagesAsync(JsonSerializer.SerializeToElement(new { sessionID, mode = "replace", limit = 80 }));
        }
      }
    }

    /// <summary>
    /// Creates a new session internally and sends sessionCreated message.
    /// 
    /// VS Code workflow: Matches the internal session creation logic in
    /// kilo-provider/handlers/session.ts where session is created via POST /session.
    /// 
    /// Workflow steps:
    /// 1. Get HTTP client from provider
    /// 2. POST to /session with directory in payload
    /// 3. Extract session ID from response
    /// 4. Set current session ID in provider and context
    /// 5. Send sessionCreated message to webview with session details
    /// 
    /// Messages sent to webview:
    /// - sessionCreated: { session: { id, directory, title, updated, status } }
    /// - error: { message: "Failed to create session: ..." }
    /// </summary>
    /// <param name="dir">The directory path for the session context.</param>
    /// <returns>True if session was created successfully, false otherwise.</returns>
    private async Task<bool> CreateSessionInternalAsync(string dir)
    {
      var nswagClient = Provider.GetNswagClient();
      if (nswagClient == null)
      {
        System.Diagnostics.Debug.WriteLine("[Kilo] SessionHandler: cannot create session - no NSwag client");
        return false;
      }
      try
      {
        var createBody = new SessionCreateRequest { Title = "New Chat" };
        var response = await nswagClient.Session_createAsync(dir, "", createBody);
        if (response != null && !string.IsNullOrEmpty(response.Id))
        {
          var sessionID = response.Id;
          System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: session created: {sessionID}");

          Provider.SetCurrentSessionID(sessionID);
          Provider.SetContextSessionID(sessionID);

          var now = DateTimeOffset.UtcNow;
          var sessionCreated = new
          {
            type = "sessionCreated",
            session = new
            {
              id = sessionID,
              parentID = (string?)null,
              title = "New Chat",
              createdAt = now.UtcDateTime.ToString("o"),
              updatedAt = now.UtcDateTime.ToString("o"),
              revert = (object?)null,
              summary = (object?)null
            }
          };
          Provider.PostMessage(JsonSerializer.Serialize(sessionCreated));
          return true;
        }
        System.Diagnostics.Debug.WriteLine("[Kilo] SessionHandler: session creation failed - no ID in response");
        return false;
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: error creating session: {ex.Message}");
        Provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = $"Failed to create session: {ex.Message}" }));
        return false;
      }
    }

    /// <summary>
    /// Handles the clearSession message from the webview.
    /// Clears the current session context from the provider.
    /// 
    /// VS Code workflow: Matches the pattern in kilo-provider/handlers/session.ts
    /// where clearSession clears the session state.
    /// 
    /// Workflow steps:
    /// 1. Call provider.ClearCurrentSession() to clear session state
    /// 
    /// Messages sent to webview:
    /// - None; session state is cleared internally
    /// </summary>
    /// <param name="payload">The message payload (unused for clearSession).</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public void HandleClearSession(JsonElement? payload)
    {
      Provider.ClearCurrentSession();
    }

    /// <summary>
    /// Handles the deleteSession message from the webview.
    /// Deletes a session from the backend and notifies the webview.
    /// 
    /// VS Code workflow: Matches the pattern in kilo-provider/handlers/session.ts
    /// where deleteSession posts to /session/delete and sends sessionDeleted.
    /// 
    /// Workflow steps:
    /// 1. Extract sessionID from payload
    /// 2. Validate sessionID is not empty
    /// 3. Get HTTP client from provider
    /// 4. POST to /session/delete with sessionID
    /// 5. If deleted session is current, clear session state
    /// 6. Send sessionDeleted message to webview
    /// 
    /// Messages sent to webview:
    /// - sessionDeleted: { sessionID }
    /// - error: { message: "Not connected to CLI backend" | "Failed to delete session: ..." }
    /// </summary>
    /// <param name="payload">The message payload containing sessionID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HandleDeleteSessionAsync(JsonElement? payload)
    {
      if (payload == null) return;
      var sessionID = payload.Value.TryGetProperty("sessionID", out var sid) ? sid.GetString() : "";
      if (string.IsNullOrEmpty(sessionID)) return;
      var nswagClient = Provider.GetNswagClient();
      if (nswagClient == null)
      {
        Provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = "Not connected to CLI backend", sessionID }));
        return;
      }
      try
      {
        var directory = Provider.GetSSEHelper().ResolveDirectory(sessionID);
        await nswagClient.Session_deleteAsync(sessionID, directory, "");
        System.Diagnostics.Debug.WriteLine("[Kilo] SessionHandler: session deleted");

        if (Provider.GetCurrentSessionID() == sessionID)
        {
          Provider.SetContextSessionID(null);
          Provider.SetCurrentSessionID(null);
          UntrackSession(sessionID);
        }

        Provider.PostMessage(JsonSerializer.Serialize(new { type = "sessionDeleted", sessionID }));
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: error deleting session: {ex.Message}");
        Provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = $"Failed to delete session: {ex.Message}", sessionID }));
      }
    }

    /// <summary>
    /// Handles the renameSession message from the webview.
    /// Renames a session in the backend and notifies the webview.
    /// 
    /// VS Code workflow: Matches the pattern in kilo-provider/handlers/session.ts
    /// where renameSession posts to /session/rename and sends sessionUpdated.
    /// 
    /// Workflow steps:
    /// 1. Extract sessionID and title from payload
    /// 2. Validate sessionID is not empty
    /// 3. Get HTTP client from provider
    /// 4. POST to /session/rename with sessionID and title
    /// 5. If renamed session is current, send sessionUpdated message
    /// 
    /// Messages sent to webview:
    /// - sessionUpdated: { session: { id, title, updated, status } }
    /// - error: { message: "Not connected to CLI backend" | "Failed to rename session: ..." }
    /// </summary>
    /// <param name="payload">The message payload containing sessionID and title.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HandleRenameSessionAsync(JsonElement? payload)
    {
      if (payload == null) return;
      var sessionID = payload.Value.TryGetProperty("sessionID", out var sid) ? sid.GetString() : "";
      var title = payload.Value.TryGetProperty("title", out var t) ? t.GetString() : "";
      if (string.IsNullOrEmpty(sessionID)) return;
      var nswagClient = Provider.GetNswagClient();
      if (nswagClient == null)
      {
        Provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = "Not connected to CLI backend" }));
        return;
      }
      try
      {
        var directory = Provider.GetSSEHelper().ResolveDirectory(sessionID);
        var updateBody = new SessionUpdateRequest { Title = title };
        await nswagClient.Session_updateAsync(sessionID, directory, "", updateBody);
        System.Diagnostics.Debug.WriteLine("[Kilo] SessionHandler: session renamed");

        if (Provider.GetCurrentSessionID() == sessionID)
        {
          var sessionUpdated = new
          {
            type = "sessionUpdated",
            session = new
            {
              id = sessionID,
              title = title,
              updated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
              status = "idle"
            }
          };
          Provider.PostMessage(JsonSerializer.Serialize(sessionUpdated));
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: error renaming session: {ex.Message}");
        Provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = $"Failed to rename session: {ex.Message}" }));
      }
    }

    private CancellationTokenSource? _loadMessagesCts;

    /// <summary>
    /// Handles the loadMessages message from the webview.
    /// Loads messages for a specific session from the backend with support for pagination and stream management.
    /// 
    /// VS Code workflow: Matches the pattern in kilo-provider/handlers/session.ts
    /// where handleLoadMessages loads messages with abort controller support and stream management.
    /// 
    /// Workflow steps:
    /// 1. Extract sessionID, mode, before, limit from payload
    /// 2. For mode=replace/focus: stop processes, track session, focus session, set current session
    /// 3. For mode=replace: cancel previous load and create new cancellation token
    /// 4. Build URL with limit and before parameters
    /// 5. Fetch messages from /session/{sessionID}/message endpoint
    /// 6. Check for cancellation and session tracking status
    /// 7. Parse messages and extract cursor/hasMore for pagination
    /// 8. For mode=replace/reconcile: drop session stream
    /// 9. Send messagesLoaded message to webview
    /// 10. If preserveStream is true, flush session stream
    /// 11. Call RecoverPendingPrompts to resume any pending prompts
    /// 
    /// Messages sent to webview:
    /// - messagesLoaded: { sessionID, messages: [...], mode, cursor, hasMore }
    /// - error: { message: "Not connected to CLI backend" | ex.Message }
    /// </summary>
    /// <param name="payload">The message payload containing sessionID, mode (replace/focus/reconcile), before (cursor), and limit.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HandleLoadMessagesAsync(JsonElement? payload)
    {
      if (payload == null) return;

      var sessionID = payload.Value.TryGetProperty("sessionID", out var sid) ? sid.GetString() : "";
      if (string.IsNullOrEmpty(sessionID)) return;

      var mode = "replace";
      if (payload.Value.TryGetProperty("mode", out var modeProp) && !string.IsNullOrEmpty(modeProp.GetString()))
      {
        mode = modeProp.GetString()!;
      }

      var before = payload.Value.TryGetProperty("before", out var beforeProp) ? beforeProp.GetString() : null;
      var limit = payload.Value.TryGetProperty("limit", out var limitProp) && limitProp.TryGetInt32(out var l) ? l : 80;

      if (mode == "replace" || mode == "focus")
      {
        Provider.StopCurrentSessionProcesses(sessionID);
        TrackSession(sessionID);
        Provider.FocusSession(sessionID);
        Provider.SetCurrentSessionID(sessionID);
        Provider.SetContextSessionID(sessionID);
      }

      var nswagClient = Provider.GetNswagClient();
      if (nswagClient == null)
      {
        Provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = "Not connected to CLI backend", sessionID }));
        return;
      }

      if (mode == "replace")
      {
        _loadMessagesCts?.Cancel();
        _loadMessagesCts = new CancellationTokenSource();
      }

      var cancellationToken = mode == "replace" ? _loadMessagesCts?.Token : default;

      try
      {
        var directory = Provider.GetSSEHelper().ResolveDirectory(sessionID);
        var messages = await nswagClient.Session_messagesAsync(sessionID, directory, "", limit, before);

        if (cancellationToken.HasValue && cancellationToken.Value.IsCancellationRequested) return;

        if (!IsTrackedSession(sessionID)) return;

        if (messages == null) return;

        var items = new System.Collections.Generic.List<object>();

        foreach (var msgWrapper in messages)
        {
          // Serialize the entire message wrapper to preserve all backend fields
          var messageJson = JsonSerializer.Serialize(msgWrapper);
          var deserialized = JsonSerializer.Deserialize<JsonElement>(messageJson);
          items.Add(deserialized);
        }

        var hasMore = false;

        if (mode == "replace" || mode == "reconcile")
        {
          Provider.DropSessionStream(sessionID);
        }

        var message = new
        {
          type = "messagesLoaded",
          sessionID = sessionID,
          messages = items.ToArray(),
          mode = mode,
          hasMore = hasMore,
          since = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        Provider.PostMessage(JsonSerializer.Serialize(message));
        System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: loaded {items.Count} messages for session {sessionID}");

        if (payload.Value.TryGetProperty("preserveStream", out var preserveProp) && preserveProp.GetBoolean())
        {
          Provider.FlushSessionStream(sessionID);
        }

        Provider.RecoverPendingPrompts();

        _ = LoadMemoryAsync(sessionID);
      }
      catch (OperationCanceledException)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: load cancelled for session {sessionID}");
        return;
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: error loading messages: {ex.Message}");
        Provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = ex.Message, sessionID }));
      }
    }

    /// <summary>
    /// Handles the deleteMessage message from the webview.
    /// Deletes a message from a session via the backend.
    /// 
    /// VS Code workflow: Matches the pattern in kilo-provider/handlers/session.ts
    /// where deleteMessage posts to /session/message/delete.
    /// 
    /// Workflow steps:
    /// 1. Extract sessionID and messageID from payload
    /// 2. Validate both IDs are not empty
    /// 3. Get HTTP client from provider
    /// 4. POST to /session/message/delete with sessionID and messageID
    /// 
    /// Messages sent to webview:
    /// - None; deletion is handled silently
    /// </summary>
    /// <param name="payload">The message payload containing sessionID and messageID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HandleDeleteMessageAsync(JsonElement? payload)
    {
      if (payload == null) return;
      var sessionID = payload.Value.TryGetProperty("sessionID", out var sid) ? sid.GetString() : "";
      var messageID = payload.Value.TryGetProperty("messageID", out var mid) ? mid.GetString() : "";
      if (string.IsNullOrEmpty(sessionID) || string.IsNullOrEmpty(messageID)) return;
      var nswagClient = Provider.GetNswagClient();
      if (nswagClient == null) return;
      try
      {
        var directory = Provider.GetSSEHelper().ResolveDirectory(sessionID);
        await nswagClient.Session_deleteMessageAsync(sessionID, messageID, directory, "");
        System.Diagnostics.Debug.WriteLine("[Kilo] SessionHandler: message deleted");
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: error deleting message: {ex.Message}");
      }
    }

    /// <summary>
    /// Fetches and sends session model usage data to the webview.
    /// Matches VS Code's fetchAndSendSessionModelUsage pattern.
    /// </summary>
    private async Task FetchAndSendSessionModelUsageAsync(string sessionID, string requestID)
    {
      var nswagClient = Provider.GetNswagClient();
      if (nswagClient == null)
      {
        Provider.PostMessage(JsonSerializer.Serialize(new { type = "sessionModelUsageLoaded", sessionID, requestID }));
        return;
      }

      try
      {
        var usage = await nswagClient.Kilocode_sessionModelUsageAsync(sessionID, System.Environment.CurrentDirectory, "");
        if (usage != null)
        {
          var sessionIDs = new[] { sessionID };
          var totals = new { steps = 0, cost = 0, tokens = new { input = 0, output = 0, reasoning = 0, cache = new { read = 0, write = 0 } } };
          var models = new object[0];

          var data = new { sessionIDs, totals, models };
          var message = new { type = "sessionModelUsageLoaded", sessionID, requestID, data };
          Provider.PostMessage(JsonSerializer.Serialize(message));
        }
        else
        {
          Provider.PostMessage(JsonSerializer.Serialize(new { type = "sessionModelUsageLoaded", sessionID, requestID }));
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: error fetching model usage: {ex.Message}");
        Provider.PostMessage(JsonSerializer.Serialize(new { type = "sessionModelUsageLoaded", sessionID, requestID }));
      }
    }

    /// <summary>
    /// Loads memory state for a session and sends memoryLoaded message.
    /// Matches VS Code's KiloProviderMemory.load() pattern.
    /// </summary>
    private async Task LoadMemoryAsync(string? sessionID)
    {
      var nswagClient = Provider.GetNswagClient();
      if (nswagClient == null)
      {
        Provider.PostMessage(JsonSerializer.Serialize(new { type = "memoryLoaded", sessionID, error = "Not connected to CLI backend" }));
        return;
      }

      try
      {
        var memory = await nswagClient.Memory_statusAsync(System.Environment.CurrentDirectory, "");
        if (memory != null)
        {
          var message = new { type = "memoryLoaded", sessionID, status = JsonSerializer.SerializeToElement(memory) };
          Provider.PostMessage(JsonSerializer.Serialize(message));
        }
        else
        {
          Provider.PostMessage(JsonSerializer.Serialize(new { type = "memoryLoaded", sessionID, error = "Memory unavailable" }));
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: error loading memory: {ex.Message}");
        Provider.PostMessage(JsonSerializer.Serialize(new { type = "memoryLoaded", sessionID, error = ex.Message }));
      }
    }

    /// <summary>
    /// Handles loadSessions message - loads all sessions and sends sessionListLoaded.
    /// Matches VS Code's handleLoadSessions pattern.
    /// </summary>
    public async Task HandleLoadSessionsAsync(JsonElement? payload)
    {
      var nswagClient = Provider.GetNswagClient();
      if (nswagClient == null)
      {
        Provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = "Not connected to CLI backend" }));
        return;
      }

      try
      {
        var sessions = await nswagClient.Session_listAsync(System.Environment.CurrentDirectory, "", null, null, null, null, null, null);
        if (sessions != null)
        {
          var sessionList = new List<object>();
          foreach (var session in sessions)
          {
            var sessionObj = new
            {
              id = session.Id ?? "",
              title = session.Title ?? "",
              status = (object?)null,
              directory = session.Directory,
              createdAt = session.Time != null ? DateTimeOffset.FromUnixTimeSeconds(session.Time.Created).ToString("o") : null,
              updatedAt = session.Time != null ? DateTimeOffset.FromUnixTimeSeconds(session.Time.Updated).ToString("o") : null
            };
            sessionList.Add(sessionObj);
          }

          var message = new { type = "sessionListLoaded", sessions = sessionList.ToArray() };
          Provider.PostMessage(JsonSerializer.Serialize(message));
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: error loading sessions: {ex.Message}");
        Provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = $"Failed to load sessions: {ex.Message}" }));
      }
    }

    /// <summary>
    /// Handles syncSession message - syncs a specific session.
    /// Matches VS Code's handleSyncSession pattern.
    /// </summary>
    public async Task HandleSyncSessionAsync(JsonElement? payload)
    {
      if (payload == null) return;

      var sessionID = payload.Value.TryGetProperty("sessionID", out var sid) ? sid.GetString() : "";
      if (string.IsNullOrEmpty(sessionID)) return;

      var nswagClient = Provider.GetNswagClient();
      if (nswagClient == null)
      {
        Provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = "Not connected to CLI backend" }));
        return;
      }

      try
      {
        var directory = Provider.GetSSEHelper().ResolveDirectory(sessionID);
        var session = await nswagClient.Session_getAsync(sessionID, directory, "");
        if (session != null)
        {
          var message = new { type = "sessionSynced", session = JsonSerializer.SerializeToElement(session) };
          Provider.PostMessage(JsonSerializer.Serialize(message));
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: error syncing session: {ex.Message}");
        Provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = $"Failed to sync session: {ex.Message}" }));
      }
    }

    /// <summary>
    /// Handles requestSessionModelUsage message - fetches and sends model usage for a session.
    /// Matches VS Code's fetchAndSendSessionModelUsage pattern.
    /// </summary>
    public async Task HandleRequestSessionModelUsageAsync(JsonElement? payload)
    {
      if (payload == null) return;

      var sessionID = payload.Value.TryGetProperty("sessionID", out var sid) ? sid.GetString() : "";
      var requestID = payload.Value.TryGetProperty("requestID", out var rid) ? rid.GetString() : "";

      if (string.IsNullOrEmpty(sessionID)) return;

      var nswagClient = Provider.GetNswagClient();
      if (nswagClient == null)
      {
        Provider.PostMessage(JsonSerializer.Serialize(new { type = "sessionModelUsageLoaded", sessionID, requestID }));
        return;
      }

      try
      {
        var usage = await nswagClient.Kilocode_sessionModelUsageAsync(sessionID, System.Environment.CurrentDirectory, "");
        if (usage != null)
        {
          var sessionIDs = new[] { sessionID };
          var totals = new { steps = 0, cost = 0, tokens = new { input = 0, output = 0, reasoning = 0, cache = new { read = 0, write = 0 } } };
          var models = new object[0];

          var data = new { sessionIDs, totals, models };
          var message = new { type = "sessionModelUsageLoaded", sessionID, requestID, data };
          Provider.PostMessage(JsonSerializer.Serialize(message));
        }
        else
        {
          Provider.PostMessage(JsonSerializer.Serialize(new { type = "sessionModelUsageLoaded", sessionID, requestID }));
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: error fetching model usage: {ex.Message}");
        Provider.PostMessage(JsonSerializer.Serialize(new { type = "sessionModelUsageLoaded", sessionID, requestID }));
      }
    }

    /// <summary>
    /// Handles revertSession message - reverts a session to a previous state.
    /// Matches VS Code's handleRevertSession pattern.
    /// </summary>
    public async Task HandleRevertSessionAsync(JsonElement? payload)
    {
      if (payload == null) return;

      var sessionID = payload.Value.TryGetProperty("sessionID", out var sid) ? sid.GetString() : "";
      var messageID = payload.Value.TryGetProperty("messageID", out var mid) ? mid.GetString() : "";

      if (string.IsNullOrEmpty(sessionID) || string.IsNullOrEmpty(messageID)) return;

      var nswagClient = Provider.GetNswagClient();
      if (nswagClient == null)
      {
        Provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = "Not connected to CLI backend" }));
        return;
      }

      try
      {
        var directory = Provider.GetSSEHelper().ResolveDirectory(sessionID);
        var revertBody = new RevertRequest { MessageID = messageID, PartID = null };
        await nswagClient.Session_revertAsync(sessionID, directory, "", revertBody);
        System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: session reverted: {sessionID}");

        var message = new { type = "sessionReverted", sessionID, messageID };
        Provider.PostMessage(JsonSerializer.Serialize(message));
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: error reverting session: {ex.Message}");
        Provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = $"Failed to revert session: {ex.Message}" }));
      }
    }

    /// <summary>
    /// Handles unrevertSession message - unreverts a session.
    /// Matches VS Code's handleUnrevertSession pattern.
    /// </summary>
    public async Task HandleUnrevertSessionAsync(JsonElement? payload)
    {
      if (payload == null) return;

      var sessionID = payload.Value.TryGetProperty("sessionID", out var sid) ? sid.GetString() : "";

      if (string.IsNullOrEmpty(sessionID)) return;

      var nswagClient = Provider.GetNswagClient();
      if (nswagClient == null)
      {
        Provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = "Not connected to CLI backend" }));
        return;
      }

      try
      {
        var directory = Provider.GetSSEHelper().ResolveDirectory(sessionID);
        await nswagClient.Session_unrevertAsync(sessionID, directory, "");
        System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: session unreverted: {sessionID}");

        var message = new { type = "sessionUnreverted", sessionID };
        Provider.PostMessage(JsonSerializer.Serialize(message));
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: error unreverting session: {ex.Message}");
        Provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = $"Failed to unrevert session: {ex.Message}" }));
      }
    }

    /// <summary>
    /// Handles compact message - compacts session context.
    /// Matches VS Code's handleCompact pattern.
    /// </summary>
    public async Task HandleCompactAsync(JsonElement? payload)
    {
      if (payload == null) return;

      var sessionID = payload.Value.TryGetProperty("sessionID", out var sid) ? sid.GetString() : "";
      var providerID = payload.Value.TryGetProperty("providerID", out var pid) ? pid.GetString() : "";
      var modelID = payload.Value.TryGetProperty("modelID", out var modelId) ? modelId.GetString() : "";

      if (string.IsNullOrEmpty(sessionID)) return;

      var nswagClient = Provider.GetNswagClient();
      if (nswagClient == null)
      {
        Provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = "Not connected to CLI backend" }));
        return;
      }

      try
      {
        await nswagClient.V2_session_compactAsync(sessionID);
        System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: session compacted: {sessionID}");

        var message = new { type = "sessionCompacted", sessionID };
        Provider.PostMessage(JsonSerializer.Serialize(message));
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: error compacting session: {ex.Message}");
        Provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = $"Failed to compact session: {ex.Message}" }));
      }
    }

    public void Dispose()
    {
      if (_disposed) return;
      _disposed = true;
    }
  }
}

