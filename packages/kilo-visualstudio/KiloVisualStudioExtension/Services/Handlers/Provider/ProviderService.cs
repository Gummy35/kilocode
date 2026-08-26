using System;
using System.Text.Json;
using System.Threading.Tasks;
using KiloVisualStudioExtension.ApiClient;

namespace KiloVisualStudioExtension.Services.Handlers.Provider
{
    /// <summary>
    /// Handles provider action operations like connect, disconnect, and OAuth authorization.
    /// Matches the VS Code pattern in provider-actions.ts.
    /// </summary>
    public class ProviderService : ServiceProviderServiceBase
    {
        private bool _disposed;

        private VSProvider Provider => _serviceProvider.GetService<VSProvider>() 
            ?? throw new InvalidOperationException("VSProvider not registered in service provider");

        public ProviderService(ServiceProvider serviceProvider):base(serviceProvider)
        {
        }

        /// <summary>
        /// Handles connectProvider message - connects a provider with API key.
        /// </summary>
        public async Task HandleConnectProviderAsync(JsonElement? payload)
        {
            if (!payload.HasValue) return;

            var requestId = payload.Value.TryGetProperty("requestId", out var rid) ? rid.GetString() : "";
            var providerID = payload.Value.TryGetProperty("providerID", out var pid) ? pid.GetString() : "";
            var apiKey = payload.Value.TryGetProperty("apiKey", out var key) ? key.GetString() : "";

            if (string.IsNullOrEmpty(requestId) || string.IsNullOrEmpty(providerID) || string.IsNullOrEmpty(apiKey))
            {
                PostProviderError(requestId, providerID, "connect", "Missing required parameters");
                return;
            }

            try
            {
                var nswagClient = Provider.GetNswagClient();
                if (nswagClient == null)
                {
                    PostProviderError(requestId, providerID, "connect", "Not connected to backend");
                    return;
                }

                var authBody = new KiloVisualStudioExtension.ApiClient.Auth();
                authBody.AdditionalProperties["type"] = "api";
                authBody.AdditionalProperties["key"] = apiKey;

                await nswagClient.Auth_setAsync(providerID, authBody);
                await Provider.DisposeGlobal();
                await Provider.FetchAndSendProvidersAsync();
                
                Provider.PostMessage(JsonSerializer.Serialize(new 
                { 
                    type = "providerConnected", 
                    requestId, 
                    providerID 
                }));
            }
            catch (Exception ex)
            {
                PostProviderError(requestId, providerID, "connect", ex.Message);
            }
        }

        /// <summary>
        /// Handles disconnectProvider message - disconnects a provider.
        /// </summary>
        public async Task HandleDisconnectProviderAsync(JsonElement? payload)
        {
            if (!payload.HasValue) return;

            var requestId = payload.Value.TryGetProperty("requestId", out var rid) ? rid.GetString() : "";
            var providerID = payload.Value.TryGetProperty("providerID", out var pid) ? pid.GetString() : "";

            if (string.IsNullOrEmpty(requestId) || string.IsNullOrEmpty(providerID))
            {
                PostProviderError(requestId, providerID, "disconnect", "Missing required parameters");
                return;
            }

            try
            {
                var nswagClient = Provider.GetNswagClient();
                if (nswagClient == null)
                {
                    PostProviderError(requestId, providerID, "disconnect", "Not connected to backend");
                    return;
                }

                // Remove auth
                await nswagClient.Auth_removeAsync(providerID);

                if (providerID == "kilo")
                {
                    Provider.PostMessage(JsonSerializer.Serialize(new { type = "profileData", data = (object)null }));
                }

                await Provider.DisposeGlobal();
                await Provider.FetchAndSendProvidersAsync();

                Provider.PostMessage(JsonSerializer.Serialize(new 
                { 
                    type = "providerDisconnected", 
                    requestId, 
                    providerID 
                }));
            }
            catch (Exception ex)
            {
                PostProviderError(requestId, providerID, "disconnect", ex.Message);
            }
        }

        /// <summary>
        /// Handles authorizeProviderOAuth message - initiates OAuth authorization.
        /// </summary>
        public async Task HandleAuthorizeProviderOAuthAsync(JsonElement? payload)
        {
            if (!payload.HasValue) return;

            var requestId = payload.Value.TryGetProperty("requestId", out var rid) ? rid.GetString() : "";
            var providerID = payload.Value.TryGetProperty("providerID", out var pid) ? pid.GetString() : "";
            var method = payload.Value.TryGetProperty("method", out var m) ? m.GetInt32() : 0;

            if (string.IsNullOrEmpty(requestId) || string.IsNullOrEmpty(providerID))
            {
                PostProviderError(requestId, providerID, "authorize", "Missing required parameters");
                return;
            }

            try
            {
                var nswagClient = Provider.GetNswagClient();
                if (nswagClient == null)
                {
                    PostProviderError(requestId, providerID, "authorize", "Not connected to backend");
                    return;
                }

                var directory = Provider.GetWorkspaceDirectory();
                var auth = await nswagClient.Provider_oauth_authorizeAsync(providerID, directory, "", new Body16
                {
                    Method = method
                });

                Provider.PostMessage(JsonSerializer.Serialize(new 
                { 
                    type = "providerOAuthReady", 
                    requestId, 
                    providerID,
                    authorization = auth
                }));
            }
            catch (Exception ex)
            {
                PostProviderError(requestId, providerID, "authorize", ex.Message);
            }
        }

        /// <summary>
        /// Handles completeProviderOAuth message - completes OAuth callback.
        /// </summary>
        public async Task HandleCompleteProviderOAuthAsync(JsonElement? payload)
        {
            if (!payload.HasValue) return;

            var requestId = payload.Value.TryGetProperty("requestId", out var rid) ? rid.GetString() : "";
            var providerID = payload.Value.TryGetProperty("providerID", out var pid) ? pid.GetString() : "";
            var method = payload.Value.TryGetProperty("method", out var m) ? m.GetInt32() : 0;
            var code = payload.Value.TryGetProperty("code", out var c) ? c.GetString() : "";

            if (string.IsNullOrEmpty(requestId) || string.IsNullOrEmpty(providerID))
            {
                PostProviderError(requestId, providerID, "connect", "Missing required parameters");
                return;
            }

            try
            {
                var nswagClient = Provider.GetNswagClient();
                if (nswagClient == null)
                {
                    PostProviderError(requestId, providerID, "connect", "Not connected to backend");
                    return;
                }

                var directory = Provider.GetWorkspaceDirectory();
                await nswagClient.Provider_oauth_callbackAsync(providerID, directory, "", new Body17
                {
                    Code = string.IsNullOrEmpty(code) ? null : code
                });

                await Provider.DisposeGlobal();
                await Provider.FetchAndSendProvidersAsync();

                Provider.PostMessage(JsonSerializer.Serialize(new 
                { 
                    type = "providerConnected", 
                    requestId, 
                    providerID 
                }));
            }
            catch (Exception ex)
            {
                PostProviderError(requestId, providerID, "connect", ex.Message);
            }
        }

        /// <summary>
        /// Handles saveCustomProvider message - saves a custom provider configuration.
        /// For now, handles API key storage. Config updates are handled separately.
        /// </summary>
        public async Task HandleSaveCustomProviderAsync(JsonElement? payload)
        {
            if (!payload.HasValue) return;

            var requestId = payload.Value.TryGetProperty("requestId", out var rid) ? rid.GetString() : "";
            var providerID = payload.Value.TryGetProperty("providerID", out var pid) ? pid.GetString() : "";
            
            if (string.IsNullOrEmpty(requestId) || string.IsNullOrEmpty(providerID))
            {
                PostProviderError(requestId, providerID, "connect", "Missing required parameters");
                return;
            }

            try
            {
                var nswagClient = Provider.GetNswagClient();
                if (nswagClient == null)
                {
                    PostProviderError(requestId, providerID, "connect", "Not connected to backend");
                    return;
                }

                var apiKey = payload.Value.TryGetProperty("apiKey", out var key) ? key.GetString() : null;
                var apiKeyChanged = payload.Value.TryGetProperty("apiKeyChanged", out var changed) ? changed.GetBoolean() : false;

                // Handle API key if changed
                if (apiKeyChanged)
                {
                    if (!string.IsNullOrEmpty(apiKey))
                    {
                        var authBody = new KiloVisualStudioExtension.ApiClient.Auth();
                        authBody.AdditionalProperties["type"] = "api";
                        authBody.AdditionalProperties["key"] = apiKey;
                        await nswagClient.Auth_setAsync(providerID, authBody);
                    }
                    else
                    {
                        // Clear API key
                        await nswagClient.Auth_removeAsync(providerID);
                    }
                }

                await Provider.DisposeGlobal();
                await Provider.FetchAndSendProvidersAsync();

                Provider.PostMessage(JsonSerializer.Serialize(new 
                { 
                    type = "providerConnected", 
                    requestId, 
                    providerID 
                }));
            }
            catch (Exception ex)
            {
                PostProviderError(requestId, providerID, "connect", ex.Message);
            }
        }

        private void PostProviderError(string requestId, string providerID, string action, string message)
        {
            Provider.PostMessage(JsonSerializer.Serialize(new 
            { 
                type = "providerActionError", 
                requestId, 
                providerID, 
                action, 
                message 
            }));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
  }
}
