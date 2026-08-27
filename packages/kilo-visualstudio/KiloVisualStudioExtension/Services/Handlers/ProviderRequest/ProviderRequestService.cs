using KiloExtensionDTOs.ExtensionMessages;
using KiloExtensionDTOs.KiloConfig;
using KiloExtensionDTOs.Providers;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.Services.Handlers.Provider;
using KiloVisualStudioExtension.Utils;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AuthState = KiloVisualStudioExtension.ApiClient.Response24Type;
using ProviderListResponse = KiloVisualStudioExtension.ApiClient.Response10;

namespace KiloVisualStudioExtension.Services.Handlers.ProviderRequest
{
  /// <summary>
  /// Handles provider-related operations like requestProviders.
  /// This matches the VS Code pattern where provider handling is extracted into separate handler modules.
  /// </summary>
  public class ProviderRequestService : ServiceProviderServiceBase
  {
    private bool _disposed;

    private int _providersGeneration = 0;
    private Task? _providersRefresh = null;
    private bool _providersQueued = false;
    private IDictionary<string, StoredProviderKey> _storedProviderKeys = new Dictionary<string, StoredProviderKey>();
    private readonly object _providersLock = new object(); // Single lock for all provider-related state
    private readonly object _providersRefreshLock = new object(); // Single lock for all provider-related state

    
    private VSProvider Provider => _serviceProvider.GetService<VSProvider>()
        ?? throw new InvalidOperationException("VSProvider not registered in service provider");

    /// <summary>
    /// Creates a new ProviderRequestService instance.
    /// </summary>
    /// <param name="serviceProvider">The service provider for dependency injection.</param>
    public ProviderRequestService(ServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    ///// <summary>
    ///// Handles the requestProviders message from the webview.
    ///// Fetches and sends the list of available providers to the webview.
    ///// </summary>
    ///// <returns>A task representing the asynchronous operation.</returns>
    //public async Task HandleRequestProvidersAsync()
    //{
    //  var nswagClient = Provider.GetNswagClient();
    //  if (nswagClient == null)
    //  {
    //    await SendEmptyProvidersAsync();
    //    return;
    //  }

    //  try
    //  {
    //    var response = await nswagClient.Provider_listAsync("", "");

    //    var providersDict = new Dictionary<string, object>();
    //    var connectedList = new List<string>();
    //    var defaultsDict = new Dictionary<string, string>();

    //    if (response != null)
    //    {
    //      if (response.All != null)
    //      {
    //        foreach (var provider in response.All)
    //        {
    //          if (provider.Id != null)
    //          {
    //            providersDict[provider.Id] = JsonSerializer.SerializeToElement(provider);
    //          }
    //        }
    //      }

    //      if (response.Connected != null)
    //      {
    //        connectedList.AddRange(response.Connected.Where(s => !string.IsNullOrEmpty(s)));
    //      }

    //      if (response.Default != null)
    //      {
    //        foreach (var kvp in response.Default)
    //        {
    //          defaultsDict[kvp.Key] = kvp.Value ?? "";
    //        }
    //      }
    //    }

    //    var message = new
    //    {
    //      type = "providersLoaded",
    //      providers = providersDict,
    //      connected = connectedList.ToArray(),
    //      defaults = defaultsDict,
    //      defaultSelection = new { },
    //      authMethods = new Dictionary<string, object[]>(),
    //      authStates = new Dictionary<string, object>()
    //    };
    //    Provider.PostMessage(JsonSerializer.Serialize(message));
    //  }
    //  catch (Exception ex)
    //  {
    //    System.Diagnostics.Debug.WriteLine($"[Kilo] ProviderRequest: error fetching providers: {ex.Message}");
    //    await SendEmptyProvidersAsync();
    //  }
    //}



    /// <summary>
    /// Fetches provider data from the backend and sends it to the webview.
    /// Matches the VS Code pattern in KiloProvider.ts:fetchAndSendProviders.
    /// 
    /// Workflow:
    /// 1. Increment generation counter to mark this request
    /// 2. If another refresh is in flight, queue this request and wait
    /// 3. Fetch provider data with retry logic for stale requests
    /// 4. Coalesce concurrent requests to avoid redundant API calls
    /// 5. Cache the result and send to webview
    /// 6. Clean up pending task reference in finally block
    /// </summary>
    internal async Task FetchAndSendProvidersAsync()
    {
      int nextGeneration;
      Task? taskToWait = null;

      // Increment generation and check for existing refresh under lock
      lock (_providersLock)
      {
        nextGeneration = ++_providersGeneration;

        if (_providersRefresh != null)
        {
          _providersQueued = true;
          taskToWait = _providersRefresh;
        }
      }

      // Wait outside the lock to avoid deadlocks
      if (taskToWait != null)
      {
        await taskToWait;
        return;
      }

      var task = FetchProvidersCoreAsync(nextGeneration);

      // Capture the task for the continuation callback
      var thisTask = task;

      // Track completion to clean up the pending reference
      var done = task.ContinueWith(_ =>
      {
        lock (_providersLock)
        {
          if (_providersRefresh == thisTask)
            _providersRefresh = null;
        }
      }, TaskContinuationOptions.ExecuteSynchronously);

      lock (_providersLock)
      {
        _providersRefresh = done;
      }

      await done;
    }


    /// <summary>
    /// Core logic for fetching provider data with generation-based staleness detection.
    /// </summary>
    private async Task FetchProvidersCoreAsync(int initialGeneration)
    {
      var generation = initialGeneration;

      while (true)
      {
        _providersQueued = false;

        var _connectionService = _serviceProvider.GetService<KiloConnectionService>();
        var _cacheService = _serviceProvider.GetService<ICacheService>();
        var _directoryService = _serviceProvider.GetService<ProjectDirectoryProvider>();
        var client = _connectionService.GetNswagClient();
        if (client == null)
        {
          // No client connected - send cached data if available and generation matches
          if (_cacheService.Contains("providersLoadedMessage") && generation == _providersGeneration)
          {
            Provider.PostMessage(_cacheService.Get<ProvidersLoadedMessage>("providersLoadedMessage"));
          }
          return;
        }

        try
        {
          var workspaceDir = _directoryService.GetWorkspaceDirectory();
          var result = await FetchProviderDataAsync(client, workspaceDir);
          
          // Check if this request is still current
          if (generation != _providersGeneration || client != _connectionService.GetNswagClient())
          {
            if (!_providersQueued)
              return;

            generation = _providersGeneration;
            continue; // Retry with new generation
          }

          // Store provider keys for authenticated model fetches
          _storedProviderKeys = result.StoredKeys;

          
          
          // Get model settings from user settings file
          var providerIdSetting = VSExtensionSettings.Get<string>("kilo-code.new.model.providerID", "");
          var modelIdSetting = VSExtensionSettings.Get<string>("kilo-code.new.model.modelID", "");

          var message = new ProvidersLoadedMessage
          {
            Providers = IndexProvidersById(result.Response.All),
            Connected = result.Response.Connected.ToList(),
            Defaults = result.Response.Default,
            DefaultSelection = ComputeDefaultSelection(
              _cacheService.Get<ConfigLoadedMessage>("configLoadedMessage"),
              providerIdSetting,
              modelIdSetting
            ),
            AuthMethods = result.AuthMethods,
            AuthStates = result.AuthStates
          };

          await _cacheService.UpdateAsync("providersLoadedMessage", message);
          Provider.PostMessage(message);
        }
        catch (Exception ex)
        {
          // Check if request was superseded
          if (generation != _providersGeneration)
          {
            if (!_providersQueued)
              return;

            generation = _providersGeneration;
            continue; // Retry with new generation
          }

          System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: Failed to fetch providers: {ex.Message}");
        }

        // Exit loop if no queued request
        if (!_providersQueued)
          return;

        generation = _providersGeneration;
      }
    }

    internal ModelSelection ComputeDefaultSelection(
      ConfigLoadedMessage? cachedConfig, string providerID, string modelID)
    {
      var configured = ParseModelString(cachedConfig?.Config?.Model);
      if (configured != null)
        return configured;
      if (!string.IsNullOrEmpty(providerID) && !string.IsNullOrEmpty(modelID))
        return new ModelSelection
        {
          ModelID = modelID,
          ProviderID = providerID
        };
      return new ModelSelection { ProviderID = "kilo", ModelID = "kilo-auto/free" };
    }

    internal ModelSelection? ParseModelString(string? raw)
    {
      if (string.IsNullOrEmpty(raw)) return null;
      var slash = raw.IndexOf("/");
      if (slash <= 0 || slash >= raw.Length - 1) return null;
      return new ModelSelection { ProviderID = raw.Substring(0, slash), ModelID = raw.Substring(slash + 1) };
    }

    private IDictionary<string, ApiClient.Provider> IndexProvidersById(ICollection<ApiClient.Provider> all) 
    {
      var normalized = new Dictionary<string, ApiClient.Provider>();
      foreach(var provider in all)
      {
        normalized[provider.Id] = provider; 
      }
      return normalized;
    }

/// <summary>
/// Fetches provider data from the backend.
/// Matches the TypeScript fetchProviderData function in provider-actions.ts.
/// </summary>
private async Task<ProviderDataResult> FetchProviderDataAsync(KiloApiClient client, string directory)
    {
      var authMethodsTask = client.Provider_authAsync(directory, "");
      var kiloAuthTask = client.Kilo_authStatusAsync(directory, "");
      var providerResponseTask = client.Provider_listAsync(directory, "");

      await Task.WhenAll(authMethodsTask, kiloAuthTask, providerResponseTask);

      var authMethods = authMethodsTask.Result;
      var kiloAuth = kiloAuthTask.Result;
      var providerResponse = providerResponseTask.Result;


      var authStates = new Dictionary<string, AuthState>();
      var storedKeys = new Dictionary<string, StoredProviderKey>();

      var all = providerResponse.All.Select(item =>
      {
        // Check if provider has a key (API authentication)
        if (!string.IsNullOrEmpty(item.Id) && !string.IsNullOrEmpty(item.Key))
        {
          authStates[item.Id] = AuthState.Api;

          // Extract baseURL from options if available
          var baseURL = "";
          //item.Options != null && (item.Options.TryGetValue("baseURL", out var urlObj)
          //  ? urlObj?.ToString()
          //  : null;

          if (!string.IsNullOrEmpty(baseURL))
          {
            storedKeys[item.Id] = new StoredProviderKey
            {
              Key = item.Key,
              BaseURL = baseURL
            };
          }
        }

        // If no key property, return item as-is
        if (string.IsNullOrEmpty(item.Key))
          return item;

        // Create a copy without the key property (for security - don't expose keys to webview)
        return new ApiClient.Provider
        {
          Id = item.Id,
          Name = item.Name,
          Description = item.Description,
          Source = item.Source,
          Env = item.Env,
          // Key is intentionally omitted
          Metadata = item.Metadata,
          Options = item.Options,
          Models = item.Models
        };
      }).ToList();

      authStates.Remove("kilo");
      if (kiloAuth != null)
        authStates["kilo"] = kiloAuth.Type;


      return new ProviderDataResult
      {
        Response = new ProviderListResponse
        {
          All = all,
          Connected = providerResponse.Connected,
          Default = providerResponse.Default,
          Failed = providerResponse.Failed,
        },
        AuthMethods = authMethods,
        AuthStates = authStates,
        StoredKeys = storedKeys
      };
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

  public class ProviderDataResult
  {
    public ProviderListResponse Response { get; set; } = null!;
    public IDictionary<string, ICollection<ProviderAuthMethod>> AuthMethods { get; set; } = null!;
    public IDictionary<string, AuthState> AuthStates { get; set; } = null!;
    public IDictionary<string, StoredProviderKey> StoredKeys { get; set; } = null!;
  }

}

