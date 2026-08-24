using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KiloExtensionDTOs.Profile;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.Services;

namespace KiloVisualStudioExtension.Services.Handlers.Notification
{
    /// <summary>
    /// Handles notification-related operations like requestNotifications, dismissNotification, resetReadNotifications.
    /// This matches the VS Code pattern where notification handling is extracted into
    /// kilo-provider/notifications.ts.
    /// </summary>
    public class NotificationHandlerService : ServiceProviderServiceBase
  {
  
        private bool _disposed;

        private VSProvider Provider => _serviceProvider.GetService<VSProvider>() 
            ?? throw new InvalidOperationException("VSProvider not registered in service provider");

        private ICacheService Cache => (ICacheService)_serviceProvider.GetService(typeof(ICacheService)) 
            ?? throw new InvalidOperationException("CacheService not registered in service provider");

        /// <summary>
        /// Creates a new NotificationHandlerService instance.
        /// </summary>
        /// <param name="serviceProvider">The service provider for dependency injection.</param>
        public NotificationHandlerService(ServiceProvider serviceProvider):base(serviceProvider)
        {
        }

        /// <summary>
        /// Handles the requestNotifications message from the webview.
        /// Fetches and sends pending notifications to the webview.
        /// </summary>
        /// <param name="payload">The message payload (unused for requestNotifications).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleRequestNotificationsAsync(JsonElement? payload)
        {
            // TypeScript: export async function fetchAndSendNotifications(ctx: NotificationsContext): Promise<void> {
            //   if (!ctx.client)
            //   {
            //     const cached = ctx.cached()
            //     if (cached)
            //     {
            //       const persisted = ctx.context?.globalState.get<string[]>(KEY, []) ?? []
            //       const dismissedIds =
            //         persisted.length > 0 ? Array.from(new Set([...cached.dismissedIds, ...persisted])) : cached.dismissedIds
            //       const message = { ...cached, dismissedIds }
            //       if (message !== cached) ctx.set(message)
            //       ctx.post(message)
            //     }
            //     return
            //   }
            //
            //   try
            //   {
            //     const { data: all } = await retry(() => ctx.client!.kilo.notifications(undefined, { throwOnError: true }))
            //     const notifications = all.filter((n) => !n.showIn || n.showIn.includes("extension"))
            //     const existing = ctx.context?.globalState.get<string[]>(KEY, []) ?? []
            //     const active = new Set(notifications.map((n) => n.id))
            //     const dismissedIds = notifications.length > 0 ? existing.filter((id) => active.has(id)) : existing
            //     if (dismissedIds.length !== existing.length) await ctx.context?.globalState.update(KEY, dismissedIds)
            //     const message = { type: "notificationsLoaded" as const, notifications, dismissedIds }
            //     ctx.set(message)
            //     ctx.post(message)
            //   } catch (error) {
            //     console.error("[Kilo New] KiloProvider: Failed to fetch notifications:", error)
            //   }
            // }

            try
            {
                var nswagClient = Provider.GetNswagClient();
                if (nswagClient == null)
                {
                    var cached = Cache.GetJson("notificationsLoadedMessage");
                    if (cached.HasValue)
                    {
                        Provider.PostMessage(cached.Value);
                    }
                    await Provider.SendNotificationsAsync(new List<KilocodeNotification>());
                    return;
                }

                var notifications = await nswagClient.Kilo_notificationsAsync("", "");
                //var notificationsList = notifications != null ? notifications.Select(n => EntityConverter.Convert(n)).ToList() : new List<object>();

                //await Provider.SendNotificationsAsync(notificationsList);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] NotificationHandler: Error fetching notifications: {ex.Message}");
                //await Provider.SendNotificationsAsync(Array.Empty<object>());
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
