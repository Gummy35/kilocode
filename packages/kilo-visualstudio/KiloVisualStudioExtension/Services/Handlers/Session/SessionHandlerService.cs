using System;
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
                    var loadPayload = JsonDocument.Parse($"{{\"sessionID\":\"{_provider.GetCurrentSessionID()}\"}}").RootElement;
                    _ = HandleLoadMessagesAsync(loadPayload);
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
            var httpClient = _provider.GetHttpClient();
            if (httpClient == null)
            {
                System.Diagnostics.Debug.WriteLine("[Kilo] SessionHandler: cannot create session - no HTTP client");
                return false;
            }
            try
            {
                var responseDoc = await httpClient.PostJsonAsync("/session", new { directory = dir });
                if (responseDoc != null && responseDoc.RootElement.TryGetProperty("id", out var id))
                {
                    var sessionID = id.GetString() ?? "";
                    System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: session created: {sessionID}");
                    
                    _provider.SetCurrentSessionID(sessionID);
                    _provider.SetContextSessionID(sessionID);
                    
                    var sessionCreated = new 
                    { 
                        type = "sessionCreated",
                        session = new 
                        { 
                            id = sessionID,
                            directory = dir,
                            title = "New Chat",
                            updated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            status = "idle"
                        }
                    };
                    _provider.PostMessage(JsonSerializer.Serialize(sessionCreated));
                    responseDoc?.Dispose();
                    return true;
                }
                responseDoc?.Dispose();
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
            var httpClient = _provider.GetHttpClient();
            if (httpClient == null)
            {
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = "Not connected to CLI backend", sessionID }));
                return;
            }
            try
            {
                await httpClient.PostJsonAsync($"/session/delete", new { sessionID });
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
            var httpClient = _provider.GetHttpClient();
            if (httpClient == null)
            {
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "error", message = "Not connected to CLI backend" }));
                return;
            }
            try
            {
                await httpClient.PostJsonAsync($"/session/rename", new { sessionID, title });
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
            
            var httpClient = _provider.GetHttpClient();
            if (httpClient == null)
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
                var url = $"/session/{sessionID}/message?limit={limit}";
                if (!string.IsNullOrEmpty(before))
                {
                    url += $"&before={before}";
                }
                
                var responseDoc = cancellationToken.HasValue 
                    ? await httpClient.GetJsonAsync(url, cancellationToken.Value)
                    : await httpClient.GetJsonAsync(url);
                
                if (cancellationToken.HasValue && cancellationToken.Value.IsCancellationRequested) return;
                
                if (!_provider.IsSessionTracked(sessionID)) return;
                
                if (responseDoc == null) return;
                
                var items = new System.Collections.Generic.List<object>();
                var cursorValue = (string?)null;
                var hasMore = false;
                
                if (responseDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in responseDoc.RootElement.EnumerateArray())
                    {
                        if (item.TryGetProperty("info", out var info) && info.TryGetProperty("time", out var time) && time.TryGetProperty("created", out var created))
                        {
                            var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(created.GetInt64()).UtcDateTime.ToString("o");
                            var partsValue = item.TryGetProperty("parts", out var parts) ? (object)parts.Clone() : Array.Empty<object>();
                            var timeValue = item.TryGetProperty("time", out var t) ? (object?)t.Clone() : null;
                            var costValue = item.TryGetProperty("cost", out var cost) ? (object?)cost.Clone() : null;
                            var tokensValue = item.TryGetProperty("tokens", out var tok) ? (object?)tok.Clone() : null;
                            var messageObj = new
                            {
                                id = info.TryGetProperty("id", out var id) ? id.GetString() : "",
                                sessionID = sessionID,
                                role = info.TryGetProperty("role", out var role) ? role.GetString() : "",
                                parts = partsValue,
                                createdAt = createdAt,
                                time = timeValue,
                                cost = costValue,
                                tokens = tokensValue
                            };
                            items.Add(messageObj);
                        }
                    }
                }
                
                if (responseDoc.RootElement.ValueKind == JsonValueKind.Object 
                  && responseDoc.RootElement.TryGetProperty("cursor", out var cursorProp) 
                  && cursorProp.ValueKind == JsonValueKind.String)
                {
                    cursorValue = cursorProp.GetString();
                    hasMore = !string.IsNullOrEmpty(cursorValue);
                }
                
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
                    cursor = cursorValue,
                    hasMore = hasMore
                };
                
                _provider.PostMessage(JsonSerializer.Serialize(message));
                System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: loaded {items.Count} messages for session {sessionID}");
                
                if (payload.Value.TryGetProperty("preserveStream", out var preserveProp) && preserveProp.GetBoolean())
                {
                    _provider.FlushSessionStream(sessionID);
                }
                
                _provider.RecoverPendingPrompts();
                
                responseDoc.Dispose();
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
            var httpClient = _provider.GetHttpClient();
            if (httpClient == null) return;
            try
            {
                await httpClient.PostJsonAsync($"/session/message/delete", new { sessionID, messageID });
                System.Diagnostics.Debug.WriteLine("[Kilo] SessionHandler: message deleted");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] SessionHandler: error deleting message: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
