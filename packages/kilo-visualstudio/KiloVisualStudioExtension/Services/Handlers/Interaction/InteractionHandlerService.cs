using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services.Handlers.Interaction
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
        /// Sends a prompt to the backend for processing via the /session/{sessionID}/prompt_async endpoint.
        /// 
        /// VS Code workflow: Matches the pattern in kilo-provider/handlers/interaction handlers
        /// where prompt is sent to trigger AI response via SSE.
        /// 
        /// Workflow steps:
        /// 1. Check if provider is connected to backend
        /// 2. Extract sessionID from payload or use current session
        /// 3. If no session exists, create a new session
        /// 4. Validate text property exists in payload
        /// 5. Construct prompt data with text part
        /// 6. POST to /session/{sessionID}/prompt_async endpoint
        /// 7. Response comes back via SSE stream
        /// 
        /// Messages sent to webview:
        /// - error: { message: "Not connected to CLI backend" | "Missing message text" | "Failed to create session" }
        /// </summary>
        /// <param name="payload">The message payload containing sessionID and text (the prompt message).</param>
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

            var kiotaClient = _provider.GetKiloClient();
            if (kiotaClient == null)
            {
                await _provider.SendErrorAsync("Not Connected", "Not connected to CLI backend");
                return;
            }
            
            try
            {
                var part = new Generated.Models.Part { Type = "text", Text = text };
                var promptData = new Generated.Api.Session.Item.PromptAsync.PromptAsyncPostRequestBody { Parts = new[] { part } };
                
                System.Diagnostics.Debug.WriteLine($"[Kilo] InteractionHandler: sending POST to /session/{sessionID}/prompt_async");
                
                await kiotaClient.Session[sessionID].PromptAsync.PostAsync(promptData);
                System.Diagnostics.Debug.WriteLine("[Kilo] InteractionHandler: prompt accepted, response will come via SSE");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] InteractionHandler: error sending prompt: {ex.Message}");
                await _provider.SendErrorAsync("Prompt Error", ex.Message);
            }
        }

        /// <summary>
        /// Handles the permission/reply message from the webview.
        /// Processes permission approval/rejection responses by forwarding to backend.
        /// 
        /// VS Code workflow: Matches the pattern in kilo-provider/handlers/permission-handler.ts
        /// where permission reply is sent to /permission/{requestId}/reply endpoint.
        /// 
        /// Workflow steps:
        /// 1. Extract requestId and response from payload
        /// 2. Validate both properties exist
        /// 3. Get HTTP client from provider
        /// 4. POST to /permission/{requestId}/reply with response value
        /// 
        /// Messages sent to webview:
        /// - None; permission response is handled silently
        /// </summary>
        /// <param name="payload">The message payload containing requestId and response (approve/reject).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandlePermissionReplyAsync(JsonElement? payload)
        {
            if (!payload.HasValue) return;

            string? requestId = null;
            string? response = null;

            if (payload.Value.TryGetProperty("requestId", out var rid))
            {
                requestId = rid.GetString();
            }
            if (payload.Value.TryGetProperty("response", out var resp))
            {
                response = resp.GetString();
            }

            if (!string.IsNullOrEmpty(requestId) && !string.IsNullOrEmpty(response))
            {
                var kiotaClient = _provider.GetKiloClient();
                if (kiotaClient == null) return;
                
                try
                {
                    await kiotaClient.Permission[requestId].Reply.PostAsync(new Generated.Permission.Item.Reply.ReplyPostRequestBody { Response = response });
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
        /// Processes question responses from the user by forwarding to backend.
        /// 
        /// VS Code workflow: Matches the pattern in kilo-provider/handlers/question.ts
        /// where question reply is sent to /question/{requestId}/reply endpoint.
        /// 
        /// Workflow steps:
        /// 1. Extract requestId and answers from payload
        /// 2. Validate both properties exist
        /// 3. Get HTTP client from provider
        /// 4. POST to /question/{requestId}/reply with answers array
        /// 
        /// Messages sent to webview:
        /// - None; question response is handled silently
        /// </summary>
        /// <param name="payload">The message payload containing requestId and answers array.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleQuestionReplyAsync(JsonElement? payload)
        {
            if (!payload.HasValue) return;

            string? requestId = null;
            JsonElement? answers = null;

            if (payload.Value.TryGetProperty("requestId", out var rid))
            {
                requestId = rid.GetString();
            }
            if (payload.Value.TryGetProperty("answers", out var ans))
            {
                answers = ans;
            }

            if (!string.IsNullOrEmpty(requestId) && answers.HasValue)
            {
                var kiotaClient = _provider.GetKiloClient();
                if (kiotaClient == null) return;
                
                try
                {
                    var answersArray = answers.Value.ValueKind == JsonValueKind.Array ? answers.Value.EnumerateArray().Select(a => a.GetString()).ToArray() : new string[] { answers.Value.GetString() };
                    await kiotaClient.Question[requestId].Reply.PostAsync(new Generated.Question.Item.Reply.ReplyPostRequestBody { Answers = answersArray });
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
        /// Processes permission response events by forwarding to backend.
        /// 
        /// VS Code workflow: Matches the pattern in kilo-provider/handlers/permission-handler.ts
        /// where permission response is posted to /permission/response endpoint.
        /// 
        /// Workflow steps:
        /// 1. Get HTTP client from provider
        /// 2. Verify client is connected
        /// 3. POST to /permission/response with payload
        /// 
        /// Messages sent to webview:
        /// - error: { message: "Not connected to backend" }
        /// </summary>
        /// <param name="payload">The message payload containing permission response data.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandlePermissionResponseAsync(JsonElement? payload)
        {
            if (payload == null) return;

            try
            {
                var kiotaClient = _provider.GetKiloClient();
                if (kiotaClient == null)
                {
                    await _provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                var permissionResponse = JsonSerializer.Deserialize<Generated.Models.PermissionResponse>(payload.Value.GetRawText());
                if (permissionResponse != null)
                {
                    await kiotaClient.Permission.PostAsync(permissionResponse);
                }
            }
            catch (Exception ex)
            {
                await _provider.SendErrorAsync("Permission response error", ex.Message);
            }
        }

        /// <summary>
        /// Handles the questionReject message from the webview.
        /// Processes question rejection from the user by forwarding to backend.
        /// 
        /// VS Code workflow: Matches the pattern in kilo-provider/handlers/question.ts
        /// where question reject is posted to /question/reject endpoint.
        /// 
        /// Workflow steps:
        /// 1. Get HTTP client from provider
        /// 2. Verify client is connected
        /// 3. POST to /question/reject with payload
        /// 
        /// Messages sent to webview:
        /// - error: { message: "Not connected to backend" }
        /// </summary>
        /// <param name="payload">The message payload containing question rejection data.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleQuestionRejectAsync(JsonElement? payload)
        {
            if (payload == null) return;

            try
            {
                var kiotaClient = _provider.GetKiloClient();
                if (kiotaClient == null)
                {
                    await _provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                var questionReject = JsonSerializer.Deserialize<Generated.Models.QuestionReject>(payload.Value.GetRawText());
                if (questionReject != null)
                {
                    await kiotaClient.Question.Reject.PostAsync(questionReject);
                }
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
