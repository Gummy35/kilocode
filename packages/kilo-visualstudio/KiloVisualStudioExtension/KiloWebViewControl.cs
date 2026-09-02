using Microsoft.VisualStudio.Shell;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

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
    public JToken? Payload { get; }

    /// <summary>
    /// Creates a new instance of WebViewMessageEventArgs.
    /// </summary>
    /// <param name="type">The message type identifier.</param>
    /// <param name="payload">Optional JSON payload data.</param>
    public WebViewMessageEventArgs(string type, JToken? payload)
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
          //var webViewHtml = GetHtmlForWebview(this);
          //CoreWebView2.NavigateToString(webViewHtml);
          
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

        JToken? payload = null;
        string messageType = "";

        try
        {
          var token = JToken.Parse(messageStr);
          var typeObj = token.Root["type"];
          if (typeObj != null)
          {
            var typeProp = typeObj.Value<string>();
            messageType = typeProp;
            payload = token.Root.DeepClone();
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



    #region Webview HTML Generation

    /// <summary>
    /// Generates the HTML content for the webview, matching the VS Code _getHtmlForWebview method.
    /// This method builds a complete HTML document with CSP headers, styles, and script references.
    /// </summary>
    /// <param name="webView">The WebView2 control to get the base path from.</param>
    /// <param name="serverPort">The backend server port for CSP configuration.</param>
    /// <returns>The complete HTML string for the webview.</returns>
    public string GetHtmlForWebview(KiloWebViewControl webView, int? serverPort = null)
    {

      var assemblyDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
      var webviewDir = Path.Combine(assemblyDir, "webview");
      var webviewUri = ".";// new Uri(webviewDir).AbsoluteUri;


        // Generate nonce for CSP
        var nonce = GetNonce();

      // Build CSP string (matching VS Code's buildCspString)
      var csp = BuildCspString(nonce, serverPort);

      // Get font size styles
      var fontStyle = GetFontStyle();

      var html = new StringBuilder();
      html.AppendLine("<!DOCTYPE html>");
      html.AppendLine("<html lang=\"en\" data-theme=\"kilo-vscode\">");
      html.AppendLine("<head>");
      html.AppendLine("  <meta charset=\"UTF-8\">");
      html.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
   //   html.AppendLine($"  <meta http-equiv=\"Content-Security-Policy\" content=\"{csp}\">");
      html.AppendLine($"  <link rel=\"stylesheet\" href=\"{webviewUri}/vscode-theme.css\">");
      html.AppendLine($"  <link rel=\"stylesheet\" href=\"{webviewUri}/webview.css\">");
      html.AppendLine("  <title>Kilo Code</title>");
      html.AppendLine("  <style>");
      html.AppendLine(fontStyle);
      html.AppendLine("    html {");
      html.AppendLine("      scrollbar-color: auto;");
      html.AppendLine("      ::-webkit-scrollbar-thumb {");
      html.AppendLine("        border: 3px solid transparent !important;");
      html.AppendLine("        background-clip: padding-box !important;");
      html.AppendLine("      }");
      html.AppendLine("    }");
      html.AppendLine("    html, body {");
      html.AppendLine("      margin: 0;");
      html.AppendLine("      padding: 0;");
      html.AppendLine("      height: 100%;");
      html.AppendLine("      overflow: hidden;");
      html.AppendLine("    }");
      html.AppendLine("    body {");
      html.AppendLine("      background-color: var(--vscode-sideBar-background, var(--vscode-editor-background));");
      html.AppendLine("      color: var(--vscode-foreground);");
      html.AppendLine("      font-family: var(--vscode-font-family);");
      html.AppendLine("    }");
      html.AppendLine("    #root {");
      html.AppendLine("      height: 100%;");
      html.AppendLine("    }");
      html.AppendLine("  </style>");
      html.AppendLine("</head>");
      html.AppendLine("<body>");
      html.AppendLine("  <div id=\"root\"></div>");
      html.AppendLine($"  <script nonce=\"{nonce}\" src=\"{webviewUri}/vscode-api.js\"></script>");
      html.AppendLine($"  <script nonce=\"{nonce}\">window.ICONS_BASE_URI = \"{webviewUri}\"; window.KILO_SHIKI_WORKER_URI = \"{webviewUri}/shiki-worker.js\";</script>");
      html.AppendLine($"  <script nonce=\"{nonce}\" src=\"{webviewUri}/webview.js\"></script>");
      html.AppendLine("</body>");
      html.AppendLine("</html>");

      return html.ToString();
    }

    /// <summary>
    /// Generates a random nonce for Content Security Policy.
    /// Matches the VS Code getNonce() function using crypto.randomBytes.
    /// </summary>
    /// <returns>A 32-character hexadecimal string.</returns>
    private static string GetNonce()
    {
      var bytes = new byte[16];
      using var rng = RandomNumberGenerator.Create();
      rng.GetBytes(bytes);
      return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }

    /// <summary>
    /// Builds the Content Security Policy string for the webview.
    /// Matches the VS Code buildCspString function.
    /// </summary>
    /// <param name="nonce">The nonce value to include in script-src.</param>
    /// <param name="port">The backend server port for localhost connections.</param>
    /// <returns>The CSP string.</returns>
    private static string BuildCspString(string nonce, int? port)
    {
      var localhost = port.HasValue
          ? $"http://localhost:{port} http://127.0.0.1:{port} ws://localhost:{port} ws://127.0.0.1:{port}"
          : "http://localhost:* http://127.0.0.1:* ws://localhost:* ws://127.0.0.1:*";

      return $"default-src 'none'; style-src 'unsafe-inline' file:// data:; script-src 'nonce-{nonce}' 'wasm-unsafe-eval' file://; worker-src file:// blob:; font-src file:// data:; connect-src {localhost} file:// data:; img-src file:// data: https:;";
    }

    /// <summary>
    /// Generates font size CSS variables based on VS Code settings.
    /// Matches the VS Code fontStyle() function.
    /// </summary>
    /// <returns>CSS string with font size variables.</returns>
    private static string GetFontStyle()
    {
      // Default font size 13 (matching VS Code default)
      var baseSize = 13;
      var sizes = new[] { 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24 };

      var vars = new StringBuilder();
      foreach (var size in sizes)
      {
        vars.AppendLine($"      --kilo-font-size-{size}: {(baseSize * size) / 13.0:F2}px;");
      }

      return $":root {{\n{vars}      --kilo-font-scale: {baseSize / 13.0:F2};\n      --font-size-x-small: var(--kilo-font-size-10);\n      --font-size-small: var(--kilo-font-size-11);\n      --font-size-base: var(--kilo-font-size-13);\n      --font-size-large: var(--kilo-font-size-16);\n    }}";
    }

    #endregion
  }
}
