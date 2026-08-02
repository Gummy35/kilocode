using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services.Handlers.StateManagement
{
    /// <summary>
    /// Handles state management operations like setState and getState.
    /// This matches the VS Code pattern where state management is extracted into separate handler modules.
    /// </summary>
    public class StateManagementService : IDisposable
    {
        private readonly VSProvider _provider;
        private bool _disposed;

        /// <summary>
        /// Creates a new StateManagementService instance.
        /// </summary>
        /// <param name="provider">The VSProvider instance to use for webview communication.</param>
        public StateManagementService(VSProvider provider)
        {
            _provider = provider;
        }

        /// <summary>
        /// Handles the setState message from the webview.
        /// Saves the webview state for later recovery.
        /// </summary>
        /// <param name="payload">The message payload containing the state.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleSetStateAsync(JsonElement? payload)
        {
            if (!payload.HasValue) return;
            
            System.Diagnostics.Debug.WriteLine($"[Kilo] StateManagement: setState received");
            
            if (payload.Value.TryGetProperty("state", out var state))
            {
                // Store state via provider's internal method
                await _provider.StoreStateAsync(state);
                System.Diagnostics.Debug.WriteLine($"[Kilo] StateManagement: state saved");
            }
        }

        /// <summary>
        /// Handles the getState message from the webview.
        /// Retrieves and sends the saved webview state.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleGetStateAsync()
        {
            System.Diagnostics.Debug.WriteLine($"[Kilo] StateManagement: getState requested");
            
            var state = await _provider.GetStoredStateAsync();
            if (state.HasValue)
            {
                var message = new { type = "setState", state = state.Value };
                _provider.PostMessage(JsonSerializer.Serialize(message));
                System.Diagnostics.Debug.WriteLine($"[Kilo] StateManagement: state sent to webview");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] StateManagement: no state to send");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
