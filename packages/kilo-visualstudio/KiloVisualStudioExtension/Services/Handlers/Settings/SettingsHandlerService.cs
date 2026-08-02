using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services.Handlers.Settings
{
    /// <summary>
    /// Handles settings-related operations like indexing settings, chat settings, throughput settings, autocomplete settings.
    /// This matches the VS Code pattern where settings handling is extracted into separate handler modules.
    /// </summary>
    public class SettingsHandlerService : IDisposable
    {
        private readonly VSProvider _provider;
        private bool _disposed;

        /// <summary>
        /// Creates a new SettingsHandlerService instance.
        /// </summary>
        /// <param name="provider">The VSProvider instance to use for webview communication.</param>
        public SettingsHandlerService(VSProvider provider)
        {
            _provider = provider;
        }

        /// <summary>
        /// Handles the requestIndexingSettings message from the webview.
        /// Sends the current indexing settings to the webview.
        /// </summary>
        /// <param name="payload">The message payload (unused).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public void HandleRequestIndexingSettings(JsonElement? payload)
        {
            var message = new { type = "indexingSettingsLoaded", settings = new { showButtonWhenDisabled = true } };
            _provider.PostMessage(JsonSerializer.Serialize(message));
        }

        /// <summary>
        /// Handles the requestChatSettings message from the webview.
        /// Sends the current chat settings to the webview.
        /// </summary>
        /// <param name="payload">The message payload (unused).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public void HandleRequestChatSettings(JsonElement? payload)
        {
            var message = new { type = "chatSettingsLoaded", settings = new { shiftTabCyclesVariant = false } };
            _provider.PostMessage(JsonSerializer.Serialize(message));
        }

        /// <summary>
        /// Handles the requestThroughputSetting message from the webview.
        /// Sends the current throughput setting to the webview.
        /// </summary>
        /// <param name="payload">The message payload (unused).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public void HandleRequestThroughputSetting(JsonElement? payload)
        {
            var message = new { type = "throughputSettingLoaded", visible = true };
            _provider.PostMessage(JsonSerializer.Serialize(message));
        }

        /// <summary>
        /// Handles the requestAutocompleteSettings message from the webview.
        /// Sends the current autocomplete settings to the webview.
        /// </summary>
        /// <param name="payload">The message payload (unused).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public void HandleRequestAutocompleteSettings(JsonElement? payload)
        {
            var message = new 
            { 
                type = "autocompleteSettingsLoaded", 
                settings = new 
                { 
                    enableAutoTrigger = false, 
                    enableSmartInlineTaskKeybinding = false, 
                    enableChatAutocomplete = false, 
                    provider = (string?)null, 
                    model = (string?)null 
                } 
            };
            _provider.PostMessage(JsonSerializer.Serialize(message));
        }

        /// <summary>
        /// Handles the requestIndexingStatus message from the webview.
        /// Fetches and sends the current indexing status to the webview.
        /// </summary>
        /// <param name="payload">The message payload (unused).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleRequestIndexingStatusAsync(JsonElement? payload)
        {
            try
            {
                var httpClient = _provider.GetHttpClient();
                if (httpClient == null)
                {
                    await _provider.SendIndexingStatusAsync(JsonDocument.Parse("{}").RootElement);
                    return;
                }

                var response = await httpClient.GetJsonAsync("/indexing");
                var status = JsonDocument.Parse("{}").RootElement;
                
                if (response != null)
                {
                    status = response.RootElement.Clone();
                }

                await _provider.SendIndexingStatusAsync(status);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] SettingsHandler: Error fetching indexing status: {ex.Message}");
                await _provider.SendIndexingStatusAsync(JsonDocument.Parse("{}").RootElement);
            }
        }

        /// <summary>
        /// Handles the requestWorkStyle message from the webview.
        /// Sends the current work style configuration to the webview.
        /// </summary>
        /// <param name="payload">The message payload (unused).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleRequestWorkStyleAsync(JsonElement? payload)
        {
            try
            {
                var httpClient = _provider.GetHttpClient();
                if (httpClient == null)
                {
                    await _provider.SendWorkStyleLoadedAsync(new { mode = "ask", autoApprove = new { enabled = false, limit = 0 } });
                    return;
                }

                var response = await httpClient.GetJsonAsync("/workstyle");
                var style = new { mode = "ask", autoApprove = new { enabled = false, limit = 0 } };
                
                if (response != null && response.RootElement.TryGetProperty("style", out var styleProp))
                {
                    style = new 
                    { 
                        mode = styleProp.TryGetProperty("mode", out var m) ? m.GetString() : "ask",
                        autoApprove = new 
                        { 
                            enabled = styleProp.TryGetProperty("autoApprove", out var ap) && ap.TryGetProperty("enabled", out var e) && e.GetBoolean(),
                            limit = styleProp.TryGetProperty("autoApprove", out var ap2) && ap2.TryGetProperty("limit", out var l) ? l.GetInt32() : 0
                        }
                    };
                }

                await _provider.SendWorkStyleLoadedAsync(style);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] SettingsHandler: Error fetching work style: {ex.Message}");
                await _provider.SendWorkStyleLoadedAsync(new { mode = "ask", autoApprove = new { enabled = false, limit = 0 } });
            }
        }

        /// <summary>
        /// Handles the applyWorkStyle message from the webview.
        /// Applies a new work style configuration.
        /// </summary>
        /// <param name="payload">The message payload containing the new work style.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleApplyWorkStyleAsync(JsonElement? payload)
        {
            if (payload == null)
            {
                await _provider.SendErrorAsync("Missing payload", "Work style configuration is required");
                return;
            }

            try
            {
                var httpClient = _provider.GetHttpClient();
                if (httpClient == null || !httpClient.IsConnected())
                {
                    await _provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                await httpClient.PostAsync("/workstyle", payload.Value);
            }
            catch (Exception ex)
            {
                await _provider.SendErrorAsync("Apply work style error", ex.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
