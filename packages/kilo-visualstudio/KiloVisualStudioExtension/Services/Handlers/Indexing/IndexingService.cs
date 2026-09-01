using KiloExtensionDTOs.ExtensionMessages;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.Services;
using KiloVisualStudioExtension.Utils;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services.Handlers.Indexing
{
    /// <summary>
    /// Handles indexing status state.
    /// </summary>
    public class IndexingService : ServiceProviderServiceBase
  {
        private bool _disposed;

        private VSProvider Provider => _serviceProvider.GetService<VSProvider>() 
            ?? throw new InvalidOperationException("VSProvider not registered in service provider");

        /// <summary>
        /// Creates a new IndexingHandlerService instance.
        /// </summary>
        public IndexingService(ServiceProvider serviceProvider): base (serviceProvider) 
        { }
        

    // TypeScript: case "requestIndexingStatus":
    //   this.fetchAndSendIndexingStatus().catch((e) =>
    //     console.error("[Kilo New] fetchAndSendIndexingStatus failed:", e),
    //   )
    //   break
    // private async fetchAndSendIndexingStatus(): Promise<void> {
    //   if (!this.client) {
    //     if (this.cachedIndexingStatusMessage) {
    //       this.postMessage(this.cachedIndexingStatusMessage)
    //     }
    //     return
    //   }
    //   const config = this.connectionService.getServerConfig()
    //   if (!config) return
    //   try {
    //     const dir = this.getWorkspaceDirectory(this.currentSession?.id)
    //     const auth = Buffer.from(`kilo:${config.password}`).toString("base64")
    //     const res = await fetch(`${config.baseUrl}/indexing/status`, {
    //       headers: {
    //         Authorization: `Basic ${auth}`,
    //         ...(dir ? { "x-kilo-directory": dir } : {}),
    //       },
    //     })
    //     if (!res.ok) throw new Error(`HTTP ${res.status}`)
    //     const status = (await res.json()) as IndexingStatus
    //     const message = { type: "indexingStatusLoaded", status }
    //     this.cachedIndexingStatusMessage = message
    //     this.postMessage(message)
    //   } catch (error) {
    //     console.error("[Kilo New] KiloProvider: Failed to fetch indexing status:", error)
    //   }
    // }
    public async Task FetchAndSendIndexingStatusAsync()
    {
      var cache = _serviceProvider.GetService<ICacheService>();
      var nswagClient = Provider.GetNswagClient();
      if (nswagClient == null)
      {
        if (cache.Contains("indexingStatusLoadedMessage"))
        {
          Provider.PostMessage(cache.Get<IndexingStatusLoadedMessage>("indexingStatusLoadedMessage"));
        }
        return;
      }

      var connectionService = _serviceProvider.GetService<KiloConnectionService>();
      var config = connectionService.GetServerConfig();
      if (config == null)
      {
        return;
      }

      try
      {
        var dir = _serviceProvider.GetService<ProjectDirectoryProvider>().GetWorkspaceDirectory();
        var result = await nswagClient.Indexing_statusAsync(dir, "");
        if (result != null)
        {
          var message = new IndexingStatusLoadedMessage
          {
            Status = EntityConverter.Convert(result)
          };
          await cache.UpdateAsync("indexingStatusLoadedMessage", message);
          Provider.PostMessage(message);
        }
        //var auth = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"kilo:{config.Password}"));
        //var baseUrl = config.BaseUrl;

        //using var httpClient = new System.Net.Http.HttpClient();
        //httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);
        //if (!string.IsNullOrEmpty(dir))
        //{
        //  httpClient.DefaultRequestHeaders.Add("x-kilo-directory", dir);
        //}

        //var res = await httpClient.GetAsync($"{baseUrl}/indexing/status");
        //if (!res.IsSuccessStatusCode)
        //{
        //  throw new Exception($"HTTP {res.StatusCode}");
        //}
        //var statusJson = await res.Content.ReadAsStringAsync();
        //var status = JsonDocument.Parse(statusJson).RootElement;
        //var message = new { type = "indexingStatusLoaded", status };
        //var messageJson = JsonSerializer.SerializeToElement(message);
        //await Cache.UpdateAsync("indexingStatusLoadedMessage", messageJson);
        //Provider.PostMessage(messageJson);
      }
      catch (Exception error)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo New] KiloProvider: Failed to fetch indexing status: {error}");
      }
    }


    public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
