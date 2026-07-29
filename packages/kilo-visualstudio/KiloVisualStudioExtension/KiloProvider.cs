using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;

namespace KiloVisualStudioExtension
{
    public class VSProvider : IDisposable
    {
        private readonly KiloWebViewControl _webView;
        private readonly KiloConnectionService _connectionService;
        private bool _isWebviewReady = false;
        private bool _disposed;
        private string? _currentSessionID;  // Track the current active session ID

        public VSProvider(KiloWebViewControl webView, KiloConnectionService connectionService)
        {
            _webView = webView;
            _connectionService = connectionService;
            _webView.OnMessageReceived += HandleMessageReceived;
            _connectionService.OnStateChange += HandleStateChange;
            _connectionService.OnSseEvent += HandleSseEvent;
        }

        private void HandleMessageReceived(object? sender, WebViewMessageEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[Kilo] KiloProvider: received message type={e.Type}");
            _ = ProcessMessageAsync(e.Type, e.Payload);
        }

        private async Task ProcessMessageAsync(string type, JsonElement? payload)
        {
            try
            {
                switch (type)
                {
                    case "webviewReady":
                        System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: webviewReady received");
                        _isWebviewReady = true;
                        await HandleWebviewReadyAsync();
                        break;

                    case "webviewInitialized":
                        System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: webviewInitialized received, releasing data send");
                        _webviewInitializedTcs?.TrySetResult(null);
                        break;

                    case "requestProviders":
                        await HandleRequestProvidersAsync();
                        break;

                    case "requestAgents":
                        await HandleRequestAgentsAsync();
                        break;

                    case "requestConfig":
                        await HandleRequestConfigAsync();
                        break;

                    case "requestMcpStatus":
                        await HandleRequestMcpStatusAsync();
                        break;

                    case "requestRecents":
                        HandleRequestRecents();
                        break;

                    case "requestFavorites":
                        HandleRequestFavorites();
                        break;

                    case "requestVariants":
                        HandleRequestVariants();
                        break;

                    case "requestNotifications":
                        await HandleRequestNotificationsAsync();
                        break;

                    case "requestModelSelections":
                        HandleRequestModelSelections();
                        break;

                    case "requestIndexingSettings":
                        HandleRequestIndexingSettings();
                        break;

                    case "requestChatSettings":
                        HandleRequestChatSettings();
                        break;

                    case "requestThroughputSetting":
                        HandleRequestThroughputSetting();
                        break;

                    case "requestAutocompleteSettings":
                        HandleRequestAutocompleteSettings();
                        break;

                    case "requestWorkStyle":
                        await HandleRequestWorkStyleAsync();
                        break;

                    case "retryConnection":
                        System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: retryConnection requested");
                        await _connectionService.ConnectAsync();
                        break;

                    case "prompt":
                        await HandlePromptAsync(payload);
                        break;

                    case "permission/reply":
                        await HandlePermissionReplyAsync(payload);
                        break;

                    case "question/reply":
                        await HandleQuestionReplyAsync(payload);
                        break;

                    case "createSession":
                        await HandleCreateSessionAsync();
                        break;

                    case "clearSession":
                        HandleClearSession();
                        break;

                    case "loadMessages":
                        await HandleLoadMessagesAsync(payload);
                        break;

                    case "deleteMessage":
                        await HandleDeleteMessageAsync(payload);
                        break;

                    case "deleteSession":
                        await HandleDeleteSessionAsync(payload);
                        break;

                    case "renameSession":
                        await HandleRenameSessionAsync(payload);
                        break;

                    case "abort":
                        await HandleAbortAsync(payload);
                        break;

                    case "sendMessage":
                        await HandleSendMessageAsync(payload);
                        break;

                    case "login":
                        await HandleLoginAsync();
                        break;

                    case "logout":
                        await HandleLogoutAsync();
                        break;

                    case "refreshProfile":
                        await HandleRefreshProfileAsync();
                        break;

                    case "openSettingsPanel":
                        await HandleOpenSettingsPanelAsync(payload);
                        break;

                    case "openConfigFile":
                        await HandleOpenConfigFileAsync(payload);
                        break;

                    case "updateSetting":
                        await HandleUpdateSettingAsync(payload);
                        break;

                    case "updateConfig":
                        await HandleUpdateConfigAsync(payload);
                        break;

                    case "requestSkills":
                        await HandleRequestSkillsAsync();
                        break;

                    case "requestCommands":
                        await HandleRequestCommandsAsync();
                        break;

                    case "requestGlobalConfig":
                        await HandleRequestGlobalConfigAsync();
                        break;

                    case "requestIndexingStatus":
                        await HandleRequestIndexingStatusAsync();
                        break;

                    case "requestKiloEmbeddingModels":
                        await HandleRequestKiloEmbeddingModelsAsync();
                        break;

                    case "requestImageModels":
                        await HandleRequestImageModelsAsync();
                        break;

                    case "requestSandboxStatus":
                        await HandleRequestSandboxStatusAsync(payload);
                        break;

                    case "requestSandboxDefault":
                        await HandleRequestSandboxDefaultAsync(payload);
                        break;

                    case "setSandboxDefault":
                        await HandleSetSandboxDefaultAsync(payload);
                        break;

                    case "toggleSandbox":
                        await HandleToggleSandboxAsync(payload);
                        break;

                    case "dismissNotification":
                        await HandleDismissNotificationAsync(payload);
                        break;

                    case "applyWorkStyle":
                        await HandleApplyWorkStyleAsync(payload);
                        break;

                    case "resetReadNotifications":
                        await HandleResetReadNotificationsAsync();
                        break;

                    case "requestCloudSessions":
                        await HandleRequestCloudSessionsAsync(payload);
                        break;

                    case "requestCloudSessionData":
                        await HandleRequestCloudSessionDataAsync(payload);
                        break;

                    case "importAndSend":
                        await HandleImportAndSendAsync(payload);
                        break;

                    case "forkSession":
                        await HandleForkSessionAsync(payload);
                        break;

                    case "compact":
                        await HandleCompactAsync(payload);
                        break;

                    case "enhancePrompt":
                        await HandleEnhancePromptAsync(payload);
                        break;

                    case "saveImage":
                        await HandleSaveImageAsync(payload);
                        break;

                    case "openSubAgentViewer":
                        await HandleOpenSubAgentViewerAsync(payload);
                        break;

                    case "reload":
                        await HandleReloadAsync();
                        break;

                    case "persistVariant":
                        HandlePersistVariant(payload);
                        break;

                    case "persistRecents":
                        HandlePersistRecents(payload);
                        break;

                    case "toggleFavorite":
                        await HandleToggleFavoriteAsync(payload);
                        break;

                    case "requestFileSearch":
                        await HandleRequestFileSearchAsync(payload);
                        break;

                    case "requestSessionSearch":
                        await HandleRequestSessionSearchAsync(payload);
                        break;

                    case "requestFilePicker":
                        await HandleRequestFilePickerAsync(payload);
                        break;

                    case "requestTerminalContext":
                        await HandleRequestTerminalContextAsync(payload);
                        break;

                    case "requestBrowserSettings":
                        HandleRequestBrowserSettings();
                        break;

                    case "requestClaudeCompatSetting":
                        HandleRequestClaudeCompatSetting();
                        break;

                    case "requestNotificationSettings":
                        HandleRequestNotificationSettings();
                        break;

                    case "requestTimelineSetting":
                        HandleRequestTimelineSetting();
                        break;

                    case "requestGitRemoteUrl":
                        await HandleRequestGitRemoteUrlAsync();
                        break;

                    case "requestRemoteStatus":
                        HandleRequestRemoteStatus();
                        break;

                    case "connectMcp":
                        await HandleConnectMcpAsync(payload);
                        break;

                    case "disconnectMcp":
                        await HandleDisconnectMcpAsync(payload);
                        break;

                    case "authenticateMcp":
                        await HandleAuthenticateMcpAsync(payload);
                        break;

                    case "removeMcp":
                        await HandleRemoveMcpAsync(payload);
                        break;

                    case "removeSkill":
                        await HandleRemoveSkillAsync(payload);
                        break;

                    case "removeAgent":
                        await HandleRemoveAgentAsync(payload);
                        break;

                    case "fetchCustomProviderModels":
                        await HandleFetchCustomProviderModelsAsync(payload);
                        break;

                    case "connectProvider":
                    case "authorizeProviderOAuth":
                    case "completeProviderOAuth":
                    case "disconnectProvider":
                    case "saveCustomProvider":
                        await HandleProviderActionAsync(payload);
                        break;

                    case "anacondaDesktopStatus":
                    case "anacondaDesktopOpen":
                    case "anacondaDesktopSync":
                    case "cancelAnacondaDesktopRequest":
                        await HandleAnacondaDesktopAsync(payload);
                        break;

                    case "requestChatCompletion":
                        await HandleRequestChatCompletionAsync(payload);
                        break;

                    case "chatCompletionAccepted":
                        HandleChatCompletionAccepted(payload);
                        break;

                    case "toggleRemote":
                    case "setRemoteEnabled":
                        await HandleToggleRemoteAsync(payload);
                        break;

                    case "openMarketplacePanel":
                        await HandleOpenMarketplacePanelAsync(payload);
                        break;

                    case "openKiloClaw":
                        await HandleOpenKiloClawAsync();
                        break;

                    case "openVSCodeSettings":
                        await HandleOpenVSCodeSettingsAsync(payload);
                        break;

                    case "setLanguage":
                        await HandleSetLanguageAsync(payload);
                        break;

                    case "setOrganization":
                        await HandleSetOrganizationAsync(payload);
                        break;

                    case "cancelLogin":
                        HandleCancelLogin();
                        break;

                    case "revertSession":
                        await HandleRevertSessionAsync(payload);
                        break;

                    case "unrevertSession":
                        await HandleUnrevertSessionAsync(payload);
                        break;

                    case "syncSession":
                        await HandleSyncSessionAsync(payload);
                        break;

                    case "loadSessions":
                        await HandleLoadSessionsAsync();
                        break;

                    case "requestSessionModelUsage":
                        await HandleRequestSessionModelUsageAsync(payload);
                        break;

                    case "permissionResponse":
                        await HandlePermissionResponseAsync(payload);
                        break;

                    case "questionReply":
                        await HandleQuestionReplyAsync(payload);
                        break;

                    case "questionReject":
                        await HandleQuestionRejectAsync(payload);
                        break;

                    case "sessionCostAlertResponse":
                        await HandleSessionCostAlertResponseAsync(payload);
                        break;

                    case "testNotification":
                        HandleTestNotification(payload);
                        break;

                    case "resetAllSettings":
                        await HandleResetAllSettingsAsync();
                        break;

                    case "telemetry":
                        HandleTelemetry(payload);
                        break;

                    case "persistModelSelectorExpanded":
                        HandlePersistModelSelectorExpanded(payload);
                        break;

                    case "requestModelSelectorExpanded":
                        HandleRequestModelSelectorExpanded();
                        break;

                    default:
                        System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: unhandled message type={type}");
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error processing message {type}: {ex.Message}");
                await SendErrorAsync("Message processing error", ex.Message);
            }
        }

        private TaskCompletionSource<object?>? _webviewInitializedTcs;

        private async Task HandleWebviewReadyAsync()
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: webviewReady received, starting initialization");
            
            // Connect to backend if not already connected
            if (_connectionService.State != ConnectionState.Connected)
            {
                await _connectionService.ConnectAsync();
            }

            // Send ready message with server info
            var serverInfo = _connectionService.GetServerInfo();
            if (serverInfo != null)
            {
                var readyMessage = new
                {
                    type = "ready",
                    serverInfo = new { port = serverInfo.Port },
                    extensionVersion = "1.0.0",
                    vscodeLanguage = "en",
                    workspaceDirectory = Environment.CurrentDirectory
                };
                _webView.PostMessage(JsonSerializer.Serialize(readyMessage));
            }

            // Send connection state
            var connState = new { type = "connectionState", state = _connectionService.State.ToString().ToLowerInvariant() };
            _webView.PostMessage(JsonSerializer.Serialize(connState));

            // Wait for webviewInitialized signal before sending data
            if (_webviewInitializedTcs == null)
            {
                _webviewInitializedTcs = new TaskCompletionSource<object?>();
            }
            
            //System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: waiting for webviewInitialized...");
            //await _webviewInitializedTcs.Task;
            //System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: webviewInitialized received, sending initial data");

            // Create a default session if none exists
            await HandleCreateSessionAsync();

            // Fetch and send initial data in parallel
            try
            {
                await Task.WhenAll(
                    HandleRequestProvidersAsync(),
                    HandleRequestAgentsAsync(),
                    HandleRequestConfigAsync(),
                    HandleRequestMcpStatusAsync(),
                    HandleRequestSkillsAsync(),
                    HandleRequestCommandsAsync(),
                    HandleRequestIndexingStatusAsync(),
                    HandleRequestNotificationsAsync(),
                    HandleRequestWorkStyleAsync()
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error during initial data fetch: {ex.Message}");
            }

            // Send extensionDataReady to signal all initial data is loaded
            var extensionReady = new { type = "extensionDataReady" };
            _webView.PostMessage(JsonSerializer.Serialize(extensionReady));

            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: initialization complete");
        }

        private void HandleStateChange(object? sender, ConnectionStateEventArgs e)
        {
            var message = new { type = "connectionState", state = e.State.ToString().ToLowerInvariant(), errorMessage = e.ErrorMessage };
            _webView.PostMessage(JsonSerializer.Serialize(message));
        }

        private void HandleSseEvent(object? sender, SseEventReceivedEventArgs e)
        {
            var message = new { type = "sse", payload = new { eventType = e.EventType, data = e.Data } };
            _webView.PostMessage(JsonSerializer.Serialize(message));
        }

        private async Task HandleRequestProvidersAsync()
        {
            var httpClient = _connectionService.GetCachedHttpClient();
            if (httpClient == null)
            {
                await SendEmptyProvidersAsync();
                return;
            }

            try
            {
                var responseDoc = await httpClient.GetJsonAsync("/provider");
                
                // Convert providers array to Record<string, Provider> format
                var providersDict = new Dictionary<string, object>();
                var connectedList = new List<string>();
                var defaultsDict = new Dictionary<string, string>();
                
                if (responseDoc != null)
                {
                    var root = responseDoc.RootElement;
                    
                    // Parse "all" array and convert to dictionary
                    if (root.TryGetProperty("all", out var all) && all.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var provider in all.EnumerateArray())
                        {
                            if (provider.TryGetProperty("id", out var id))
                            {
                                providersDict[id.GetString() ?? ""] = provider;
                            }
                        }
                    }
                    
                    // Parse "connected" array
                    if (root.TryGetProperty("connected", out var connected) && connected.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in connected.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                connectedList.Add(item.GetString() ?? "");
                            }
                        }
                    }
                    
                    // Parse "default" object
                    if (root.TryGetProperty("default", out var defaults) && defaults.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in defaults.EnumerateObject())
                        {
                            defaultsDict[prop.Name] = prop.Value.GetString() ?? "";
                        }
                    }
                }
                
                var message = new 
                { 
                    type = "providersLoaded", 
                    providers = providersDict,
                    connected = connectedList.ToArray(),
                    defaults = defaultsDict,
                    defaultSelection = new { },
                    authMethods = new Dictionary<string, object[]>(),
                    authStates = new Dictionary<string, object>()
                };
                _webView.PostMessage(JsonSerializer.Serialize(message));
                responseDoc?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error fetching providers: {ex.Message}");
                await SendEmptyProvidersAsync();
            }
        }

        private async Task SendEmptyProvidersAsync()
        {
            var message = new
            {
                type = "providersLoaded",
                providers = new Dictionary<string, object>(),
                connected = Array.Empty<string>(),
                defaults = new Dictionary<string, string>(),
                defaultSelection = new { },
                authMethods = new Dictionary<string, object[]>(),
                authStates = new Dictionary<string, object>()
            };
            _webView.PostMessage(JsonSerializer.Serialize(message));
        }

        private async Task HandleRequestAgentsAsync()
        {
            var httpClient = _connectionService.GetCachedHttpClient();
            if (httpClient == null)
            {
                await SendEmptyAgentsAsync();
                return;
            }

            try
            {
                var responseDoc = await httpClient.GetJsonAsync("/experimental/tool/ids");
                var agentsList = new List<object>();
                if (responseDoc != null && responseDoc.RootElement.TryGetProperty("agents", out var agents))
                {
                    foreach (var agent in agents.EnumerateArray())
                    {
                        agentsList.Add(agent);
                    }
                }
                var message = new 
                { 
                    type = "agentsLoaded", 
                    agents = agentsList.ToArray(),
                    allAgents = agentsList.ToArray(),
                    defaultAgent = "ask"
                };
                _webView.PostMessage(JsonSerializer.Serialize(message));
                responseDoc?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error fetching agents: {ex.Message}");
                await SendEmptyAgentsAsync();
            }
        }

        private async Task SendEmptyAgentsAsync()
        {
            var message = new 
            { 
                type = "agentsLoaded", 
                agents = Array.Empty<object>(),
                allAgents = Array.Empty<object>(),
                defaultAgent = "ask"
            };
            _webView.PostMessage(JsonSerializer.Serialize(message));
        }

        private async Task HandleRequestConfigAsync()
        {
            var httpClient = _connectionService.GetCachedHttpClient();
            if (httpClient == null)
            {
                await SendEmptyConfigAsync();
                return;
            }

            try
            {
                var responseDoc = await httpClient.GetJsonAsync("/config");
                JsonElement config = JsonDocument.Parse("{}").RootElement;
                JsonElement features = JsonDocument.Parse("{}").RootElement;
                if (responseDoc != null && responseDoc.RootElement.TryGetProperty("config", out var c))
                    config = c;
                if (responseDoc != null && responseDoc.RootElement.TryGetProperty("features", out var f))
                    features = f;
                var message = new { type = "configLoaded", config, features };
                _webView.PostMessage(JsonSerializer.Serialize(message));
                responseDoc?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] KiloProvider: error fetching config: {ex.Message}");
                await SendEmptyConfigAsync();
            }
        }

        private async Task SendEmptyConfigAsync()
        {
            var message = new { type = "configLoaded", config = new { }, features = new { } };
            _webView.PostMessage(JsonSerializer.Serialize(message));
        }

        private async Task HandleRequestMcpStatusAsync()
        {
            var httpClient = _connectionService.GetCachedHttpClient();
            if (httpClient == null)
            {
                await SendEmptyMcpStatusAsync();
                return;
            }

            try
            {
                var responseDoc = await httpClient.GetJsonAsync("/mcp");
                JsonElement status = JsonDocument.Parse("{}").RootElement;
                if (responseDoc != null && responseDoc.RootElement.TryGetProperty("status", out var s))
                    status = s;
                var message = new { type = "mcpStatusLoaded", status };
                _webView.PostMessage(JsonSerializer.Serialize(message));
                responseDoc?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] KiloProvider: error fetching MCP status: {ex.Message}");
                await SendEmptyMcpStatusAsync();
            }
        }

        private async Task SendEmptyMcpStatusAsync()
        {
            var message = new { type = "mcpStatusLoaded", status = new { } };
            _webView.PostMessage(JsonSerializer.Serialize(message));
        }

        private void HandleRequestRecents()
        {
            var message = new { type = "recentsLoaded", recents = new object[0] };
            _webView.PostMessage(JsonSerializer.Serialize(message));
        }

        private void HandleRequestFavorites()
        {
            var message = new { type = "favoritesLoaded", favorites = new object[0] };
            _webView.PostMessage(JsonSerializer.Serialize(message));
        }

        private void HandleRequestVariants()
        {
            var message = new { type = "variantsLoaded", variants = new { } };
            _webView.PostMessage(JsonSerializer.Serialize(message));
        }

        private async Task HandleRequestNotificationsAsync()
        {
            var message = new { type = "notificationsLoaded", notifications = new object[0] };
            _webView.PostMessage(JsonSerializer.Serialize(message));
        }

        private void HandleRequestModelSelections()
        {
            var message = new { type = "modelSelectionsLoaded", selections = new { } };
            _webView.PostMessage(JsonSerializer.Serialize(message));
        }

        private void HandleRequestIndexingSettings()
        {
            var message = new { type = "indexingSettingsLoaded", settings = new { showButtonWhenDisabled = true } };
            _webView.PostMessage(JsonSerializer.Serialize(message));
        }

        private void HandleRequestChatSettings()
        {
            var message = new { type = "chatSettingsLoaded", settings = new { shiftTabCyclesVariant = false } };
            _webView.PostMessage(JsonSerializer.Serialize(message));
        }

        private void HandleRequestThroughputSetting()
        {
            var message = new { type = "throughputSettingLoaded", visible = true };
            _webView.PostMessage(JsonSerializer.Serialize(message));
        }

        private void HandleRequestAutocompleteSettings()
        {
          _webView.PostMessage(JsonSerializer.Serialize(new { type = "autocompleteSettingsLoaded", settings = new { enableAutoTrigger = false, enableSmartInlineTaskKeybinding = false, enableChatAutocomplete = false, provider = (string?)null, model = (string?)null } }));
        }

        private async Task HandleRequestWorkStyleAsync()
        {
            var message = new { type = "workStyleLoaded", style = new { mode = "ask", autoApprove = new { enabled = false, limit = 0 } } };
            _webView.PostMessage(JsonSerializer.Serialize(message));
        }

        private async Task HandlePromptAsync(JsonElement? payload)
        {
            if (!_connectionService.GetHttpClient().IsConnected())
            {
                await SendErrorAsync("Not Connected", "Not connected to CLI backend");
                return;
            }

            if (!payload.HasValue) return;

            // Extract sessionID from payload - must be a valid session ID starting with "ses"
            string? sessionID = null;
            if (payload.Value.TryGetProperty("sessionID", out var sessionIDProp) && !string.IsNullOrEmpty(sessionIDProp.GetString()))
            {
                var sid = sessionIDProp.GetString()!;
                if (sid.StartsWith("ses_"))
                {
                    sessionID = sid;
                }
            }
            
            // Fall back to tracked current session
            if (string.IsNullOrEmpty(sessionID) && !string.IsNullOrEmpty(_currentSessionID))
            {
                sessionID = _currentSessionID;
            }

            if (string.IsNullOrEmpty(sessionID))
            {
                System.Diagnostics.Debug.WriteLine("[Kilo] KiloProvider: no valid session ID available for prompt (draftIDs are not valid session IDs)");
                await SendErrorAsync("Prompt Error", "No active session - please create a session first");
                return;
            }

            // Extract text from payload
            if (!payload.Value.TryGetProperty("text", out var textProp) || string.IsNullOrEmpty(textProp.GetString()))
            {
                System.Diagnostics.Debug.WriteLine("[Kilo] KiloProvider: missing text in prompt request");
                await SendErrorAsync("Prompt Error", "Missing message text");
                return;
            }
            var text = textProp.GetString()!;
            var len = text.Length < 50 ? text.Length : 50;
            System.Diagnostics.Debug.WriteLine($"[Kilo] KiloProvider: prompt received for session {sessionID}: {text.Substring(0, len)}...");

            var httpClient = _connectionService.GetHttpClient();
            try
            {
                // Build prompt data - start with minimal payload
                var part = new { type = "text", text };
                var promptData = new { 
                    parts = new[] { part }
                };
                
                var json = JsonSerializer.Serialize(promptData);
                System.Diagnostics.Debug.WriteLine($"[Kilo] KiloProvider: sending POST to /session/{sessionID}/prompt_async with body: {json}");
                
                // Use prompt_async - returns 204 No Content, so use PostAsync instead of PostJsonAsync
                var response = await httpClient.PostAsync($"/session/{sessionID}/prompt_async", promptData);
                if (response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine("[Kilo] KiloProvider: prompt accepted, response will come via SSE");
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"[Kilo] KiloProvider: prompt failed: {(int)response.StatusCode} - {errorBody}");
                    await SendErrorAsync("Prompt Error", $"Server returned {(int)response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] KiloProvider: error sending prompt: {ex.Message}");
                await SendErrorAsync("Prompt Error", ex.Message);
            }
        }

        private async Task HandlePermissionReplyAsync(JsonElement? payload)
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
                var httpClient = _connectionService.GetHttpClient();
                try
                {
                    var url = $"/permission/{requestId.Value.GetString()}/reply";
                    await httpClient.PostJsonAsync(url, new { response = response.Value.GetString() });
                    System.Diagnostics.Debug.WriteLine("[Kilo] KiloProvider: permission reply sent");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Kilo] KiloProvider: error sending permission reply: {ex.Message}");
                }
            }
        }

        private async Task HandleQuestionReplyAsync(JsonElement? payload)
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
                var httpClient = _connectionService.GetHttpClient();
                try
                {
                    var url = $"/question/{requestId.Value.GetString()}/reply";
                    await httpClient.PostJsonAsync(url, new { answers });
                    System.Diagnostics.Debug.WriteLine("[Kilo] KiloProvider: question reply sent");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Kilo] KiloProvider: error sending question reply: {ex.Message}");
                }
            }
        }

        private async Task SendErrorAsync(string title, string message)
        {
            var error = new { type = "error", title, message };
            _webView.PostMessage(JsonSerializer.Serialize(error));
        }

        // Session management handlers
        private async Task HandleCreateSessionAsync()
        {
            var httpClient = _connectionService.GetHttpClient();
            if (httpClient == null) return;
            try
            {
                var dir = Environment.CurrentDirectory;
                var responseDoc = await httpClient.PostJsonAsync("/session", new { directory = dir });
                if (responseDoc != null && responseDoc.RootElement.TryGetProperty("id", out var id))
                {
                    _currentSessionID = id.GetString() ?? "";
                    var sessionID = _currentSessionID;
                    System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: session created: {sessionID}");
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
                    _webView.PostMessage(JsonSerializer.Serialize(sessionCreated));
                }
                responseDoc?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error creating session: {ex.Message}");
            }
        }

        private void HandleClearSession()
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: clearSession");
        }

        private async Task HandleLoadMessagesAsync(JsonElement? payload)
        {
            if (!payload.HasValue) return;
            var sessionID = payload.Value.TryGetProperty("sessionID", out var sid) ? sid.GetString() : "";
            if (string.IsNullOrEmpty(sessionID)) return;
            var httpClient = _connectionService.GetHttpClient();
            if (httpClient == null) return;
            try
            {
                var response = await httpClient.GetJsonAsync($"/session/messages?sessionID={sessionID}");
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: loaded messages for session {sessionID}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error loading messages: {ex.Message}");
            }
        }

        private async Task HandleDeleteMessageAsync(JsonElement? payload)
        {
            if (!payload.HasValue) return;
            var sessionID = payload.Value.TryGetProperty("sessionID", out var sid) ? sid.GetString() : "";
            var messageID = payload.Value.TryGetProperty("messageID", out var mid) ? mid.GetString() : "";
            if (string.IsNullOrEmpty(sessionID) || string.IsNullOrEmpty(messageID)) return;
            var httpClient = _connectionService.GetHttpClient();
            if (httpClient == null) return;
            try
            {
                await httpClient.PostJsonAsync($"/session/message/delete", new { sessionID, messageID });
                System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: message deleted");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error deleting message: {ex.Message}");
            }
        }

        private async Task HandleDeleteSessionAsync(JsonElement? payload)
        {
            if (!payload.HasValue) return;
            var sessionID = payload.Value.TryGetProperty("sessionID", out var sid) ? sid.GetString() : "";
            if (string.IsNullOrEmpty(sessionID)) return;
            var httpClient = _connectionService.GetHttpClient();
            if (httpClient == null) return;
            try
            {
                await httpClient.PostJsonAsync($"/session/delete", new { sessionID });
                System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: session deleted");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error deleting session: {ex.Message}");
            }
        }

        private async Task HandleRenameSessionAsync(JsonElement? payload)
        {
            if (!payload.HasValue) return;
            var sessionID = payload.Value.TryGetProperty("sessionID", out var sid) ? sid.GetString() : "";
            var title = payload.Value.TryGetProperty("title", out var t) ? t.GetString() : "";
            if (string.IsNullOrEmpty(sessionID)) return;
            var httpClient = _connectionService.GetHttpClient();
            if (httpClient == null) return;
            try
            {
                await httpClient.PostJsonAsync($"/session/rename", new { sessionID, title });
                System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: session renamed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error renaming session: {ex.Message}");
            }
        }

        private async Task HandleAbortAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: abort");
        }

        private async Task HandleSendMessageAsync(JsonElement? payload)
        {
            if (!payload.HasValue) return;
            var text = payload.Value.TryGetProperty("text", out var t) ? t.GetString() : "";
            if (string.IsNullOrEmpty(text)) return;
            await HandlePromptAsync(payload);
        }

        // Auth handlers
        private async Task HandleLoginAsync()
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: login");
        }

        private async Task HandleLogoutAsync()
        {
            var httpClient = _connectionService.GetHttpClient();
            if (httpClient == null) return;
            try
            {
                await httpClient.PostJsonAsync("/auth/logout", new { });
                System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: logout");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error logging out: {ex.Message}");
            }
        }

        private async Task HandleRefreshProfileAsync()
        {
            var httpClient = _connectionService.GetHttpClient();
            if (httpClient == null) return;
            try
            {
                var profile = await httpClient.GetJsonAsync("/kilo/profile");
                if (profile != null)
                {
                    var message = new { type = "profileData", data = profile.RootElement };
                    _webView.PostMessage(JsonSerializer.Serialize(message));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error refreshing profile: {ex.Message}");
            }
        }

        // Settings handlers
        private async Task HandleOpenSettingsPanelAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: openSettingsPanel");
        }

        private async Task HandleOpenConfigFileAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: openConfigFile");
        }

        private async Task HandleUpdateSettingAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: updateSetting");
        }

        private async Task HandleUpdateConfigAsync(JsonElement? payload)
        {
            if (!payload.HasValue) return;
            var httpClient = _connectionService.GetHttpClient();
            if (httpClient == null) return;
            try
            {
                await httpClient.PostJsonAsync("/config/update", payload.Value);
                System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: config updated");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error updating config: {ex.Message}");
            }
        }

        // Additional request handlers
        private async Task HandleRequestSkillsAsync()
        {
            var httpClient = _connectionService.GetHttpClient();
            if (httpClient == null)
            {
                _webView.PostMessage(JsonSerializer.Serialize(new { type = "skillsLoaded", skills = new object[0] }));
                return;
            }
            try
            {
                var response = await httpClient.GetJsonAsync("/app/skills");
                JsonElement skills = JsonDocument.Parse("[]").RootElement;
                if (response != null && response.RootElement.TryGetProperty("skills", out var s))
                    skills = s;
                var message = new { type = "skillsLoaded", skills };
                _webView.PostMessage(JsonSerializer.Serialize(message));
                response?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error fetching skills: {ex.Message}");
                _webView.PostMessage(JsonSerializer.Serialize(new { type = "skillsLoaded", skills = new object[0] }));
            }
        }

        private async Task HandleRequestCommandsAsync()
        {
            _webView.PostMessage(JsonSerializer.Serialize(new { type = "commandsLoaded", commands = new object[0] }));
        }

        private async Task HandleRequestGlobalConfigAsync()
        {
            var httpClient = _connectionService.GetHttpClient();
            if (httpClient == null)
            {
                _webView.PostMessage(JsonSerializer.Serialize(new { type = "globalConfigLoaded", config = new { } }));
                return;
            }
            try
            {
                var response = await httpClient.GetJsonAsync("/config/global");
                JsonElement config = JsonDocument.Parse("{}").RootElement;
                if (response != null && response.RootElement.TryGetProperty("config", out var c))
                    config = c;
                var message = new { type = "globalConfigLoaded", config };
                _webView.PostMessage(JsonSerializer.Serialize(message));
                response?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error fetching global config: {ex.Message}");
                _webView.PostMessage(JsonSerializer.Serialize(new { type = "globalConfigLoaded", config = new { } }));
            }
        }

        private async Task HandleRequestIndexingStatusAsync()
        {
            _webView.PostMessage(JsonSerializer.Serialize(new { type = "indexingStatusLoaded", status = "off" }));
        }

        private async Task HandleRequestKiloEmbeddingModelsAsync()
        {
            _webView.PostMessage(JsonSerializer.Serialize(new { type = "kiloEmbeddingModelsLoaded", models = new object[0] }));
        }

        private async Task HandleRequestImageModelsAsync()
        {
            _webView.PostMessage(JsonSerializer.Serialize(new { type = "imageModelsLoaded", models = new object[0] }));
        }

        private async Task HandleRequestSandboxStatusAsync(JsonElement? payload)
        {
            var sessionID = payload?.TryGetProperty("sessionID", out var sid) == true ? sid.GetString() : "";
            _webView.PostMessage(JsonSerializer.Serialize(new { type = "sandboxStatusLoaded", sessionID, available = false, reason = "Not implemented" }));
        }

        private async Task HandleRequestSandboxDefaultAsync(JsonElement? payload)
        {
            _webView.PostMessage(JsonSerializer.Serialize(new { type = "sandboxDefaultLoaded", enabled = false }));
        }

        private async Task HandleSetSandboxDefaultAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: setSandboxDefault");
        }

        private async Task HandleToggleSandboxAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: toggleSandbox");
        }

        private async Task HandleDismissNotificationAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: dismissNotification");
        }

        private async Task HandleApplyWorkStyleAsync(JsonElement? payload)
        {
          if (!payload.HasValue) return;
          System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: applyWorkStyle received");
          // WorkStyle is applied client-side; just acknowledge
        }

        private async Task HandleResetReadNotificationsAsync()
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: resetReadNotifications");
        }

        private async Task HandleRequestCloudSessionsAsync(JsonElement? payload)
        {
            _webView.PostMessage(JsonSerializer.Serialize(new { type = "cloudSessionsLoaded", sessions = new object[0] }));
        }

        private async Task HandleRequestCloudSessionDataAsync(JsonElement? payload)
        {
            _webView.PostMessage(JsonSerializer.Serialize(new { type = "cloudSessionDataLoaded", session = new { } }));
        }

        private async Task HandleImportAndSendAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: importAndSend");
        }

        private async Task HandleForkSessionAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: forkSession");
        }

        private async Task HandleCompactAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: compact");
        }

        private async Task HandleEnhancePromptAsync(JsonElement? payload)
        {
            if (!payload.HasValue) return;
            var text = payload.Value.TryGetProperty("text", out var t) ? t.GetString() : "";
            var requestId = payload.Value.TryGetProperty("requestId", out var rid) ? rid.GetString() : "";
            if (string.IsNullOrEmpty(requestId)) return;
            _webView.PostMessage(JsonSerializer.Serialize(new { type = "enhancePromptResult", requestId, text = text }));
        }

        private async Task HandleSaveImageAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: saveImage");
        }

        private async Task HandleOpenSubAgentViewerAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: openSubAgentViewer");
        }

        private async Task HandleReloadAsync()
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: reload");
        }

        private void HandlePersistVariant(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: persistVariant");
        }

        private void HandlePersistRecents(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: persistRecents");
        }

        private async Task HandleToggleFavoriteAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: toggleFavorite");
        }

        // Context request handlers
        private async Task HandleRequestFileSearchAsync(JsonElement? payload)
        {
            var requestId = payload?.TryGetProperty("requestId", out var rid) == true ? rid.GetString() : "";
            _webView.PostMessage(JsonSerializer.Serialize(new { type = "fileSearchResult", requestId, files = new object[0] }));
        }

        private async Task HandleRequestSessionSearchAsync(JsonElement? payload)
        {
            var requestId = payload?.TryGetProperty("requestId", out var rid) == true ? rid.GetString() : "";
            _webView.PostMessage(JsonSerializer.Serialize(new { type = "sessionSearchResult", requestId, sessions = new object[0] }));
        }

        private async Task HandleRequestFilePickerAsync(JsonElement? payload)
        {
            var requestId = payload?.TryGetProperty("requestId", out var rid) == true ? rid.GetString() : "";
            _webView.PostMessage(JsonSerializer.Serialize(new { type = "filePickerResult", requestId, files = new object[0] }));
        }

        private async Task HandleRequestTerminalContextAsync(JsonElement? payload)
        {
            var requestId = payload?.TryGetProperty("requestId", out var rid) == true ? rid.GetString() : "";
            _webView.PostMessage(JsonSerializer.Serialize(new { type = "terminalContextResult", requestId, content = "" }));
        }

        // Settings request handlers
        private void HandleRequestBrowserSettings()
        {
            _webView.PostMessage(JsonSerializer.Serialize(new { type = "browserSettingsLoaded", settings = new { } }));
        }

        private void HandleRequestClaudeCompatSetting()
        {
            _webView.PostMessage(JsonSerializer.Serialize(new { type = "claudeCompatSettingLoaded", enabled = false }));
        }

        private void HandleRequestNotificationSettings()
        {
            _webView.PostMessage(JsonSerializer.Serialize(new { type = "notificationSettingsLoaded", settings = new { } }));
        }

        private void HandleRequestTimelineSetting()
        {
      //_webView.PostMessage(JsonSerializer.Serialize(new { type = "timelineSettingLoaded", value = 0 }));

      _webView.PostMessage(JsonSerializer.Serialize(new { type = "timelineSettingLoaded", visible = true }));
        }

        private async Task HandleRequestGitRemoteUrlAsync()
        {
            _webView.PostMessage(JsonSerializer.Serialize(new { type = "gitRemoteUrlLoaded", gitUrl = (string?)null }));
        }

        private void HandleRequestRemoteStatus()
        {
            _webView.PostMessage(JsonSerializer.Serialize(new { type = "remoteStatus", enabled = false, connected = false }));
        }

        // MCP handlers
        private async Task HandleConnectMcpAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: connectMcp");
        }

        private async Task HandleDisconnectMcpAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: disconnectMcp");
        }

        private async Task HandleAuthenticateMcpAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: authenticateMcp");
        }

        private async Task HandleRemoveMcpAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: removeMcp");
        }

        private async Task HandleRemoveSkillAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: removeSkill");
        }

        private async Task HandleRemoveAgentAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: removeAgent");
        }

        private async Task HandleFetchCustomProviderModelsAsync(JsonElement? payload)
        {
            var requestId = payload?.TryGetProperty("requestId", out var rid) == true ? rid.GetString() : "";
            _webView.PostMessage(JsonSerializer.Serialize(new { type = "customProviderModelsFetched", requestId, models = new object[0] }));
        }

        private async Task HandleProviderActionAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: providerAction");
        }

        private async Task HandleAnacondaDesktopAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: anacondaDesktop");
        }

        private async Task HandleRequestChatCompletionAsync(JsonElement? payload)
        {
            var requestId = payload?.TryGetProperty("requestId", out var rid) == true ? rid.GetString() : "";
            _webView.PostMessage(JsonSerializer.Serialize(new { type = "chatCompletionResult", requestId, text = "" }));
        }

        private void HandleChatCompletionAccepted(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: chatCompletionAccepted");
        }

        private async Task HandleToggleRemoteAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: toggleRemote");
        }

        private async Task HandleOpenMarketplacePanelAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: openMarketplacePanel");
        }

        private async Task HandleOpenKiloClawAsync()
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: openKiloClaw");
        }

        private async Task HandleOpenVSCodeSettingsAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: openVSCodeSettings");
        }

        private async Task HandleSetLanguageAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: setLanguage");
        }

        private async Task HandleSetOrganizationAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: setOrganization");
        }

        private void HandleCancelLogin()
        {
            _webView.PostMessage(JsonSerializer.Serialize(new { type = "deviceAuthCancelled" }));
        }

        private async Task HandleRevertSessionAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: revertSession");
        }

        private async Task HandleUnrevertSessionAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: unrevertSession");
        }

        private async Task HandleSyncSessionAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: syncSession");
        }

        private async Task HandleLoadSessionsAsync()
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: loadSessions");
        }

        private async Task HandleRequestSessionModelUsageAsync(JsonElement? payload)
        {
            var sessionID = payload?.TryGetProperty("sessionID", out var sid) == true ? sid.GetString() : "";
            var requestID = payload?.TryGetProperty("requestID", out var rid) == true ? rid.GetString() : "";
            _webView.PostMessage(JsonSerializer.Serialize(new { type = "sessionModelUsageLoaded", sessionID, requestID, data = new { sessionIDs = new object[0] } }));
        }

        private async Task HandlePermissionResponseAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: permissionResponse");
        }

        private async Task HandleQuestionRejectAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: questionReject");
        }

        private async Task HandleSessionCostAlertResponseAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: sessionCostAlertResponse");
        }

        private void HandleTestNotification(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: testNotification");
        }

        private async Task HandleResetAllSettingsAsync()
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: resetAllSettings");
        }

        private void HandleTelemetry(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: telemetry");
        }

        private void HandlePersistModelSelectorExpanded(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: persistModelSelectorExpanded");
        }

        private void HandleRequestModelSelectorExpanded()
        {
            _webView.PostMessage(JsonSerializer.Serialize(new { type = "modelSelectorExpandedLoaded", value = true }));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _webView.OnMessageReceived -= HandleMessageReceived;
            _connectionService.OnStateChange -= HandleStateChange;
            _connectionService.OnSseEvent -= HandleSseEvent;
        }
    }
}
