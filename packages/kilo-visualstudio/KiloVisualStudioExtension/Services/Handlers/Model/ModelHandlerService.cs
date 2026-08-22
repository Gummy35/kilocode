using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KiloVisualStudioExtension.ApiClient;
using ApiImageModel = KiloVisualStudioExtension.ApiClient.Anonymous10;

namespace KiloVisualStudioExtension.Services.Handlers.Model
{
    /// <summary>
    /// Handles model selection related operations like requestModelSelections, persistVariant, persistRecents.
    /// This matches the VS Code pattern where model selection handling is extracted into
    /// kilo-provider/model-state.ts.
    /// </summary>
    public class ModelHandlerService : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private bool _disposed;

        private VSProvider Provider => _serviceProvider.GetService<VSProvider>() 
            ?? throw new InvalidOperationException("VSProvider not registered in service provider");

        /// <summary>
        /// Creates a new ModelHandlerService instance.
        /// </summary>
        /// <param name="serviceProvider">The service provider for dependency injection.</param>
        public ModelHandlerService(ServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Handles the requestModelSelections message from the webview.
        /// Sends the current model selection state to the webview.
        /// </summary>
        /// <param name="payload">The message payload (unused for requestModelSelections).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public void HandleRequestModelSelections(JsonElement? payload)
        {
            // Send current model selection state
            var message = new 
            { 
                type = "modelSelectionsLoaded",
                selections = new {}
            };
            Provider.PostMessage(JsonSerializer.Serialize(message));
        }

        /// <summary>
        /// Handles the persistVariant message from the webview.
        /// Persists the selected variant (model/agent combination).
        /// </summary>
        /// <param name="payload">The message payload containing variant details.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public void HandlePersistVariant(JsonElement? payload)
        {
            // Persist variant to storage
            if (payload.HasValue)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] ModelHandler: Persisting variant: {payload.Value.GetRawText()}");
                // Persist to StateStorage or backend - placeholder for future implementation
            }
        }

        /// <summary>
        /// Handles the persistRecents message from the webview.
        /// Persists recently used models.
        /// </summary>
        /// <param name="payload">The message payload containing recent models.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public void HandlePersistRecents(JsonElement? payload)
        {
            // Persist recent models to storage
            if (payload.HasValue)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] ModelHandler: Persisting recents: {payload.Value.GetRawText()}");
                // Persist to StateStorage or backend - placeholder for future implementation
            }
        }

        /// <summary>
        /// Handles the requestKiloEmbeddingModels message from the webview.
        /// Embedding models endpoint is not available in the current API.
        /// Sends empty array as placeholder.
        /// </summary>
        /// <param name="payload">The message payload (unused).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleRequestKiloEmbeddingModelsAsync(JsonElement? payload)
        {
            // Embedding models endpoint does not exist in current API
            await Provider.SendKiloEmbeddingModelsAsync(Array.Empty<object>());
        }

        /// <summary>
        /// Handles the requestImageModels message from the webview.
        /// Fetches and sends the list of available image generation models.
        /// Uses Kilo.Models.Images endpoint from generated Kiota client.
        /// </summary>
        /// <param name="payload">The message payload (unused).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleRequestImageModelsAsync(JsonElement? payload)
        {
            try
            {
                var nswagClient = Provider.GetNswagClient();
                if (nswagClient == null)
                {
                    await Provider.SendImageModelsAsync(new List<ApiImageModel>());
                    return;
                }

                var models = await nswagClient.Kilo_models_imagesAsync("", "");
                var modelsList = models != null ? models.ToList() : new List<ApiImageModel>();

                await Provider.SendImageModelsAsync(modelsList);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] ModelHandler: Error fetching image models: {ex.Message}");
                await Provider.SendImageModelsAsync(new List<ApiImageModel>());
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}

