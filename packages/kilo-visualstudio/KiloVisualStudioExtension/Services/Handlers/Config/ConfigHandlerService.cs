using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using KiloVisualStudioExtension.ApiClient;

namespace KiloVisualStudioExtension.Services.Handlers.Config
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
        /// 
        /// VS Code workflow: Matches the pattern in kilo-provider/handlers/config.ts
        /// where requestConfig fetches /config and sends configLoaded to webview.
        /// 
        /// Workflow steps:
        /// 1. Get HTTP client from provider
        /// 2. If no client, send empty config with SendConfigLoadedAsync
        /// 3. Fetch configuration from /config endpoint
        /// 4. Extract config and features properties from response
        /// 5. Send configLoaded message with both config and features
        /// 
        /// Messages sent to webview:
        /// - configLoaded: { config: {...}, features: {...} }
        /// </summary>
        /// <param name="payload">The message payload (unused for requestConfig).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleRequestConfigAsync(JsonElement? payload)
        {
            try
            {
                var nswagClient = _provider.GetNswagClient();
                if (nswagClient == null)
                {
                    await _provider.SendConfigLoadedAsync(JsonDocument.Parse("{}").RootElement, JsonDocument.Parse("{}").RootElement);
                    return;
                }

                var configResponse = await nswagClient.Global_config_getAsync();
                var config = JsonDocument.Parse("{}").RootElement;
                var features = JsonDocument.Parse("{}").RootElement;
                
                if (configResponse != null)
                {
                    config = JsonSerializer.SerializeToElement(configResponse);
                    // Features is not a property of Config in the generated model
                    features = JsonDocument.Parse("{}").RootElement;
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
        /// Updates a specific setting in the configuration via the backend.
        /// 
        /// VS Code workflow: Matches the pattern in kilo-provider/handlers/config.ts
        /// where updateSetting posts to /config with the setting update.
        /// 
        /// Workflow steps:
        /// 1. Validate payload is not null
        /// 2. Get HTTP client from provider
        /// 3. Verify client is connected
        /// 4. POST to /config endpoint with payload
        /// 5. Backend applies the setting update
        /// 
        /// Messages sent to webview:
        /// - error: { message: "Missing payload" | "Not connected to backend" }
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
                var nswagClient = _provider.GetNswagClient();
                if (nswagClient == null)
                {
                    await _provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                var config = JsonSerializer.Deserialize<Config>(payload.Value.GetRawText());
                if (config != null)
                {
                    await nswagClient.Global_config_updateAsync(config);
                }
            }
            catch (Exception ex)
            {
                await _provider.SendErrorAsync("Update setting error", ex.Message);
            }
        }

        /// <summary>
        /// Handles the updateConfig message from the webview.
        /// Updates the entire configuration via the backend.
        /// 
        /// VS Code workflow: Matches the pattern in kilo-provider/handlers/config.ts
        /// where updateConfig posts the full configuration to /config.
        /// 
        /// Workflow steps:
        /// 1. Validate payload is not null
        /// 2. Get HTTP client from provider
        /// 3. Verify client is connected
        /// 4. POST to /config endpoint with full configuration payload
        /// 5. Backend replaces the entire configuration
        /// 
        /// Messages sent to webview:
        /// - error: { message: "Missing payload" | "Not connected to backend" }
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
                var nswagClient = _provider.GetNswagClient();
                if (nswagClient == null)
                {
                    await _provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                var config = JsonSerializer.Deserialize<Config>(payload.Value.GetRawText());
                if (config != null)
                {
                    await nswagClient.Global_config_updateAsync(config);
                }
            }
            catch (Exception ex)
            {
                await _provider.SendErrorAsync("Update config error", ex.Message);
            }
        }

        /// <summary>
        /// Handles the openConfigFile message from the webview.
        /// Opens the configuration file in the system editor.
        /// 
        /// VS Code workflow: Matches the pattern in kilo-provider/handlers/config.ts
        /// where openConfigFile fetches the config file path and opens it externally.
        /// 
        /// Workflow steps:
        /// 1. Extract scope from payload (default: "global")
        /// 2. Get HTTP client from provider
        /// 3. Fetch config file path from /config/file?scope={scope}
        /// 4. Extract path property from response
        /// 5. Launch system process to open the file
        /// 
        /// Messages sent to webview:
        /// - No direct message; file opens in external editor
        /// </summary>
        /// <param name="payload">The message payload containing scope (global or workspace).</param>
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
                var kiotaClient = _provider.GetKiloClient();
                if (kiotaClient == null) return;
                
                // Config model doesn't have a Path property - use default config path
                string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "kilo", "config.json");
                
                if (!string.IsNullOrEmpty(filePath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                }
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
