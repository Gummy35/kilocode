using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Documents;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.Utils;
using ApiImageModel = KiloVisualStudioExtension.ApiClient.Anonymous10;

namespace KiloVisualStudioExtension.Services.Handlers.MiscRequest
{
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

        /// <summary>
        /// Creates a new MiscRequestHandlerService instance.
        /// </summary>
        /// <param name="serviceProvider">The service provider for dependency injection.</param>
        public MiscRequestHandlerService(ServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Handles the requestRecents message from the webview.
        /// Sends empty recents list (placeholder for future implementation).
        /// </summary>
        /// <param name="payload">The message payload (unused).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public void HandleRequestRecents(JsonElement? payload)
        {
      var selected = new List<ModelSelection>();
      selected.Add(new ModelSelection
      {
        providerID = "openrama",
        modelID = "qwen3.5-122b"
      });
      var message = new { 
              type = "recentsLoaded", 
              recents = selected 
            };
            Provider.PostMessage(JsonSerializer.Serialize(message));
        }

        /// <summary>
        /// Handles the requestFavorites message from the webview.
        /// Sends empty favorites list (placeholder for future implementation).
        /// </summary>
        /// <param name="payload">The message payload (unused).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public void HandleRequestFavorites(JsonElement? payload)
        {
          var favorites = new List<FavoriteModel>();
          favorites.Add(new FavoriteModel
          {
            providerID = "openrama",
            modelID = "qwen3.5-122b"
          });
          var message = new { type = "favoritesLoaded", 
              favorites = favorites
          };
            Provider.PostMessage(JsonSerializer.Serialize(message));
        }

        /// <summary>
        /// Handles the requestVariants message from the webview.
        /// Sends empty variants object (placeholder for future implementation).
        /// </summary>
        /// <param name="payload">The message payload (unused).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public void HandleRequestVariants(JsonElement? payload)
        {
            var message = new { type = "variantsLoaded", variants = new { } };
            Provider.PostMessage(JsonSerializer.Serialize(message));
        }

        /// <summary>
        /// Handles the requestSkills message from the webview.
        /// Fetches and sends available skills to the webview.
        /// </summary>
        /// <param name="payload">The message payload (unused).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleRequestSkillsAsync(JsonElement? payload)
        {
            try
            {
                var nswagClient = Provider.GetNswagClient();
                if (nswagClient == null)
                {
                    await Provider.SendSkillsAsync(new List<KiloExtensionDTOs.Agents.SkillInfo>());
                    return;
                }

                var skills = await nswagClient.App_skillsAsync("", "");
                List<KiloExtensionDTOs.Agents.SkillInfo> skillsList;
                if (skills != null)
                {
                    skillsList = skills.Select(s => new KiloExtensionDTOs.Agents.SkillInfo
                    {
                        Name = s.Name,
                        Description = s.Description,
                        Location = s.Location
                    }).ToList();
                }
                else
                {
                    skillsList = new List<KiloExtensionDTOs.Agents.SkillInfo>();
                }

                await Provider.SendSkillsAsync(skillsList);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] MiscRequestHandler: Error fetching skills: {ex.Message}");
                await Provider.SendSkillsAsync(new List<KiloExtensionDTOs.Agents.SkillInfo>());
            }
        }

        /// <summary>
        /// Handles the requestCommands message from the webview.
        /// Fetches and sends available commands to the webview.
        /// </summary>
        /// <param name="payload">The message payload (unused).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleRequestCommandsAsync(JsonElement? payload)
        {
            try
            {
                var nswagClient = Provider.GetNswagClient();
                if (nswagClient == null)
                {
                    await Provider.SendCommandsAsync(new List<KiloExtensionDTOs.Agents.SlashCommandInfo>());
                    return;
                }

                var commands = await nswagClient.Command_listAsync("", "");
                List<KiloExtensionDTOs.Agents.SlashCommandInfo> commandsList;
                if (commands != null)
                {
                    commandsList = commands.Select(c => new KiloExtensionDTOs.Agents.SlashCommandInfo
                    {
                        Name = c.Name,
                        Description = c.Description,
                        Hints = c.Hints != null ? c.Hints.ToList() : new List<string>()
                    }).ToList();
                }
                else
                {
                    commandsList = new List<KiloExtensionDTOs.Agents.SlashCommandInfo>();
                }

                await Provider.SendCommandsAsync(commandsList);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] MiscRequestHandler: Error fetching commands: {ex.Message}");
                await Provider.SendCommandsAsync(new List<KiloExtensionDTOs.Agents.SlashCommandInfo>());
            }
        }

        /// <summary>
        /// Handles the requestGlobalConfig message from the webview.
        /// Fetches and sends global configuration to the webview.
        /// </summary>
        /// <param name="payload">The message payload (unused).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleRequestGlobalConfigAsync(JsonElement? payload)
        {
            try
            {
                var nswagClient = Provider.GetNswagClient();
                if (nswagClient == null)
                {
                    await Provider.SendGlobalConfigAsync(new KiloExtensionDTOs.KiloConfig.Config());
                    return;
                }

                var config = await nswagClient.Global_config_getAsync();
                var configData = config != null ? EntityConverter.Convert(config) : new KiloExtensionDTOs.KiloConfig.Config();
                await Provider.SendGlobalConfigAsync(configData);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] MiscRequestHandler: Error fetching global config: {ex.Message}");
                await Provider.SendGlobalConfigAsync(new KiloExtensionDTOs.KiloConfig.Config());
            }
        }

        /// <summary>
        /// Handles the requestIndexingStatus message from the webview.
        /// Sends the indexing status to the webview.
        /// </summary>
        /// <param name="payload">The message payload (unused).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleRequestIndexingStatusAsync(JsonElement? payload)
        {
            await Provider.SendIndexingStatusAsync(JsonDocument.Parse("\"off\"").RootElement);
        }

        /// <summary>
        /// Handles the requestKiloEmbeddingModels message from the webview.
        /// Sends the list of Kilo embedding models to the webview.
        /// </summary>
        /// <param name="payload">The message payload (unused).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleRequestKiloEmbeddingModelsAsync(JsonElement? payload)
        {
            await Provider.SendKiloEmbeddingModelsAsync(Array.Empty<object>());
        }

        /// <summary>
        /// Handles the requestImageModels message from the webview.
        /// Sends the list of image generation models to the webview.
        /// </summary>
        /// <param name="payload">The message payload (unused).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleRequestImageModelsAsync(JsonElement? payload)
        {
            await Provider.SendImageModelsAsync(new List<ApiImageModel>());
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}

