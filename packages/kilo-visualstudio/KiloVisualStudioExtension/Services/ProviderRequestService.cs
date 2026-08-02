using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services
{
    /// <summary>
    /// Handles provider-related operations like requestProviders.
    /// This matches the VS Code pattern where provider handling is extracted into separate handler modules.
    /// </summary>
    public class ProviderRequestService : IDisposable
    {
        private readonly VSProvider _provider;
        private bool _disposed;

        /// <summary>
        /// Creates a new ProviderRequestService instance.
        /// </summary>
        /// <param name="provider">The VSProvider instance to use for webview communication.</param>
        public ProviderRequestService(VSProvider provider)
        {
            _provider = provider;
        }

        /// <summary>
        /// Handles the requestProviders message from the webview.
        /// Fetches and sends the list of available providers to the webview.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleRequestProvidersAsync()
        {
            var httpClient = _provider.GetHttpClient();
            if (httpClient == null)
            {
                await SendEmptyProvidersAsync();
                return;
            }

            try
            {
                var responseDoc = await httpClient.GetJsonAsync("/provider");
                
                // Convert providers array to Record<string, Provider> format
                var providersDict = new Dictionary<string, object>();
                var connectedList = new List<string>();
                var defaultsDict = new Dictionary<string, string>();
                
                if (responseDoc != null)
                {
                    var root = responseDoc.RootElement.Clone();
                    responseDoc.Dispose();
                    
                    // Parse "all" array and convert to dictionary
                    if (root.TryGetProperty("all", out var all) && all.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var provider in all.EnumerateArray())
                        {
                            if (provider.TryGetProperty("id", out var id))
                            {
                                providersDict[id.GetString() ?? ""] = provider.Clone();
                            }
                        }
                    }
                    
                    // Parse "connected" array
                    if (root.TryGetProperty("connected", out var connected) && connected.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in connected.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                connectedList.Add(item.GetString() ?? "");
                            }
                        }
                    }
                    
                    // Parse "default" object
                    if (root.TryGetProperty("default", out var defaults) && defaults.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in defaults.EnumerateObject())
                        {
                            defaultsDict[prop.Name] = prop.Value.GetString() ?? "";
                        }
                    }
                }
                
                var message = new 
                { 
                    type = "providersLoaded", 
                    providers = providersDict,
                    connected = connectedList.ToArray(),
                    defaults = defaultsDict,
                    defaultSelection = new { },
                    authMethods = new Dictionary<string, object[]>(),
                    authStates = new Dictionary<string, object>()
                };
                _provider.PostMessage(JsonSerializer.Serialize(message));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] ProviderRequest: error fetching providers: {ex.Message}");
                await SendEmptyProvidersAsync();
            }
        }

        private async Task SendEmptyProvidersAsync()
        {
            var message = new
            {
                type = "providersLoaded",
                providers = new Dictionary<string, object>(),
                connected = Array.Empty<string>(),
                defaults = new Dictionary<string, string>(),
                defaultSelection = new { },
                authMethods = new Dictionary<string, object[]>(),
                authStates = new Dictionary<string, object>()
            };
            _provider.PostMessage(JsonSerializer.Serialize(message));
            await Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
