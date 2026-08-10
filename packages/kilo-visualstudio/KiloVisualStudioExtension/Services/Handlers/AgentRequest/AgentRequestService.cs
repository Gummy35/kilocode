using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KiloVisualStudioExtension.Generated;

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
            var kiotaClient = _provider.GetKiloClient();
            if (kiotaClient == null)
            {
                await SendEmptyAgentsAsync();
                return;
            }

            try
            {
                var agents = await kiotaClient.Agent.GetAsync();
                var agentsList = new List<object>();
                
                if (agents != null)
                {
                    foreach (var agent in agents)
                    {
                        if (agent.Mode == Generated.Models.Agent_mode.Subagent)
                            continue;
                        if (agent.Hidden == true)
                            continue;
                        
                        var mappedAgent = new
                        {
                            name = agent.Name ?? "",
                            description = agent.Description ?? "",
                            mode = agent.Mode?.ToString() ?? "",
                            native = agent.Native == true,
                            hidden = agent.Hidden == true,
                            color = agent.Color ?? "",
                            deprecated = agent.Deprecated == true,
                            permission = agent.Permission != null ? JsonSerializer.SerializeToElement(agent.Permission) : null,
                            model = agent.Model?.ToString() ?? ""
                        };
                        agentsList.Add(mappedAgent);
                    }
                }
                
                string defaultAgent = "code";
                if (agentsList.Count > 0)
                {
                    var firstAgent = agentsList[0];
                    if (firstAgent is IDictionary<string, object> firstDict && 
                        firstDict.TryGetValue("name", out var nameObj))
                    {
                        defaultAgent = nameObj?.ToString() ?? "code";
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
