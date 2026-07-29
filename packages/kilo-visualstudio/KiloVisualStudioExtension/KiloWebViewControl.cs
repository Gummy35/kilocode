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
          var message = MapSseEventToWebviewMessage(eventType, data);
          if (message == null) return;
          
          var json = JsonSerializer.Serialize(message);
          CoreWebView2.PostWebMessageAsString(json);
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"[Kilo] WebView2 post SSE error: {ex.Message}");
        }
      }
    }
    
    private object? MapSseEventToWebviewMessage(string eventType, string data)
    {
      try
      {
        var jsonData = JsonSerializer.Deserialize<JsonElement>(data);
        
        // Check if this is a sync event (has "name" field) or stream event (has "type" field)
        if (jsonData.TryGetProperty("name", out var nameProp))
        {
          // Sync event format: {"name":"...","data":{...}}
          var name = nameProp.GetString() ?? "";
          var dataProp = jsonData.GetProperty("data");
          
          return name switch
          {
            "message.updated.1" => CreateMessageCreated(dataProp),
            "message.removed.1" => CreateMessageRemoved(dataProp),
            "message.part.updated.1" => CreatePartUpdated(dataProp, null),
            "message.part.removed.1" => CreatePartRemoved(dataProp),
            "session.created.1" => CreateSessionCreated(dataProp),
            "session.updated.1" => null,
            "session.deleted.1" => CreateSessionDeleted(dataProp),
            _ => null
          };
        }
        
        // Stream event format: {"type":"...","properties":{...}}
        if (!jsonData.TryGetProperty("type", out var typeProp))
          return null;
          
        var type = typeProp.GetString() ?? "";
        var properties = jsonData.TryGetProperty("properties", out var propsProp) ? propsProp : JsonDocument.Parse("{}").RootElement;
        
        if (type == "message.part.delta")
          return CreatePartDelta(properties);
          
        return type switch
        {
          "session.status" => CreateSessionStatus(properties),
          "session.turn.close" => CreateSessionTurnClosed(properties),
          "permission.asked" => CreatePermissionRequest(properties),
          "permission.replied" => CreatePermissionResolved(properties),
          "todo.updated" => CreateTodoUpdated(properties),
          "question.asked" => CreateQuestionRequest(properties),
          "question.replied" => CreateQuestionResolved(properties),
          "question.rejected" => CreateQuestionResolved(properties),
          "suggestion.shown" => CreateSuggestionRequest(properties),
          "suggestion.accepted" => CreateSuggestionResolved(properties),
          "suggestion.dismissed" => CreateSuggestionResolved(properties),
          "session.error" => CreateSessionError(properties),
          "sandbox.status.changed" => CreateSandboxStatus(properties),
          "indexing.status" => CreateIndexingStatus(properties),
          _ => null
        };
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] SSE mapping error: {ex.Message}");
        return null;
      }
    }
    
    private object CreateMessageCreated(JsonElement data)
    {
      var info = data.GetProperty("info");
      var time = info.GetProperty("time");
      var created = time.GetProperty("created");
      var createdAt = DateTimeOffset.FromUnixTimeSeconds((long)created.GetDouble()).ToUniversalTime().ToString("o");
      
      return new
      {
        type = "messageCreated",
        message = new
        {
          id = info.GetProperty("id").GetString(),
          sessionID = info.GetProperty("sessionID").GetString(),
          role = info.GetProperty("role").GetString(),
          createdAt,
          parts = info.GetProperty("parts")
        }
      };
    }
    
    private object CreateMessageRemoved(JsonElement data)
    {
      return new
      {
        type = "messageRemoved",
        sessionID = data.GetProperty("sessionID").GetString(),
        messageID = data.GetProperty("messageID").GetString()
      };
    }
    
    private object CreatePartUpdated(JsonElement data, string? sessionID)
    {
      var part = data.GetProperty("part");
      return new
      {
        type = "partUpdated",
        sessionID = data.GetProperty("sessionID").GetString(),
        messageID = part.GetProperty("messageID").GetString(),
        part
      };
    }
    
    private object CreatePartRemoved(JsonElement data)
    {
      return new
      {
        type = "partRemoved",
        sessionID = data.GetProperty("sessionID").GetString(),
        messageID = data.GetProperty("messageID").GetString(),
        partID = data.GetProperty("partID").GetString()
      };
    }
    
    private object CreateSessionCreated(JsonElement data)
    {
      var info = data.GetProperty("info");
      return new
      {
        type = "sessionCreated",
        session = new
        {
          id = info.GetProperty("id").GetString(),
          directory = info.GetProperty("directory").GetString(),
          title = info.GetProperty("title").GetString(),
          updated = info.GetProperty("updated").GetInt64(),
          status = "idle"
        }
      };
    }
    
    private object CreateSessionDeleted(JsonElement data)
    {
      return new
      {
        type = "sessionDeleted",
        sessionID = data.GetProperty("sessionID").GetString()
      };
    }
    
    private object CreatePartDelta(JsonElement properties)
    {
      var partID = properties.GetProperty("partID").GetString();
      var messageID = properties.GetProperty("messageID").GetString();
      var sessionID = properties.GetProperty("sessionID").GetString();
      var delta = properties.GetProperty("delta").GetString();
      
      return new
      {
        type = "partUpdated",
        sessionID,
        messageID,
        part = new { id = partID, type = "text", messageID, text = delta },
        delta = new { type = "text-delta", textDelta = delta }
      };
    }
    
    private object CreateSessionStatus(JsonElement properties)
    {
      var status = properties.GetProperty("status");
      var statusType = status.GetProperty("type").GetString();
      var sessionID = properties.GetProperty("sessionID").GetString();
      
      if (statusType == "retry")
      {
        return new
        {
          type = "sessionStatus",
          sessionID,
          status = statusType,
          attempt = status.GetProperty("attempt").GetInt32(),
          message = status.TryGetProperty("message", out var msg) ? msg.GetString() : null,
          next = status.TryGetProperty("next", out var next) ? next.GetInt64() : (long?)null
        };
      }
      else if (statusType == "offline")
      {
        return new
        {
          type = "sessionStatus",
          sessionID,
          status = statusType,
          message = status.TryGetProperty("message", out var msg) ? msg.GetString() : null
        };
      }
      
      return new
      {
        type = "sessionStatus",
        sessionID,
        status = statusType
      };
    }
    
    private object CreateSessionTurnClosed(JsonElement properties)
    {
      return new
      {
        type = "sessionTurnClosed",
        sessionID = properties.GetProperty("sessionID").GetString(),
        reason = properties.GetProperty("reason").GetString()
      };
    }
    
    private object CreatePermissionRequest(JsonElement properties)
    {
      var permission = properties.GetProperty("permission").GetString();
      return new
      {
        type = "permissionRequest",
        permission = new
        {
          id = properties.GetProperty("id").GetString(),
          sessionID = properties.GetProperty("sessionID").GetString(),
          toolName = permission,
          patterns = properties.TryGetProperty("patterns", out var patterns) ? patterns : JsonDocument.Parse("[]").RootElement,
          always = properties.TryGetProperty("always", out var always) ? always : JsonDocument.Parse("[]").RootElement,
          args = properties.TryGetProperty("metadata", out var metadata) ? metadata : JsonDocument.Parse("{}").RootElement,
          message = $"Permission required: {permission}",
          tool = properties.TryGetProperty("tool", out var tool) ? tool : JsonDocument.Parse("{}").RootElement
        }
      };
    }
    
    private object CreatePermissionResolved(JsonElement properties)
    {
      return new
      {
        type = "permissionResolved",
        permissionID = properties.GetProperty("requestID").GetString()
      };
    }
    
    private object CreateTodoUpdated(JsonElement properties)
    {
      return new
      {
        type = "todoUpdated",
        sessionID = properties.GetProperty("sessionID").GetString(),
        items = properties.GetProperty("todos")
      };
    }
    
    private object CreateQuestionRequest(JsonElement properties)
    {
      return new
      {
        type = "questionRequest",
        question = new
        {
          id = properties.GetProperty("id").GetString(),
          sessionID = properties.GetProperty("sessionID").GetString(),
          questions = properties.GetProperty("questions"),
          blocking = properties.TryGetProperty("blocking", out var blocking) ? blocking.GetBoolean() : false,
          tool = properties.TryGetProperty("tool", out var tool) ? tool : JsonDocument.Parse("{}").RootElement
        }
      };
    }
    
    private object CreateQuestionResolved(JsonElement properties)
    {
      return new
      {
        type = "questionResolved",
        requestID = properties.GetProperty("requestID").GetString()
      };
    }
    
    private object CreateSuggestionRequest(JsonElement properties)
    {
      return new
      {
        type = "suggestionRequest",
        suggestion = new
        {
          id = properties.GetProperty("id").GetString(),
          sessionID = properties.GetProperty("sessionID").GetString(),
          text = properties.GetProperty("text").GetString(),
          actions = properties.GetProperty("actions"),
          blocking = properties.TryGetProperty("blocking", out var blocking) ? blocking.GetBoolean() : false,
          tool = properties.TryGetProperty("tool", out var tool) ? tool : JsonDocument.Parse("{}").RootElement
        }
      };
    }
    
    private object CreateSuggestionResolved(JsonElement properties)
    {
      return new
      {
        type = "suggestionResolved",
        requestID = properties.GetProperty("requestID").GetString()
      };
    }
    
    private object CreateSessionError(JsonElement properties)
    {
      return new
      {
        type = "sessionError",
        sessionID = properties.TryGetProperty("sessionID", out var sid) ? sid.GetString() : null,
        error = properties.TryGetProperty("error", out var error) ? error : JsonDocument.Parse("{}").RootElement
      };
    }
    
    private object CreateSandboxStatus(JsonElement properties)
    {
      return new
      {
        type = "sandboxStatus",
        sessionID = properties.GetProperty("sessionID").GetString(),
        directory = properties.GetProperty("directory").GetString(),
        enabled = properties.GetProperty("enabled").GetBoolean(),
        available = properties.GetProperty("available").GetBoolean(),
        reason = properties.TryGetProperty("reason", out var reason) ? reason.GetString() : null,
        version = properties.GetProperty("version").GetInt32()
      };
    }
    
    private object CreateIndexingStatus(JsonElement properties)
    {
      return new
      {
        type = "indexingStatusLoaded",
        status = properties.GetProperty("status").GetString()
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
