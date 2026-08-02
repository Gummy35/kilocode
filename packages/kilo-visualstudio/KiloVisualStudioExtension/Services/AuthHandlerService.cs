using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services
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
        /// Initiates the login flow with the backend.
        /// </summary>
        /// <param name="payload">The message payload (unused for login).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleLoginAsync(JsonElement? payload)
        {
            try
            {
                var httpClient = _provider.GetHttpClient();
                if (httpClient == null || !httpClient.IsConnected())
                {
                    await _provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                var response = await httpClient.GetJsonAsync("/kilo/profile");
                if (response != null && response.RootElement.TryGetProperty("profile", out var profile))
                {
                    await _provider.SendProfileDataAsync(profile.Clone());
                }
            }
            catch (Exception ex)
            {
                await _provider.SendErrorAsync("Login error", ex.Message);
            }
        }

        /// <summary>
        /// Handles the logout message from the webview.
        /// Logs out the current user.
        /// </summary>
        /// <param name="payload">The message payload (unused for logout).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleLogoutAsync(JsonElement? payload)
        {
            var httpClient = _provider.GetHttpClient();
            if (httpClient == null) return;
            try
            {
                await httpClient.PostJsonAsync("/auth/logout", new { });
                System.Diagnostics.Debug.WriteLine("[Kilo] AuthHandler: logout");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] AuthHandler: error logging out: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles the refreshProfile message from the webview.
        /// Refreshes the user profile data from the backend.
        /// </summary>
        /// <param name="payload">The message payload (unused for refreshProfile).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleRefreshProfileAsync(JsonElement? payload)
        {
            var httpClient = _provider.GetHttpClient();
            if (httpClient == null) return;
            try
            {
                var profile = await httpClient.GetJsonAsync("/kilo/profile");
                if (profile != null)
                {
                    var message = new { type = "profileData", data = profile.RootElement };
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
