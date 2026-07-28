using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.VisualStudio.Shell;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Controls;

namespace KiloVisualStudioExtension
{
    public class WebviewMessage
    {
        public string Type { get; set; } = "";
        public JsonElement? Payload { get; set; }
    }

    public class WebViewMessageEventArgs : EventArgs
    {
        public string Type { get; }
        public JsonElement? Payload { get; }

        public WebViewMessageEventArgs(string type, JsonElement? payload)
        {
            Type = type;
            Payload = payload;
        }
    }

    public class KiloWebViewControl : WebView2, IDisposable
    {
        private bool _isInitialized;
        private string? _baseUrl;
        private KiloConnectionService? _connectionService;

        public event EventHandler<WebViewMessageEventArgs>? OnMessageReceived;

        public KiloWebViewControl()
        {
            Width = double.NaN;
            Height = double.NaN;
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
            AllowExternalDrop = false;
        }

        public void SetConnectionService(KiloConnectionService service)
        {
            _connectionService = service;
            _connectionService.OnSseEvent += SseClient_OnSseEvent;
        }

        public async Task InitializeAsync()
        {
            if (_isInitialized)
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var userDataFolder = Path.Combine(
                    Path.GetTempPath(),
                    "KiloWebView",
                    Guid.NewGuid().ToString());
                
                var envOptions = new CoreWebView2EnvironmentOptions();
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder, envOptions);
                await this.EnsureCoreWebView2Async(env);
                
                CoreWebView2.Settings.IsScriptEnabled = true;
                CoreWebView2.Settings.IsWebMessageEnabled = true;
                CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
                CoreWebView2.Settings.IsStatusBarEnabled = false;

                CoreWebView2.WebMessageReceived += OnWebMessageReceived;
                CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
                CoreWebView2.SourceChanged += CoreWebView2_SourceChanged;

                var assemblyDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
                var webviewPath = Path.Combine(assemblyDir, "webview", "index.html");
                
                System.Diagnostics.Debug.WriteLine($"[Kilo] Assembly dir: {assemblyDir}");
                System.Diagnostics.Debug.WriteLine($"[Kilo] Webview path: {webviewPath}");
                System.Diagnostics.Debug.WriteLine($"[Kilo] File exists: {File.Exists(webviewPath)}");
                if (File.Exists(webviewPath))
                {
                    var webviewDir = Path.Combine(assemblyDir, "webview");
                    var webviewUri = new Uri(webviewDir).AbsoluteUri;
                    System.Diagnostics.Debug.WriteLine($"[Kilo] Loading webview from {webviewUri}");
                    CoreWebView2.Navigate(webviewUri + "/index.html");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[Kilo] Webview index.html not found, listing webview dir:");
                    var webviewDir = Path.Combine(assemblyDir, "webview");
                    if (Directory.Exists(webviewDir))
                    {
                        foreach (var f in Directory.GetFiles(webviewDir))
                        {
                            System.Diagnostics.Debug.WriteLine($"  - {Path.GetFileName(f)}");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"  Webview dir does not exist: {webviewDir}");
                    }
                    CoreWebView2.NavigateToString("<html><body><h2>Webview Not Available</h2><p>index.html not found</p></body></html>");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] WebView2 initialization error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine(ex.StackTrace);
            }

            _isInitialized = true;
        }

        private void CoreWebView2_SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[Kilo] Source changed to: {CoreWebView2?.Source}");
        }

        private void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[Kilo] Navigation completed: Success={e.IsSuccess}, WebErrorStatus={e.WebErrorStatus}");
            if (!e.IsSuccess)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] Navigation failed: {e.WebErrorStatus}");
            }
        }

        private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                // Use WebMessageAsJson instead of TryGetWebMessageAsString (which throws)
                var messageStr = e.WebMessageAsJson;
                System.Diagnostics.Debug.WriteLine($"[Kilo] WebView2: Received raw message: {messageStr}");
                
                // Try to parse the message
                JsonElement? payload = null;
                string messageType = "";
                
                try
                {
                    var json = JsonDocument.Parse(messageStr);
                    if (json.RootElement.TryGetProperty("type", out var typeProp))
                    {
                        messageType = typeProp.GetString() ?? "";
                    }
                    if (json.RootElement.TryGetProperty("payload", out var payloadProp))
                    {
                        payload = payloadProp.Clone();
                    }
                }
                catch (Exception parseEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[Kilo] WebView2: JSON parse error: {parseEx.Message}");
                    System.Diagnostics.Debug.WriteLine($"[Kilo] WebView2: Raw message was: {messageStr}");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[Kilo] WebView: received message type={messageType}");

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                OnMessageReceived?.Invoke(this, new WebViewMessageEventArgs(messageType, payload));

                switch (messageType)
                {
                    case "prompt":
                        await HandlePrompt(payload);
                        break;
                    case "permission/reply":
                        await HandlePermissionReply(payload);
                        break;
                    case "question/reply":
                        await HandleQuestionReply(payload);
                        break;
                    case "config/read":
                        await HandleRequestConfig(payload);
                        break;
                    case "config/write":
                        await HandleConfigWrite(payload);
                        break;
                    case "webviewReady":
                        await HandleWebViewReady(payload);
                        break;
                    case "requestProviders":
                        await HandleRequestProviders(payload);
                        break;
                    case "requestAgents":
                        await HandleRequestAgents(payload);
                        break;
                    case "requestConfig":
                        await HandleRequestConfig(payload);
                        break;
                    case "retryConnection":
                        await HandleRetryConnection(payload);
                        break;
                    case "action":
                        await HandleAction(payload);
                        break;
                    case "setState":
                        await HandleSetState(payload);
                        break;
                    case "webviewFocusChanged":
                        System.Diagnostics.Debug.WriteLine("[Kilo] WebView: focus changed");
                        break;
                    case "requestModelSelectorExpanded":
                        await HandleRequestModelSelectorExpanded(payload);
                        break;
                    case "requestAutocompleteSettings":
                        await HandleRequestAutocompleteSettings(payload);
                        break;
                    case "requestIndexingSettings":
                        await HandleRequestIndexingSettings(payload);
                        break;
                    case "requestChatSettings":
                        await HandleRequestChatSettings(payload);
                        break;
                    case "requestWorkStyle":
                        await HandleRequestWorkStyle(payload);
                        break;
                    case "requestKiloEmbeddingModels":
                        await HandleRequestKiloEmbeddingModels(payload);
                        break;
                    case "requestImageModels":
                        await HandleRequestImageModels(payload);
                        break;
                    case "requestMcpStatus":
                        await HandleRequestMcpStatus(payload);
                        break;
                    case "requestVariants":
                        await HandleRequestVariants(payload);
                        break;
                    case "requestModelSelections":
                        await HandleRequestModelSelections(payload);
                        break;
                    case "requestRecents":
                        await HandleRequestRecents(payload);
                        break;
                    case "requestFavorites":
                        await HandleRequestFavorites(payload);
                        break;
                    case "requestNotifications":
                        await HandleRequestNotifications(payload);
                        break;
                    default:
                        System.Diagnostics.Debug.WriteLine($"[Kilo] WebView: message type {messageType} (no handler yet)");
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] WebView2 message error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine(ex.StackTrace);
            }
        }

        private async Task HandlePrompt(JsonElement? payload)
        {
            if (_connectionService?.GetHttpClient() is not { } httpClient) return;
            if (!payload.HasValue) return;

            var promptText = payload.Value.TryGetProperty("text", out var textProp) ? textProp.GetString() : null;
            if (string.IsNullOrEmpty(promptText)) return;

            var body = new { prompt = promptText };
            await httpClient.PostAsync("/session/prompt", body);
        }

        private async Task HandlePermissionReply(JsonElement? payload)
        {
            if (_connectionService?.GetHttpClient() is not { } httpClient) return;
            if (!payload.HasValue) return;

            var requestID = payload.Value.TryGetProperty("requestID", out var idProp) ? idProp.GetString() : null;
            var reply = payload.Value.TryGetProperty("reply", out var replyProp) ? replyProp.GetString() : null;
            if (string.IsNullOrEmpty(requestID) || string.IsNullOrEmpty(reply)) return;

            var body = new { reply };
            await httpClient.PostAsync($"/permission/reply/{requestID}", body);
        }

        private async Task HandleQuestionReply(JsonElement? payload)
        {
            if (_connectionService?.GetHttpClient() is not { } httpClient) return;
            if (!payload.HasValue) return;

            var requestID = payload.Value.TryGetProperty("requestID", out var idProp) ? idProp.GetString() : null;
            var answer = payload.Value.TryGetProperty("answer", out var answerProp) ? answerProp.GetString() : null;
            if (string.IsNullOrEmpty(requestID) || string.IsNullOrEmpty(answer)) return;

            var body = new { answer };
            await httpClient.PostAsync($"/question/reply/{requestID}", body);
        }

        private async Task HandleRequestConfig(JsonElement? payload)
        {
            if (_connectionService?.GetHttpClient() is not { } httpClient) return;

            try
            {
                var config = await httpClient.GetJsonAsync<JsonElement>("/config");
                if (config.ValueKind != JsonValueKind.Null)
                {
                    var response = new { type = "configLoaded", config = config, globalConfig = config, projectConfig = config, features = new { indexing = false, sandboxControls = false } };
                    PostMessage(JsonSerializer.Serialize(response));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] HandleRequestConfig error: {ex.Message}");
                var error = new { type = "error", message = ex.Message, code = "config_error" };
                PostMessage(JsonSerializer.Serialize(error));
            }
        }

        private async Task HandleConfigWrite(JsonElement? payload)
        {
            if (_connectionService?.GetHttpClient() is not { } httpClient) return;
            if (!payload.HasValue) return;

            await httpClient.PostAsync("/config", new { config = payload.Value });
        }

        private async Task HandleRequestProviders(JsonElement? payload)
        {
            if (_connectionService?.GetHttpClient() is not { } httpClient) return;

            try
            {
                var providersObj = await httpClient.GetJsonAsync<JsonElement>("/provider");
                var providersDict = new Dictionary<string, object>();
                var connectedList = new List<string>();
                var failedList = new List<string>();
                var defaultsDict = new Dictionary<string, string>();

                if (providersObj.TryGetProperty("all", out var allProp) && allProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var provider in allProp.EnumerateArray())
                    {
                        if (provider.TryGetProperty("id", out var idProp) && provider.TryGetProperty("name", out var nameProp))
                        {
                            var providerId = idProp.GetString() ?? "";
                            var providerName = nameProp.GetString() ?? "";
                            var modelsDict = new Dictionary<string, object>();

                            if (provider.TryGetProperty("models", out var modelsProp) && modelsProp.ValueKind == JsonValueKind.Object)
                            {
                                foreach (var modelEntry in modelsProp.EnumerateObject())
                                {
                                    modelsDict[modelEntry.Name] = new { id = modelEntry.Name };
                                }
                            }

                            providersDict[providerId] = new
                            {
                                id = providerId,
                                name = providerName,
                                models = modelsDict,
                                source = provider.TryGetProperty("source", out var srcProp) ? srcProp.GetString() : "env",
                                env = provider.TryGetProperty("env", out var envProp) ? (object)envProp : new object[0],
                                metadata = provider.TryGetProperty("metadata", out var metaProp) ? (object)metaProp : new { }
                            };
                        }
                    }
                }

                if (providersObj.TryGetProperty("connected", out var connectedProp) && connectedProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var conn in connectedProp.EnumerateArray())
                    {
                        if (conn.ValueKind == JsonValueKind.String) connectedList.Add(conn.GetString() ?? "");
                    }
                }

                if (providersObj.TryGetProperty("failed", out var failedProp) && failedProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var fail in failedProp.EnumerateArray())
                    {
                        if (fail.ValueKind == JsonValueKind.String) failedList.Add(fail.GetString() ?? "");
                    }
                }

                if (providersObj.TryGetProperty("default", out var defaultsProp) && defaultsProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (var entry in defaultsProp.EnumerateObject())
                    {
                        if (entry.Value.ValueKind == JsonValueKind.String)
                        {
                            defaultsDict[entry.Name] = entry.Value.GetString() ?? "";
                        }
                    }
                }

                var defaultSelection = new { providerID = "", modelID = "" };
                if (defaultsDict.Count > 0)
                {
                    var firstKey = defaultsDict.Keys.First();
                    defaultSelection = new { providerID = firstKey, modelID = defaultsDict[firstKey] };
                }

                var response = new
                {
                    type = "providersLoaded",
                    providers = providersDict,
                    connected = connectedList.ToArray(),
                    defaults = defaultsDict,
                    defaultSelection = defaultSelection,
                    authMethods = new object[0],
                    authStates = new Dictionary<string, string>()
                };
                PostMessage(JsonSerializer.Serialize(response));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] HandleRequestProviders error: {ex.Message}");
                var error = new { type = "error", message = ex.Message, code = "providers_error" };
                PostMessage(JsonSerializer.Serialize(error));
            }
        }

        private async Task HandleRequestAgents(JsonElement? payload)
        {
            if (_connectionService?.GetHttpClient() is not { } httpClient) return;

            try
            {
                var agents = await httpClient.GetJsonAsync<string[]>("/experimental/tool/ids");
                if (agents != null && agents.Length > 0)
                {
                    var agentList = agents.Select(a => new
                    {
                        name = a,
                        displayName = "",
                        description = "",
                        mode = "primary" as string,
                        native = false,
                        hidden = false,
                        deprecated = false
                    }).ToArray();
                    var response = new
                    {
                        type = "agentsLoaded",
                        agents = agentList,
                        allAgents = agentList,
                        defaultAgent = "default"
                    };
                    PostMessage(JsonSerializer.Serialize(response));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] HandleRequestAgents error: {ex.Message}");
                var error = new { type = "error", message = ex.Message, code = "agents_error" };
                PostMessage(JsonSerializer.Serialize(error));
            }
        }

        private async Task HandleWebViewReady(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] WebView: webview ready, triggering initialization");
            
            if (_connectionService?.GetHttpClient() is not { } httpClient) return;

            try
            {
                var config = await httpClient.GetJsonAsync<JsonElement>("/config");
                if (config.ValueKind != JsonValueKind.Null)
                {
                    var response = new { type = "configLoaded", config = config, globalConfig = config, projectConfig = config, features = new { indexing = false, sandboxControls = false } };
                    PostMessage(JsonSerializer.Serialize(response));
                }

                var providersResult = await httpClient.GetJsonAsync<JsonElement>("/provider");
                if (providersResult.ValueKind != JsonValueKind.Null)
                {
                    var providersDict = new Dictionary<string, object>();
                    var connectedList = new List<string>();

                    if (providersResult.TryGetProperty("all", out var allProp) && allProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var provider in allProp.EnumerateArray())
                        {
                            if (provider.TryGetProperty("id", out var idProp) && provider.TryGetProperty("name", out var nameProp))
                            {
                                var providerId = idProp.GetString() ?? "";
                                var providerName = nameProp.GetString() ?? "";
                                var modelsDict = new Dictionary<string, object>();

                                if (provider.TryGetProperty("models", out var modelsProp) && modelsProp.ValueKind == JsonValueKind.Object)
                                {
                                    foreach (var modelEntry in modelsProp.EnumerateObject())
                                    {
                                        modelsDict[modelEntry.Name] = new { id = modelEntry.Name };
                                    }
                                }

                                providersDict[providerId] = new
                                {
                                    id = providerId,
                                    name = providerName,
                                    models = modelsDict,
                                    source = provider.TryGetProperty("source", out var srcProp) ? srcProp.GetString() : "env"
                                };
                            }
                        }
                    }

                    if (providersResult.TryGetProperty("connected", out var connectedProp) && connectedProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var conn in connectedProp.EnumerateArray())
                        {
                            if (conn.ValueKind == JsonValueKind.String) connectedList.Add(conn.GetString() ?? "");
                        }
                    }

                    var providersResponse = new
                    {
                        type = "providersLoaded",
                        providers = providersDict,
                        connected = connectedList.ToArray(),
                        defaults = new Dictionary<string, string>(),
                        defaultSelection = new { providerID = "", modelID = "" },
                        authMethods = new object[0],
                        authStates = new Dictionary<string, string>()
                    };
                    PostMessage(JsonSerializer.Serialize(providersResponse));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] HandleWebViewReady error: {ex.Message}");
            }
        }

        private async Task HandleRetryConnection(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] WebView: retry connection requested");
            if (_connectionService != null)
            {
                try
                {
                    await _connectionService.ConnectAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Kilo] HandleRetryConnection error: {ex.Message}");
                }
            }
        }

        private async Task HandleAction(JsonElement? payload)
        {
            if (_connectionService?.GetHttpClient() is not { } httpClient) return;
            if (!payload.HasValue) return;

            try
            {
                if (payload.Value.TryGetProperty("action", out var actionProp))
                {
                    var action = actionProp.GetString();
                    System.Diagnostics.Debug.WriteLine($"[Kilo] HandleAction: {action}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] HandleAction error: {ex.Message}");
            }
        }

        private async Task HandleSetState(JsonElement? payload)
        {
            if (!payload.HasValue) return;
            System.Diagnostics.Debug.WriteLine($"[Kilo] HandleSetState: state received");
        }

        private async Task HandleRequestModelSelectorExpanded(JsonElement? payload)
        {
            if (_connectionService?.GetHttpClient() is not { } httpClient) return;

            try
            {
                //var providers = await httpClient.GetJsonAsync<object>("/provider");
                //if (providers != null)
                //{
                    var response = new { type = "modelSelectorExpandedLoaded", value = false };
                    PostMessage(JsonSerializer.Serialize(response));
                //}
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] HandleRequestModelSelectorExpanded error: {ex.Message}");
                var error = new { type = "error", message = ex.Message, code = "model_selector_error" };
                PostMessage(JsonSerializer.Serialize(error));
            }
        }

        private async Task HandleRequestAutocompleteSettings(JsonElement? payload)
        {
            if (_connectionService?.GetHttpClient() is not { } httpClient) return;

            try
            {
                var settings = await httpClient.GetJsonAsync<object>("/config");
                if (settings != null)
                {
                    var response = new { type = "autocompleteSettingsLoaded", settings = new { enableAutoTrigger = true, enableSmartInlineTaskKeybinding = false, enableChatAutocomplete = false, provider = (string)null, model = (string)null } };
                    PostMessage(JsonSerializer.Serialize(response));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] HandleRequestAutocompleteSettings error: {ex.Message}");
                var error = new { type = "error", message = ex.Message, code = "autocomplete_settings_error" };
                PostMessage(JsonSerializer.Serialize(error));
            }
        }

        private async Task HandleRequestIndexingSettings(JsonElement? payload)
        {
            if (_connectionService?.GetHttpClient() is not { } httpClient) return;

            try
            {
                var settings = await httpClient.GetJsonAsync<object>("/config");
                if (settings != null)
                {
                    var response = new { type = "indexingSettingsLoaded", settings = new { showButtonWhenDisabled = true } };
                    PostMessage(JsonSerializer.Serialize(response));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] HandleRequestIndexingSettings error: {ex.Message}");
                var error = new { type = "error", message = ex.Message, code = "indexing_settings_error" };
                PostMessage(JsonSerializer.Serialize(error));
            }
        }

        private async Task HandleRequestChatSettings(JsonElement? payload)
        {
            if (_connectionService?.GetHttpClient() is not { } httpClient) return;

            try
            {
                var settings = await httpClient.GetJsonAsync<object>("/config");
                if (settings != null)
                {
                    var response = new { type = "chatSettingsLoaded", settings = new { shiftTabCyclesVariant = false } };
                    PostMessage(JsonSerializer.Serialize(response));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] HandleRequestChatSettings error: {ex.Message}");
                var error = new { type = "error", message = ex.Message, code = "chat_settings_error" };
                PostMessage(JsonSerializer.Serialize(error));
            }
        }

        private async Task HandleRequestWorkStyle(JsonElement? payload)
        {
            if (_connectionService?.GetHttpClient() is not { } httpClient) return;

            try
            {
                var settings = await httpClient.GetJsonAsync<object>("/config");
                if (settings != null)
                {
                    var response = new { type = "workStyleLoaded", style = new { mode = "ask" as string, autoApprove = new object[0], deniedAlways = new object[0] } };
                    PostMessage(JsonSerializer.Serialize(response));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] HandleRequestWorkStyle error: {ex.Message}");
                var error = new { type = "error", message = ex.Message, code = "work_style_error" };
                PostMessage(JsonSerializer.Serialize(error));
            }
        }

        private async Task HandleRequestKiloEmbeddingModels(JsonElement? payload)
        {
            if (_connectionService?.GetHttpClient() is not { } httpClient) return;

            try
            {
                var models = await httpClient.GetJsonAsync<object>("/provider");
                if (models != null)
                {
                    var response = new { type = "kiloEmbeddingModelsLoaded", catalog = new { defaultModel = "", models = new object[0], aliases = new object[0] } };
                    PostMessage(JsonSerializer.Serialize(response));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] HandleRequestKiloEmbeddingModels error: {ex.Message}");
                var error = new { type = "error", message = ex.Message, code = "embedding_models_error" };
                PostMessage(JsonSerializer.Serialize(error));
            }
        }

        private async Task HandleRequestImageModels(JsonElement? payload)
        {
            if (_connectionService?.GetHttpClient() is not { } httpClient) return;

            try
            {
                var models = await httpClient.GetJsonAsync<object>("/provider");
                if (models != null)
                {
                    var response = new { type = "imageModelsLoaded", models = new object[0] };
                    PostMessage(JsonSerializer.Serialize(response));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] HandleRequestImageModels error: {ex.Message}");
                var error = new { type = "error", message = ex.Message, code = "image_models_error" };
                PostMessage(JsonSerializer.Serialize(error));
            }
        }

        private async Task HandleRequestMcpStatus(JsonElement? payload)
        {
            if (_connectionService?.GetHttpClient() is not { } httpClient) return;

            try
            {
                var status = await httpClient.GetJsonAsync<JsonElement>("/mcp");
                var statusDict = new Dictionary<string, object>();
                if (status.ValueKind == JsonValueKind.Object)
                {
                    foreach (var entry in status.EnumerateObject())
                    {
                        statusDict[entry.Name] = new
                        {
                            status = "connected" as string,
                            error = (string?)null
                        };
                    }
                }
                var response = new { type = "mcpStatusLoaded", status = statusDict };
                PostMessage(JsonSerializer.Serialize(response));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] HandleRequestMcpStatus error: {ex.Message}");
                var error = new { type = "error", message = ex.Message, code = "mcp_status_error" };
                PostMessage(JsonSerializer.Serialize(error));
            }
        }

        private async Task HandleRequestVariants(JsonElement? payload)
        {
            if (_connectionService?.GetHttpClient() is not { } httpClient) return;

            try
            {
                var variantsDict = new Dictionary<string, string>();
                var response = new { type = "variantsLoaded", variants = variantsDict };
                PostMessage(JsonSerializer.Serialize(response));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] HandleRequestVariants error: {ex.Message}");
                var error = new { type = "error", message = ex.Message, code = "variants_error" };
                PostMessage(JsonSerializer.Serialize(error));
            }
        }

        private async Task HandleRequestModelSelections(JsonElement? payload)
        {
            if (_connectionService?.GetHttpClient() is not { } httpClient) return;

            try
            {
                var selectionsDict = new Dictionary<string, object>();
                var response = new { type = "modelSelectionsLoaded", selections = selectionsDict };
                PostMessage(JsonSerializer.Serialize(response));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] HandleRequestModelSelections error: {ex.Message}");
                var error = new { type = "error", message = ex.Message, code = "model_selections_error" };
                PostMessage(JsonSerializer.Serialize(error));
            }
        }

        private async Task HandleRequestRecents(JsonElement? payload)
        {
            if (_connectionService?.GetHttpClient() is not { } httpClient) return;

            try
            {
                var sessions = await httpClient.GetJsonAsync<JsonElement[]>("/session");
                var recentsList = new List<object>();
                
                if (sessions != null)
                {
                    foreach (var session in sessions)
                    {
                        if (session.ValueKind == JsonValueKind.Object)
                        {
                            var id = session.TryGetProperty("id", out var idProp) ? idProp.GetString() : "";
                            var title = session.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : "";
                            var updated = session.TryGetProperty("updated", out var updatedProp) ? updatedProp.GetInt64() : 0;
                            
                            recentsList.Add(new { id, title, updated });
                        }
                    }
                }
                
                var response = new { type = "recentsLoaded", recents = recentsList.ToArray() };
                PostMessage(JsonSerializer.Serialize(response));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] HandleRequestRecents error: {ex.Message}");
                var error = new { type = "error", message = ex.Message, code = "recents_error" };
                PostMessage(JsonSerializer.Serialize(error));
            }
        }

        private async Task HandleRequestFavorites(JsonElement? payload)
        {
            if (_connectionService?.GetHttpClient() is not { } httpClient) return;

            try
            {
                var favoritesList = new List<object>();
                var response = new { type = "favoritesLoaded", favorites = favoritesList.ToArray() };
                PostMessage(JsonSerializer.Serialize(response));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] HandleRequestFavorites error: {ex.Message}");
                var error = new { type = "error", message = ex.Message, code = "favorites_error" };
                PostMessage(JsonSerializer.Serialize(error));
            }
        }

        private async Task HandleRequestNotifications(JsonElement? payload)
        {
            if (_connectionService?.GetHttpClient() is not { } httpClient) return;

            try
            {
                var notificationsList = new List<object>();
                var dismissedIds = new List<string>();
                var response = new { type = "notificationsLoaded", notifications = notificationsList.ToArray(), dismissedIds = dismissedIds.ToArray() };
                PostMessage(JsonSerializer.Serialize(response));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] HandleRequestNotifications error: {ex.Message}");
                var error = new { type = "error", message = ex.Message, code = "notifications_error" };
                PostMessage(JsonSerializer.Serialize(error));
            }
        }

        private void SseClient_OnSseEvent(object? sender, SseEventReceivedEventArgs e)
        {
            if (!ThreadHelper.CheckAccess())
            {
                ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    SendSseEventToWebview(e.EventType, e.Data);
                });
                return;
            }

            SendSseEventToWebview(e.EventType, e.Data);
        }

        private void SendSseEventToWebview(string eventType, string data)
        {
            if (_isInitialized && CoreWebView2 != null)
            {
                var message = new { type = "sse", payload = new { eventType = eventType, data } };
                var json = JsonSerializer.Serialize(message);
                try
                {
                    CoreWebView2.PostWebMessageAsString(json);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Kilo] WebView2 post SSE error: {ex.Message}");
                }
            }
        }

        public void PostMessage(string message)
        {
            if (_isInitialized && CoreWebView2 != null)
            {
                try
                {
                    CoreWebView2.PostWebMessageAsString(message);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Kilo] WebView2 post message error: {ex.Message}");
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_isInitialized && CoreWebView2 != null)
                {
                    CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                }
                if (_connectionService != null)
                {
                    _connectionService.OnSseEvent -= SseClient_OnSseEvent;
                }
            }
            base.Dispose(disposing);
        }
    }
}
