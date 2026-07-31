using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.VisualStudio.Shell;

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
    private static readonly string _sharedUserDataFolder = Path.Combine(
        Path.GetTempPath(),
        "KiloWebView");

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
      // SSE handling is now done by SSEHelper in VSProvider
      // This method is kept for backward compatibility but does nothing
    }

    public async Task InitializeAsync()
    {
      if (_isInitialized)
        return;

      await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

      try
      {
        var envOptions = new CoreWebView2EnvironmentOptions();
        var env = await CoreWebView2Environment.CreateAsync(null, _sharedUserDataFolder, envOptions);
        try
        {
          await this.EnsureCoreWebView2Async(env);
        } catch (Exception e)
        {
          System.Diagnostics.Debug.Write(e.Message);
        }
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
