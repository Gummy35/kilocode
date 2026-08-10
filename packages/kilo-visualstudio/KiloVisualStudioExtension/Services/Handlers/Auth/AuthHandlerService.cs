using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services.Handlers.Auth
{
    /// <summary>
    /// Handles authentication-related operations like login, logout, and profile refresh.
    /// This matches the VS Code pattern where auth handling is extracted into
    /// kilo-provider/handlers/auth.ts.
    /// </summary>
    public class AuthHandlerService : IDisposable
    {
        private readonly VSProvider _provider;
        private bool _disposed;

        /// <summary>
        /// Creates a new AuthHandlerService instance.
        /// </summary>
        /// <param name="provider">The VSProvider instance to use for webview communication.</param>
        public AuthHandlerService(VSProvider provider)
        {
            _provider = provider;
        }

        /// <summary>
        /// Handles the login message from the webview.
        /// Initiates the login flow with the backend by fetching the user profile.
        /// 
        /// VS Code workflow: Matches the pattern in kilo-provider/handlers/auth.ts where
        /// handleLogin fetches /kilo/profile and sends profileData to the webview.
        /// 
        /// Workflow steps:
        /// 1. Check if HTTP client is connected to backend
        /// 2. Fetch user profile from /kilo/profile endpoint
        /// 3. Extract profile property from response
        /// 4. Send profileData message to webview with cloned profile data
        /// 
        /// Messages sent to webview:
        /// - profileData: { profile: { email, name, id, ... } }
        /// - error: { message: "Not connected to backend" }
        /// </summary>
        /// <param name="payload">The message payload (unused for login).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleLoginAsync(JsonElement? payload)
        {
            try
            {
                var kiotaClient = _provider.GetKiloClient();
                if (kiotaClient == null)
                {
                    await _provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                var profile = await kiotaClient.Kilo.Profile.GetAsync();
                if (profile != null)
                {
                    await _provider.SendProfileDataAsync(JsonSerializer.SerializeToElement(profile));
                }
            }
            catch (Exception ex)
            {
                await _provider.SendErrorAsync("Login error", ex.Message);
            }
        }

        /// <summary>
        /// Handles the refreshProfile message from the webview.
        /// Refreshes the user profile data from the backend and sends it to the webview.
        /// 
        /// VS Code workflow: Matches the pattern in kilo-provider/handlers/auth.ts where
        /// handleRefreshProfile fetches /kilo/profile and posts profileData to webview.
        /// 
        /// Workflow steps:
        /// 1. Get HTTP client from provider
        /// 2. Verify client is connected
        /// 3. Fetch user profile from /kilo/profile endpoint
        /// 4. Construct message with type "profileData" and profile data
        /// 5. Post message to webview via provider
        /// 
        /// Messages sent to webview:
        /// - profileData: { data: { email, name, id, ... } }
        /// </summary>
        /// <param name="payload">The message payload (unused for refreshProfile).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleRefreshProfileAsync(JsonElement? payload)
        {
            var kiotaClient = _provider.GetKiloClient();
            if (kiotaClient == null) return;
            try
            {
                var profile = await kiotaClient.Kilo.Profile.GetAsync();
                if (profile != null)
                {
                    var message = new { type = "profileData", data = JsonSerializer.SerializeToElement(profile) };
                    _provider.PostMessage(JsonSerializer.Serialize(message));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] AuthHandler: error refreshing profile: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
