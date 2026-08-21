using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KiloVisualStudioExtension.ApiClient;

namespace KiloVisualStudioExtension.Services.Handlers.Notification
{
    /// <summary>
    /// Handles notification-related operations like requestNotifications, dismissNotification, resetReadNotifications.
    /// This matches the VS Code pattern where notification handling is extracted into
    /// kilo-provider/notifications.ts.
    /// </summary>
    public class NotificationHandlerService : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private bool _disposed;

        private VSProvider Provider => _serviceProvider.GetService<VSProvider>() 
            ?? throw new InvalidOperationException("VSProvider not registered in service provider");

        /// <summary>
        /// Creates a new NotificationHandlerService instance.
        /// </summary>
        /// <param name="serviceProvider">The service provider for dependency injection.</param>
        public NotificationHandlerService(ServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Handles the requestNotifications message from the webview.
        /// Fetches and sends pending notifications to the webview.
        /// </summary>
        /// <param name="payload">The message payload (unused for requestNotifications).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleRequestNotificationsAsync(JsonElement? payload)
        {
            try
            {
                var nswagClient = Provider.GetNswagClient();
                if (nswagClient == null)
                {
                    await Provider.SendNotificationsAsync(Array.Empty<object>());
                    return;
                }

                var notifications = await nswagClient.Kilo_notificationsAsync("", "");
                var notificationsList = notifications != null ? notifications.Select(n => (object)n).ToArray() : Array.Empty<object>();

                await Provider.SendNotificationsAsync(notificationsList);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] NotificationHandler: Error fetching notifications: {ex.Message}");
                await Provider.SendNotificationsAsync(Array.Empty<object>());
            }
        }

        /// <summary>
        /// Handles the dismissNotification message from the webview.
        /// Notification dismiss endpoint is not available in the current API.
        /// </summary>
        /// <param name="payload">The message payload containing notification ID.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleDismissNotificationAsync(JsonElement? payload)
        {
            // Notification dismiss endpoint does not exist in current API
            await Provider.SendErrorAsync("Not supported", "Notification dismiss is not available in the current API");
        }

        /// <summary>
        /// Handles the resetReadNotifications message from the webview.
        /// Reset read notifications endpoint is not available in the current API.
        /// </summary>
        /// <param name="payload">The message payload (unused for resetReadNotifications).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleResetReadNotificationsAsync(JsonElement? payload)
        {
            // Reset read notifications endpoint does not exist in current API
            await Provider.SendErrorAsync("Not supported", "Reset read notifications is not available in the current API");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}

