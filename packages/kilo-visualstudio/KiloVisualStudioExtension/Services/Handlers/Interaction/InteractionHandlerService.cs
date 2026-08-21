using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KiloVisualStudioExtension.ApiClient;

namespace KiloVisualStudioExtension.Services.Handlers.Interaction
{
    /// <summary>
    /// Handles user interaction operations like permission replies, question replies, and prompt handling.
    /// This matches the VS Code pattern where interaction handling is extracted into separate handler modules.
    /// </summary>
    public class InteractionHandlerService : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private bool _disposed;

        private VSProvider Provider => _serviceProvider.GetService<VSProvider>() 
            ?? throw new InvalidOperationException("VSProvider not registered in service provider");

        /// <summary>
        /// Creates a new InteractionHandlerService instance.
        /// </summary>
        /// <param name="serviceProvider">The service provider for dependency injection.</param>
        public InteractionHandlerService(ServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
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
            if (!Provider.IsConnected())
            {
                await Provider.SendErrorAsync("Not Connected", "Not connected to CLI backend");
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
            
            if (string.IsNullOrEmpty(sessionID) && !string.IsNullOrEmpty(Provider.GetCurrentSessionID()))
            {
                sessionID = Provider.GetCurrentSessionID();
            }

            if (string.IsNullOrEmpty(sessionID))
            {
                System.Diagnostics.Debug.WriteLine("[Kilo] InteractionHandler: no session available, creating new session...");
                var created = await Provider.CreateSessionInternalAsync();
                if (!created)
                {
                    await Provider.SendErrorAsync("Prompt Error", "Failed to create session");
                    return;
                }
                sessionID = Provider.GetCurrentSessionID();
                
                if (string.IsNullOrEmpty(sessionID))
                {
                    await Provider.SendErrorAsync("Prompt Error", "Failed to create session");
                    return;
                }
            }

            if (!payload.Value.TryGetProperty("text", out var textProp) || string.IsNullOrEmpty(textProp.GetString()))
            {
                System.Diagnostics.Debug.WriteLine("[Kilo] InteractionHandler: missing text in prompt request");
                await Provider.SendErrorAsync("Prompt Error", "Missing message text");
                return;
            }
            var text = textProp.GetString()!;
            var len = text.Length < 50 ? text.Length : 50;
            System.Diagnostics.Debug.WriteLine($"[Kilo] InteractionHandler: prompt received for session {sessionID}: {text.Substring(0, len)}...");

            var nswagClient = Provider.GetNswagClient();
            if (nswagClient == null)
            {
                await Provider.SendErrorAsync("Not Connected", "Not connected to CLI backend");
                return;
            }
            
            try
            {
                var part = new Parts2();
                part.AdditionalProperties["type"] = "text";
                part.AdditionalProperties["text"] = text;
                
                var promptBody = new Body24 
                { 
                    Parts = new System.Collections.Generic.List<Parts2> { part } 
                };
                
                System.Diagnostics.Debug.WriteLine($"[Kilo] InteractionHandler: sending POST to /session/{sessionID}/prompt_async");
                
                await nswagClient.Session_prompt_asyncAsync(sessionID, System.Environment.CurrentDirectory, "", promptBody);
                System.Diagnostics.Debug.WriteLine("[Kilo] InteractionHandler: prompt accepted, response will come via SSE");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] InteractionHandler: error sending prompt: {ex.Message}");
                await Provider.SendErrorAsync("Prompt Error", ex.Message);
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
                var nswagClient = Provider.GetNswagClient();
                if (nswagClient == null) return;
                
                try
                {
                    var reply = response.ToLowerInvariant() switch
                    {
                        "approve" or "allow" => Body13Reply.Once,
                        "always" => Body13Reply.Always,
                        "reject" or "deny" => Body13Reply.Reject,
                        _ => Body13Reply.Once
                    };
                    
                    var replyBody = new Body13 
                    { 
                        Reply = reply,
                        Message = response 
                    };
                    await nswagClient.Permission_replyAsync(requestId, System.Environment.CurrentDirectory, "", replyBody);
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
                var nswagClient = Provider.GetNswagClient();
                if (nswagClient == null) return;
                
                try
                {
                    var answersArray = answers.Value.ValueKind == JsonValueKind.Array 
                        ? answers.Value.EnumerateArray().Select(a => a.GetString()).ToArray() 
                        : new string[] { answers.Value.GetString() };
                    
                    var questionAnswerList = new System.Collections.Generic.List<QuestionAnswer>();
                    foreach (var answer in answersArray)
                    {
                        var qa = new QuestionAnswer();
                        if (!string.IsNullOrEmpty(answer))
                        {
                            qa.Add(answer);
                        }
                        questionAnswerList.Add(qa);
                    }
                    
                    var replyBody = new Body12 { Answers = questionAnswerList };
                    await nswagClient.Question_replyAsync(requestId, System.Environment.CurrentDirectory, "", replyBody);
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
        /// NOTE: NSwag client does not have a Permission_PostAsync method. The Kiota implementation
        /// referenced kiotaClient.Permission.PostAsync(permissionResponse) but this endpoint does not
        /// exist in the generated NSwag client. This is a known limitation - permission response
        /// posting is not yet available via NSwag.
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
                // TODO: NSwag client needs Permission_PostAsync method added
                // var nswagClient = Provider.GetNswagClient();
                // if (nswagClient == null)
                // {
                //     await Provider.SendErrorAsync("Not connected", "Not connected to backend");
                //     return;
                // }
                // var permissionResponse = JsonSerializer.Deserialize<...>(payload.Value.GetRawText());
                // await nswagClient.Permission_PostAsync(permissionResponse);
                
                await Provider.SendErrorAsync("Not implemented", "Permission response posting is not yet supported via NSwag");
            }
            catch (Exception ex)
            {
                await Provider.SendErrorAsync("Permission response error", ex.Message);
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
                var nswagClient = Provider.GetNswagClient();
                if (nswagClient == null)
                {
                    await Provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                if (payload.Value.TryGetProperty("requestId", out var rid))
                {
                    var requestId = rid.GetString();
                    if (!string.IsNullOrEmpty(requestId))
                    {
                        await nswagClient.Question_rejectAsync(requestId, System.Environment.CurrentDirectory, "");
                    }
                }
            }
            catch (Exception ex)
            {
                await Provider.SendErrorAsync("Question reject error", ex.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}

