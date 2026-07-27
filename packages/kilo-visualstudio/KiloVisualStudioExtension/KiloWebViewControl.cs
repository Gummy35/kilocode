using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.VisualStudio.Shell;
using System.Net.Http;

namespace KiloVisualStudio
{
    /// <summary>
    /// WebView2 control that hosts the Kilo Code webview UI.
    /// </summary>
    public class KiloWebViewControl : WebView2, IDisposable
    {
        private bool _isInitialized;
        private string? _baseUrl;

        public KiloWebViewControl()
        {
            Dock = System.Windows.Forms.DockStyle.Fill;
            AllowExternalDrop = false;
        }

        /// <summary>
        /// Initialize WebView2 and navigate to the webview.
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_isInitialized)
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Ensure WebView2 runtime is available
            var env = await CoreWebView2Environment.CreateAsync();
            await InitializeCoreWebView2Async(env);

            // Get the backend URL from the package
            var backendManager = CliBackendManager.Instance;
            if (backendManager != null)
            {
                _baseUrl = backendManager.BaseUrl;
            }

            // Navigate to the webview
            if (!string.IsNullOrEmpty(_baseUrl))
            {
                Source = new Uri($"{_baseUrl}/webview");
            }
            else
            {
                // Fallback to bundled webview
                var webviewPath = Path.Combine(
                    Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".",
                    "webview",
                    "index.html");
                
                if (File.Exists(webviewPath))
                {
                    Source = new Uri($"file:///{webviewPath}");
                }
            }

            _isInitialized = true;
        }

        private async Task InitializeCoreWebView2Async(CoreWebView2Environment env)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            
            await this.EnsureCoreWebView2Async(env);
            
            // Configure WebView2 settings
            CoreWebView2.Settings.IsScriptEnabled = true;
            CoreWebView2.Settings.IsWebMessageEnabled = true;
            CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
            CoreWebView2.Settings.IsStatusBarEnabled = false;

            // Setup message handler for extension↔webview communication
            CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        }

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var message = e.TryGetWebMessageAsString();
                // Handle messages from webview here
                // Example: forward to CLI backend via HTTP
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebView2 message error: {ex.Message}");
            }
        }

        /// <summary>
        /// Send a message to the webview.
        /// </summary>
        public void PostMessage(string message)
        {
            if (_isInitialized && CoreWebView2 != null)
            {
                CoreWebView2.PostWebMessageAsString(message);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_isInitialized && CoreWebView2 != null)
                {
                    CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                    // CoreWebView2 doesn't have a Dispose method, just unsubscribe from events
                }
            }
            base.Dispose(disposing);
        }
    }
}
