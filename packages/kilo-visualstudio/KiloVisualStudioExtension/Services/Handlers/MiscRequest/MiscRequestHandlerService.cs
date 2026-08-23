using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.Services;

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
    public class MiscRequestHandlerService : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private bool _disposed;

        private VSProvider Provider => _serviceProvider.GetService<VSProvider>() 
            ?? throw new InvalidOperationException("VSProvider not registered in service provider");

        private KiloConnectionService ConnectionService => _serviceProvider.GetService<KiloConnectionService>() 
            ?? throw new InvalidOperationException("KiloConnectionService not registered in service provider");

        private ICacheService Cache => _serviceProvider.GetService<ICacheService>() 
            ?? throw new InvalidOperationException("CacheService not registered in service provider");

        /// <summary>
        /// Creates a new MiscRequestHandlerService instance.
        /// </summary>
        /// <param name="serviceProvider">The service provider for dependency injection.</param>
        public MiscRequestHandlerService(ServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
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
        public async Task HandleRequestSkillsAsync(JsonElement? payload)
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
        public async Task HandleRequestCommandsAsync(JsonElement? payload)
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

        // TypeScript: case "requestGlobalConfig":
        //   this.fetchAndSendGlobalConfig().catch((e) => console.error("[Kilo New] fetchAndSendGlobalConfig failed:", e))
        //   break
        // private async fetchAndSendGlobalConfig(): Promise<void> {
        //   if (!this.client || this.connectionState !== "connected") return
        //   try {
        //     const { data: config } = await this.client.global.config.get({ throwOnError: true })
        //     this.cachedGlobalConfig = config ?? null
        //     this.postMessage({ type: "globalConfigLoaded", config })
        //   } catch (error) {
        //     console.error("[Kilo New] KiloProvider: Failed to fetch global config:", error)
        //   }
        // }
        public async Task HandleRequestGlobalConfigAsync(JsonElement? payload)
        {
            var nswagClient = Provider.GetNswagClient();
            if (nswagClient == null || Provider.GetConnectionState() != "connected")
            {
                return;
            }

            try
            {
                var config = await nswagClient.Global_config_getAsync();
                var configData = config != null ? JsonSerializer.SerializeToElement(config) : JsonSerializer.SerializeToElement(new object());
                await Cache.UpdateAsync("globalConfig", configData);
                var message = new { type = "globalConfigLoaded", config = configData };
                Provider.PostMessage(JsonSerializer.SerializeToElement(message));
            }
            catch (Exception error)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo New] KiloProvider: Failed to fetch global config: {error}");
            }
        }

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
        public async Task HandleRequestIndexingStatusAsync(JsonElement? payload)
        {
            var nswagClient = Provider.GetNswagClient();
            if (nswagClient == null)
            {
                var cachedMessage = Cache.GetJson("indexingStatusLoadedMessage");
                if (cachedMessage.HasValue)
                {
                    Provider.PostMessage(cachedMessage.Value);
                }
                return;
            }

            var config = ConnectionService.GetServerConfig();
            if (config == null)
            {
                return;
            }

            try
            {
                var dir = Provider.GetWorkspaceDirectory(null);
                var auth = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"kilo:{config.Password}"));
                var baseUrl = config.BaseUrl;
                
                using var httpClient = new System.Net.Http.HttpClient();
                httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);
                if (!string.IsNullOrEmpty(dir))
                {
                    httpClient.DefaultRequestHeaders.Add("x-kilo-directory", dir);
                }
                
                var res = await httpClient.GetAsync($"{baseUrl}/indexing/status");
                if (!res.IsSuccessStatusCode)
                {
                    throw new Exception($"HTTP {res.StatusCode}");
                }
                var statusJson = await res.Content.ReadAsStringAsync();
                var status = JsonDocument.Parse(statusJson).RootElement;
                var message = new { type = "indexingStatusLoaded", status };
                var messageJson = JsonSerializer.SerializeToElement(message);
                await Cache.UpdateAsync("indexingStatusLoadedMessage", messageJson);
                Provider.PostMessage(messageJson);
            }
            catch (Exception error)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo New] KiloProvider: Failed to fetch indexing status: {error}");
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
