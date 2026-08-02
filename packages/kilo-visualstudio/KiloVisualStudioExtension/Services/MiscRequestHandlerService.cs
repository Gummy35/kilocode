using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services
{
    /// <summary>
    /// Handles miscellaneous request operations like recents, favorites, variants, skills, commands.
    /// These are simple request handlers that return static or cached data.
    /// </summary>
    public class MiscRequestHandlerService : IDisposable
    {
        private readonly VSProvider _provider;
        private bool _disposed;

        /// <summary>
        /// Creates a new MiscRequestHandlerService instance.
        /// </summary>
        /// <param name="provider">The VSProvider instance to use for webview communication.</param>
        public MiscRequestHandlerService(VSProvider provider)
        {
            _provider = provider;
        }

        /// <summary>
        /// Handles the requestRecents message from the webview.
        /// Sends empty recents list (placeholder for future implementation).
        /// </summary>
        /// <param name="payload">The message payload (unused).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public void HandleRequestRecents(JsonElement? payload)
        {
            var message = new { type = "recentsLoaded", recents = new object[0] };
            _provider.PostMessage(JsonSerializer.Serialize(message));
        }

        /// <summary>
        /// Handles the requestFavorites message from the webview.
        /// Sends empty favorites list (placeholder for future implementation).
        /// </summary>
        /// <param name="payload">The message payload (unused).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public void HandleRequestFavorites(JsonElement? payload)
        {
            var message = new { type = "favoritesLoaded", favorites = new object[0] };
            _provider.PostMessage(JsonSerializer.Serialize(message));
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
            _provider.PostMessage(JsonSerializer.Serialize(message));
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
                var httpClient = _provider.GetHttpClient();
                if (httpClient == null)
                {
                    await _provider.SendSkillsAsync(Array.Empty<object>());
                    return;
                }

                var response = await httpClient.GetJsonAsync("/skill");
                var skills = Array.Empty<object>();
                
                if (response != null && response.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var skillsList = new System.Collections.Generic.List<object>();
                    foreach (var skill in response.RootElement.EnumerateArray())
                    {
                        skillsList.Add(skill.Clone());
                    }
                    skills = skillsList.ToArray();
                }

                await _provider.SendSkillsAsync(skills);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] MiscRequestHandler: Error fetching skills: {ex.Message}");
                await _provider.SendSkillsAsync(Array.Empty<object>());
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
                var httpClient = _provider.GetHttpClient();
                if (httpClient == null)
                {
                    await _provider.SendCommandsAsync(Array.Empty<object>());
                    return;
                }

                var response = await httpClient.GetJsonAsync("/command");
                var commands = Array.Empty<object>();
                
                if (response != null && response.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var commandsList = new System.Collections.Generic.List<object>();
                    foreach (var cmd in response.RootElement.EnumerateArray())
                    {
                        commandsList.Add(cmd.Clone());
                    }
                    commands = commandsList.ToArray();
                }

                await _provider.SendCommandsAsync(commands);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] MiscRequestHandler: Error fetching commands: {ex.Message}");
                await _provider.SendCommandsAsync(Array.Empty<object>());
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
                var httpClient = _provider.GetHttpClient();
                if (httpClient == null)
                {
                    await _provider.SendGlobalConfigAsync(JsonDocument.Parse("{}").RootElement);
                    return;
                }

                var response = await httpClient.GetJsonAsync("/config/global");
                var config = JsonDocument.Parse("{}").RootElement;
                
                if (response != null && response.RootElement.TryGetProperty("config", out var c))
                {
                    config = c.Clone();
                }

                await _provider.SendGlobalConfigAsync(config);
                response?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] MiscRequestHandler: Error fetching global config: {ex.Message}");
                await _provider.SendGlobalConfigAsync(JsonDocument.Parse("{}").RootElement);
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
            await _provider.SendIndexingStatusAsync(JsonDocument.Parse("\"off\"").RootElement);
        }

        /// <summary>
        /// Handles the requestKiloEmbeddingModels message from the webview.
        /// Sends the list of Kilo embedding models to the webview.
        /// </summary>
        /// <param name="payload">The message payload (unused).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleRequestKiloEmbeddingModelsAsync(JsonElement? payload)
        {
            await _provider.SendKiloEmbeddingModelsAsync(Array.Empty<object>());
        }

        /// <summary>
        /// Handles the requestImageModels message from the webview.
        /// Sends the list of image generation models to the webview.
        /// </summary>
        /// <param name="payload">The message payload (unused).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleRequestImageModelsAsync(JsonElement? payload)
        {
            await _provider.SendImageModelsAsync(Array.Empty<object>());
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
