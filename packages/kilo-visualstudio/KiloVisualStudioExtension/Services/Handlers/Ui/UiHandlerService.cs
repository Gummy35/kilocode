using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services.Handlers.Ui
{
    /// <summary>
    /// Handles UI and panel operations like open settings panel, open sub-agent viewer, reload, save image.
    /// This matches the VS Code pattern where UI operations are extracted into separate handler modules.
    /// </summary>
    public class UiHandlerService : IDisposable
    {
        private readonly VSProvider _provider;
        private bool _disposed;

        /// <summary>
        /// Creates a new UiHandlerService instance.
        /// </summary>
        /// <param name="provider">The VSProvider instance to use for webview communication.</param>
        public UiHandlerService(VSProvider provider)
        {
            _provider = provider;
        }

        /// <summary>
        /// Handles the openSettingsPanel message from the webview.
        /// Opens the settings panel with the specified tab.
        /// </summary>
        /// <param name="payload">The message payload containing tab name.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleOpenSettingsPanelAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] UiHandler: openSettingsPanel");
            string? tab = null;
            if (payload.HasValue && payload.Value.TryGetProperty("tab", out var tabProp))
            {
                tab = tabProp.GetString();
            }
            var navigateMsg = new { type = "navigate", view = "settings", tab };
            _provider.PostMessage(JsonSerializer.Serialize(navigateMsg));
        }

        /// <summary>
        /// Handles the settingsTabChanged message from the webview.
        /// Notifies when the settings tab changes.
        /// </summary>
        /// <param name="payload">The message payload.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public void HandleSettingsTabChanged(JsonElement? payload)
        {
            if (payload.HasValue && payload.Value.TryGetProperty("tab", out var tabProp))
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] UiHandler: settingsTabChanged - tab={tabProp.GetString()}");
            }
        }

        /// <summary>
        /// Handles the openSubAgentViewer message from the webview.
        /// Opens the sub-agent viewer for a specific session.
        /// </summary>
        /// <param name="payload">The message payload containing session ID.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleOpenSubAgentViewerAsync(JsonElement? payload)
        {
            if (payload == null)
            {
                await _provider.SendErrorAsync("Missing payload", "Open sub-agent viewer payload is required");
                return;
            }

            try
            {
                string? sessionID = null;
                if (payload.Value.TryGetProperty("sessionID", out var sidProp))
                {
                    sessionID = sidProp.GetString();
                }
                
                string? title = null;
                if (payload.Value.TryGetProperty("title", out var titleProp))
                {
                    title = titleProp.GetString();
                }

                System.Diagnostics.Debug.WriteLine($"[Kilo] UiHandler: Open sub-agent viewer - session: {sessionID}");
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "subAgentViewerOpened", sessionID, title }));
            }
            catch (Exception ex)
            {
                await _provider.SendErrorAsync("Open sub-agent viewer error", ex.Message);
            }
        }

        /// <summary>
        /// Handles the reload message from the webview.
        /// Reloads config, skills, agents, and commands from disk by rebooting the backend instance.
        /// Matches VS Code's handleReload pattern - calls /instance/reload endpoint.
        /// </summary>
        /// <param name="payload">The message payload (unused).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleReloadAsync(JsonElement? payload)
        {
            var httpClient = _provider.GetHttpClient();
            if (httpClient == null || !httpClient.IsConnected())
            {
                System.Diagnostics.Debug.WriteLine("[Kilo] UiHandler: reload - no client connection");
                return;
            }

            var directory = System.Environment.CurrentDirectory;
            
            try
            {
                await httpClient.PostJsonAsync($"/instance/reload", new { directory });
                System.Diagnostics.Debug.WriteLine($"[Kilo] UiHandler: Backend reloaded for directory {directory}");
                
                // Clear cached commands and refresh agents/config
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "configReloaded" }));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] UiHandler: reload failed: {ex.Message}");
                // Match VS Code: log error but don't throw - user sees error in UI
            }
        }

        /// <summary>
        /// Handles the saveImage message from the webview.
        /// Saves an image to disk.
        /// </summary>
        /// <param name="payload">The message payload containing image data.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleSaveImageAsync(JsonElement? payload)
        {
            if (payload == null)
            {
                await _provider.SendErrorAsync("Missing payload", "Save image payload is required");
                return;
            }

            try
            {
                var imageData = payload.Value.TryGetProperty("imageData", out var data) ? data.GetString() : "";
                var filename = payload.Value.TryGetProperty("filename", out var fn) ? fn.GetString() : "image.png";
                
                if (string.IsNullOrEmpty(imageData))
                {
                    await _provider.SendErrorAsync("Invalid payload", "Image data is required");
                    return;
                }
                
                var bytes = Convert.FromBase64String(imageData);
                var path = Path.Combine(System.Environment.CurrentDirectory, filename);
                File.WriteAllBytes(path, bytes);
                System.Diagnostics.Debug.WriteLine($"[Kilo] UiHandler: Image saved: {path}");
                
                _provider.PostMessage(JsonSerializer.Serialize(new { type = "imageSaved", path }));
            }
            catch (Exception ex)
            {
                await _provider.SendErrorAsync("Save image error", ex.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
