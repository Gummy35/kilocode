using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services
{
    /// <summary>
    /// Handles configuration-related operations like requestConfig, updateSetting, updateConfig.
    /// This matches the VS Code pattern where config handling is extracted into
    /// separate handler modules.
    /// </summary>
    public class ConfigHandlerService : IDisposable
    {
        private readonly VSProvider _provider;
        private bool _disposed;

        /// <summary>
        /// Creates a new ConfigHandlerService instance.
        /// </summary>
        /// <param name="provider">The VSProvider instance to use for webview communication.</param>
        public ConfigHandlerService(VSProvider provider)
        {
            _provider = provider;
        }

        /// <summary>
        /// Handles the requestConfig message from the webview.
        /// Fetches and sends the current configuration to the webview.
        /// </summary>
        /// <param name="payload">The message payload (unused for requestConfig).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleRequestConfigAsync(JsonElement? payload)
        {
            try
            {
                var httpClient = _provider.GetHttpClient();
                if (httpClient == null)
                {
                    await _provider.SendConfigLoadedAsync(JsonDocument.Parse("{}").RootElement, JsonDocument.Parse("{}").RootElement);
                    return;
                }

                var response = await httpClient.GetJsonAsync("/config");
                var config = JsonDocument.Parse("{}").RootElement;
                var features = JsonDocument.Parse("{}").RootElement;
                
                if (response != null)
                {
                    var root = response.RootElement.Clone();
                    if (root.TryGetProperty("config", out var c))
                        config = c.Clone();
                    if (root.TryGetProperty("features", out var f))
                        features = f.Clone();
                }

                await _provider.SendConfigLoadedAsync(config, features);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] ConfigHandler: Error fetching config: {ex.Message}");
                await _provider.SendConfigLoadedAsync(JsonDocument.Parse("{}").RootElement, JsonDocument.Parse("{}").RootElement);
            }
        }

        /// <summary>
        /// Handles the updateSetting message from the webview.
        /// Updates a specific setting in the configuration.
        /// </summary>
        /// <param name="payload">The message payload containing setting name and value.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleUpdateSettingAsync(JsonElement? payload)
        {
            if (payload == null)
            {
                await _provider.SendErrorAsync("Missing payload", "Payload is required for updateSetting");
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

                await httpClient.PostAsync("/config", payload.Value);
            }
            catch (Exception ex)
            {
                await _provider.SendErrorAsync("Update setting error", ex.Message);
            }
        }

        /// <summary>
        /// Handles the updateConfig message from the webview.
        /// Updates the entire configuration.
        /// </summary>
        /// <param name="payload">The message payload containing the new configuration.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleUpdateConfigAsync(JsonElement? payload)
        {
            if (payload == null)
            {
                await _provider.SendErrorAsync("Missing payload", "Payload is required for updateConfig");
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

                await httpClient.PostAsync("/config", payload.Value);
            }
            catch (Exception ex)
            {
                await _provider.SendErrorAsync("Update config error", ex.Message);
            }
        }

        /// <summary>
        /// Handles the openConfigFile message from the webview.
        /// Opens the configuration file in the editor.
        /// </summary>
        /// <param name="payload">The message payload containing scope.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleOpenConfigFileAsync(JsonElement? payload)
        {
            if (payload == null) return;
            
            System.Diagnostics.Debug.WriteLine("[Kilo] ConfigHandler: openConfigFile");
            
            string scope = "global";
            if (payload.Value.TryGetProperty("scope", out var scopeProp))
            {
                scope = scopeProp.GetString() ?? "global";
            }
            
            try
            {
                var httpClient = _provider.GetHttpClient();
                if (httpClient == null) return;
                
                var url = $"/config/file?scope={scope}";
                var responseDoc = await httpClient.GetJsonAsync(url);
                
                if (responseDoc != null && responseDoc.RootElement.TryGetProperty("path", out var pathProp))
                {
                    string filePath = pathProp.GetString() ?? "";
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = filePath,
                            UseShellExecute = true
                        });
                    }
                }
                responseDoc?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] ConfigHandler: error opening config file: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
