using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services.Handlers.Model
{
    /// <summary>
    /// Handles model selection related operations like requestModelSelections, persistVariant, persistRecents.
    /// This matches the VS Code pattern where model selection handling is extracted into
    /// kilo-provider/model-state.ts.
    /// </summary>
    public class ModelHandlerService : IDisposable
    {
        private readonly VSProvider _provider;
        private bool _disposed;

        /// <summary>
        /// Creates a new ModelHandlerService instance.
        /// </summary>
        /// <param name="provider">The VSProvider instance to use for webview communication.</param>
        public ModelHandlerService(VSProvider provider)
        {
            _provider = provider;
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
            _provider.PostMessage(JsonSerializer.Serialize(message));
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
        /// Fetches and sends the list of available Kilo embedding models.
        /// </summary>
        /// <param name="payload">The message payload (unused).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleRequestKiloEmbeddingModelsAsync(JsonElement? payload)
        {
            try
            {
                var kiotaClient = _provider.GetKiloClient();
                if (kiotaClient == null)
                {
                    await _provider.SendKiloEmbeddingModelsAsync(Array.Empty<object>());
                    return;
                }

                var models = await kiotaClient.Model.Embedding.GetAsync();
                var modelsList = models != null ? models.Select(m => (object)m).ToArray() : Array.Empty<object>();

                await _provider.SendKiloEmbeddingModelsAsync(modelsList);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] ModelHandler: Error fetching embedding models: {ex.Message}");
                await _provider.SendKiloEmbeddingModelsAsync(Array.Empty<object>());
            }
        }

        /// <summary>
        /// Handles the requestImageModels message from the webview.
        /// Fetches and sends the list of available image generation models.
        /// </summary>
        /// <param name="payload">The message payload (unused).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleRequestImageModelsAsync(JsonElement? payload)
        {
            try
            {
                var kiotaClient = _provider.GetKiloClient();
                if (kiotaClient == null)
                {
                    await _provider.SendImageModelsAsync(Array.Empty<object>());
                    return;
                }

                var models = await kiotaClient.Model.Image.GetAsync();
                var modelsList = models != null ? models.Select(m => (object)m).ToArray() : Array.Empty<object>();

                await _provider.SendImageModelsAsync(modelsList);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] ModelHandler: Error fetching image models: {ex.Message}");
                await _provider.SendImageModelsAsync(Array.Empty<object>());
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
