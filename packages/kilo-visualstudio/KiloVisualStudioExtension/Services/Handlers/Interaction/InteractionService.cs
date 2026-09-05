using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KiloExtensionDTOs.ExtensionMessages;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.Services.Handlers.Session;
using KiloVisualStudioExtension.Utils;
using Microsoft.VisualStudio.PlatformUI;
using Newtonsoft.Json.Linq;

namespace KiloVisualStudioExtension.Services.Handlers.Interaction
{
    /// <summary>
    /// Handles user interaction operations like permission replies, question replies, and prompt handling.
    /// This matches the VS Code pattern where interaction handling is extracted into separate handler modules.
    /// </summary>
    public class InteractionService : ServiceProviderServiceBase
  {
        private bool _disposed;

        private readonly Dictionary<string, string> _permissionDirectories = new();
        private readonly Dictionary<string, string> _questionDirectories = new();

        private VSProvider Provider => _serviceProvider.GetService<VSProvider>() 
            ?? throw new InvalidOperationException("VSProvider not registered in service provider");

        /// <summary>
        /// Detects if an exception represents a 404/NotFoundError (stale permission/question).
        /// Matches the VS Code isNotFoundError() pattern from permission-handler.ts.
        /// </summary>
        private static bool IsNotFoundError(Exception ex)
        {
            if (ex is ApiException apiEx)
            {
                if (apiEx.StatusCode == 404) return true;
                
                if (!string.IsNullOrEmpty(apiEx.Response))
                {
                    try
                    {
                        var json = JObject.Parse(apiEx.Response);
                        var data = json["data"];
                        if (data != null)
                        {
                            var name = data["name"]?.ToString();
                            var status = data["status"]?.ToString();
                            if (name == "NotFoundError" || status == "404") return true;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Performs stale cleanup for a permission - removes directory mapping and posts error to webview.
        /// Matches the VS Code staleCleanup() pattern from permission-handler.ts.
        /// </summary>
        private void StalePermissionCleanup(string permissionId)
        {
            _permissionDirectories.Remove(permissionId);
      Provider.PostMessage(new PermissionErrorMessage
      {
        PermissionID = permissionId,
        Stale = true
      });
            _ = FetchAndSendPendingPermissionsAsync();
        }

        /// <summary>
        /// Performs stale cleanup for a question - removes directory mapping and posts error to webview.
        /// Mirrors the permission stale cleanup pattern.
        /// </summary>
        private void StaleQuestionCleanup(string questionId)
        {
            _questionDirectories.Remove(questionId);
            Provider.PostMessage(new QuestionErrorMessage {
              RequestID = questionId,              
              //  questionID = questionId,
              //  stale = true
            });
            _ = FetchAndSendPendingQuestionsAsync();
        }

        /// <summary>
        /// Creates a new InteractionHandlerService instance.
        /// </summary>
        /// <param name="serviceProvider">The service provider for dependency injection.</param>
        public InteractionService(ServiceProvider serviceProvider): base(serviceProvider)
        {
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
                await HandlePermissionResponseInternalAsync(requestId, response, null, Array.Empty<string>(), Array.Empty<string>());
            }
        }

        private async Task HandlePermissionResponseInternalAsync(string requestId, string response, string? sessionID, string[] approvedAlways, string[] deniedAlways)
        {
            var nswagClient = Provider.GetNswagClient();
            if (nswagClient == null) return;

            string dir;
            if (!_permissionDirectories.TryGetValue(requestId, out dir))
            {
                dir = _serviceProvider.GetService<ProjectDirectoryProvider>().GetWorkspaceDirectory(sessionID);
            }

            var staleCleanup = () =>
            {
                _permissionDirectories.Remove(requestId);
                Provider.PostMessage(new PermissionErrorMessage
                {
                    PermissionID = requestId,
                    Stale = true
                }));
                _ = FetchAndSendPendingPermissionsAsync();
            };

            // Save always-rules first if any (matching VS Code pattern)
            if (approvedAlways.Length > 0 || deniedAlways.Length > 0)
            {
                try
                {
                    var saveBody = new Body14
                    {
                        ApprovedAlways = approvedAlways.ToList(),
                        DeniedAlways = deniedAlways.ToList()
                    };
                    await nswagClient.Permission_saveAlwaysRulesAsync(requestId, dir, "", saveBody);
                }
                catch (Exception ex)
                {
                    // Check if it's a 404/not found error
                    if (IsNotFoundError(ex))
                    {
                        staleCleanup();
                        return;
                    }
                    System.Diagnostics.Debug.WriteLine($"[Kilo] InteractionHandler: failed to save always-rules: {ex.Message}");
                    Provider.PostMessage(new PermissionErrorMessage
                    {
                        PermissionID = requestId
                    });
                    return;
                }
            }

            try
            {
                var reply = response.ToLowerInvariant() switch
                {
                    "approve" or "allow" => Body13Reply.Once,
                    "always" => Body13Reply.Always,
                    "reject" or "deny" => Body13Reply.Reject,
                    _ => Body13Reply.Once
                };

                await nswagClient.Permission_replyAsync(requestId, dir, "", new Body13 
                { 
                    Reply = reply,
                    Message = response 
                });
                
                _permissionDirectories.Remove(requestId);
                System.Diagnostics.Debug.WriteLine("[Kilo] InteractionHandler: permission reply sent");
            }
            catch (Exception ex)
            {
                if (IsNotFoundError(ex))
                {
                    staleCleanup();
                    return;
                }
                System.Diagnostics.Debug.WriteLine($"[Kilo] InteractionHandler: permission reply error: {ex.Message}");
        Provider.PostMessage(new PermissionErrorMessage
        {
          PermissionID = requestId
        });
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
            string? sessionID = null;

            if (payload.Value.TryGetProperty("requestId", out var rid))
            {
                requestId = rid.GetString();
            }
            if (payload.Value.TryGetProperty("answers", out var ans))
            {
                answers = ans;
            }
            if (payload.Value.TryGetProperty("sessionID", out var sid))
            {
                sessionID = sid.GetString();
            }

            if (!string.IsNullOrEmpty(requestId) && answers.HasValue)
            {
                var nswagClient = Provider.GetNswagClient();
                if (nswagClient == null) return;

                string dir;
                if (!_questionDirectories.TryGetValue(requestId, out dir))
                {
                    dir = Provider.GetWorkspaceDirectory(sessionID);
                }

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
                    await nswagClient.Question_replyAsync(requestId, dir, "", replyBody);
                    _questionDirectories.Remove(requestId);
                    System.Diagnostics.Debug.WriteLine("[Kilo] InteractionHandler: question reply sent");
                }
                catch (Exception ex)
                {
                    if (IsNotFoundError(ex))
                    {
                        System.Diagnostics.Debug.WriteLine($"[Kilo] InteractionHandler: question {requestId} is stale (404)");
                        StaleQuestionCleanup(requestId);
                        return;
                    }
                    System.Diagnostics.Debug.WriteLine($"[Kilo] InteractionHandler: error sending question reply: {ex.Message}");
                    Provider.PostMessage(JsonSerializer.Serialize(new
                    {
                        type = "questionError",
                        questionID = requestId
                    }));
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
            if (payload == null || !payload.HasValue) return;

            string? requestId = null;
            string? response = null;
            string? sessionID = null;
            string[]? approvedAlways = null;
            string[]? deniedAlways = null;

            if (payload.Value.TryGetProperty("requestId", out var rid))
            {
                requestId = rid.GetString();
            }
            if (payload.Value.TryGetProperty("response", out var resp))
            {
                response = resp.GetString();
            }
            if (payload.Value.TryGetProperty("sessionID", out var sid))
            {
                sessionID = sid.GetString();
            }
            if (payload.Value.TryGetProperty("approvedAlways", out var approved) && approved.ValueKind == JsonValueKind.Array)
            {
                approvedAlways = approved.EnumerateArray().Select(a => a.GetString()!).ToArray();
            }
            if (payload.Value.TryGetProperty("deniedAlways", out var denied) && denied.ValueKind == JsonValueKind.Array)
            {
                deniedAlways = denied.EnumerateArray().Select(d => d.GetString()!).ToArray();
            }

            if (!string.IsNullOrEmpty(requestId) && !string.IsNullOrEmpty(response))
            {
                await HandlePermissionResponseInternalAsync(requestId, response, sessionID, approvedAlways ?? Array.Empty<string>(), deniedAlways ?? Array.Empty<string>());
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
            if (payload == null || !payload.HasValue) return;

            string? requestId = null;
            string? sessionID = null;

            if (payload.Value.TryGetProperty("requestId", out var rid))
            {
                requestId = rid.GetString();
            }
            if (payload.Value.TryGetProperty("sessionID", out var sid))
            {
                sessionID = sid.GetString();
            }

            if (!string.IsNullOrEmpty(requestId))
            {
                var nswagClient = Provider.GetNswagClient();
                if (nswagClient == null) return;

                string dir;
                if (!_questionDirectories.TryGetValue(requestId, out dir))
                {
                    dir = Provider.GetWorkspaceDirectory(sessionID);
                }

                try
                {
                    await nswagClient.Question_rejectAsync(requestId, dir, "");
                    _questionDirectories.Remove(requestId);
                }
                catch (Exception ex)
                {
                    if (IsNotFoundError(ex))
                    {
                        System.Diagnostics.Debug.WriteLine($"[Kilo] InteractionHandler: question {requestId} is stale (404)");
                        StaleQuestionCleanup(requestId);
                        return;
                    }
                    System.Diagnostics.Debug.WriteLine($"[Kilo] InteractionHandler: question reject error: {ex.Message}");
                    Provider.PostMessage(JsonSerializer.Serialize(new
                    {
                        type = "questionError",
                        questionID = requestId
                    }));
                }
            }
        }

        public async Task FetchAndSendPendingPermissionsAsync()
        {
            var nswagClient = Provider.GetNswagClient();
            if (nswagClient == null) return;

            var workspaceDir = _serviceProvider.GetService<ProjectDirectoryProvider>().GetWorkspaceDirectory();
            var seen = new HashSet<string>();
            var validDirs = new HashSet<string>();

            var dirs = new HashSet<string> { workspaceDir };
            foreach (var kvp in _serviceProvider.GetService<ProjectDirectoryProvider>().GetSessionDirectories())
            {
                if (_serviceProvider.GetService<SessionHandlerService>().IsTrackedSession(kvp.Key))
                {
                    dirs.Add(kvp.Value);
                }
            }

            foreach (var dir in dirs)
            {
                try
                {
                    var perms = await nswagClient.Permission_listAsync(dir, "");
                    validDirs.Add(dir);

                    foreach (var perm in perms)
                    {
                        if (seen.Contains(perm.Id)) continue;
                        seen.Add(perm.Id);

                        if (!_serviceProvider.GetService<SessionHandlerService>().IsTrackedSession(perm.SessionID)) continue;

                        _permissionDirectories[perm.Id] = dir;
                        Provider.PostMessage(JsonSerializer.Serialize(new
                        {
                            type = "permissionRequest",
                            permission = new
                            {
                                id = perm.Id,
                                sessionID = perm.SessionID,
                                toolName = perm.Permission,
                                patterns = perm.Patterns,
                                always = perm.Always,
                                args = perm.Metadata,
                                message = $"Permission required: {perm.Permission}",
                                tool = perm.Tool != null ? System.Text.Json.JsonSerializer.SerializeToElement(perm.Tool) : (System.Text.Json.JsonElement?)null
                            }
                        }));
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Kilo] InteractionHandler: failed to fetch permissions for {dir}: {ex.Message}");
                }
            }

            PrunePermissionDirectories(seen, validDirs);
        }

        public async Task FetchAndSendPendingQuestionsAsync()
        {
            var nswagClient = Provider.GetNswagClient();
            if (nswagClient == null) return;

            var workspaceDir = _serviceProvider.GetService<ProjectDirectoryProvider>().GetWorkspaceDirectory();
            var seen = new HashSet<string>();
            var validDirs = new HashSet<string>();

            var dirs = new HashSet<string> { workspaceDir };
            foreach (var kvp in _serviceProvider.GetService<ProjectDirectoryProvider>().GetSessionDirectories())
            {
                if (_serviceProvider.GetService<SessionHandlerService>().IsTrackedSession(kvp.Key))
                {
                    dirs.Add(kvp.Value);
                }
            }

            foreach (var dir in dirs)
            {
                try
                {
                    var questions = await nswagClient.Question_listAsync(dir, "");
                    validDirs.Add(dir);

                    foreach (var q in questions)
                    {
                        if (seen.Contains(q.Id)) continue;
                        seen.Add(q.Id);

                        if (!_serviceProvider.GetService<SessionHandlerService>().IsTrackedSession(q.SessionID)) continue;

                        _questionDirectories[q.Id] = dir;
                        Provider.PostMessage(System.Text.Json.JsonSerializer.Serialize(new
                        {
                            type = "questionRequest",
                            question = new
                            {
                                id = q.Id,
                                sessionID = q.SessionID,
                                questions = q.Questions != null ? System.Text.Json.JsonSerializer.SerializeToElement(q.Questions) : (System.Text.Json.JsonElement?)null,
                                blocking = q.Blocking,
                                tool = q.Tool != null ? System.Text.Json.JsonSerializer.SerializeToElement(q.Tool) : (System.Text.Json.JsonElement?)null
                            }
                        }));
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Kilo] InteractionHandler: failed to fetch questions for {dir}: {ex.Message}");
                }
            }

            PruneQuestionDirectories(seen, validDirs);
        }

        private void PrunePermissionDirectories(HashSet<string> seen, HashSet<string> validDirs)
        {
            var toRemove = _permissionDirectories.Keys.Where(k => !seen.Contains(k)).ToList();
            foreach (var key in toRemove)
            {
                _permissionDirectories.Remove(key);
            }
        }

        private void PruneQuestionDirectories(HashSet<string> seen, HashSet<string> validDirs)
        {
            var toRemove = _questionDirectories.Keys.Where(k => !seen.Contains(k)).ToList();
            foreach (var key in toRemove)
            {
                _questionDirectories.Remove(key);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}

