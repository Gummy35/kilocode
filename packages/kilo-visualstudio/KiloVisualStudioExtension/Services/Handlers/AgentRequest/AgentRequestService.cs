using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KiloExtensionDTOs.ExtensionMessages;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.Utils;

namespace KiloVisualStudioExtension.Services.Handlers.AgentRequest
{
  /// <summary>
  /// Handles agent-related operations like requestAgents.
  /// This matches the VS Code pattern where agent handling is extracted into separate handler modules.
  /// </summary>
  public class AgentRequestService : ServiceProviderServiceBase
  {
    private bool _disposed;

    /// <summary>
    /// Creates a new AgentRequestService instance.
    /// </summary>
    /// <param name="serviceProvider">The service provider for dependency injection.</param>
    public AgentRequestService(ServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    private VSProvider Provider => _serviceProvider.GetService<VSProvider>()
        ?? throw new InvalidOperationException("VSProvider not registered in service provider");

    /// <summary>
    /// Handles the requestAgents message from the webview.
    /// Fetches and sends the list of available agents to the webview.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task FetchAndSendAgentsAsync()
    {
      var nswagClient = Provider.GetNswagClient();
      var _cacheService = _serviceProvider.GetService<ICacheService>();
      if (nswagClient == null)
      {
        var cachedMessage = _cacheService.Get<AgentsLoadedMessage>("agentsLoadedMessage");
        if (cachedMessage != null)
        {
          Provider.PostMessage(cachedMessage);
        }
        return;
      }

      try    
      {
        var workspace = _serviceProvider.GetService<ProjectDirectoryProvider>().GetWorkspaceDirectory();
        var agents = await Retry.RetryAsync(() => nswagClient.App_agentsAsync(workspace, ""));

        var filtered = FilterVisibleAgents(agents);
        var visible = filtered.Item1;
        var defaultAgent = filtered.Item2;

        var message = new AgentsLoadedMessage {
          Agents = visible.Select(EntityConverter.Convert).ToList(), 
          AllAgents = agents.Select(EntityConverter.Convert).ToList(),
          DefaultAgent = defaultAgent
        };

        Provider.PostMessage(message);
        await _cacheService.UpdateAsync("agentsLoadedMessage", message);
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] AgentRequest: error fetching agents: {ex.Message}");
        await SendEmptyAgentsAsync();
      }
    }

    private Tuple<List<ApiClient.Agent>, string> FilterVisibleAgents(ICollection<ApiClient.Agent> agents)
    {
      var visible = agents.Where(a => a.Mode != AgentMode.Subagent && !a.Hidden).ToList();
      var defaultAgent = visible.Count > 0 ? visible.First().Name : "code";
      return new Tuple<List<Agent>, string>(visible, defaultAgent);
    }

    private async Task SendEmptyAgentsAsync()
    {
      var message = new AgentsLoadedMessage
      {
        Agents = [],
        AllAgents = [],
        DefaultAgent = "code"
      };

      Provider.PostMessage(message);
      await Task.CompletedTask;
    }

    public void Dispose()
    {
      if (_disposed) return;
      _disposed = true;
    }
  }
}

