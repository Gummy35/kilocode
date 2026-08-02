using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services
{
    /// <summary>
    /// Handles MCP (Model Context Protocol) related operations like connectMcp, disconnectMcp, authenticateMcp.
    /// This matches the VS Code pattern where MCP handling is extracted into separate handler modules.
    /// </summary>
    public class McpHandlerService : IDisposable
    {
        private readonly VSProvider _provider;
        private bool _disposed;

        /// <summary>
        /// Creates a new McpHandlerService instance.
        /// </summary>
        /// <param name="provider">The VSProvider instance to use for webview communication.</param>
        public McpHandlerService(VSProvider provider)
        {
            _provider = provider;
        }

        /// <summary>
        /// Handles the requestMcpStatus message from the webview.
        /// Fetches and sends the current MCP server status to the webview.
        /// </summary>
        /// <param name="payload">The message payload (unused for requestMcpStatus).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleRequestMcpStatusAsync(JsonElement? payload)
        {
            try
            {
                var httpClient = _provider.GetHttpClient();
                if (httpClient == null)
                {
                    await _provider.SendMcpStatusAsync(JsonDocument.Parse("{}").RootElement);
                    return;
                }

                var response = await httpClient.GetJsonAsync("/mcp");
                var mcpStatus = JsonDocument.Parse("{}").RootElement;
                
                if (response != null)
                {
                    mcpStatus = response.RootElement.Clone();
                }

                await _provider.SendMcpStatusAsync(mcpStatus);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] McpHandler: Error fetching MCP status: {ex.Message}");
                await _provider.SendMcpStatusAsync(JsonDocument.Parse("{}").RootElement);
            }
        }

        /// <summary>
        /// Handles the connectMcp message from the webview.
        /// Connects to an MCP server.
        /// </summary>
        /// <param name="payload">The message payload containing MCP server configuration.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleConnectMcpAsync(JsonElement? payload)
        {
            if (payload == null)
            {
                await _provider.SendErrorAsync("Missing payload", "MCP configuration is required");
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

                await httpClient.PostAsync("/mcp/connect", payload.Value);
            }
            catch (Exception ex)
            {
                await _provider.SendErrorAsync("Connect MCP error", ex.Message);
            }
        }

        /// <summary>
        /// Handles the disconnectMcp message from the webview.
        /// Disconnects from an MCP server.
        /// </summary>
        /// <param name="payload">The message payload containing MCP server ID.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleDisconnectMcpAsync(JsonElement? payload)
        {
            if (payload == null || !payload.Value.TryGetProperty("serverId", out var serverIdProp))
            {
                await _provider.SendErrorAsync("Missing serverId", "serverId is required");
                return;
            }

            var serverId = serverIdProp.GetString();
            if (string.IsNullOrEmpty(serverId))
            {
                await _provider.SendErrorAsync("Invalid serverId", "serverId cannot be empty");
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

                await httpClient.PostAsync($"/mcp/{serverId}/disconnect", null);
            }
            catch (Exception ex)
            {
                await _provider.SendErrorAsync("Disconnect MCP error", ex.Message);
            }
        }

        /// <summary>
        /// Handles the authenticateMcp message from the webview.
        /// Initiates OAuth authentication for an MCP server.
        /// </summary>
        /// <param name="payload">The message payload containing MCP server ID and auth details.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleAuthenticateMcpAsync(JsonElement? payload)
        {
            if (payload == null)
            {
                await _provider.SendErrorAsync("Missing payload", "Authentication details are required");
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

                await httpClient.PostAsync("/mcp/authenticate", payload.Value);
            }
            catch (Exception ex)
            {
                await _provider.SendErrorAsync("Authenticate MCP error", ex.Message);
            }
        }

        /// <summary>
        /// Handles the removeMcp message from the webview.
        /// Removes an MCP server configuration.
        /// </summary>
        /// <param name="payload">The message payload containing MCP server ID.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleRemoveMcpAsync(JsonElement? payload)
        {
            if (payload == null || !payload.Value.TryGetProperty("serverId", out var serverIdProp))
            {
                await _provider.SendErrorAsync("Missing serverId", "serverId is required");
                return;
            }

            var serverId = serverIdProp.GetString();
            if (string.IsNullOrEmpty(serverId))
            {
                await _provider.SendErrorAsync("Invalid serverId", "serverId cannot be empty");
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

                await httpClient.PostAsync($"/mcp/{serverId}/remove", null);
            }
            catch (Exception ex)
            {
                await _provider.SendErrorAsync("Remove MCP error", ex.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
