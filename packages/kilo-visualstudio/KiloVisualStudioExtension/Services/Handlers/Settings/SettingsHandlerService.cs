using System;
using System.Text.Json;
using System.Threading.Tasks;
using KiloVisualStudioExtension.ApiClient;

namespace KiloVisualStudioExtension.Services.Handlers.Settings
{
    /// <summary>
    /// Handles settings-related operations like indexing settings, chat settings, throughput settings, autocomplete settings.
    /// This matches the VS Code pattern where settings handling is extracted into separate handler modules.
    /// </summary>
    public class SettingsHandlerService : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private bool _disposed;

        private VSProvider Provider => _serviceProvider.GetService<VSProvider>() 
            ?? throw new InvalidOperationException("VSProvider not registered in service provider");

        /// <summary>
        /// Creates a new SettingsHandlerService instance.
        /// </summary>
        /// <param name="serviceProvider">The service provider for dependency injection.</param>
        public SettingsHandlerService(ServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
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
            Provider.PostMessage(JsonSerializer.Serialize(message));
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
            Provider.PostMessage(JsonSerializer.Serialize(message));
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
            Provider.PostMessage(JsonSerializer.Serialize(message));
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
            Provider.PostMessage(JsonSerializer.Serialize(message));
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
                var nswagClient = Provider.GetNswagClient();
                if (nswagClient == null)
                {
                    await Provider.SendIndexingStatusAsync(JsonDocument.Parse("{}").RootElement);
                    return;
                }

                var status = await nswagClient.Indexing_statusAsync("", "");
                var statusData = status != null ? JsonSerializer.SerializeToElement(status) : JsonDocument.Parse("{}").RootElement;

                await Provider.SendIndexingStatusAsync(statusData);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] SettingsHandler: Error fetching indexing status: {ex.Message}");
                await Provider.SendIndexingStatusAsync(JsonDocument.Parse("{}").RootElement);
            }
        }

        /// <summary>
        /// Handles the requestWorkStyle message from the webview.
        /// Work style endpoint is not available in the current API.
        /// Sends default work style configuration.
        /// </summary>
        /// <param name="payload">The message payload (unused).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleRequestWorkStyleAsync(JsonElement? payload)
        {
            // Work style endpoint does not exist in current API
            await Provider.SendWorkStyleLoadedAsync(new { mode = "ask", autoApprove = new { enabled = false, limit = 0 } });
        }

        /// <summary>
        /// Handles the applyWorkStyle message from the webview.
        /// Work style endpoint is not available in the current API.
        /// Returns error indicating the feature is not supported.
        /// </summary>
        /// <param name="payload">The message payload containing the new work style.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleApplyWorkStyleAsync(JsonElement? payload)
        {
            // Work style endpoint does not exist in current API
            await Provider.SendErrorAsync("Not supported", "Work style configuration is not available in the current API");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}

