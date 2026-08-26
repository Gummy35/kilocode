using KiloExtensionDTOs.ExtensionMessages;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.Services;
using KiloVisualStudioExtension.Utils;
using Microsoft.ServiceHub.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services.Handlers.MiscRequest
{
  /// <summary>
  /// Model selection for recent/favorite models.
  /// </summary>
  public class ModelSelection
  {
    public string providerID { get; set; } = "";
    public string modelID { get; set; } = "";
  }

  /// <summary>
  /// Handles miscellaneous request operations like recents, favorites, variants, skills, commands.
  /// These are simple request handlers that return static or cached data.
  /// </summary>
  public class MiscRequestHandlerService : ServiceProviderServiceBase
  {
    private readonly ServiceProvider _serviceProvider;
    private bool _disposed;

    private VSProvider Provider => _serviceProvider.GetService<VSProvider>()
        ?? throw new InvalidOperationException("VSProvider not registered in service provider");

    private KiloConnectionService ConnectionService => (KiloConnectionService)_serviceProvider.GetService(typeof(KiloConnectionService))
        ?? throw new InvalidOperationException("KiloConnectionService not registered in service provider");

    private ICacheService Cache => (ICacheService)_serviceProvider.GetService(typeof(ICacheService))
        ?? throw new InvalidOperationException("CacheService not registered in service provider");

    /// <summary>
    /// Creates a new MiscRequestHandlerService instance.
    /// </summary>
    /// <param name="serviceProvider">The service provider for dependency injection.</param>
    public MiscRequestHandlerService(ServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    // TypeScript: case "requestVariants": {
    //   const variants = this.extensionContext?.globalState.get<Record<string, string>>("variantSelections") ?? {}
    //   this.postMessage({ type: "variantsLoaded", variants })
    //   break
    // }
    public void HandleRequestVariants(JsonElement? payload)
    {
      var variants = Cache.GetJson("variantSelections")
          ?? JsonSerializer.SerializeToElement(new Dictionary<string, string>());

      var message = new { type = "variantsLoaded", variants = variants };
      var messageJson = JsonSerializer.SerializeToElement(message);
      _ = Cache.UpdateAsync("variantsLoadedMessage", messageJson);
      Provider.PostMessage(messageJson);
    }

    // TypeScript: case "persistRecents":
    //   await this.extensionContext?.globalState.update("recentModels", validateRecents(message.recents))
    //   break
    // case "requestRecents": {
    //   const recents = validateRecents(this.extensionContext?.globalState.get("recentModels"))
    //   this.postMessage({ type: "recentsLoaded", recents })
    //   break
    // }
    public void HandlePersistRecents(JsonElement? payload)
    {
      if (payload.HasValue && payload.Value.TryGetProperty("recents", out var recentsProp))
      {
        var validated = Provider.ValidateRecents(recentsProp);
        _ = Cache.UpdateAsync("recentModels", validated);
      }
    }

    public void HandleRequestRecents(JsonElement? payload)
    {
      var recents = Cache.GetJson("recentModels")
          ?? JsonSerializer.SerializeToElement(new List<ModelSelection>());

      var message = new { type = "recentsLoaded", recents = recents };
      var messageJson = JsonSerializer.SerializeToElement(message);
      _ = Cache.UpdateAsync("recentsLoadedMessage", messageJson);
      Provider.PostMessage(messageJson);
    }

    // TypeScript: case "toggleFavorite": {
    //   await this.toggleFavorite(message)
    //   break
    // }
    // case "requestFavorites": {
    //   const favorites = validateFavorites(this.extensionContext?.globalState.get("favoriteModels"))
    //   this.postMessage({ type: "favoritesLoaded", favorites })
    //   break
    // }
    public void HandleToggleFavorite(JsonElement? payload)
    {
      if (!payload.HasValue) return;

      var action = payload.Value.TryGetProperty("action", out var actionProp) ? actionProp.GetString() : "";
      var providerID = payload.Value.TryGetProperty("providerID", out var pidProp) ? pidProp.GetString() : "";
      var modelID = payload.Value.TryGetProperty("modelID", out var midProp) ? midProp.GetString() : "";

      var current = Cache.GetJson("favoriteModels")
          ?? JsonSerializer.SerializeToElement(new List<ModelSelection>());

      var key = $"{providerID}/{modelID}";
      var existing = new List<ModelSelection>();

      if (current.ValueKind == JsonValueKind.Array)
      {
        foreach (var item in current.EnumerateArray())
        {
          var itemPid = item.TryGetProperty("providerID", out var p) ? p.GetString() : "";
          var itemMid = item.TryGetProperty("modelID", out var m) ? m.GetString() : "";
          existing.Add(new ModelSelection { providerID = itemPid, modelID = itemMid });
        }
      }

      if (action == "add")
      {
        if (!existing.Any(f => $"{f.providerID}/{f.modelID}" == key))
        {
          existing.Add(new ModelSelection { providerID = providerID, modelID = modelID });
        }
      }
      else if (action == "remove")
      {
        existing = existing.Where(f => $"{f.providerID}/{f.modelID}" != key).ToList();
      }

      var validated = JsonSerializer.SerializeToElement(existing);
      _ = Cache.UpdateAsync("favoriteModels", validated);
    }

    public void HandleRequestFavorites(JsonElement? payload)
    {
      var favorites = Cache.GetJson("favoriteModels")
          ?? JsonSerializer.SerializeToElement(new List<ModelSelection>());

      var message = new { type = "favoritesLoaded", favorites = favorites };
      var messageJson = JsonSerializer.SerializeToElement(message);
      _ = Cache.UpdateAsync("favoritesLoadedMessage", messageJson);
      Provider.PostMessage(messageJson);
    }

    // TypeScript: case "requestSkills":
    //   this.fetchAndSendSkills().catch((e) => console.error("[Kilo New] fetchAndSendSkills failed:", e))
    //   break
    // private async fetchAndSendSkills(): Promise<void> {
    //   if (!this.client) {
    //     if (this.cachedSkillsMessage) {
    //       this.postMessage(this.cachedSkillsMessage)
    //     }
    //     return
    //   }
    //   try {
    //     const workspaceDir = this.getWorkspaceDirectory()
    //     const { data: skills } = await retry(() =>
    //       this.client!.app.skills({ directory: workspaceDir }, { throwOnError: true }),
    //     )
    //     const message = { type: "skillsLoaded", skills }
    //     this.cachedSkillsMessage = message
    //     this.postMessage(message)
    //   } catch (error) {
    //     console.error("[Kilo New] KiloProvider: Failed to fetch skills:", error)
    //   }
    // }
    public async Task HandleRequestSkillsAsync()
    {
      var nswagClient = Provider.GetNswagClient();
      if (nswagClient == null)
      {
        var cachedMessage = Cache.GetJson("skillsLoadedMessage");
        if (cachedMessage.HasValue)
        {
          Provider.PostMessage(cachedMessage.Value);
        }
        return;
      }

      try
      {
        var workspaceDir = Provider.GetWorkspaceDirectory();
        var skills = await nswagClient.App_skillsAsync(workspaceDir, "");
        var skillsData = skills != null ? JsonSerializer.SerializeToElement(skills) : JsonSerializer.SerializeToElement(new List<Anonymous3>());
        var message = new { type = "skillsLoaded", skills = skillsData };
        var messageJson = JsonSerializer.SerializeToElement(message);
        _ = Cache.UpdateAsync("skillsLoadedMessage", messageJson);
        Provider.PostMessage(messageJson);
      }
      catch (Exception error)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo New] KiloProvider: Failed to fetch skills: {error}");
      }
    }

    // TypeScript: case "requestCommands":
    //   this.fetchAndSendCommands().catch((e) => console.error("[Kilo New] fetchAndSendCommands failed:", e))
    //   break
    // private async fetchAndSendCommands(): Promise<void> {
    //   if (!this.client) {
    //     if (this.cachedCommandsMessage) {
    //       this.postMessage(this.cachedCommandsMessage)
    //     }
    //     return
    //   }
    //   try {
    //     const dir = this.getWorkspaceDirectory()
    //     const message = await loadCommands(this.client, dir)
    //     this.cachedCommandsMessage = message
    //     this.postMessage(message)
    //   } catch (error) {
    //     console.error("[Kilo New] KiloProvider: Failed to fetch commands:", error)
    //   }
    // }
    public async Task HandleRequestCommandsAsync()
    {
      var nswagClient = Provider.GetNswagClient();
      if (nswagClient == null)
      {
        var cachedMessage = Cache.GetJson("commandsLoadedMessage");
        if (cachedMessage.HasValue)
        {
          Provider.PostMessage(cachedMessage.Value);
        }
        return;
      }

      try
      {
        var dir = Provider.GetWorkspaceDirectory();
        var commands = await nswagClient.Command_listAsync(dir, "");
        var commandsData = commands != null ? JsonSerializer.SerializeToElement(commands) : JsonSerializer.SerializeToElement(new List<Command>());
        var message = new { type = "commandsLoaded", commands = commandsData };
        var messageJson = JsonSerializer.SerializeToElement(message);
        _ = Cache.UpdateAsync("commandsLoadedMessage", messageJson);
        Provider.PostMessage(messageJson);
      }
      catch (Exception error)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo New] KiloProvider: Failed to fetch commands: {error}");
      }
    }


   

    // TypeScript: case "requestKiloEmbeddingModels":
    //   this.fetchAndSendKiloEmbeddingModels().catch((e) =>
    //     console.error("[Kilo New] fetchAndSendKiloEmbeddingModels failed:", e),
    //   )
    //   break
    // private async fetchAndSendKiloEmbeddingModels(): Promise<void> {
    //   const catalog = await fetchKiloEmbeddingModelCatalog()
    //   const message = { type: "kiloEmbeddingModelsLoaded", catalog }
    //   this.cachedKiloEmbeddingModelsMessage = message
    //   this.postMessage(message)
    // }
    public async Task HandleRequestKiloEmbeddingModelsAsync(JsonElement? payload)
    {
      var catalog = await FetchKiloEmbeddingModelCatalog();
      var message = new { type = "kiloEmbeddingModelsLoaded", catalog };
      var messageJson = JsonSerializer.SerializeToElement(message);
      await Cache.UpdateAsync("kiloEmbeddingModelsLoadedMessage", messageJson);
      Provider.PostMessage(messageJson);
    }

    private async Task<object> FetchKiloEmbeddingModelCatalog()
    {
      return new { };
    }

    // TypeScript: case "requestImageModels":
    //   this.fetchAndSendImageModels().catch((e) => console.error("[Kilo New] fetchAndSendImageModels failed:", e))
    //   break
    // private async fetchAndSendImageModels(): Promise<void> {
    //   const dir = this.getWorkspaceDirectory()
    //   const result = await fetchImageModels(this.connectionService, dir)
    //   if (!result.ok) {
    //     if (this.cachedImageModelsMessage) {
    //       this.postMessage(this.cachedImageModelsMessage)
    //     }
    //     return
    //   }
    //   const message = { type: "imageModelsLoaded" as const, models: result.models }
    //   this.cachedImageModelsMessage = message
    //   this.postMessage(message)
    // }
    public async Task HandleRequestImageModelsAsync(JsonElement? payload)
    {
      var dir = Provider.GetWorkspaceDirectory();
      var result = await FetchImageModels(dir);
      if (!result.Ok)
      {
        var cachedMessage = Cache.GetJson("imageModelsLoadedMessage");
        if (cachedMessage.HasValue)
        {
          Provider.PostMessage(cachedMessage.Value);
        }
        return;
      }
      var message = new { type = "imageModelsLoaded", models = result.Models };
      var messageJson = JsonSerializer.SerializeToElement(message);
      await Cache.UpdateAsync("imageModelsLoadedMessage", messageJson);
      Provider.PostMessage(messageJson);
    }

    private async Task<(bool Ok, object Models)> FetchImageModels(string dir)
    {
      return (true, new object[0]);
    }

    public void Dispose()
    {
      if (_disposed) return;
      _disposed = true;
    }
  }
}
