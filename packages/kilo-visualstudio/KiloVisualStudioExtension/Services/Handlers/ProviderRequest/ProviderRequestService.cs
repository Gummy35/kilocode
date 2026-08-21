using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KiloVisualStudioExtension.ApiClient;

namespace KiloVisualStudioExtension.Services.Handlers.ProviderRequest
{
    /// <summary>
    /// Handles provider-related operations like requestProviders.
    /// This matches the VS Code pattern where provider handling is extracted into separate handler modules.
    /// </summary>
    public class ProviderRequestService : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private bool _disposed;

        private VSProvider Provider => _serviceProvider.GetService<VSProvider>() 
            ?? throw new InvalidOperationException("VSProvider not registered in service provider");

        /// <summary>
        /// Creates a new ProviderRequestService instance.
        /// </summary>
        /// <param name="serviceProvider">The service provider for dependency injection.</param>
        public ProviderRequestService(ServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Handles the requestProviders message from the webview.
        /// Fetches and sends the list of available providers to the webview.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleRequestProvidersAsync()
        {
            var nswagClient = Provider.GetNswagClient();
            if (nswagClient == null)
            {
                await SendEmptyProvidersAsync();
                return;
            }

            try
            {
                var response = await nswagClient.Provider_listAsync("", "");
                
                var providersDict = new Dictionary<string, object>();
                var connectedList = new List<string>();
                var defaultsDict = new Dictionary<string, string>();
                
                if (response != null)
                {
                    if (response.All != null)
                    {
                        foreach (var provider in response.All)
                        {
                            if (provider.Id != null)
                            {
                                providersDict[provider.Id] = JsonSerializer.SerializeToElement(provider);
                            }
                        }
                    }
                    
                    if (response.Connected != null)
                    {
                        connectedList.AddRange(response.Connected.Where(s => !string.IsNullOrEmpty(s)));
                    }
                    
                    if (response.Default != null)
                    {
                        foreach (var kvp in response.Default)
                        {
                            defaultsDict[kvp.Key] = kvp.Value ?? "";
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
                Provider.PostMessage(JsonSerializer.Serialize(message));
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
            Provider.PostMessage(JsonSerializer.Serialize(message));
            await Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}

