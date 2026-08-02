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
                var httpClient = _provider.GetHttpClient();
                if (httpClient == null)
                {
                    await _provider.SendNotificationsAsync(Array.Empty<object>());
                    return;
                }

                var response = await httpClient.GetJsonAsync("/notification");
                var notifications = Array.Empty<object>();
                
                if (response != null && response.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var notificationsList = new System.Collections.Generic.List<object>();
                    foreach (var notif in response.RootElement.EnumerateArray())
                    {
                        notificationsList.Add(notif.Clone());
                    }
                    notifications = notificationsList.ToArray();
                }

                await _provider.SendNotificationsAsync(notifications);
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
                var httpClient = _provider.GetHttpClient();
                if (httpClient == null || !httpClient.IsConnected())
                {
                    await _provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                await httpClient.PostAsync($"/notification/{notificationId}/dismiss", null);
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
                var httpClient = _provider.GetHttpClient();
                if (httpClient == null || !httpClient.IsConnected())
                {
                    await _provider.SendErrorAsync("Not connected", "Not connected to backend");
                    return;
                }

                await httpClient.PostAsync("/notification/reset-read", null);
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
