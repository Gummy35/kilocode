using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services
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
        /// Clears the current session context.
        /// </summary>
        /// <param name="payload">The message payload (unused for clearSession).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public void HandleClearSession(JsonElement? payload)
        {
            _provider.ClearCurrentSession();
        }

        /// <summary>
        /// Handles the deleteSession message from the webview.
        /// Deletes a session from the backend.
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
        /// Renames a session in the backend.
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
        /// Loads messages for a specific session from the backend.
        /// </summary>
        /// <param name="payload">The message payload containing sessionID and other options.</param>
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
        /// Deletes a message from a session.
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
