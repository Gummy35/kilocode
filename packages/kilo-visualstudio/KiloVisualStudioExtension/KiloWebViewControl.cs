using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.VisualStudio.Shell;

namespace KiloVisualStudioExtension
{
  /// <summary>
  /// Event arguments for WebView messages sent from the webview to the extension.
  /// Contains the message type and optional JSON payload.
  /// </summary>
  public class WebViewMessageEventArgs : EventArgs
  {
    /// <summary>
    /// The type of message (e.g., "prompt", "loadMessages", "sendMessage").
    /// </summary>
    public string Type { get; }
    
    /// <summary>
    /// Optional JSON payload containing message data.
    /// </summary>
    public JsonElement? Payload { get; }

    /// <summary>
    /// Creates a new instance of WebViewMessageEventArgs.
    /// </summary>
    /// <param name="type">The message type identifier.</param>
    /// <param name="payload">Optional JSON payload data.</param>
    public WebViewMessageEventArgs(string type, JsonElement? payload)
    {
      Type = type;
      Payload = payload;
    }
  }

  /// <summary>
  /// WebView2 control wrapper for hosting the Kilo Code webview interface.
  /// Manages WebView2 initialization, message handling, and communication
  /// between the Visual Studio extension and the webview.
  /// </summary>
  public class KiloWebViewControl : WebView2, IDisposable
  {
    /// <summary>
    /// Flag indicating whether the WebView2 control has been initialized.
    /// </summary>
    private bool _isInitialized;
    
    /// <summary>
    /// Shared folder for WebView2 user data (cookies, cache, etc.).
    /// Located in the system temp directory.
    /// </summary>
    private static readonly string _sharedUserDataFolder = Path.Combine(
        Path.GetTempPath(),
        "KiloWebView");

    /// <summary>
    /// Event raised when a message is received from the webview.
    /// </summary>
    public event EventHandler<WebViewMessageEventArgs>? OnMessageReceived;

    /// <summary>
    /// Initializes a new instance of KiloWebViewControl with stretch layout.
    /// </summary>
    public KiloWebViewControl()
    {
      Width = double.NaN;
      Height = double.NaN;
      HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
      VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
      AllowExternalDrop = false;
    }

    /// <summary>
    /// Sets the connection service reference (kept for backward compatibility).
    /// SSE handling is now managed by SSEHelper in VSProvider.
    /// </summary>
    /// <param name="service">The KiloConnectionService instance (not used).</param>
    public void SetConnectionService(KiloConnectionService service)
    {
      // SSE handling is now done by SSEHelper in VSProvider
      // This method is kept for backward compatibility but does nothing
    }

    /// <summary>
    /// Asynchronously initializes the WebView2 control.
    /// Creates the WebView2 environment, configures settings, and navigates to the local webview HTML file.
    /// Must be called on the UI thread. Idempotent - subsequent calls are ignored.
    /// </summary>
    /// <returns>A task representing the asynchronous initialization operation.</returns>
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

    /// <summary>
    /// Event handler for WebView2 web messages. Parses incoming JSON messages and raises
    /// the OnMessageReceived event with the message type and payload.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The web message received event arguments.</param>
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

    /// <summary>
    /// Posts a message to the webview. The message is sent as a JSON string.
    /// Only works if the WebView2 control has been initialized.
    /// </summary>
    /// <param name="message">The JSON message string to send to the webview.</param>
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

    /// <summary>
    /// Releases unmanaged and managed resources used by the WebView2 control.
    /// Unsubscribes from WebView2 events to prevent memory leaks.
    /// </summary>
    /// <param name="disposing">True if called from Dispose, false from finalizer.</param>
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
