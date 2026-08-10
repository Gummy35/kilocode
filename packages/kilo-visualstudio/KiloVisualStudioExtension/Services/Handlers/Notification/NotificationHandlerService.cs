using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services.Handlers.Notification
{
    /// <summary>
    /// Handles notification-related operations like requestNotifications, dismissNotification, resetReadNotifications.
    /// This matches the VS Code pattern where notification handling is extracted into
    /// kilo-provider/notifications.ts.
    /// </summary>
    public class NotificationHandlerService : IDisposable
    {
        private readonly VSProvider _provider;
        private bool _disposed;

        /// <summary>
        /// Creates a new NotificationHandlerService instance.
        /// </summary>
        /// <param name="provider">The VSProvider instance to use for webview communication.</param>
        public NotificationHandlerService(VSProvider provider)
        {
            _provider = provider;
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
                var kiotaClient = _provider.GetKiloClient();
                if (kiotaClient == null)
                {
                    await _provider.SendNotificationsAsync(Array.Empty<object>());
                    return;
                }

                var notifications = await kiotaClient.Notification.GetAsync();
                var notificationsList = notifications != null ? notifications.Select(n => (object)n).ToArray() : Array.Empty<object>();

                await _provider.SendNotificationsAsync(notificationsList);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] NotificationHandler: Error fetching notifications: {ex.Message}");
                await _provider.SendNotificationsAsync(Array.Empty<object>());
            }
        }

        /// <summary>
        /// Handles the dismissNotification message from the webview.
        /// Dismisses a specific notification.
        /// </summary>
        /// <param name="payload">The message payload containing notification ID.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleDismissNotificationAsync(JsonElement? payload)
        {
            if (payload == null || !payload.Value.TryGetProperty("notificationId", out var notifIdProp))
            {
                await _provider.SendErrorAsync("Missing notificationId", "notificationId is required");
                return;
            }

            var notificationId = notifIdProp.GetString();
            if (string.IsNullOrEmpty(notificationId))
            {
                await _provider.SendErrorAsync("Invalid notificationId", "notificationId cannot be empty");
                return;
            }

            try
            {
                var kiotaClient = _provider.GetKiloClient();
                if (kiotaClient == null)
                {
                    await _provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                await kiotaClient.Notification[notificationId].Dismiss.PostAsync(new Generated.Notification.Item.Dismiss.DismissPostRequestBody());
            }
            catch (Exception ex)
            {
                await _provider.SendErrorAsync("Dismiss notification error", ex.Message);
            }
        }

        /// <summary>
        /// Handles the resetReadNotifications message from the webview.
        /// Resets all read notifications to unread status.
        /// </summary>
        /// <param name="payload">The message payload (unused for resetReadNotifications).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleResetReadNotificationsAsync(JsonElement? payload)
        {
            try
            {
                var kiotaClient = _provider.GetKiloClient();
                if (kiotaClient == null)
                {
                    await _provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                await kiotaClient.Notification.ResetRead.PostAsync(new Generated.Notification.ResetRead.ResetReadPostRequestBody());
            }
            catch (Exception ex)
            {
                await _provider.SendErrorAsync("Reset read notifications error", ex.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
