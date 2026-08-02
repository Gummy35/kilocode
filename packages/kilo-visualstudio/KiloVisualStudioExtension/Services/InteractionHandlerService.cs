using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services
{
    /// <summary>
    /// Handles user interaction operations like permission replies, question replies, and prompt handling.
    /// This matches the VS Code pattern where interaction handling is extracted into separate handler modules.
    /// </summary>
    public class InteractionHandlerService : IDisposable
    {
        private readonly VSProvider _provider;
        private bool _disposed;

        /// <summary>
        /// Creates a new InteractionHandlerService instance.
        /// </summary>
        /// <param name="provider">The VSProvider instance to use for webview communication.</param>
        public InteractionHandlerService(VSProvider provider)
        {
            _provider = provider;
        }

        /// <summary>
        /// Handles the prompt message from the webview.
        /// Sends a prompt to the backend for processing.
        /// </summary>
        /// <param name="payload">The message payload containing the prompt.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandlePromptAsync(JsonElement? payload)
        {
            if (!_provider.IsConnected())
            {
                await _provider.SendErrorAsync("Not Connected", "Not connected to CLI backend");
                return;
            }

            if (!payload.HasValue) return;

            string? sessionID = null;
            if (payload.Value.TryGetProperty("sessionID", out var sessionIDProp) && !string.IsNullOrEmpty(sessionIDProp.GetString()))
            {
                var sid = sessionIDProp.GetString()!;
                if (sid.StartsWith("ses_"))
                {
                    sessionID = sid;
                }
            }
            
            if (string.IsNullOrEmpty(sessionID) && !string.IsNullOrEmpty(_provider.GetCurrentSessionID()))
            {
                sessionID = _provider.GetCurrentSessionID();
            }

            if (string.IsNullOrEmpty(sessionID))
            {
                System.Diagnostics.Debug.WriteLine("[Kilo] InteractionHandler: no session available, creating new session...");
                var created = await _provider.CreateSessionInternalAsync();
                if (!created)
                {
                    await _provider.SendErrorAsync("Prompt Error", "Failed to create session");
                    return;
                }
                sessionID = _provider.GetCurrentSessionID();
                
                if (string.IsNullOrEmpty(sessionID))
                {
                    await _provider.SendErrorAsync("Prompt Error", "Failed to create session");
                    return;
                }
            }

            if (!payload.Value.TryGetProperty("text", out var textProp) || string.IsNullOrEmpty(textProp.GetString()))
            {
                System.Diagnostics.Debug.WriteLine("[Kilo] InteractionHandler: missing text in prompt request");
                await _provider.SendErrorAsync("Prompt Error", "Missing message text");
                return;
            }
            var text = textProp.GetString()!;
            var len = text.Length < 50 ? text.Length : 50;
            System.Diagnostics.Debug.WriteLine($"[Kilo] InteractionHandler: prompt received for session {sessionID}: {text.Substring(0, len)}...");

            var httpClient = _provider.GetHttpClient();
            try
            {
                var part = new { type = "text", text };
                var promptData = new { 
                    parts = new[] { part }
                };
                
                var json = JsonSerializer.Serialize(promptData);
                System.Diagnostics.Debug.WriteLine($"[Kilo] InteractionHandler: sending POST to /session/{sessionID}/prompt_async with body: {json}");
                
                var response = await httpClient.PostAsync($"/session/{sessionID}/prompt_async", promptData);
                if (response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine("[Kilo] InteractionHandler: prompt accepted, response will come via SSE");
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"[Kilo] InteractionHandler: prompt failed: {(int)response.StatusCode} - {errorBody}");
                    await _provider.SendErrorAsync("Prompt Error", $"Server returned {(int)response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] InteractionHandler: error sending prompt: {ex.Message}");
                await _provider.SendErrorAsync("Prompt Error", ex.Message);
            }
        }

        /// <summary>
        /// Handles the permission/reply message from the webview.
        /// Processes permission approval/rejection responses.
        /// </summary>
        /// <param name="payload">The message payload containing permission response.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandlePermissionReplyAsync(JsonElement? payload)
        {
            if (!payload.HasValue) return;

            JsonElement? requestId = null;
            JsonElement? response = null;

            if (payload.Value.TryGetProperty("requestId", out var rid))
            {
                requestId = rid;
            }
            if (payload.Value.TryGetProperty("response", out var resp))
            {
                response = resp;
            }

            if (requestId.HasValue && response.HasValue)
            {
                var httpClient = _provider.GetHttpClient();
                try
                {
                    var url = $"/permission/{requestId.Value.GetString()}/reply";
                    await httpClient.PostJsonAsync(url, new { response = response.Value.GetString() });
                    System.Diagnostics.Debug.WriteLine("[Kilo] InteractionHandler: permission reply sent");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Kilo] InteractionHandler: error sending permission reply: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Handles the question/reply message from the webview.
        /// Processes question responses from the user.
        /// </summary>
        /// <param name="payload">The message payload containing question response.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleQuestionReplyAsync(JsonElement? payload)
        {
            if (!payload.HasValue) return;

            JsonElement? requestId = null;
            JsonElement? answers = null;

            if (payload.Value.TryGetProperty("requestId", out var rid))
            {
                requestId = rid;
            }
            if (payload.Value.TryGetProperty("answers", out var ans))
            {
                answers = ans;
            }

            if (requestId.HasValue && answers.HasValue)
            {
                var httpClient = _provider.GetHttpClient();
                try
                {
                    var url = $"/question/{requestId.Value.GetString()}/reply";
                    await httpClient.PostJsonAsync(url, new { answers });
                    System.Diagnostics.Debug.WriteLine("[Kilo] InteractionHandler: question reply sent");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Kilo] InteractionHandler: error sending question reply: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Handles the permissionResponse message from the webview.
        /// Processes permission response events.
        /// </summary>
        /// <param name="payload">The message payload.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandlePermissionResponseAsync(JsonElement? payload)
        {
            if (payload == null) return;

            try
            {
                var httpClient = _provider.GetHttpClient();
                if (httpClient == null || !httpClient.IsConnected())
                {
                    await _provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                await httpClient.PostAsync("/permission/response", payload.Value);
            }
            catch (Exception ex)
            {
                await _provider.SendErrorAsync("Permission response error", ex.Message);
            }
        }

        /// <summary>
        /// Handles the questionReject message from the webview.
        /// Processes question rejection from the user.
        /// </summary>
        /// <param name="payload">The message payload.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleQuestionRejectAsync(JsonElement? payload)
        {
            if (payload == null) return;

            try
            {
                var httpClient = _provider.GetHttpClient();
                if (httpClient == null || !httpClient.IsConnected())
                {
                    await _provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                await httpClient.PostAsync("/question/reject", payload.Value);
            }
            catch (Exception ex)
            {
                await _provider.SendErrorAsync("Question reject error", ex.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
