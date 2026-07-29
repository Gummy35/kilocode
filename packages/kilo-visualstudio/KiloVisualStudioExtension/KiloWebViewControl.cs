using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.VisualStudio.Shell;
using System.Collections.Generic;

namespace KiloVisualStudioExtension
{
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
      service.OnSseEvent += SseClient_OnSseEvent;
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
          System.Diagnostics.Debug.WriteLine("[Kilo] Webview index.html not found");
          CoreWebView2.NavigateToString("<html><body><h2>Webview Not Available</h2></body></html>");
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] WebView2 initialization error: {ex.Message}");
      }

      _isInitialized = true;
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
      try
      {
        var messageStr = e.WebMessageAsJson;
        System.Diagnostics.Debug.WriteLine($"[Kilo] WebView2: Received message: {messageStr}");

        JsonElement? payload = null;
        string messageType = "";

        try
        {
          var json = JsonDocument.Parse(messageStr);
          if (json.RootElement.TryGetProperty("type", out var typeProp))
          {
            messageType = typeProp.GetString() ?? "";
            payload = json.RootElement.Clone();
          }
        }
        catch (Exception parseEx)
        {
          System.Diagnostics.Debug.WriteLine($"[Kilo] WebView2: JSON parse error: {parseEx.Message}");
          return;
        }

        System.Diagnostics.Debug.WriteLine($"[Kilo] WebView: received message type={messageType}");

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        OnMessageReceived?.Invoke(this, new WebViewMessageEventArgs(messageType, payload));
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] WebView2 message error: {ex.Message}");
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
        try
        {
          // Parse the SSE data
          var jsonData = JsonSerializer.Deserialize<JsonElement>(data);
          
          // Check if data has a payload field (Kilo's event wrapper format)
          string messageType;
          JsonElement? payloadData = null;
          
          if (jsonData.TryGetProperty("payload", out var payloadProp) && payloadProp.ValueKind == JsonValueKind.Object)
          {
            // Kilo event format: {"payload":{"type":"...","id":"...","properties":{...}}}
            payloadData = payloadProp;
            if (payloadProp.TryGetProperty("type", out var typeProp))
            {
              var payloadType = typeProp.GetString() ?? "";
              messageType = MapEventType(payloadType);
            }
            else
            {
              messageType = "sse";
            }
          }
          else
          {
            // Direct event format: use eventType from SSE
            messageType = MapEventType(eventType);
          }
          
          // For known event types, forward the data with the mapped type
          // For unknown types, use the SSE envelope format
          object message;
          if (messageType == "sse")
          {
            message = new { type = "sse", payload = new { eventType = eventType, data } };
          }
          else if (payloadData.HasValue)
          {
            // Use the payload data directly with the mapped type
            var messageObj = new Dictionary<string, object>();
            messageObj["type"] = messageType;
            
            foreach (var prop in payloadData.Value.EnumerateObject())
            {
              if (prop.Name != "type") // Skip the type field, we already set it
              {
                messageObj[prop.Name] = prop.Value.Clone();
              }
            }
            
            message = messageObj;
          }
          else
          {
            // Forward original data with mapped type
            var messageObj = new Dictionary<string, object>();
            messageObj["type"] = messageType;
            
            if (jsonData.ValueKind == JsonValueKind.Object)
            {
              foreach (var prop in jsonData.EnumerateObject())
              {
                messageObj[prop.Name] = prop.Value.Clone();
              }
            }
            
            message = messageObj;
          }
          
          var json = JsonSerializer.Serialize(message);
          System.Diagnostics.Debug.WriteLine($"[Kilo] SSE forwarding: {eventType} -> {messageType}");
          CoreWebView2.PostWebMessageAsString(json);
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"[Kilo] WebView2 post SSE error: {ex.Message}");
        }
      }
    }
    
    private string MapEventType(string eventType)
    {
      return eventType switch
      {
        "session.status" => "sessionStatus",
        "session.created" => "sessionCreated",
        "session.updated" => "sessionUpdated",
        "session.deleted" => "sessionDeleted",
        "message.updated" => "messageCreated",
        "message.removed" => "messageRemoved",
        "message.part.updated" => "partUpdated",
        "message.part.removed" => "partRemoved",
        "message.part.delta" => "partUpdated",
        "session.turn.closed" => "sessionTurnClosed",
        "session.error" => "sessionError",
        "permission.asked" => "permissionRequest",
        "permission.replied" => "permissionResolved",
        "question.asked" => "questionRequest",
        "question.resolved" => "questionResolved",
        "question.error" => "questionError",
        "suggestion.asked" => "suggestionRequest",
        "suggestion.resolved" => "suggestionResolved",
        "suggestion.error" => "suggestionError",
        "todo.updated" => "todoUpdated",
        "server.connected" => "serverConnected", // Will be handled specially
        "server.heartbeat" => null, // Ignore heartbeats
        _ => "sse" // Fallback for unknown event types
      };
    }

    public void PostMessage(string message)
    {
      if (_isInitialized && CoreWebView2 != null)
      {
        try
        {
          System.Diagnostics.Debug.WriteLine($"[Kilo] WebView2: PostMessage sending: {message.Substring(0, Math.Min(100, message.Length))}...");
          CoreWebView2.PostWebMessageAsString(message);
          System.Diagnostics.Debug.WriteLine($"[Kilo] WebView2: PostMessage sent successfully");
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"[Kilo] WebView2 post message error: {ex.Message}");
        }
      }
      else
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] WebView2: PostMessage skipped - not initialized or CoreWebView2 is null");
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
      }
      base.Dispose(disposing);
    }
  }
}
