using System;
using System.IO;
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
                var messageStr = e.TryGetWebMessageAsString();
                var message = JsonSerializer.Deserialize<WebviewMessage>(messageStr);
                if (message == null) return;

                System.Diagnostics.Debug.WriteLine($"[Kilo] WebView: received message type={message.Type}");

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                OnMessageReceived?.Invoke(this, new WebViewMessageEventArgs(message.Type, message.Payload));

                switch (message.Type)
                {
                    case "prompt":
                        await HandlePrompt(message.Payload);
                        break;
                    case "permission/reply":
                        await HandlePermissionReply(message.Payload);
                        break;
                    case "question/reply":
                        await HandleQuestionReply(message.Payload);
                        break;
                    case "config/read":
                        await HandleConfigRead(message.Payload);
                        break;
                    case "config/write":
                        await HandleConfigWrite(message.Payload);
                        break;
                    default:
                        System.Diagnostics.Debug.WriteLine($"[Kilo] WebView: unknown message type {message.Type}");
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] WebView2 message error: {ex.Message}");
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

        private async Task HandleConfigRead(JsonElement? payload)
        {
            if (_connectionService?.GetHttpClient() is not { } httpClient) return;

            var config = await httpClient.GetJsonAsync<object>("/config");
            if (config != null)
            {
                var response = new { type = "config/read", payload = config };
                PostMessage(JsonSerializer.Serialize(response));
            }
        }

        private async Task HandleConfigWrite(JsonElement? payload)
        {
            if (_connectionService?.GetHttpClient() is not { } httpClient) return;
            if (!payload.HasValue) return;

            await httpClient.PostAsync("/config", new { config = payload.Value });
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
                CoreWebView2.PostWebMessageAsString(json);
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
