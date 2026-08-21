using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KiloVisualStudioExtension.ApiClient;

namespace KiloVisualStudioExtension.Services.Handlers.AgentRequest
{
    /// <summary>
    /// Handles agent-related operations like requestAgents.
    /// This matches the VS Code pattern where agent handling is extracted into separate handler modules.
    /// </summary>
    public class AgentRequestService : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private bool _disposed;

        /// <summary>
        /// Creates a new AgentRequestService instance.
        /// </summary>
        /// <param name="serviceProvider">The service provider for dependency injection.</param>
        public AgentRequestService(ServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        private VSProvider Provider => _serviceProvider.GetService<VSProvider>() 
            ?? throw new InvalidOperationException("VSProvider not registered in service provider");

        /// <summary>
        /// Handles the requestAgents message from the webview.
        /// Fetches and sends the list of available agents to the webview.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleRequestAgentsAsync()
        {
            var nswagClient = Provider.GetNswagClient();
            if (nswagClient == null)
            {
                await SendEmptyAgentsAsync();
                return;
            }

            try
            {
                var agents = await nswagClient.App_agentsAsync("", "");
                var agentsList = new List<object>();
                
                if (agents != null)
                {
                    foreach (var agent in agents)
                    {
                        if (agent.Mode.ToString().ToLowerInvariant() == "subagent")
                            continue;
                        if (agent.Hidden == true)
                            continue;
                        
                        var mappedAgent = new
                        {
                            name = agent.Name ?? "",
                            description = agent.Description ?? "",
                            mode = agent.Mode.ToString().ToLowerInvariant(),
                            native = agent.Native == true,
                            hidden = agent.Hidden == true,
                            color = agent.Color ?? "",
                            deprecated = agent.Deprecated == true,
                            permission = agent.Permission != null && agent.Permission.Count > 0 ? JsonSerializer.SerializeToElement(agent.Permission) : (JsonElement?)null,
                            model = agent.Model != null ? agent.Model.ToString() ?? "" : ""
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
                Provider.PostMessage(JsonSerializer.Serialize(message));
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

