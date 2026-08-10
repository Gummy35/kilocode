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
            var message = new { type = "chatSettingsLoaded", settings = new { shiftTabCyclesVariant = true } };
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
                    provider = (string?)"", 
                    model = (string?)"" 
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
                var kiotaClient = _provider.GetKiloClient();
                if (kiotaClient == null)
                {
                    await _provider.SendIndexingStatusAsync(JsonDocument.Parse("{}").RootElement);
                    return;
                }

                var status = await kiotaClient.Indexing.GetAsync();
                var statusData = status != null ? JsonSerializer.SerializeToElement(status) : JsonDocument.Parse("{}").RootElement;

                await _provider.SendIndexingStatusAsync(statusData);
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
                var kiotaClient = _provider.GetKiloClient();
                if (kiotaClient == null)
                {
                    await _provider.SendWorkStyleLoadedAsync(new { mode = "ask", autoApprove = new { enabled = false, limit = 0 } });
                    return;
                }

                var style = await kiotaClient.WorkStyle.GetAsync();
                var styleData = style != null ? new { mode = style.Mode ?? "ask", autoApprove = new { enabled = style.AutoApprove?.Enabled ?? false, limit = style.AutoApprove?.Limit ?? 0 } } : new { mode = "ask", autoApprove = new { enabled = false, limit = 0 } };

                await _provider.SendWorkStyleLoadedAsync(styleData);
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
                var kiotaClient = _provider.GetKiloClient();
                if (kiotaClient == null)
                {
                    await _provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                var workStyle = JsonSerializer.Deserialize<Generated.Models.WorkStyle>(payload.Value.GetRawText());
                if (workStyle != null)
                {
                    await kiotaClient.WorkStyle.PatchAsync(workStyle);
                }
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
