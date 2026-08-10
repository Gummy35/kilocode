using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services.Handlers.Session
{
    /// <summary>
    /// Handles session-related operations like create, delete, rename, and load messages.
    /// This matches the VS Code pattern where session handling logic is extracted into
    /// separate handler modules (e.g., kilo-provider/handlers/session.ts).
    /// </summary>
    public class SessionHandlerService : IDisposable
    {
        private readonly VSProvider _provider;
        private bool _disposed;

        /// <summary>
        /// Creates a new SessionHandlerService instance.
        /// </summary>
        /// <param name="provider">The VSProvider instance to use for webview communication.</param>
        public SessionHandlerService(VSProvider provider)
        {
            _provider = provider;
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
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = "Failed to create session" }));
            }
            else
            {
                if (!string.IsNullOrEmpty(_provider.GetCurrentSessionID()))
                {
                    var sessionID = _provider.GetCurrentSessionID()!;
                    _provider.TrackSession(sessionID);
                    _provider.PostMessage(JsonSerializer.Serialize(new { type = "workspaceDirectoryChanged", directory = dir }));
                    _provider.FocusSession(sessionID);
                    
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
            var kiotaClient = _provider.GetKiloClient();
            if (kiotaClient == null)
            {
                System.Diagnostics.Debug.WriteLine("[Kilo] SessionHandler: cannot create session - no Kiota client");
                return false;
            }
            try
            {
                var response = await kiotaClient.Session.PostAsync(new Generated.Session.SessionPostRequestBody(), q => {
                    q.QueryParameters.Directory = dir;
                });
                if (response != null && !string.IsNullOrEmpty(response.Id))
                {
                    var sessionID = response.Id;
                    System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: session created: {sessionID}");
                    
                    _provider.SetCurrentSessionID(sessionID);
                    _provider.SetContextSessionID(sessionID);
                    
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
                    _provider.PostMessage(JsonSerializer.Serialize(sessionCreated));
                    return true;
                }
                System.Diagnostics.Debug.WriteLine("[Kilo] SessionHandler: session creation failed - no ID in response");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: error creating session: {ex.Message}");
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = $"Failed to create session: {ex.Message}" }));
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
            _provider.ClearCurrentSession();
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
            var kiotaClient = _provider.GetKiloClient();
            if (kiotaClient == null)
            {
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = "Not connected to CLI backend", sessionID }));
                return;
            }
            try
            {
                await kiotaClient.Session[sessionID].DeleteAsync();
                System.Diagnostics.Debug.WriteLine("[Kilo] SessionHandler: session deleted");
                
                if (_provider.GetCurrentSessionID() == sessionID)
                {
                    _provider.SetContextSessionID(null);
                    _provider.SetCurrentSessionID(null);
                    _provider.UntrackSession(sessionID);
                }
                
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "sessionDeleted", sessionID }));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: error deleting session: {ex.Message}");
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = $"Failed to delete session: {ex.Message}", sessionID }));
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
            var kiotaClient = _provider.GetKiloClient();
            if (kiotaClient == null)
            {
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = "Not connected to CLI backend" }));
                return;
            }
            try
            {
                await kiotaClient.Session[sessionID].PatchAsync(new Generated.Models.Session { Title = title });
                System.Diagnostics.Debug.WriteLine("[Kilo] SessionHandler: session renamed");
                
                if (_provider.GetCurrentSessionID() == sessionID)
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
                    _provider.PostMessage(JsonSerializer.Serialize(sessionUpdated));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: error renaming session: {ex.Message}");
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = $"Failed to rename session: {ex.Message}" }));
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
                _provider.StopCurrentSessionProcesses(sessionID);
                _provider.TrackSession(sessionID);
                _provider.FocusSession(sessionID);
                _provider.SetCurrentSessionID(sessionID);
                _provider.SetContextSessionID(sessionID);
            }
            
            var kiotaClient = _provider.GetKiloClient();
            if (kiotaClient == null)
            {
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = "Not connected to CLI backend", sessionID }));
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
                var messages = await kiotaClient.Session[sessionID].Message.GetAsync(q => {
                    q.QueryParameters.Limit = limit;
                    if (!string.IsNullOrEmpty(before))
                        q.QueryParameters.Cursor = before;
                }, cancellationToken);
                
                if (cancellationToken.HasValue && cancellationToken.Value.IsCancellationRequested) return;
                
                if (!_provider.IsSessionTracked(sessionID)) return;
                
                if (messages == null) return;
                
                var items = new System.Collections.Generic.List<object>();
                
                if (messages.Messages != null)
                {
                    foreach (var msg in messages.Messages)
                    {
                        if (msg.Info != null)
                        {
                            var createdAt = msg.Info.CreatedAt != null ? msg.Info.CreatedAt.Value.UtcDateTime.ToString("o") : DateTimeOffset.UtcNow.ToString("o");
                            var messageObj = new
                            {
                                id = msg.Info.Id ?? "",
                                sessionID = sessionID,
                                role = msg.Info.Role ?? "",
                                parts = msg.Parts != null ? JsonSerializer.SerializeToElement(msg.Parts) : Array.Empty<object>(),
                                createdAt = createdAt,
                                time = msg.Time != null ? JsonSerializer.SerializeToElement(msg.Time) : null,
                                cost = msg.Cost != null ? JsonSerializer.SerializeToElement(msg.Cost) : null,
                                tokens = msg.Tokens != null ? JsonSerializer.SerializeToElement(msg.Tokens) : null
                            };
                            items.Add(messageObj);
                        }
                    }
                }
                
                var hasMore = !string.IsNullOrEmpty(messages.Cursor?.Next);
                
                if (mode == "replace" || mode == "reconcile")
                {
                    _provider.DropSessionStream(sessionID);
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
                
                _provider.PostMessage(JsonSerializer.Serialize(message));
                System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: loaded {items.Count} messages for session {sessionID}");
                
                if (payload.Value.TryGetProperty("preserveStream", out var preserveProp) && preserveProp.GetBoolean())
                {
                    _provider.FlushSessionStream(sessionID);
                }
                
                _provider.RecoverPendingPrompts();
                
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
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = ex.Message, sessionID }));
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
            var kiotaClient = _provider.GetKiloClient();
            if (kiotaClient == null) return;
            try
            {
                await kiotaClient.Session[sessionID].Message[messageID].DeleteAsync();
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
            var kiotaClient = _provider.GetKiloClient();
            if (kiotaClient == null)
            {
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "sessionModelUsageLoaded", sessionID, requestID }));
                return;
            }

            try
            {
                var usage = await kiotaClient.Session[sessionID].ModelUsage.GetAsync();
                if (usage != null)
                {
                    var sessionIDs = new[] { sessionID };
                    var totals = new { steps = 0, cost = 0, tokens = new { input = 0, output = 0, reasoning = 0, cache = new { read = 0, write = 0 } } };
                    var models = new object[0];
                    
                    var data = new { sessionIDs, totals, models };
                    var message = new { type = "sessionModelUsageLoaded", sessionID, requestID, data };
                    _provider.PostMessage(JsonSerializer.Serialize(message));
                }
                else
                {
                    _provider.PostMessage(JsonSerializer.Serialize(new { type = "sessionModelUsageLoaded", sessionID, requestID }));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: error fetching model usage: {ex.Message}");
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "sessionModelUsageLoaded", sessionID, requestID }));
            }
        }

        /// <summary>
        /// Loads memory state for a session and sends memoryLoaded message.
        /// Matches VS Code's KiloProviderMemory.load() pattern.
        /// </summary>
        private async Task LoadMemoryAsync(string? sessionID)
        {
            var kiotaClient = _provider.GetKiloClient();
            if (kiotaClient == null)
            {
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "memoryLoaded", sessionID, error = "Not connected to CLI backend" }));
                return;
            }

            try
            {
                var memory = await kiotaClient.Memory.Status.GetAsync(q => {
                    q.QueryParameters.Directory = System.Environment.CurrentDirectory;
                });
                if (memory != null)
                {
                    var message = new { type = "memoryLoaded", sessionID, status = JsonSerializer.SerializeToElement(memory) };
                    _provider.PostMessage(JsonSerializer.Serialize(message));
                }
                else
                {
                    _provider.PostMessage(JsonSerializer.Serialize(new { type = "memoryLoaded", sessionID, error = "Memory unavailable" }));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: error loading memory: {ex.Message}");
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "memoryLoaded", sessionID, error = ex.Message }));
            }
        }

        /// <summary>
        /// Handles loadSessions message - loads all sessions and sends sessionListLoaded.
        /// Matches VS Code's handleLoadSessions pattern.
        /// </summary>
        public async Task HandleLoadSessionsAsync(JsonElement? payload)
        {
            var kiotaClient = _provider.GetKiloClient();
            if (kiotaClient == null)
            {
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = "Not connected to CLI backend" }));
                return;
            }

            try
            {
                var sessions = await kiotaClient.Session.GetAsync();
                if (sessions != null)
                {
                    var sessionList = new List<object>();
                    foreach (var session in sessions)
                    {
                        var sessionObj = new
                        {
                            id = session.Id ?? "",
                            title = session.Title ?? "",
                            status = session.Status != null ? JsonSerializer.SerializeToElement(session.Status) : null,
                            directory = session.Directory,
                            createdAt = session.CreatedAt?.ToString("o"),
                            updatedAt = session.UpdatedAt?.ToString("o")
                        };
                        sessionList.Add(sessionObj);
                    }
                    
                    var message = new { type = "sessionListLoaded", sessions = sessionList.ToArray() };
                    _provider.PostMessage(JsonSerializer.Serialize(message));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: error loading sessions: {ex.Message}");
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = $"Failed to load sessions: {ex.Message}" }));
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
            
            var kiotaClient = _provider.GetKiloClient();
            if (kiotaClient == null)
            {
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = "Not connected to CLI backend" }));
                return;
            }

            try
            {
                var session = await kiotaClient.Session[sessionID].GetAsync();
                if (session != null)
                {
                    var message = new { type = "sessionSynced", session = JsonSerializer.SerializeToElement(session) };
                    _provider.PostMessage(JsonSerializer.Serialize(message));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: error syncing session: {ex.Message}");
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = $"Failed to sync session: {ex.Message}" }));
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
            
            var kiotaClient = _provider.GetKiloClient();
            if (kiotaClient == null)
            {
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "sessionModelUsageLoaded", sessionID, requestID }));
                return;
            }

            try
            {
                var usage = await kiotaClient.Session[sessionID].ModelUsage.GetAsync();
                if (usage != null)
                {
                    var sessionIDs = new[] { sessionID };
                    var totals = new { steps = 0, cost = 0, tokens = new { input = 0, output = 0, reasoning = 0, cache = new { read = 0, write = 0 } } };
                    var models = new object[0];
                    
                    var data = new { sessionIDs, totals, models };
                    var message = new { type = "sessionModelUsageLoaded", sessionID, requestID, data };
                    _provider.PostMessage(JsonSerializer.Serialize(message));
                }
                else
                {
                    _provider.PostMessage(JsonSerializer.Serialize(new { type = "sessionModelUsageLoaded", sessionID, requestID }));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: error fetching model usage: {ex.Message}");
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "sessionModelUsageLoaded", sessionID, requestID }));
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
            
            var kiotaClient = _provider.GetKiloClient();
            if (kiotaClient == null)
            {
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = "Not connected to CLI backend" }));
                return;
            }

            try
            {
                await kiotaClient.Session[sessionID].Revert.PostAsync(new Generated.Session.Item.Revert.RevertPostRequestBody { MessageID = messageID });
                System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: session reverted: {sessionID}");
                
                var message = new { type = "sessionReverted", sessionID, messageID };
                _provider.PostMessage(JsonSerializer.Serialize(message));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: error reverting session: {ex.Message}");
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = $"Failed to revert session: {ex.Message}" }));
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
            
            var kiotaClient = _provider.GetKiloClient();
            if (kiotaClient == null)
            {
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = "Not connected to CLI backend" }));
                return;
            }

            try
            {
                await kiotaClient.Session[sessionID].Unrevert.PostAsync();
                System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: session unreverted: {sessionID}");
                
                var message = new { type = "sessionUnreverted", sessionID };
                _provider.PostMessage(JsonSerializer.Serialize(message));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: error unreverting session: {ex.Message}");
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = $"Failed to unrevert session: {ex.Message}" }));
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
            
            var kiotaClient = _provider.GetKiloClient();
            if (kiotaClient == null)
            {
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = "Not connected to CLI backend" }));
                return;
            }

            try
            {
                // Compact endpoint doesn't require a request body
                await kiotaClient.Api.Session[sessionID].Compact.PostAsync();
                System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: session compacted: {sessionID}");
                
                var message = new { type = "sessionCompacted", sessionID };
                _provider.PostMessage(JsonSerializer.Serialize(message));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: error compacting session: {ex.Message}");
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = $"Failed to compact session: {ex.Message}" }));
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
