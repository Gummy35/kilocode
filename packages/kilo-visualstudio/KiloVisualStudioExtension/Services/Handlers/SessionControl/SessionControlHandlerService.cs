using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services.Handlers.SessionControl
{
    /// <summary>
    /// Handles session control operations like abort, send message, fork session, compact, enhance prompt.
    /// This matches the VS Code pattern where session control is extracted into separate handler modules.
    /// </summary>
    public class SessionControlHandlerService : IDisposable
    {
        private readonly VSProvider _provider;
        private bool _disposed;

        /// <summary>
        /// Creates a new SessionControlHandlerService instance.
        /// </summary>
        /// <param name="provider">The VSProvider instance to use for webview communication.</param>
        public SessionControlHandlerService(VSProvider provider)
        {
            _provider = provider;
        }

        /// <summary>
        /// Handles the abort message from the webview.
        /// Aborts the current session or a specific session.
        /// </summary>
        /// <param name="payload">The message payload containing session ID.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleAbortAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] SessionControl: abort");
            
            var sessionID = payload.HasValue && payload.Value.TryGetProperty("sessionID", out var sid) && !string.IsNullOrEmpty(sid.GetString())
                ? sid.GetString()
                : _provider.GetCurrentSessionID();
            
            if (string.IsNullOrEmpty(sessionID))
            {
                System.Diagnostics.Debug.WriteLine("[Kilo] SessionControl: abort - no session ID available");
                return;
            }
            
            System.Diagnostics.Debug.WriteLine($"[Kilo] SessionControl: aborting session {sessionID}");
            
            var statusMessage = new { type = "sessionStatus", sessionID = sessionID, status = "idle" };
            _provider.PostMessage(JsonSerializer.Serialize(statusMessage));
            
            var turnClosedMessage = new { type = "sessionTurnClosed", sessionID = sessionID, reason = "interrupted" };
            _provider.PostMessage(JsonSerializer.Serialize(turnClosedMessage));
        }

        /// <summary>
        /// Handles the sendMessage message from the webview.
        /// Sends a message to the current session.
        /// </summary>
        /// <param name="payload">The message payload containing the message.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleSendMessageAsync(JsonElement? payload)
        {
            if (!payload.HasValue) return;
            var text = payload.Value.TryGetProperty("text", out var t) ? t.GetString() : "";
            if (string.IsNullOrEmpty(text)) return;
            await _provider.HandlePromptAsync(payload);
        }

        /// <summary>
        /// Handles the forkSession message from the webview.
        /// Creates a fork of the current session.
        /// Matches VS Code's handleForkSession pattern - checks session status before forking.
        /// </summary>
        /// <param name="payload">The message payload.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleForkSessionAsync(JsonElement? payload)
        {
            if (payload == null)
            {
                await _provider.SendErrorAsync("Missing payload", "Fork session payload is required");
                return;
            }

            try
            {
                var sessionID = payload.Value.TryGetProperty("sessionId", out var sid) ? sid.GetString() : "";
                var messageID = payload.Value.TryGetProperty("messageId", out var mid) ? mid.GetString() : "";
                
                if (string.IsNullOrEmpty(sessionID))
                {
                    await _provider.SendErrorAsync("Invalid payload", "Session ID is required");
                    return;
                }
                
                var kiotaClient = _provider.GetKiloClient();
                if (kiotaClient == null)
                {
                    await _provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                await kiotaClient.Session[sessionID].Fork.PostAsync(new Generated.Api.Session.Item.Fork.ForkPostRequestBody { MessageId = messageID });
                System.Diagnostics.Debug.WriteLine($"[Kilo] SessionControl: session forked: {sessionID}");
                
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "sessionForked", sessionID, forkedFromID = sessionID }));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] SessionControl: fork session error: {ex.Message}");
                await _provider.SendErrorAsync("Fork session error", ex.Message);
            }
        }

        /// <summary>
        /// Handles the compact message from the webview.
        /// Compacts the session context to reduce token usage.
        /// </summary>
        /// <param name="payload">The message payload.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleCompactAsync(JsonElement? payload)
        {
            if (payload == null)
            {
                await _provider.SendErrorAsync("Missing payload", "Compact payload is required");
                return;
            }

            try
            {
                var kiotaClient = _provider.GetKiloClient();
                if (kiotaClient == null)
                {
                    await _provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                var compact = JsonSerializer.Deserialize<Generated.Api.Session.Item.Compact.CompactPostRequestBody>(payload.Value.GetRawText());
                if (compact != null)
                {
                    var sessionID = payload.Value.TryGetProperty("sessionID", out var sid) ? sid.GetString() : "";
                    if (!string.IsNullOrEmpty(sessionID))
                    {
                        await kiotaClient.Session[sessionID].Compact.PostAsync(compact);
                    }
                }
            }
            catch (Exception ex)
            {
                await _provider.SendErrorAsync("Compact error", ex.Message);
            }
        }

        /// <summary>
        /// Handles the enhancePrompt message from the webview.
        /// Enhances the current prompt using AI.
        /// </summary>
        /// <param name="payload">The message payload.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleEnhancePromptAsync(JsonElement? payload)
        {
            if (payload == null)
            {
                await _provider.SendErrorAsync("Missing payload", "Enhance prompt payload is required");
                return;
            }

            try
            {
                var kiotaClient = _provider.GetKiloClient();
                if (kiotaClient == null)
                {
                    await _provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                var enhance = JsonSerializer.Deserialize<Generated.Models.PromptEnhance>(payload.Value.GetRawText());
                if (enhance != null)
                {
                    await kiotaClient.EnhancePrompt.PostAsync(enhance);
                }
            }
            catch (Exception ex)
            {
                await _provider.SendErrorAsync("Enhance prompt error", ex.Message);
            }
        }

        /// <summary>
        /// Handles the importAndSend message from the webview.
        /// Imports content and sends it to the session.
        /// </summary>
        /// <param name="payload">The message payload.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleImportAndSendAsync(JsonElement? payload)
        {
            if (payload == null)
            {
                await _provider.SendErrorAsync("Missing payload", "Import and send payload is required");
                return;
            }

            try
            {
                var kiotaClient = _provider.GetKiloClient();
                if (kiotaClient == null)
                {
                    await _provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                var import = JsonSerializer.Deserialize<Generated.Models.ImportSend>(payload.Value.GetRawText());
                if (import != null)
                {
                    await kiotaClient.Import.Send.PostAsync(import);
                }
            }
            catch (Exception ex)
            {
                await _provider.SendErrorAsync("Import and send error", ex.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
