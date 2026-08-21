using System;
using System.Text.Json;
using System.Threading.Tasks;
using KiloVisualStudioExtension.ApiClient;

namespace KiloVisualStudioExtension.Services.Handlers.Mcp
{
    /// <summary>
    /// Handles MCP (Model Context Protocol) related operations like connectMcp, disconnectMcp, authenticateMcp.
    /// This matches the VS Code pattern where MCP handling is extracted into separate handler modules.
    /// </summary>
    public class McpHandlerService : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private bool _disposed;

        private VSProvider Provider => _serviceProvider.GetService<VSProvider>() 
            ?? throw new InvalidOperationException("VSProvider not registered in service provider");

        /// <summary>
        /// Creates a new McpHandlerService instance.
        /// </summary>
        /// <param name="serviceProvider">The service provider for dependency injection.</param>
        public McpHandlerService(ServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Handles the requestMcpStatus message from the webview.
        /// Fetches and sends the current MCP server status to the webview.
        /// 
        /// VS Code workflow: Matches the pattern in MCP handler modules where
        /// MCP status is fetched from /mcp endpoint and sent to webview.
        /// 
        /// Workflow steps:
        /// 1. Get HTTP client from provider
        /// 2. If no client, send empty MCP status
        /// 3. Fetch MCP status from /mcp endpoint
        /// 4. Extract MCP status from response
        /// 5. Send MCP status to webview via SendMcpStatusAsync
        /// 
        /// Messages sent to webview:
        /// - mcpStatus: { servers: [...], connected: [...] }
        /// </summary>
        /// <param name="payload">The message payload (unused for requestMcpStatus).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleRequestMcpStatusAsync(JsonElement? payload)
        {
            try
            {
                var nswagClient = Provider.GetNswagClient();
                if (nswagClient == null)
                {
                    await Provider.SendMcpStatusAsync(JsonDocument.Parse("{}").RootElement);
                    return;
                }

                var directory = System.Environment.CurrentDirectory;
                var mcpStatusDict = await nswagClient.Mcp_statusAsync(directory, "");
                var mcpStatus = mcpStatusDict != null && mcpStatusDict.Count > 0 
                    ? JsonSerializer.SerializeToElement(mcpStatusDict) 
                    : JsonDocument.Parse("{}").RootElement;

                await Provider.SendMcpStatusAsync(mcpStatus);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] McpHandler: Error fetching MCP status: {ex.Message}");
                await Provider.SendMcpStatusAsync(JsonDocument.Parse("{}").RootElement);
            }
        }

        /// <summary>
        /// Handles the connectMcp message from the webview.
        /// Connects to an MCP server with the provided configuration.
        /// 
        /// VS Code workflow: Matches the pattern in MCP handler modules where
        /// MCP server connection is established via /mcp/connect endpoint.
        /// 
        /// Workflow steps:
        /// 1. Validate payload is not null
        /// 2. Get HTTP client from provider
        /// 3. Verify client is connected
        /// 4. POST to /mcp/connect with MCP server configuration
        /// 
        /// Messages sent to webview:
        /// - error: { message: "Missing payload" | "Not connected to backend" }
        /// </summary>
        /// <param name="payload">The message payload containing MCP server configuration (serverId, type, command/url, etc.).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleConnectMcpAsync(JsonElement? payload)
        {
            if (payload == null)
            {
                await Provider.SendErrorAsync("Missing payload", "MCP configuration is required");
                return;
            }

            try
            {
                var nswagClient = Provider.GetNswagClient();
                if (nswagClient == null)
                {
                    await Provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                if (payload.Value.TryGetProperty("serverId", out var serverIdProp))
                {
                    var serverId = serverIdProp.GetString();
                    if (!string.IsNullOrEmpty(serverId))
                    {
                        await nswagClient.Mcp_connectAsync(serverId, System.Environment.CurrentDirectory, "");
                    }
                }
            }
            catch (Exception ex)
            {
                await Provider.SendErrorAsync("Connect MCP error", ex.Message);
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
                await Provider.SendErrorAsync("Missing serverId", "serverId is required");
                return;
            }

            var serverId = serverIdProp.GetString();
            if (string.IsNullOrEmpty(serverId))
            {
                await Provider.SendErrorAsync("Invalid serverId", "serverId cannot be empty");
                return;
            }

            try
            {
                var nswagClient = Provider.GetNswagClient();
                if (nswagClient == null)
                {
                    await Provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                await nswagClient.Mcp_disconnectAsync(serverId, System.Environment.CurrentDirectory, "");
            }
            catch (Exception ex)
            {
                await Provider.SendErrorAsync("Disconnect MCP error", ex.Message);
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
            if (payload == null || !payload.Value.TryGetProperty("serverId", out var serverIdProp))
            {
                await Provider.SendErrorAsync("Missing serverId", "serverId is required");
                return;
            }

            var serverId = serverIdProp.GetString();
            if (string.IsNullOrEmpty(serverId))
            {
                await Provider.SendErrorAsync("Invalid serverId", "serverId cannot be empty");
                return;
            }

            try
            {
                var nswagClient = Provider.GetNswagClient();
                if (nswagClient == null)
                {
                    await Provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                await nswagClient.Mcp_auth_authenticateAsync(serverId, System.Environment.CurrentDirectory, "");
            }
            catch (Exception ex)
            {
                await Provider.SendErrorAsync("Authenticate MCP error", ex.Message);
            }
        }

        /// <summary>
        /// Handles the removeMcp message from the webview.
        /// Removes an MCP server configuration.
        /// 
        /// NOTE: NSwag client does not have Mcp_removeAsync method. The Kiota implementation
        /// referenced kiotaClient.Mcp[serverId].Remove.PostAsync() but this endpoint does not
        /// exist in the generated NSwag client. This is a known limitation - MCP removal
        /// functionality is not yet available via NSwag.
        /// </summary>
        /// <param name="payload">The message payload containing MCP server ID.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleRemoveMcpAsync(JsonElement? payload)
        {
            if (payload == null || !payload.Value.TryGetProperty("serverId", out var serverIdProp))
            {
                await Provider.SendErrorAsync("Missing serverId", "serverId is required");
                return;
            }

            var serverId = serverIdProp.GetString();
            if (string.IsNullOrEmpty(serverId))
            {
                await Provider.SendErrorAsync("Invalid serverId", "serverId cannot be empty");
                return;
            }

            try
            {
                var nswagClient = Provider.GetNswagClient();
                if (nswagClient == null)
                {
                    await Provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                // TODO: NSwag client needs Mcp_removeAsync method added
                // await nswagClient.Mcp_removeAsync(serverId, System.Environment.CurrentDirectory, "");
                await Provider.SendErrorAsync("Not implemented", "MCP removal is not yet supported via NSwag");
            }
            catch (Exception ex)
            {
                await Provider.SendErrorAsync("Remove MCP error", ex.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}

