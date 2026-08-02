using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using VSLangProj110;
using KiloVisualStudioExtension.Services;

namespace KiloVisualStudioExtension
{
    /// <summary>
    /// Main provider class that handles communication between the webview and the Kilo backend.
    /// Message handling logic is delegated to specialized handler services to keep this class
    /// under 1500 lines, matching the VS Code pattern where handlers are extracted into separate modules.
    /// 
    /// Handler services:
    /// - SessionHandlerService: session create/delete/rename/loadMessages
    /// - AuthHandlerService: login/logout/refreshProfile
    /// - ConfigHandlerService: requestConfig/updateSetting/updateConfig
    /// - ProviderRequestService: requestProviders
    /// - AgentRequestService: requestAgents
    /// - StateManagementService: setState/getState
    /// - InteractionHandlerService: prompt/permission/reply/question/reply
    /// - SessionControlHandlerService: abort/sendMessage
    /// - UiHandlerService: openSettingsPanel/settingsTabChanged
    /// </summary>
    public class VSProvider : IDisposable
    {
        protected readonly KiloWebViewControl _webView;
        protected readonly KiloConnectionService _connectionService;
        protected readonly SSEHelper _sseHelper;
        
        private readonly SessionHandlerService _sessionHandler;
        private readonly AuthHandlerService _authHandler;
        private readonly ConfigHandlerService _configHandler;
        private readonly ProviderRequestService _providerRequestHandler;
        private readonly AgentRequestService _agentRequestHandler;
        private readonly StateManagementService _stateManagementHandler;
        private readonly McpHandlerService _mcpHandler;
        private readonly NotificationHandlerService _notificationHandler;
        private readonly ModelHandlerService _modelHandler;
        private readonly SettingsHandlerService _settingsHandler;
        private readonly MiscRequestHandlerService _miscRequestHandler;
        private readonly InteractionHandlerService _interactionHandler;
        private readonly SessionControlHandlerService _sessionControlHandler;
        private readonly UiHandlerService _uiHandler;
        
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

        /// <summary>
        /// Constructor for factory creation (webView may be null initially).
        /// Initializes all handler services.
        /// </summary>
        public VSProvider(KiloWebViewControl? webView, KiloConnectionService connectionService)
        {
            _webView = webView!;
            _connectionService = connectionService;
            _sseHelper = new SSEHelper(PostMessage);
            
            _sessionHandler = new SessionHandlerService(this);
            _authHandler = new AuthHandlerService(this);
            _configHandler = new ConfigHandlerService(this);
            _providerRequestHandler = new ProviderRequestService(this);
            _agentRequestHandler = new AgentRequestService(this);
            _stateManagementHandler = new StateManagementService(this);
            _mcpHandler = new McpHandlerService(this);
            _notificationHandler = new NotificationHandlerService(this);
            _modelHandler = new ModelHandlerService(this);
            _settingsHandler = new SettingsHandlerService(this);
            _miscRequestHandler = new MiscRequestHandlerService(this);
            _interactionHandler = new InteractionHandlerService(this);
            _sessionControlHandler = new SessionControlHandlerService(this);
            _uiHandler = new UiHandlerService(this);
            
            if (webView != null)
            {
                webView.OnMessageReceived += HandleMessageReceived;
            }
            _connectionService.OnStateChange += HandleStateChange;
            _connectionService.OnSseEvent += HandleSseEvent;
        }

        #region Internal Helper Methods for Handler Services

        internal void PostMessage(string message)
        {
            _webView.PostMessage(message);
        }

        internal HttpClientWrapper? GetHttpClient()
        {
            return _connectionService.GetHttpClient();
        }

        internal bool IsConnected()
        {
            var httpClient = _connectionService.GetHttpClient();
            return httpClient != null && httpClient.IsConnected();
        }

        internal async Task SendErrorAsync(string title, string message)
        {
            var error = new { type = "error", title, message };
            _webView.PostMessage(JsonSerializer.Serialize(error));
            await Task.CompletedTask;
        }

        internal async Task SendProfileDataAsync(JsonElement profile)
        {
            var msg = new { type = "profileData", data = profile };
            _webView.PostMessage(JsonSerializer.Serialize(msg));
            await Task.CompletedTask;
        }

        internal async Task SendConfigLoadedAsync(JsonElement config, JsonElement features)
        {
            var msg = new { type = "configLoaded", config, features };
            _webView.PostMessage(JsonSerializer.Serialize(msg));
            await Task.CompletedTask;
        }

        internal async Task SendMcpStatusAsync(JsonElement status)
        {
            var msg = new { type = "mcpStatusLoaded", status };
            _webView.PostMessage(JsonSerializer.Serialize(msg));
            await Task.CompletedTask;
        }

        internal async Task SendNotificationsAsync(object[] notifications)
        {
            var msg = new { type = "notificationsLoaded", notifications };
            _webView.PostMessage(JsonSerializer.Serialize(msg));
            await Task.CompletedTask;
        }

        internal async Task SendKiloEmbeddingModelsAsync(object[] models)
        {
            var msg = new { type = "kiloEmbeddingModelsLoaded", models };
            _webView.PostMessage(JsonSerializer.Serialize(msg));
            await Task.CompletedTask;
        }

        internal async Task SendImageModelsAsync(object[] models)
        {
            var msg = new { type = "imageModelsLoaded", models };
            _webView.PostMessage(JsonSerializer.Serialize(msg));
            await Task.CompletedTask;
        }

        internal async Task SendSkillsAsync(object[] skills)
        {
            var msg = new { type = "skillsLoaded", skills };
            _webView.PostMessage(JsonSerializer.Serialize(msg));
            await Task.CompletedTask;
        }

        internal async Task SendCommandsAsync(object[] commands)
        {
            var msg = new { type = "commandsLoaded", commands };
            _webView.PostMessage(JsonSerializer.Serialize(msg));
            await Task.CompletedTask;
        }

        internal async Task SendGlobalConfigAsync(JsonElement config)
        {
            var msg = new { type = "globalConfigLoaded", config };
            _webView.PostMessage(JsonSerializer.Serialize(msg));
            await Task.CompletedTask;
        }

        internal async Task SendIndexingStatusAsync(JsonElement status)
        {
            var msg = new { type = "indexingStatusLoaded", status };
            _webView.PostMessage(JsonSerializer.Serialize(msg));
            await Task.CompletedTask;
        }

        internal async Task SendWorkStyleLoadedAsync(object style)
        {
            var msg = new { type = "workStyleLoaded", style };
            _webView.PostMessage(JsonSerializer.Serialize(msg));
            await Task.CompletedTask;
        }

        internal async Task SendSessionCreatedAsync(JsonDocument response)
        {
            if (response != null && response.RootElement.TryGetProperty("session", out var session))
            {
                var msg = new { type = "sessionCreated", session = session.Clone() };
                _webView.PostMessage(JsonSerializer.Serialize(msg));
            }
            await Task.CompletedTask;
        }

        internal async Task SendSessionDeletedAsync(string sessionID)
        {
            var msg = new { type = "sessionDeleted", sessionID };
            _webView.PostMessage(JsonSerializer.Serialize(msg));
            await Task.CompletedTask;
        }

        internal async Task SendSessionUpdatedAsync(JsonDocument response)
        {
            if (response != null && response.RootElement.TryGetProperty("session", out var session))
            {
                var msg = new { type = "sessionUpdated", session = session.Clone() };
                _webView.PostMessage(JsonSerializer.Serialize(msg));
            }
            await Task.CompletedTask;
        }

        internal async Task SendMessageDeletedAsync(string sessionID, string messageID)
        {
            var msg = new { type = "messageRemoved", sessionID, messageID };
            _webView.PostMessage(JsonSerializer.Serialize(msg));
            await Task.CompletedTask;
        }

        internal void ClearCurrentSession()
        {
            _currentSessionID = null;
            _contextSessionID = null;
        }

        internal void RemoveTrackedSession(string sessionID)
        {
            _sseHelper.UntrackSession(sessionID);
        }

        internal void TrackSession(string sessionID)
        {
            _sseHelper.TrackSession(sessionID);
        }

        internal bool IsSessionTracked(string sessionID)
        {
            return _sseHelper.IsSessionTracked(sessionID);
        }

        internal void UntrackSession(string sessionID)
        {
            _sseHelper.UntrackSession(sessionID);
        }

        internal void FocusSession(string? sessionID)
        {
            _webView.PostMessage(JsonSerializer.Serialize(new { type = "focusSession", sessionID = sessionID ?? "" }));
        }

        internal void StopCurrentSessionProcesses(string? next)
        {
            var sid = _contextSessionID ?? _currentSessionID;
            if (string.IsNullOrEmpty(sid) || sid == next) return;
            System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: stopping processes for session {sid}");
        }

        internal void DropSessionStream(string sessionID)
        {
            System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: dropping stream for session {sessionID}");
        }

        internal void FlushSessionStream(string sessionID)
        {
            System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: flushing stream for session {sessionID}");
        }

        internal void RecoverPendingPrompts()
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

        internal string? GetCurrentSessionID()
        {
            return _currentSessionID;
        }

        internal void SetCurrentSessionID(string? sessionID)
        {
            _currentSessionID = sessionID;
        }

        internal void SetContextSessionID(string? sessionID)
        {
            _contextSessionID = sessionID;
        }

        internal async Task StoreStateAsync(JsonElement state)
        {
            _webviewState = state;
            await Task.CompletedTask;
        }

        internal async Task<JsonElement?> GetStoredStateAsync()
        {
            return _webviewState;
        }

        internal async Task<bool> CreateSessionInternalAsync()
        {
            return await CreateSessionInternalAsync(System.Environment.CurrentDirectory);
        }

        internal async Task HandlePromptAsync(JsonElement? payload)
        {
            await _interactionHandler.HandlePromptAsync(payload);
        }

        #endregion

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
                        await _providerRequestHandler.HandleRequestProvidersAsync();
                        break;

                    case "requestAgents":
                        await _agentRequestHandler.HandleRequestAgentsAsync();
                        break;

                    case "requestConfig":
                        await _configHandler.HandleRequestConfigAsync(payload);
                        break;

                    case "requestMcpStatus":
                        await _mcpHandler.HandleRequestMcpStatusAsync(payload);
                        break;

                    case "requestRecents":
                        _miscRequestHandler.HandleRequestRecents(payload);
                        break;

                    case "requestFavorites":
                        _miscRequestHandler.HandleRequestFavorites(payload);
                        break;

                    case "requestVariants":
                        _miscRequestHandler.HandleRequestVariants(payload);
                        break;

                    case "requestNotifications":
                        await _notificationHandler.HandleRequestNotificationsAsync(payload);
                        break;

                    case "requestModelSelections":
                        _modelHandler.HandleRequestModelSelections(payload);
                        break;

                    case "requestIndexingSettings":
                        _settingsHandler.HandleRequestIndexingSettings(payload);
                        break;

                    case "requestChatSettings":
                        _settingsHandler.HandleRequestChatSettings(payload);
                        break;

                    case "requestThroughputSetting":
                        _settingsHandler.HandleRequestThroughputSetting(payload);
                        break;

                    case "requestAutocompleteSettings":
                        _settingsHandler.HandleRequestAutocompleteSettings(payload);
                        break;

                    case "requestWorkStyle":
                        await _settingsHandler.HandleRequestWorkStyleAsync(payload);
                        break;

                    case "retryConnection":
                        System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: retryConnection requested");
                        await _connectionService.ConnectAsync();
                        break;

                    case "prompt":
                        await _interactionHandler.HandlePromptAsync(payload);
                        break;

                    case "permission/reply":
                        await _interactionHandler.HandlePermissionReplyAsync(payload);
                        break;

                    case "question/reply":
                        await _interactionHandler.HandleQuestionReplyAsync(payload);
                        break;

                    case "createSession":
                        await _sessionHandler.HandleCreateSessionAsync(payload);
                        break;

                    case "clearSession":
                        _sessionHandler.HandleClearSession(payload);
                        break;

                    case "setState":
                        await _stateManagementHandler.HandleSetStateAsync(payload);
                        break;

                    case "getState":
                        await _stateManagementHandler.HandleGetStateAsync();
                        break;

                    case "loadMessages":
                        await _sessionHandler.HandleLoadMessagesAsync(payload);
                        break;

                    case "deleteMessage":
                        await _sessionHandler.HandleDeleteMessageAsync(payload);
                        break;

                    case "deleteSession":
                        await _sessionHandler.HandleDeleteSessionAsync(payload);
                        break;

                    case "renameSession":
                        await _sessionHandler.HandleRenameSessionAsync(payload);
                        break;

                    case "abort":
                        await _sessionControlHandler.HandleAbortAsync(payload);
                        break;

                    case "sendMessage":
                        await _sessionControlHandler.HandleSendMessageAsync(payload);
                        break;

                    case "login":
                        await _authHandler.HandleLoginAsync(payload);
                        break;

                    case "logout":
                        await _authHandler.HandleLogoutAsync(payload);
                        break;

                    case "refreshProfile":
                        await _authHandler.HandleRefreshProfileAsync(payload);
                        break;

                    case "openSettingsPanel":
                        await _uiHandler.HandleOpenSettingsPanelAsync(payload);
                        break;

                    case "openConfigFile":
                        await _configHandler.HandleOpenConfigFileAsync(payload);
                        break;

                    case "updateSetting":
                        await _configHandler.HandleUpdateSettingAsync(payload);
                        break;

                    case "updateConfig":
                        await _configHandler.HandleUpdateConfigAsync(payload);
                        break;

                    case "requestSkills":
                        await _miscRequestHandler.HandleRequestSkillsAsync(payload);
                        break;

                    case "requestCommands":
                        await _miscRequestHandler.HandleRequestCommandsAsync(payload);
                        break;

                    case "requestGlobalConfig":
                        await _miscRequestHandler.HandleRequestGlobalConfigAsync(payload);
                        break;

                    case "requestIndexingStatus":
                        await _settingsHandler.HandleRequestIndexingStatusAsync(payload);
                        break;

                    case "requestKiloEmbeddingModels":
                        await _modelHandler.HandleRequestKiloEmbeddingModelsAsync(payload);
                        break;

                    case "requestImageModels":
                        await _modelHandler.HandleRequestImageModelsAsync(payload);
                        break;

                    case "settingsTabChanged":
                        _uiHandler.HandleSettingsTabChanged(payload);
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

        protected virtual async Task HandleWebviewReadyAsync()
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: webviewReady received");
            _isWebviewReady = true;
            FlushPendingKiloModel();
            await SyncWebviewStateAsync("webviewReady");
            FlushPendingReviewComments();
            RecoverPendingPrompts();
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
            
            if (!_isWebviewReady)
            {
                System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: syncWebviewState skipped (webview not ready)");
                return;
            }

            var connState = new { type = "connectionState", state = _connectionService.State.ToString().ToLowerInvariant() };
            _webView.PostMessage(JsonSerializer.Serialize(connState));

            var serverInfo = _connectionService.GetServerInfo();
            
            if (serverInfo != null)
            {
                var langConfig = "en";
                var extensionVersion = "1.0.0";
                var readyMessage = new 
                { 
                    type = "ready",
                    serverInfo,
                    extensionVersion,
                    vscodeLanguage = langConfig,
                    languageOverride = (string?)null,
                    workspaceDirectory = System.Environment.CurrentDirectory
                };
                _webView.PostMessage(JsonSerializer.Serialize(readyMessage));
            }

            if (_connectionService.State == ConnectionState.Connected)
            {
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

                await RefreshSessionDetailsAsync();

                if (_cachedStats != null)
                {
                    _webView.PostMessage(JsonSerializer.Serialize(_cachedStats));
                }
                var gitStatusMessage = new { type = "gitStatus", repo = _cachedGitRepo };
                _webView.PostMessage(JsonSerializer.Serialize(gitStatusMessage));

                var reconcile = _sessionStatusMap.Count == 0;
                await SeedSessionStatusMapAsync(reconcile);

                SendRemoteStatus();
            }

            if (_webviewState.HasValue)
            {
                var stateMessage = new { type = "setState", state = _webviewState.Value };
                _webView.PostMessage(JsonSerializer.Serialize(stateMessage));
                System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: restored state to webview");
            }

            var extensionReady = new { type = "extensionDataReady" };
            _webView.PostMessage(JsonSerializer.Serialize(extensionReady));
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: extensionDataReady sent");
        }

        private async Task RefreshSessionDetailsAsync()
        {
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

        private async Task SeedSessionStatusMapAsync(bool reconcile)
        {
            var httpClient = _connectionService.GetHttpClient();
            if (httpClient == null || !httpClient.IsConnected()) return;

            try
            {
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

        private async Task FlushPendingPromptsAsync()
        {
            while (_promptRecoveryQueued && _isWebviewReady)
            {
                var httpClient = _connectionService.GetHttpClient();
                if (httpClient == null || !httpClient.IsConnected()) return;
                
                _promptRecoveryQueued = false;
                
                var dirs = new[] { System.Environment.CurrentDirectory };
                var seen = new HashSet<string>();

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
                    
                    _currentSessionID = sessionID;
                    _contextSessionID = sessionID;
                    
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

        #region Dispose

        /// <summary>
        /// Disposes of all resources used by the VSProvider.
        /// Unsubscribes from events and disposes handler services.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _webView.OnMessageReceived -= HandleMessageReceived;
            _connectionService.OnStateChange -= HandleStateChange;
            _connectionService.OnSseEvent -= HandleSseEvent;
            
            _sessionHandler?.Dispose();
            _authHandler?.Dispose();
            _configHandler?.Dispose();
            _providerRequestHandler?.Dispose();
            _agentRequestHandler?.Dispose();
            _stateManagementHandler?.Dispose();
            _mcpHandler?.Dispose();
            _notificationHandler?.Dispose();
            _modelHandler?.Dispose();
            _settingsHandler?.Dispose();
            _miscRequestHandler?.Dispose();
            _interactionHandler?.Dispose();
            _sessionControlHandler?.Dispose();
            _uiHandler?.Dispose();
        }

        #endregion
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
