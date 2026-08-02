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
                var httpClient = _provider.GetHttpClient();
                if (httpClient == null || !httpClient.IsConnected())
                {
                    await _provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                await httpClient.PostAsync("/session/fork", payload.Value);
            }
            catch (Exception ex)
            {
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
                var httpClient = _provider.GetHttpClient();
                if (httpClient == null || !httpClient.IsConnected())
                {
                    await _provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                await httpClient.PostAsync("/session/compact", payload.Value);
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
                var httpClient = _provider.GetHttpClient();
                if (httpClient == null || !httpClient.IsConnected())
                {
                    await _provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                await httpClient.PostAsync("/prompt/enhance", payload.Value);
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
                var httpClient = _provider.GetHttpClient();
                if (httpClient == null || !httpClient.IsConnected())
                {
                    await _provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                await httpClient.PostAsync("/import/send", payload.Value);
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
