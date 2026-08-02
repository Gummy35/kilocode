using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services.Handlers.AgentRequest
{
    /// <summary>
    /// Handles agent-related operations like requestAgents.
    /// This matches the VS Code pattern where agent handling is extracted into separate handler modules.
    /// </summary>
    public class AgentRequestService : IDisposable
    {
        private readonly VSProvider _provider;
        private bool _disposed;

        /// <summary>
        /// Creates a new AgentRequestService instance.
        /// </summary>
        /// <param name="provider">The VSProvider instance to use for webview communication.</param>
        public AgentRequestService(VSProvider provider)
        {
            _provider = provider;
        }

        /// <summary>
        /// Handles the requestAgents message from the webview.
        /// Fetches and sends the list of available agents to the webview.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleRequestAgentsAsync()
        {
            var httpClient = _provider.GetHttpClient();
            if (httpClient == null)
            {
                await SendEmptyAgentsAsync();
                return;
            }

            try
            {
                // Use /app/agents endpoint like VS Code does (not /experimental/tool/ids)
                var responseDoc = await httpClient.GetJsonAsync("/agent");
                var agentsList = new System.Collections.Generic.List<object>();
                
                if (responseDoc != null)
                {
                    var root = responseDoc.RootElement.Clone();
                    responseDoc.Dispose();
                    
                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var agent in root.EnumerateArray())
                        {
                            // Filter out hidden agents and subagent mode (matching VS Code's filterVisibleAgents)
                            if (agent.TryGetProperty("mode", out var modeProp) && modeProp.GetString() == "subagent")
                                continue;
                            if (agent.TryGetProperty("hidden", out var hiddenProp) && hiddenProp.GetBoolean())
                                continue;
                            
                            // Map agent to the subset of fields sent to webview (matching VS Code's mapAgent)
                            JsonElement? permissionElement = null;
                            if (agent.TryGetProperty("permission", out var perm))
                            {
                                permissionElement = perm.Clone();
                            }
                            
                            var mappedAgent = new
                            {
                                name = agent.TryGetProperty("name", out var name) ? name.GetString() : "",
                                description = agent.TryGetProperty("description", out var desc) ? desc.GetString() : "",
                                mode = agent.TryGetProperty("mode", out var m) ? m.GetString() : "",
                                native = agent.TryGetProperty("native", out var nat) && nat.ValueKind == JsonValueKind.True,
                                hidden = agent.TryGetProperty("hidden", out var h) && h.ValueKind == JsonValueKind.True,
                                color = agent.TryGetProperty("color", out var c) ? c.GetString() : "",
                                deprecated = agent.TryGetProperty("deprecated", out var d) && d.ValueKind == JsonValueKind.True,
                                permission = permissionElement,
                                model = agent.TryGetProperty("model", out var model) ? model.GetString() : ""
                            };
                            agentsList.Add(mappedAgent);
                        }
                    }
                }
                
                // Determine default agent (first visible agent, or "code" as fallback)
                string defaultAgent = "code";
                if (agentsList.Count > 0)
                {
                    var firstAgent = agentsList[0];
                    if (firstAgent is JsonElement firstElement && 
                        firstElement.TryGetProperty("name", out var nameElement))
                    {
                        defaultAgent = nameElement.GetString() ?? "code";
                    }
                }
                
                var message = new 
                { 
                    type = "agentsLoaded", 
                    agents = agentsList.ToArray(),
                    allAgents = agentsList.ToArray(),
                    defaultAgent = defaultAgent
                };
                _provider.PostMessage(JsonSerializer.Serialize(message));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] AgentRequest: error fetching agents: {ex.Message}");
                await SendEmptyAgentsAsync();
            }
        }

        private async Task SendEmptyAgentsAsync()
        {
            var message = new 
            { 
                type = "agentsLoaded", 
                agents = Array.Empty<object>(),
                allAgents = Array.Empty<object>(),
                defaultAgent = "code"
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
