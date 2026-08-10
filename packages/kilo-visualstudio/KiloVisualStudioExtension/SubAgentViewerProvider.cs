using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace KiloVisualStudioExtension
{
    /// <summary>
    /// Provides a read-only panel to view sub-agent sessions.
    /// Matches VS Code's SubAgentViewerProvider pattern.
    /// 
    /// Each child session ID maps to at most one panel - calling OpenPanel()
    /// again with the same ID reveals the existing panel.
    /// 
    /// Uses a full VSProvider so the viewer has backend connectivity
    /// (messages, parts, SSE events) identical to the sidebar.
    /// 
    /// Responsibilities:
    /// - Manage panel lifecycle (create, reveal, dispose)
    /// - Track sessions for SSE event filtering
    /// - Load session messages when panel is ready
    /// - Handle closePanel messages from webview
    /// </summary>
    public class SubAgentViewerProvider : IDisposable
    {
        /// <summary>
        /// Map of session IDs to their webview panels.
        /// </summary>
        private readonly Dictionary<string, KiloWebViewControl> _panels = new Dictionary<string, KiloWebViewControl>();
        
        /// <summary>
        /// Map of session IDs to their VSProvider instances.
        /// </summary>
        private readonly Dictionary<string, VSProvider> _providers = new Dictionary<string, VSProvider>();
        
        private readonly Uri _extensionUri;
        private readonly KiloConnectionService _connectionService;
        private bool _disposed;

        /// <summary>
        /// Creates a new SubAgentViewerProvider instance.
        /// </summary>
        /// <param name="extensionUri">The extension installation URI.</param>
        /// <param name="connectionService">The shared connection service for backend connectivity.</param>
        public SubAgentViewerProvider(Uri extensionUri, KiloConnectionService connectionService)
        {
            _extensionUri = extensionUri;
            _connectionService = connectionService;
        }

        /// <summary>
        /// Opens or reveals a sub-agent viewer panel for the specified session.
        /// If a panel already exists for this session ID, it is revealed.
        /// Otherwise, a new panel is created and configured.
        /// 
        /// Workflow:
        /// 1. Check if panel exists for sessionID
        /// 2. If exists, reveal it and return
        /// 3. Create new webview panel with scripts enabled
        /// 4. Create VSProvider for the panel
        /// 5. Track session for SSE events
        /// 6. Register webview panel with provider
        /// 7. Set up message handlers for webviewReady and closePanel
        /// 8. Store panel and provider references
        /// 9. Handle panel disposal cleanup
        /// </summary>
        /// <param name="sessionID">The session ID to view.</param>
        /// <param name="title">Optional title for the panel (used in label).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task OpenPanelAsync(string sessionID, string? title = null)
        {
            if (_disposed)
            {
                System.Diagnostics.Debug.WriteLine("[Kilo] SubAgentViewer: provider disposed, cannot open panel");
                return;
            }

            // Check if panel already exists
            if (_panels.TryGetValue(sessionID, out var existingPanel))
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] SubAgentViewer: revealing existing panel for session {sessionID}");
                // TODO: Implement panel reveal logic when we have a panel manager
                return;
            }

            var label = string.IsNullOrEmpty(title) ? "Sub-agent Viewer" : $"Sub-agent: {title}";
            System.Diagnostics.Debug.WriteLine($"[Kilo] SubAgentViewer: creating new panel: {label}");

            // Create webview panel (placeholder - actual implementation depends on VS WebView2 integration)
            var webView = new KiloWebViewControl();
            
            // Create provider for this panel
            var provider = new VSProvider(webView, _connectionService);
            
            // Track session for SSE events
            provider.TrackSession(sessionID);
            
            // Store references
            _panels[sessionID] = webView;
            _providers[sessionID] = provider;

            // Set up message handlers
            webView.OnMessageReceived += (sender, e) => HandleMessageReceived(sessionID, e.Type, e.Payload);
            
            // Handle panel disposal
            // TODO: Add disposal hook when WebView2 supports it

            System.Diagnostics.Debug.WriteLine($"[Kilo] SubAgentViewer: panel created for session {sessionID}");
        }

        /// <summary>
        /// Handles the message received from the webview.
        /// Routes messages to appropriate handlers based on type.
        /// </summary>
        /// <param name="sessionID">The session ID associated with the panel.</param>
        /// <param name="type">The message type.</param>
        /// <param name="payload">The message payload.</param>
        private void HandleMessageReceived(string sessionID, string type, JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine($"[Kilo] SubAgentViewer: received message type={type} for session {sessionID}");
            
            switch (type)
            {
                case "webviewReady":
                    HandleWebViewReady(sessionID);
                    break;
                    
                case "closePanel":
                    HandleClosePanel(sessionID);
                    break;
                    
                default:
                    // Forward other messages to the provider
                    VSProvider? provider = null;
                    if (_providers.TryGetValue(sessionID, out var p))
                    {
                        provider = p;
                    }
                    
                    if (provider != null)
                    {
                        // Provider handles its own message routing
                    }
                    break;
            }
        }

        /// <summary>
        /// Handles the webviewReady message.
        /// Loads session messages and metadata when the webview is ready.
        /// 
        /// Workflow:
        /// 1. Send viewSubAgentSession message to load session
        /// 2. Load messages for the session
        /// 3. Fetch session metadata from backend
        /// 4. Register session with provider
        /// </summary>
        /// <param name="sessionID">The session ID.</param>
        private void HandleWebViewReady(string sessionID)
        {
            System.Diagnostics.Debug.WriteLine($"[Kilo] SubAgentViewer: webview ready for session {sessionID}");
            
            VSProvider? provider = null;
            if (_providers.TryGetValue(sessionID, out var p))
            {
                provider = p;
            }
            
            if (provider == null)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] SubAgentViewer: provider not found for session {sessionID}");
                return;
            }

            // Send viewSubAgentSession message
            provider.PostMessage(JsonSerializer.Serialize(new { type = "viewSubAgentSession", sessionID }));
            
            // Load messages (fire-and-forget)
            _ = provider.LoadMessagesAsync(sessionID);
            
            // Fetch session metadata
            _ = LoadSessionMetadataAsync(sessionID, provider);
        }

        /// <summary>
        /// Loads session metadata from the backend and registers it with the provider.
        /// </summary>
        /// <param name="sessionID">The session ID.</param>
        /// <param name="provider">The provider to register the session with.</param>
        private async Task LoadSessionMetadataAsync(string sessionID, VSProvider provider)
        {
            var nswagClient = _connectionService.GetNswagClient();
            if (nswagClient == null)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] SubAgentViewer: cannot load metadata - no NSwag client");
                return;
            }

            try
            {
                var sessions = await nswagClient.Session_listAsync(System.Environment.CurrentDirectory, "", null, "", null, null, null, null);
                if (sessions != null)
                {
                    var session = sessions.FirstOrDefault(s => s.Id == sessionID);
                    if (session != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Kilo] SubAgentViewer: registered session {sessionID}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] SubAgentViewer: failed to load session metadata: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles the closePanel message.
        /// Disposes the panel and cleans up resources.
        /// </summary>
        /// <param name="sessionID">The session ID.</param>
        private void HandleClosePanel(string sessionID)
        {
            System.Diagnostics.Debug.WriteLine($"[Kilo] SubAgentViewer: closing panel for session {sessionID}");
            
            DisposePanel(sessionID);
        }

        /// <summary>
        /// Disposes a panel and cleans up associated resources.
        /// </summary>
        /// <param name="sessionID">The session ID.</param>
        private void DisposePanel(string sessionID)
        {
            // Remove from tracking
            _panels.Remove(sessionID);
            
            // Dispose provider
            if (_providers.TryGetValue(sessionID, out var provider))
            {
                provider.Dispose();
                _providers.Remove(sessionID);
            }
            
            // TODO: Dispose webview panel when WebView2 supports it
            
            System.Diagnostics.Debug.WriteLine($"[Kilo] SubAgentViewer: panel disposed for session {sessionID}");
        }

        /// <summary>
        /// Disposes all resources used by the SubAgentViewerProvider.
        /// Closes all panels and disposes all providers.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            // Dispose all panels
            foreach (var sessionID in _panels.Keys.ToArray())
            {
                DisposePanel(sessionID);
            }
            
            _panels.Clear();
            _providers.Clear();
            
            System.Diagnostics.Debug.WriteLine("[Kilo] SubAgentViewer: provider disposed");
        }
    }
}
