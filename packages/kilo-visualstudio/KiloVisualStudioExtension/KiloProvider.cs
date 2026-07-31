using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;

namespace KiloVisualStudioExtension
{
    public class VSProvider : IDisposable
    {
        private readonly KiloWebViewControl _webView;
        private readonly KiloConnectionService _connectionService;
        private readonly SSEHelper _sseHelper;
        private bool _isWebviewReady = false;
        private bool _disposed;
        private JsonElement? _webviewState;
        private string? _contextSessionID;
        private readonly List<Action> _readyResolvers = new List<Action>();
        private List<JsonElement>? _pendingReviewComments = null;
        private bool _promptRecoveryQueued = false;
        private Task? _promptRecovery;
        private JsonElement? _pendingKiloModel = null;
        private JsonElement? _cachedStats = null;
        private bool _cachedGitRepo = false;
        private readonly Dictionary<string, string> _sessionStatusMap = new Dictionary<string, string>();

        public VSProvider(KiloWebViewControl webView, KiloConnectionService connectionService)
        {
            _webView = webView;
            _connectionService = connectionService;
            _sseHelper = new SSEHelper(PostMessage);
            _webView.OnMessageReceived += HandleMessageReceived;
            _connectionService.OnStateChange += HandleStateChange;
            _connectionService.OnSseEvent += HandleSseEvent;
        }

        private void PostMessage(string message)
        {
            _webView.PostMessage(message);
        }

        private string? _currentSessionID
        {
            get => _sseHelper.CurrentSessionID;
            set => _sseHelper.SetCurrentSession(value);
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

                    case "setState":
                        await HandleSetStateAsync(payload);
                        break;

                    case "getState":
                        await HandleGetStateAsync();
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

                    case "settingsTabChanged":
                        HandleSettingsTabChanged(payload);
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

        private async Task HandleWebviewReadyAsync()
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: webviewReady received");
            
            // 1. Set webview ready flag
            _isWebviewReady = true;

            // 2. Clear visible task streams (VS Code: this.visibleTaskStreams.clear())
            // Note: VisibleTaskStreams is an Agent Manager feature not yet implemented in VS extension

            // 3. Flush pending Kilo model
            FlushPendingKiloModel();

            // 4. Sync webview state (equivalent to VS Code's syncWebviewState)
            await SyncWebviewStateAsync("webviewReady");

            // 5. Flush pending review comments
            FlushPendingReviewComments();

            // 6. Recover pending prompts
            RecoverPendingPrompts();

            // 7. Resolve ready resolvers (VS Code: this.readyResolvers.splice(0).forEach((r) => r()))
            ResolveReadyResolvers();

            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: webviewReady initialization complete");
        }

        private void FlushPendingKiloModel()
        {
            if (!_isWebviewReady || _pendingKiloModel == null) return;

            var pending = _pendingKiloModel;
            _pendingKiloModel = null;
            
            if (pending is JsonElement element)
            {
                var message = new { type = "selectKiloModel", modelID = element.TryGetProperty("modelID", out var modelId) ? modelId.GetString() : null, agent = element.TryGetProperty("agent", out var agent) ? agent.GetString() : null };
                _webView.PostMessage(JsonSerializer.Serialize(message));
            }
        }

        public void SelectKiloModel(string? modelID = null, string? agent = null)
        {
            if (string.IsNullOrEmpty(modelID) && string.IsNullOrEmpty(agent)) return;
            
            _pendingKiloModel = JsonSerializer.SerializeToElement(new { modelID, agent });
            FlushPendingKiloModel();
        }

        private async Task SyncWebviewStateAsync(string reason)
        {
            System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: syncWebviewState({reason})");
            
            // Check if webview is ready (should already be true at this point)
            if (!_isWebviewReady)
            {
                System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: syncWebviewState skipped (webview not ready)");
                return;
            }

            // Always push connection state first
            var connState = new { type = "connectionState", state = _connectionService.State.ToString().ToLowerInvariant() };
            _webView.PostMessage(JsonSerializer.Serialize(connState));

            // Get server info
            var serverInfo = _connectionService.GetServerInfo();
            
            // Re-send ready so the webview can recover after refresh
            if (serverInfo != null)
            {
                // Get language from VS settings (default to "en")
                var langConfig = GetVscodeLanguage();
                var extensionVersion = GetExtensionVersion();
                var readyMessage = new 
                { 
                    type = "ready",
                    serverInfo,
                    extensionVersion,
                    vscodeLanguage = langConfig,
                    languageOverride = (string?)null,
                    workspaceDirectory = Environment.CurrentDirectory
                };
                _webView.PostMessage(JsonSerializer.Serialize(readyMessage));
            }

            // If connected, fetch and push profile data
            if (_connectionService.State == ConnectionState.Connected)
            {
                // Fetch profile
                try
                {
                    var httpClient = _connectionService.GetHttpClient();
                    if (httpClient != null)
                    {
                        var profileDoc = await httpClient.GetJsonAsync("/kilo/profile");
                        JsonElement? profileData = null;
                        if (profileDoc != null && profileDoc.RootElement.TryGetProperty("profile", out var profile))
                        {
                            profileData = profile.Clone();
                        }
                        profileDoc?.Dispose();
                        
                        var profileMessage = new { type = "profileData", data = profileData };
                        _webView.PostMessage(JsonSerializer.Serialize(profileMessage));
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error fetching profile: {ex.Message}");
                }

                // Refresh session details if current session exists
                await RefreshSessionDetailsAsync();

                // Re-send cached worktree stats and git status after webview reload
                if (_cachedStats != null)
                {
                    _webView.PostMessage(JsonSerializer.Serialize(_cachedStats));
                }
                var gitStatusMessage = new { type = "gitStatus", repo = _cachedGitRepo };
                _webView.PostMessage(JsonSerializer.Serialize(gitStatusMessage));

                // Seed session status map so the Settings panel knows about already-running sessions
                // Only reconcile (reset missing busy→idle) when the map is empty
                var reconcile = _sessionStatusMap.Count == 0;
                SeedSessionStatusMap(reconcile);

                // Send remote status (no-op for now - VS extension doesn't have remote status service)
                SendRemoteStatus();
            }

            // Send saved state to webview if it exists (for webview reload recovery)
            if (_webviewState.HasValue)
            {
                var stateMessage = new { type = "setState", state = _webviewState.Value };
                _webView.PostMessage(JsonSerializer.Serialize(stateMessage));
                System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: restored state to webview");
            }

            // Signal that all initial extension data has been loaded
            // This allows webview contexts to retry their data requests if needed
            var extensionReady = new { type = "extensionDataReady" };
            _webView.PostMessage(JsonSerializer.Serialize(extensionReady));
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: extensionDataReady sent");
        }

        private async Task RefreshSessionDetailsAsync()
        {
            // Refresh details for the current session if one exists
            if (string.IsNullOrEmpty(_currentSessionID)) return;

            var httpClient = _connectionService.GetHttpClient();
            if (httpClient == null || !httpClient.IsConnected()) return;

            try
            {
                var responseDoc = await httpClient.GetJsonAsync($"/session/{_currentSessionID}");
                if (responseDoc != null)
                {
                    var root = responseDoc.RootElement.Clone();
                    responseDoc.Dispose();
                    
                    if (root.TryGetProperty("session", out var session))
                    {
                        // Send sessionUpdated message to webview with refreshed details
                        var updatedMessage = new { type = "sessionUpdated", session = session.Clone() };
                        _webView.PostMessage(JsonSerializer.Serialize(updatedMessage));
                    }
                    
                    
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error refreshing session details: {ex.Message}");
            }
        }

        private async Task SeedSessionStatusMap(bool reconcile)
        {
            // Fetch session statuses from backend for tracked sessions
            var httpClient = _connectionService.GetHttpClient();
            if (httpClient == null || !httpClient.IsConnected()) return;

            try
            {
                // Get all sessions to populate status map
                var responseDoc = await httpClient.GetJsonAsync("/session");
                if (responseDoc != null)
                {
                    var root = responseDoc.RootElement.Clone();
                    responseDoc.Dispose();
                    
                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var session in root.EnumerateArray())
                        {
                            if (session.TryGetProperty("id", out var id) && session.TryGetProperty("status", out var status))
                            {
                                var sessionID = id.GetString() ?? "";
                                var sessionStatus = status.GetString() ?? "idle";
                                
                                // Only reconcile (reset to idle) if map is empty and status is busy
                                if (reconcile && sessionStatus == "busy")
                                {
                                    _sessionStatusMap[sessionID] = "idle";
                                }
                                else if (!reconcile || !_sessionStatusMap.ContainsKey(sessionID))
                                {
                                    _sessionStatusMap[sessionID] = sessionStatus;
                                }
                            }
                        }
                    }
                    
                    
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error seeding session status map: {ex.Message}");
            }
        }

        private void SendRemoteStatus()
        {
            // VS extension doesn't have a remote status service yet
            // This is a no-op placeholder matching VS Code's pattern
        }

        public void SetCachedStats(JsonElement stats)
        {
            _cachedStats = stats;
        }

        public void SetCachedGitRepo(bool isRepo)
        {
            _cachedGitRepo = isRepo;
        }

        public void UpdateSessionStatus(string sessionID, string status)
        {
            _sessionStatusMap[sessionID] = status;
        }

        private string GetVscodeLanguage()
        {
            // TODO: Integrate with Visual Studio localization settings
            // For now, return default English
            return "en";
        }

        private string GetExtensionVersion()
        {
            // TODO: Read from extension manifest (AssemblyInfo or VSIX manifest)
            // For now, return a default version
            return "1.0.0";
        }

        private void FlushPendingReviewComments()
        {
            if (!_isWebviewReady || _pendingReviewComments == null || _pendingReviewComments.Count == 0) return;

            var pending = _pendingReviewComments;
            _pendingReviewComments = null;

            foreach (var entry in pending)
            {
                if (entry is JsonElement element)
                {
                    var autoSend = element.TryGetProperty("autoSend", out var autoSendProp) && autoSendProp.GetBoolean();
                    JsonElement? comments = null;
                    if (element.TryGetProperty("comments", out var commentsProp))
                    {
                        comments = commentsProp.Clone();
                    }
                    PostMessage(JsonSerializer.Serialize(new { type = "appendReviewComments", comments, autoSend }));
                }
            }
        }

        public async Task AppendReviewCommentsAsync(object comments, bool autoSend = false)
        {
            if (_pendingReviewComments == null)
            {
                _pendingReviewComments = new List<JsonElement>();
            }
            _pendingReviewComments.Add(JsonSerializer.SerializeToElement(new { comments, autoSend }));

            if (!_isWebviewReady)
            {
                return;
            }

            FlushPendingReviewComments();
        }

        private void RecoverPendingPrompts()
        {
            _promptRecoveryQueued = true;
            if (!_isWebviewReady) return;
            var httpClient = _connectionService.GetHttpClient();
            if (httpClient == null || !httpClient.IsConnected()) return;
            if (_promptRecovery != null) return;

            _promptRecovery = FlushPendingPromptsAsync().ContinueWith(_ =>
            {
                _promptRecovery = null;
                if (_promptRecoveryQueued && _isWebviewReady)
                {
                    RecoverPendingPrompts();
                }
            });
        }

        private async Task FlushPendingPromptsAsync()
        {
            while (_promptRecoveryQueued && _isWebviewReady)
            {
                var httpClient = _connectionService.GetHttpClient();
                if (httpClient == null || !httpClient.IsConnected()) return;
                
                _promptRecoveryQueued = false;
                
                var dirs = GetRecoveryDirectories();
                var seen = new HashSet<string>();

                // Fetch pending permissions
                foreach (var dir in dirs)
                {
                    try
                    {
                        var responseDoc = await httpClient.GetJsonAsync($"/permission?directory={Uri.EscapeDataString(dir)}");
                        if (responseDoc != null && responseDoc.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var perm in responseDoc.RootElement.EnumerateArray())
                            {
                                if (perm.TryGetProperty("id", out var id) && !seen.Contains(id.GetString() ?? ""))
                                {
                                    var requestId = id.GetString() ?? "";
                                    seen.Add(requestId);
                                    
                                    if (perm.TryGetProperty("sessionID", out var sid) && !string.IsNullOrEmpty(sid.GetString()))
                                    {
                                        var sessionID = sid.GetString()!;
                                        var permission = perm.TryGetProperty("permission", out var permProp) ? permProp.GetString() : "";
                                        JsonElement? patterns = null;
                                        if (perm.TryGetProperty("patterns", out var patternsProp))
                                        {
                                            patterns = patternsProp.Clone();
                                        }
                                        var always = perm.TryGetProperty("always", out var alwaysProp) && alwaysProp.GetBoolean();
                                        JsonElement? metadata = null;
                                        if (perm.TryGetProperty("metadata", out var metaProp))
                                        {
                                            metadata = metaProp.Clone();
                                        }
                                        var tool = perm.TryGetProperty("tool", out var toolProp) ? toolProp.GetString() : "";

                                        PostMessage(JsonSerializer.Serialize(new
                                        {
                                            type = "permissionRequest",
                                            permission = new
                                            {
                                                id = requestId,
                                                sessionID,
                                                toolName = permission,
                                                patterns,
                                                always,
                                                args = metadata,
                                                message = $"Permission required: {permission}",
                                                tool
                                            }
                                        }));
                                    }
                                }
                            }
                        }
                        responseDoc?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error fetching permissions for {dir}: {ex.Message}");
                    }
                }

                // Fetch pending questions
                foreach (var dir in dirs)
                {
                    try
                    {
                        var responseDoc = await httpClient.GetJsonAsync($"/question?directory={Uri.EscapeDataString(dir)}");
                        if (responseDoc != null && responseDoc.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var q in responseDoc.RootElement.EnumerateArray())
                            {
                                if (q.TryGetProperty("id", out var id) && !seen.Contains(id.GetString() ?? ""))
                                {
                                    var requestId = id.GetString() ?? "";
                                    seen.Add(requestId);
                                    
                                    if (q.TryGetProperty("sessionID", out var sid) && !string.IsNullOrEmpty(sid.GetString()))
                                    {
                                        var sessionID = sid.GetString()!;
                                        JsonElement? questions = null;
                                        if (q.TryGetProperty("questions", out var qProp))
                                        {
                                            questions = qProp.Clone();
                                        }
                                        var blocking = q.TryGetProperty("blocking", out var blockProp) && blockProp.GetBoolean();
                                        var tool = q.TryGetProperty("tool", out var toolProp) ? toolProp.GetString() : "";

                                        PostMessage(JsonSerializer.Serialize(new
                                        {
                                            type = "questionRequest",
                                            question = new
                                            {
                                                id = requestId,
                                                sessionID,
                                                questions,
                                                blocking,
                                                tool
                                            }
                                        }));
                                    }
                                }
                            }
                        }
                        responseDoc?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error fetching questions for {dir}: {ex.Message}");
                    }
                }

                // Fetch pending suggestions
                foreach (var dir in dirs)
                {
                    try
                    {
                        var responseDoc = await httpClient.GetJsonAsync($"/suggestion?directory={Uri.EscapeDataString(dir)}");
                        if (responseDoc != null && responseDoc.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var suggestion in responseDoc.RootElement.EnumerateArray())
                            {
                                if (suggestion.TryGetProperty("id", out var id) && !seen.Contains(id.GetString() ?? ""))
                                {
                                    seen.Add(id.GetString() ?? "");
                                    PostMessage(JsonSerializer.Serialize(new { type = "suggestionRequest", suggestion }));
                                }
                            }
                        }
                        responseDoc?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error fetching suggestions for {dir}: {ex.Message}");
                    }
                }
            }
        }

        private string[] GetRecoveryDirectories()
        {
            // Return workspace directory and any session-specific directories
            // For now, return current directory as workspace root
            return new[] { Environment.CurrentDirectory };
        }

        public Task WaitForReadyAsync()
        {
            if (_isWebviewReady)
            {
                return Task.CompletedTask;
            }
            var tcs = new TaskCompletionSource<bool>();
            _readyResolvers.Add(() => tcs.SetResult(true));
            return tcs.Task;
        }

        private void ResolveReadyResolvers()
        {
            var resolvers = _readyResolvers.ToArray();
            _readyResolvers.Clear();
            foreach (var resolver in resolvers)
            {
                resolver();
            }
        }

        private void HandleStateChange(object? sender, ConnectionStateEventArgs e)
        {
            var message = new { type = "connectionState", state = e.State.ToString().ToLowerInvariant(), errorMessage = e.ErrorMessage };
            _webView.PostMessage(JsonSerializer.Serialize(message));
        }

        private void HandleSseEvent(object? sender, SseEventReceivedEventArgs e)
        {
            _sseHelper.HandleEvent(e.EventType, e.Data);
        }

        private async Task HandleRequestProvidersAsync()
        {
            var httpClient = _connectionService.GetHttpClient();
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
                    var root = responseDoc.RootElement.Clone();
                    responseDoc.Dispose();
                    
                    // Parse "all" array and convert to dictionary
                    if (root.TryGetProperty("all", out var all) && all.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var provider in all.EnumerateArray())
                        {
                            if (provider.TryGetProperty("id", out var id))
                            {
                                providersDict[id.GetString() ?? ""] = provider.Clone();
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
            var httpClient = _connectionService.GetHttpClient();
            if (httpClient == null)
            {
                await SendEmptyAgentsAsync();
                return;
            }

            try
            {
                // Use /app/agents endpoint like VS Code does (not /experimental/tool/ids)
                var responseDoc = await httpClient.GetJsonAsync("/agent");
                var agentsList = new List<object>();
                
                if (responseDoc != null)
                {
                    var root = responseDoc.RootElement.Clone();
                    responseDoc.Dispose();
                    
                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var agent in root.EnumerateArray())
                        {
                            // Filter out hidden agents and subagent mode (matching VS Code's filterVisibleAgents)
                            if (agent.TryGetProperty("mode", out var modeProp) && modeProp.GetString() == "subagent")
                                continue;
                            if (agent.TryGetProperty("hidden", out var hiddenProp) && hiddenProp.GetBoolean())
                                continue;
                            
                            // Map agent to the subset of fields sent to webview (matching VS Code's mapAgent)
                            JsonElement? permissionElement = null;
                            if (agent.TryGetProperty("permission", out var perm))
                            {
                                permissionElement = perm.Clone();
                            }
                            
                            var mappedAgent = new
                            {
                                name = agent.TryGetProperty("name", out var name) ? name.GetString() : "",
                                //displayName = agent.TryGetProperty("displayName", out var displayName) ? displayName.GetString() : "",
                                description = agent.TryGetProperty("description", out var desc) ? desc.GetString() : "",
                                mode = agent.TryGetProperty("mode", out var m) ? m.GetString() : "",
                                native = agent.TryGetProperty("native", out var nat) && nat.ValueKind == JsonValueKind.True,
                                hidden = agent.TryGetProperty("hidden", out var h) && h.ValueKind == JsonValueKind.True,
                                color = agent.TryGetProperty("color", out var c) ? c.GetString() : "",
                                deprecated = agent.TryGetProperty("deprecated", out var d) && d.ValueKind == JsonValueKind.True,
                                permission = permissionElement,
                                model = agent.TryGetProperty("model", out var model) ? model.GetString() : ""
                            };
                            agentsList.Add(mappedAgent);
                        }
                    }
                }
                
                // Determine default agent (first visible agent, or "code" as fallback)
                string defaultAgent = "code";
                if (agentsList.Count > 0)
                {
                    var firstAgent = agentsList[0];
                    if (firstAgent is System.Text.Json.JsonElement firstElement && 
                        firstElement.TryGetProperty("name", out var nameElement))
                    {
                        defaultAgent = nameElement.GetString() ?? "code";
                    }
                }
                
                var message = new 
                { 
                    type = "agentsLoaded", 
                    agents = agentsList.ToArray(),
                    allAgents = agentsList.ToArray(),
                    defaultAgent = defaultAgent
                };
                _webView.PostMessage(JsonSerializer.Serialize(message));
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
                defaultAgent = "code"
            };
            _webView.PostMessage(JsonSerializer.Serialize(message));
        }

        private async Task HandleRequestConfigAsync()
        {
            var httpClient = _connectionService.GetHttpClient();
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
                if (responseDoc != null)
                {
                    var root = responseDoc.RootElement.Clone();
                    responseDoc.Dispose();
                    
                    if (root.TryGetProperty("config", out var c))
                        config = c.Clone();
                    if (root.TryGetProperty("features", out var f))
                        features = f.Clone();
                    
                    
                }
                var message = new { type = "configLoaded", config, features };
                _webView.PostMessage(JsonSerializer.Serialize(message));
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
            var httpClient = _connectionService.GetHttpClient();
            if (httpClient == null)
            {
                await SendEmptyMcpStatusAsync();
                return;
            }

            try
            {
                var responseDoc = await httpClient.GetJsonAsync("/mcp");
                JsonElement status = JsonDocument.Parse("{}").RootElement;
                if (responseDoc != null)
                {
                    var root = responseDoc.RootElement.Clone();
                    responseDoc.Dispose();
                    
                    if (root.TryGetProperty("status", out var s))
                        status = s.Clone();
                    
                    
                }
                var message = new { type = "mcpStatusLoaded", status };
                _webView.PostMessage(JsonSerializer.Serialize(message));
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

            // Auto-create session if none available (matching VS Code's resolveSession behavior)
            if (string.IsNullOrEmpty(sessionID))
            {
                System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: no session available, creating new session...");
                var created = await HandleCreateSessionAsyncInternal();
                if (!created)
                {
                    await SendErrorAsync("Prompt Error", "Failed to create session");
                    return;
                }
                sessionID = _currentSessionID;
                
                if (string.IsNullOrEmpty(sessionID))
                {
                    await SendErrorAsync("Prompt Error", "Failed to create session");
                    return;
                }
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
            var dir = Environment.CurrentDirectory;
            var success = await CreateSessionInternalAsync(dir);
            if (!success)
            {
                PostMessage(JsonSerializer.Serialize(new { type = "error", message = "Failed to create session" }));
            }
        }

        private async Task<bool> CreateSessionInternalAsync(string dir)
        {
            var httpClient = _connectionService.GetHttpClient();
            if (httpClient == null)
            {
                System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: cannot create session - no HTTP client");
                return false;
            }
            try
            {
                var responseDoc = await httpClient.PostJsonAsync("/session", new { directory = dir });
                if (responseDoc != null && responseDoc.RootElement.TryGetProperty("id", out var id))
                {
                    var sessionID = id.GetString() ?? "";
                    System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: session created: {sessionID}");
                    
                    // Stop current session processes (if any) - similar to VS Code's stopCurrentSessionProcesses(session.id)
                    // Process management would need to be implemented separately for Visual Studio
                    
                    // Set current session (this also tracks the session for SSE events)
                    _currentSessionID = sessionID;
                    
                    // Set context session ID - similar to VS Code's contextSessionID = session.id
                    _contextSessionID = sessionID;
                    
                    // Focus session - similar to VS Code's focusSession(session.id)
                    // For Visual Studio, this would reset any session focus tracking
                    
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
                    responseDoc?.Dispose();
                    return true;
                }
                responseDoc?.Dispose();
                System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: session creation failed - no ID in response");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error creating session: {ex.Message}");
                PostMessage(JsonSerializer.Serialize(new { type = "error", message = $"Failed to create session: {ex.Message}" }));
                return false;
            }
        }

        private async Task<bool> HandleCreateSessionAsyncInternal()
        {
            return await CreateSessionInternalAsync(Environment.CurrentDirectory);
        }

        private void HandleClearSession()
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: clearSession");
            
            // Get the current session ID before clearing
            var sessionID = _currentSessionID;
            
            // Stop current session processes (if any)
            // Note: In VS Code, this calls stopCurrentSessionProcesses() which stops background processes
            // Process management would need to be implemented separately for Visual Studio
            
            // Clear the context session ID
            _contextSessionID = null;
            
            // Clear current session (via _currentSessionID which wraps _sseHelper.CurrentSessionID)
            _currentSessionID = null;
            
            // Untrack the session so SSE events are no longer processed for it
            if (!string.IsNullOrEmpty(sessionID))
            {
                _sseHelper.UntrackSession(sessionID);
            }
            
            // Focus session (reset stream focus)
            // In VS Code, this calls focusSession() which resets the focused session
            // For Visual Studio, this would reset any session focus tracking
        }

        private async Task HandleSetStateAsync(JsonElement? payload)
        {
            if (!payload.HasValue) return;
            
            System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: setState received");
            
            if (payload.Value.TryGetProperty("state", out var state))
            {
                _webviewState = state;
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: state saved");
            }
        }

        private async Task HandleGetStateAsync()
        {
            System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: getState requested");
            
            if (_webviewState.HasValue)
            {
                var message = new { type = "setState", state = _webviewState.Value };
                _webView.PostMessage(JsonSerializer.Serialize(message));
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: state sent to webview");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: no state to send");
            }
        }

        private async Task HandleLoadMessagesAsync(JsonElement? payload)
        {
            if (!payload.HasValue) return;
            
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
                _currentSessionID = sessionID;
                _contextSessionID = sessionID;  // Also set contextSessionID like VS Code does
            }
            
            var httpClient = _connectionService.GetHttpClient();
            if (httpClient == null)
            {
                PostMessage(JsonSerializer.Serialize(new { type = "error", message = "Not connected to CLI backend", sessionID }));
                return;
            }
            
            try
            {
                var url = $"/session/{sessionID}/message?limit={limit}";
                if (!string.IsNullOrEmpty(before))
                {
                    url += $"&before={before}";
                }
                
                var responseDoc = await httpClient.GetJsonAsync(url);
                
                if (responseDoc == null) return;
                
                var items = new List<object>();
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
                
                var message = new
                {
                    type = "messagesLoaded",
                    sessionID = sessionID,
                    messages = items.ToArray(),
                    mode = mode,
                    cursor = cursorValue,
                    hasMore = hasMore
                };
                
                _webView.PostMessage(JsonSerializer.Serialize(message));
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: loaded {items.Count} messages for session {sessionID}");
                
                responseDoc.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error loading messages: {ex.Message}");
                PostMessage(JsonSerializer.Serialize(new { type = "error", message = ex.Message, sessionID }));
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
            if (httpClient == null)
            {
                PostMessage(JsonSerializer.Serialize(new { type = "error", message = "Not connected to CLI backend", sessionID }));
                return;
            }
            try
            {
                // Stop session processes before deleting (similar to VS Code's stopSessionProcesses)
                // Process management would need to be implemented separately for Visual Studio
                
                await httpClient.PostJsonAsync($"/session/delete", new { sessionID });
                System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: session deleted");
                
                // If deleting the current session, clear session state (similar to VS Code)
                if (_currentSessionID == sessionID)
                {
                    _contextSessionID = null;
                    _currentSessionID = null;
                    // Untrack the deleted session
                    _sseHelper.UntrackSession(sessionID);
                    // Focus session with undefined (similar to VS Code's focusSession(undefined))
                }
                
                // Notify webview of deletion (similar to VS Code's sessionDeleted message)
                PostMessage(JsonSerializer.Serialize(new { type = "sessionDeleted", sessionID }));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error deleting session: {ex.Message}");
                PostMessage(JsonSerializer.Serialize(new { type = "error", message = $"Failed to delete session: {ex.Message}", sessionID }));
            }
        }

        private async Task HandleRenameSessionAsync(JsonElement? payload)
        {
            if (!payload.HasValue) return;
            var sessionID = payload.Value.TryGetProperty("sessionID", out var sid) ? sid.GetString() : "";
            var title = payload.Value.TryGetProperty("title", out var t) ? t.GetString() : "";
            if (string.IsNullOrEmpty(sessionID)) return;
            var httpClient = _connectionService.GetHttpClient();
            if (httpClient == null)
            {
                PostMessage(JsonSerializer.Serialize(new { type = "error", message = "Not connected to CLI backend" }));
                return;
            }
            try
            {
                await httpClient.PostJsonAsync($"/session/rename", new { sessionID, title });
                System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: session renamed");
                
                // If renaming the current session, update it (similar to VS Code's setCurrentSession)
                if (_currentSessionID == sessionID)
                {
                    // Send sessionUpdated message with updated title (similar to VS Code)
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
                    PostMessage(JsonSerializer.Serialize(sessionUpdated));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error renaming session: {ex.Message}");
                PostMessage(JsonSerializer.Serialize(new { type = "error", message = $"Failed to rename session: {ex.Message}" }));
            }
        }

        private async Task HandleAbortAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: abort");
            
            // Extract session ID from payload or use current session
            var sessionID = payload.HasValue && payload.Value.TryGetProperty("sessionID", out var sid) && !string.IsNullOrEmpty(sid.GetString())
                ? sid.GetString()
                : _currentSessionID;
            
            if (string.IsNullOrEmpty(sessionID))
            {
                System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: abort - no session ID available");
                return;
            }
            
            // In VS Code, this calls stopSession() via the SDK to abort the running task
            // For now, we log - actual abort would require SDK integration
            System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: aborting session {sessionID}");
            
            // Set session status to idle
            var statusMessage = new { type = "sessionStatus", sessionID = sessionID, status = "idle" };
            _webView.PostMessage(JsonSerializer.Serialize(statusMessage));
            
            // Flush the stream for this session
            var turnClosedMessage = new { type = "sessionTurnClosed", sessionID = sessionID, reason = "interrupted" };
            _webView.PostMessage(JsonSerializer.Serialize(turnClosedMessage));
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
            // VS extension uses the same webview as sidebar - navigate to settings tab
            string? tab = null;
            if (payload.HasValue && payload.Value.TryGetProperty("tab", out var tabProp))
            {
                tab = tabProp.GetString();
            }
            // Send navigate message to webview to open settings panel
            var navigateMsg = new { type = "navigate", view = "settings", tab };
            _webView.PostMessage(JsonSerializer.Serialize(navigateMsg));
        }

        private void HandleSettingsTabChanged(JsonElement? payload)
        {
            // Extension receives this notification from webview when tab changes
            // No action needed - the webview manages its own tab state
            if (payload.HasValue && payload.Value.TryGetProperty("tab", out var tabProp))
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: settingsTabChanged - tab={tabProp.GetString()}");
            }
        }

        private async Task HandleOpenConfigFileAsync(JsonElement? payload)
        {
            if (!payload.HasValue) return;
            
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: openConfigFile");
            
            string scope = "global";
            if (payload.Value.TryGetProperty("scope", out var scopeProp))
            {
                scope = scopeProp.GetString() ?? "global";
            }
            
            // For Visual Studio, we'll open the config file using System.Diagnostics.Process
            // The CLI backend handles the actual config file location
            try
            {
                var httpClient = _connectionService.GetHttpClient();
                if (httpClient == null) return;
                
                // Request config file path from backend
                var url = $"/config/file?scope={scope}";
                var responseDoc = await httpClient.GetJsonAsync(url);
                
                if (responseDoc != null && responseDoc.RootElement.TryGetProperty("path", out var pathProp))
                {
                    string filePath = pathProp.GetString() ?? "";
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        // Open file in default editor
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = filePath,
                            UseShellExecute = true
                        });
                    }
                }
                responseDoc?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error opening config file: {ex.Message}");
            }
        }

        private async Task HandleUpdateSettingAsync(JsonElement? payload)
        {
            if (!payload.HasValue) return;
            
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: updateSetting");
            
            var httpClient = _connectionService.GetHttpClient();
            if (httpClient == null) return;
            
            try
            {
                await httpClient.PostJsonAsync("/config/update", payload.Value);
                System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: setting updated");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error updating setting: {ex.Message}");
            }
        }

        private async Task HandleUpdateConfigAsync(JsonElement? payload)
        {
            if (!payload.HasValue) return;

            var httpClient = _connectionService.GetHttpClient();
            if (httpClient == null || !_connectionService.State.Equals(ConnectionState.Connected))
            {
                _webView.PostMessage(JsonSerializer.Serialize(new { type = "configUpdateFailed", message = "Not connected to CLI backend" }));
                return;
            }

            var config = payload.Value;
            JsonElement partial = JsonDocument.Parse("{}").RootElement;
            JsonElement project = JsonDocument.Parse("{}").RootElement;
            JsonElement globalUnset = JsonDocument.Parse("[]").RootElement;
            JsonElement projectUnset = JsonDocument.Parse("[]").RootElement;

            if (config.TryGetProperty("config", out var partialProp))
                partial = partialProp.Clone();
            if (config.TryGetProperty("projectConfig", out var projectProp))
                project = projectProp.Clone();
            if (config.TryGetProperty("globalUnset", out var globalUnsetProp))
                globalUnset = globalUnsetProp.Clone();
            if (config.TryGetProperty("projectUnset", out var projectUnsetProp))
                projectUnset = projectUnsetProp.Clone();

            var refreshProviders = partial.TryGetProperty("provider", out _) ||
                                   partial.TryGetProperty("disabled_providers", out _) ||
                                   partial.TryGetProperty("enabled_providers", out _) ||
                                   partial.TryGetProperty("hide_prompt_training_models", out _);
            var refreshAgents = partial.TryGetProperty("default_agent", out _) ||
                                partial.TryGetProperty("agent", out _) ||
                                project.TryGetProperty("default_agent", out _) ||
                                project.TryGetProperty("agent", out _);

            var hasGlobal = !IsJsonObjectEmpty(partial) || !IsJsonArrayEmpty(globalUnset);
            var hasProject = !IsJsonObjectEmpty(project) || !IsJsonArrayEmpty(projectUnset);

            var dir = Environment.CurrentDirectory;

            try
            {
                if (hasGlobal)
                {
                    var globalPayload = new
                    {
                        scope = "global",
                        set = partial,
                        unset = globalUnset,
                        directory = dir
                    };
                    await httpClient.PostJsonAsync("/config/overlay-update", globalPayload);
                }
                if (hasProject)
                {
                    var projectPayload = new
                    {
                        scope = "project",
                        set = project,
                        unset = projectUnset,
                        directory = dir
                    };
                    await httpClient.PostJsonAsync("/config/overlay-update", projectPayload);
                }
            }
            catch (Exception ex)
            {
                PostConfigFailure(ex);
                return;
            }

            try
            {
                var mergedTask = httpClient.GetJsonAsync($"/config?directory={Uri.EscapeDataString(dir)}");
                var globalTask = httpClient.GetJsonAsync("/config/global");
                var overlayTask = httpClient.GetJsonAsync($"/config/overlay?directory={Uri.EscapeDataString(dir)}&scope=project");

                await Task.WhenAll(mergedTask, globalTask, overlayTask);

                JsonElement merged = JsonDocument.Parse("{}").RootElement;
                JsonElement globalConfig = JsonDocument.Parse("{}").RootElement;
                JsonElement overlay = JsonDocument.Parse("{}").RootElement;

                if (mergedTask.Result != null)
                {
                    var root = mergedTask.Result.RootElement.Clone();
                    if (root.TryGetProperty("config", out var c))
                        merged = c.Clone();
                    mergedTask.Result.Dispose();
                }
                if (globalTask.Result != null)
                {
                    var root = globalTask.Result.RootElement.Clone();
                    if (root.TryGetProperty("config", out var c))
                        globalConfig = c.Clone();
                    globalTask.Result.Dispose();
                }
                if (overlayTask.Result != null)
                {
                    var root = overlayTask.Result.RootElement.Clone();
                    if (root.TryGetProperty("overlay", out var o) && o.TryGetProperty("project", out var p))
                        overlay = p.Clone();
                    overlayTask.Result.Dispose();
                }

                var settings = new
                {
                    maxCost = 0,
                    languageCommitMessage = GetCommitMessageLanguage()
                };

                var features = GetConfigFeatures(merged);

                var cachedConfig = new
                {
                    type = "configLoaded",
                    config = merged,
                    globalConfig = globalConfig,
                    projectConfig = overlay,
                    settings = settings,
                    features = features
                };

                _webView.PostMessage(JsonSerializer.Serialize(new
                {
                    type = "configUpdated",
                    config = merged,
                    globalConfig = globalConfig,
                    projectConfig = overlay,
                    settings = settings,
                    features = features
                }));

                if (refreshProviders)
                    await HandleRequestProvidersAsync();
                if (refreshAgents)
                    await HandleRequestAgentsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: Config write succeeded but post-write refresh failed: {ex.Message}");
                JsonElement patch;
                if (!partial.TryGetProperty("indexing", out _) && !project.TryGetProperty("indexing", out _))
                {
                    patch = MergeJsonObjects(partial, project);
                }
                else
                {
                    var indexing = MergeJsonObjects(
                        partial.TryGetProperty("indexing", out var pi) ? pi : JsonDocument.Parse("{}").RootElement,
                        project.TryGetProperty("indexing", out var pj) ? pj : JsonDocument.Parse("{}").RootElement
                    );
                    patch = MergeJsonWithIndexing(partial, project, indexing);
                }

                var cached = GetCachedConfig();
                var features = GetCachedConfigFeatures();
                var optimistic = MergeJsonWithPatch(cached, patch);

                var settings = new
                {
                    maxCost = 0,
                    languageCommitMessage = GetCommitMessageLanguage()
                };

                _webView.PostMessage(JsonSerializer.Serialize(new
                {
                    type = "configUpdated",
                    config = optimistic,
                    globalConfig = GetCachedGlobalConfig(),
                    settings = settings,
                    features = features
                }));
            }
        }

        private bool IsJsonObjectEmpty(JsonElement obj)
        {
            return obj.ValueKind == JsonValueKind.Object && !obj.EnumerateObject().Any();
        }

        private bool IsJsonArrayEmpty(JsonElement arr)
        {
            return arr.ValueKind == JsonValueKind.Array && !arr.EnumerateArray().Any();
        }

        private JsonElement MergeJsonObjects(JsonElement a, JsonElement b)
        {
            var writer = new JsonDocumentBuilder();
            if (a.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in a.EnumerateObject())
                    writer.Add(prop.Name, prop.Value.Clone());
            }
            if (b.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in b.EnumerateObject())
                    writer.Add(prop.Name, prop.Value.Clone());
            }
            return writer.Build();
        }

        private JsonElement MergeJsonWithIndexing(JsonElement partial, JsonElement project, JsonElement indexing)
        {
            var writer = new JsonDocumentBuilder();
            if (partial.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in partial.EnumerateObject())
                {
                    if (!prop.Name.Equals("indexing"))
                        writer.Add(prop.Name, prop.Value.Clone());
                }
            }
            if (project.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in project.EnumerateObject())
                {
                    if (!prop.Name.Equals("indexing"))
                        writer.Add(prop.Name, prop.Value.Clone());
                }
            }
            writer.Add("indexing", indexing);
            return writer.Build();
        }

        private JsonElement MergeJsonWithPatch(JsonElement? cached, JsonElement patch)
        {
            if (!cached.HasValue)
                return patch;

            var writer = new JsonDocumentBuilder();
            var cachedObj = cached.Value;
            if (cachedObj.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in cachedObj.EnumerateObject())
                    writer.Add(prop.Name, prop.Value.Clone());
            }
            if (patch.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in patch.EnumerateObject())
                    writer.Add(prop.Name, prop.Value.Clone());
            }
            return writer.Build();
        }

        private void PostConfigFailure(Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: Failed to update config: {error.Message}");
            _webView.PostMessage(JsonSerializer.Serialize(new
            {
                type = "configUpdateFailed",
                message = error.Message ?? "Failed to update config"
            }));
        }

        private string GetCommitMessageLanguage()
        {
            var config = GetConfig();
            if (config.HasValue && config.Value.TryGetProperty("language_commit_message", out var lang))
                return lang.GetString() ?? "en";
            return "en";
        }

        private JsonElement GetConfigFeatures(JsonElement config)
        {
            var writer = new JsonDocumentBuilder();
            if (config.TryGetProperty("providers", out var providers))
                writer.Add("providers", providers.Clone());
            if (config.TryGetProperty("indexing", out var indexing))
                writer.Add("indexing", indexing.Clone());
            return writer.Build();
        }

        private JsonElement? GetCachedConfig()
        {
            return null;
        }

        private JsonElement GetCachedConfigFeatures()
        {
            return JsonDocument.Parse("{}").RootElement;
        }

        private JsonElement? GetCachedGlobalConfig()
        {
            return null;
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
            var settings = new 
            { 
                browserPath = (string?)null,
                launchArgs = new string[0],
                enabled = false
            };
            _webView.PostMessage(JsonSerializer.Serialize(new { type = "browserSettingsLoaded", settings }));
        }

        private void HandleRequestClaudeCompatSetting()
        {
            var config = GetConfig();
            var enabled = config?.TryGetProperty("experimental", out var exp) == true 
                && exp.TryGetProperty("claude_compat", out var compat) 
                && compat.ValueKind == JsonValueKind.True;
            _webView.PostMessage(JsonSerializer.Serialize(new { type = "claudeCompatSettingLoaded", enabled }));
        }

        private void HandleRequestNotificationSettings()
        {
            var config = GetConfig();
            var settings = new 
            {
                playSound = config?.TryGetProperty("notifications", out var notif) == true 
                    && notif.TryGetProperty("play_sound", out var sound) 
                    && sound.ValueKind == JsonValueKind.True,
                showPopup = config?.TryGetProperty("notifications", out var notif2) == true 
                    && notif2.TryGetProperty("show_popup", out var popup) 
                    && popup.ValueKind == JsonValueKind.True
            };
            _webView.PostMessage(JsonSerializer.Serialize(new { type = "notificationSettingsLoaded", settings }));
        }

        private void HandleRequestTimelineSetting()
        {
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
            if (!payload.HasValue) return;
            
            string? language = null;
            if (payload.Value.TryGetProperty("language", out var langProp))
            {
                language = langProp.GetString();
            }
            
            if (string.IsNullOrEmpty(language)) return;
            
            var httpClient = _connectionService.GetHttpClient();
            if (httpClient == null) return;
            
            try
            {
                await httpClient.PostJsonAsync("/config/update", new { experimental = new { language } });
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: language set to {language}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error setting language: {ex.Message}");
            }
        }

        private async Task HandleSetOrganizationAsync(JsonElement? payload)
        {
            if (!payload.HasValue) return;
            
            string? organizationId = null;
            if (payload.Value.TryGetProperty("organizationId", out var orgProp))
            {
                organizationId = orgProp.GetString();
            }
            
            var httpClient = _connectionService.GetHttpClient();
            if (httpClient == null) return;
            
            try
            {
                await httpClient.PostJsonAsync("/kilo/set-organization", new { organizationId });
                System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: organization set");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error setting organization: {ex.Message}");
            }
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
            var httpClient = _connectionService.GetHttpClient();
            if (httpClient == null) return;
            
            try
            {
                await httpClient.PostAsync("/config/reset", new { });
                System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: all settings reset");
                
                // Re-fetch config after reset
                await HandleRequestConfigAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error resetting settings: {ex.Message}");
            }
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
            _webView.PostMessage(JsonSerializer.Serialize(new { type = "modelSelectorExpandedLoaded", value = false }));
        }

        private JsonElement? GetConfig()
        {
            var httpClient = _connectionService.GetHttpClient();
            if (httpClient == null) return null;
            
            try
            {
                var responseDoc = httpClient.GetJsonAsync("/config").Result;
                if (responseDoc != null && responseDoc.RootElement.TryGetProperty("config", out var config))
                {
                    return config.Clone();
                }
                responseDoc?.Dispose();
            }
            catch
            {
                // Ignore errors
            }
            return null;
        }

        public void SetProjectDirectory(string? projectDirectory)
        {
            // Store project directory for use in future operations
            // Currently a no-op as we use Environment.CurrentDirectory
            System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: SetProjectDirectory({projectDirectory})");
        }

        public async Task PostMessageAsync(object message)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var json = System.Text.Json.JsonSerializer.Serialize(message);
            _webView.PostMessage(json);
        }

        public void SetRemoteService(object? service)
        {
            // VS extension doesn't have a remote status service yet
            // This is a no-op placeholder matching VS Code's pattern
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

    internal class JsonDocumentBuilder
    {
        private readonly Dictionary<string, JsonElement> _properties = new Dictionary<string, JsonElement>();

        public void Add(string name, JsonElement value)
        {
            _properties[name] = value;
        }

        public JsonElement Build()
        {
            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream);
            writer.WriteStartObject();
            foreach (var prop in _properties)
            {
                writer.WritePropertyName(prop.Key);
                prop.Value.WriteTo(writer);
            }
            writer.WriteEndObject();
            writer.Flush();
            stream.Position = 0;
            using var doc = JsonDocument.Parse(stream);
            return doc.RootElement.Clone();
        }
    }
}
