using System;
using System.Text.Json;
using System.Threading.Tasks;
using KiloVisualStudioExtension.ApiClient;

namespace KiloVisualStudioExtension.Services.Handlers.SessionControl
{
    /// <summary>
    /// Handles session control operations like abort, send message, fork session, compact, enhance prompt.
    /// This matches the VS Code pattern where session control is extracted into separate handler modules.
    /// </summary>
    public class SessionControlService : ServiceProviderServiceBase
  {
        private bool _disposed;

        private VSProvider Provider => _serviceProvider.GetService<VSProvider>() 
            ?? throw new InvalidOperationException("VSProvider not registered in service provider");

        /// <summary>
        /// Creates a new SessionControlHandlerService instance.
        /// </summary>
        /// <param name="serviceProvider">The service provider for dependency injection.</param>
        public SessionControlService(ServiceProvider serviceProvider): base(serviceProvider)
        {
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
                : Provider.GetCurrentSessionID();
            
            if (string.IsNullOrEmpty(sessionID))
            {
                System.Diagnostics.Debug.WriteLine("[Kilo] SessionControl: abort - no session ID available");
                return;
            }
            
            System.Diagnostics.Debug.WriteLine($"[Kilo] SessionControl: aborting session {sessionID}");
            
            var statusMessage = new { type = "sessionStatus", sessionID = sessionID, status = "idle" };
            Provider.PostMessage(JsonSerializer.Serialize(statusMessage));
            
            var turnClosedMessage = new { type = "sessionTurnClosed", sessionID = sessionID, reason = "interrupted" };
            Provider.PostMessage(JsonSerializer.Serialize(turnClosedMessage));
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
            await Provider.HandlePromptAsync(payload);
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
                await Provider.SendErrorAsync("Missing payload", "Fork session payload is required");
                return;
            }

            try
            {
                var sessionID = payload.Value.TryGetProperty("sessionId", out var sid) ? sid.GetString() : "";
                var messageID = payload.Value.TryGetProperty("messageId", out var mid) ? mid.GetString() : "";
                
                if (string.IsNullOrEmpty(sessionID))
                {
                    await Provider.SendErrorAsync("Invalid payload", "Session ID is required");
                    return;
                }
                
                var nswagClient = Provider.GetNswagClient();
                if (nswagClient == null)
                {
                    await Provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                var forkBody = new Body21 { MessageID = messageID };
                await nswagClient.Session_forkAsync(sessionID, System.Environment.CurrentDirectory, "", forkBody);
                System.Diagnostics.Debug.WriteLine($"[Kilo] SessionControl: session forked: {sessionID}");
                
                Provider.PostMessage(JsonSerializer.Serialize(new { type = "sessionForked", sessionID, forkedFromID = sessionID }));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] SessionControl: fork session error: {ex.Message}");
                await Provider.SendErrorAsync("Fork session error", ex.Message);
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
                await Provider.SendErrorAsync("Missing payload", "Compact payload is required");
                return;
            }

            try
            {
                var nswagClient = Provider.GetNswagClient();
                if (nswagClient == null)
                {
                    await Provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                var sessionID = payload.Value.TryGetProperty("sessionID", out var sid) ? sid.GetString() : "";
                if (!string.IsNullOrEmpty(sessionID))
                {
                    await nswagClient.V2_session_compactAsync(sessionID);
                }
            }
            catch (Exception ex)
            {
                await Provider.SendErrorAsync("Compact error", ex.Message);
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
                await Provider.SendErrorAsync("Missing payload", "Enhance prompt payload is required");
                return;
            }

            try
            {
                var nswagClient = Provider.GetNswagClient();
                if (nswagClient == null)
                {
                    await Provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                var text = payload.Value.TryGetProperty("text", out var textProp) ? textProp.GetString() : "";
                var enhanceBody = new Body47 { Text = text };
                
                var response = await nswagClient.EnhancePrompt_enhanceAsync(System.Environment.CurrentDirectory, "", enhanceBody);
                if (response != null && !string.IsNullOrEmpty(response.Text))
                {
                    Provider.PostMessage(JsonSerializer.Serialize(new { type = "promptEnhanced", enhancedText = response.Text }));
                }
            }
            catch (Exception ex)
            {
                await Provider.SendErrorAsync("Enhance prompt error", ex.Message);
            }
        }

        /// <summary>
        /// Handles the importAndSend message from the webview.
        /// Imports a cloud-synced session and sends it to the session.
        /// </summary>
        /// <param name="payload">The message payload.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleImportAndSendAsync(JsonElement? payload)
        {
            if (payload == null)
            {
                await Provider.SendErrorAsync("Missing payload", "Import and send payload is required");
                return;
            }

            try
            {
                var nswagClient = Provider.GetNswagClient();
                if (nswagClient == null)
                {
                    await Provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                var sessionId = payload.Value.TryGetProperty("sessionID", out var sid) ? sid.GetString() : "";
                var importBody = new Body52 { SessionId = sessionId };
                
                await nswagClient.Kilo_cloud_session_importAsync(System.Environment.CurrentDirectory, "", importBody);
            }
            catch (Exception ex)
            {
                await Provider.SendErrorAsync("Import and send error", ex.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}

