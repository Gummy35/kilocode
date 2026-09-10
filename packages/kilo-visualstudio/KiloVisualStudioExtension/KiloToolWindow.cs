using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension
{
    /// <summary>
    /// Main tool window for the Kilo Code extension.
    /// Hosts the WebView2 control and VSProvider for the chat interface.
    /// This is the primary sidebar panel where users interact with the AI assistant.
    /// </summary>
    [Guid("8a8f8e8c-1234-5678-9abc-def012345680")]
    public class KiloToolWindow : ToolWindowPane
    {
        /// <summary>
        /// The WebView2 control that hosts the SolidJS webview UI.
        /// </summary>
        private KiloWebViewControl? _webView;
        
        /// <summary>
        /// The VSProvider instance that handles communication between
        /// the webview and the backend services.
        /// </summary>
        private VSProvider? _vsProvider;

        /// <summary>
        /// Gets the WebView2 control instance.
        /// </summary>
        public KiloWebViewControl? WebView => _webView;

        public static KiloToolWindow Instance
        {
          get;set;
        }

        /// <summary>
        /// Initializes a new instance of the KiloToolWindow.
        /// Sets up the WebView2 control and begins asynchronous initialization.
        /// </summary>
        public KiloToolWindow() : base(null)
        {
            Caption = "Kilo Code";
            _webView = new KiloWebViewControl();
            Content = _webView;

            // Fire-and-forget initialization - safe because WebView is created synchronously
            _ = InitializeWebViewAsync();
            Instance = this;
        }

        /// <summary>
        /// Asynchronously initializes the WebView2 control and VSProvider.
        /// Connects to the backend service (which starts lazily if not already running).
        /// Must be called on the UI thread.
        /// </summary>
        /// <returns>A task representing the asynchronous initialization operation.</returns>
        private async Task InitializeWebViewAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Use ProviderFactory to create provider with shared connection service
            _webView.SetConnectionService(KiloVisualStudioExtensionPackage.GetConnectionService());
            _vsProvider = ProviderFactory.CreateSidebarProvider(_webView);

            _vsProvider.ContinueInWorktreeHandler = (sessionId, progress) => { return Task.CompletedTask; };
            // agentManagerProvider.continueFromSidebar(sessionId, progress),
            _vsProvider.CreateWorktreeHandler = (baseBranch, branchName) => { return Task.CompletedTask; };
            //    agentManagerProvider.createFromSidebar(baseBranch, branchName),
  
            // Connect to backend (starts lazily if not already running)
            await KiloVisualStudioExtensionPackage.GetConnectionService().ConnectAsync();
            await _webView!.InitializeAsync();
        }

        /// <summary>
        /// Disposes of tool window resources.
        /// Cleans up the VSProvider and WebView2 control.
        /// </summary>
        /// <param name="disposing">True if called from Dispose, false from finalizer.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _vsProvider?.Dispose();
                _webView?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
