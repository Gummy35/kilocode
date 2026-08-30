using KiloExtensionDTOs.ExtensionMessages;
using KiloExtensionDTOs.Profile;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.Services;
using KiloVisualStudioExtension.Utils;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Navigation;

namespace KiloVisualStudioExtension.Services.Handlers.Notification
{
  /// <summary>
  /// Handles notification-related operations like requestNotifications, dismissNotification, resetReadNotifications.
  /// This matches the VS Code pattern where notification handling is extracted into
  /// kilo-provider/notifications.ts.
  /// </summary>
  public class NotificationService : ServiceProviderServiceBase
  {

    private bool _disposed;
    private NotificationsLoadedMessage? _cachedMessage;


    private VSProvider Provider => _serviceProvider.GetService<VSProvider>()
            ?? throw new InvalidOperationException("VSProvider not registered in service provider");

    private ICacheService Cache => (ICacheService)_serviceProvider.GetService(typeof(ICacheService))
        ?? throw new InvalidOperationException("CacheService not registered in service provider");

    /// <summary>
    /// Creates a new NotificationHandlerService instance.
    /// </summary>
    /// <param name="serviceProvider">The service provider for dependency injection.</param>
    public NotificationService(ServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    private NotificationsContext GetNotificationsContext()
    {
      var connectionService = _serviceProvider.GetService<KiloConnectionService>();
      return new NotificationsContext
      {
        //Context = this.extensionContext
        Client = connectionService.GetNswagClient(),
        Cached = () => this._cachedMessage,
        Set = (message) => this._cachedMessage = message,
        Post = (message) => Provider.PostMessage(message),
        Notify = (id) => connectionService.NotifyNotificationDismissed(id)
      };
    }

    /// <summary>
    /// Handles the requestNotifications message from the webview.
    /// Fetches and sends pending notifications to the webview.
    /// </summary>
    /// <param name="payload">The message payload (unused for requestNotifications).</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task FetchAndSendNotificationsAsync()
    {
      await FetchAndSendNotificationsAsync(GetNotificationsContext());
    }


    // TypeScript: export async function fetchAndSendContextNotifications(ctx: NotificationsContext): Promise<void> {

    public async Task FetchAndSendNotificationsAsync(NotificationsContext ctx)
    {
      var _cacheService = _serviceProvider.GetService<ICacheService>();
      //   if (!ctx.client)
      if (ctx.Client == null)
      //   {
      {
        //     const cached = ctx.cached()
        var cached = ctx.Cached();
        //     if (cached)
        if (cached != null)
        //     {
        {
          //       const persisted = ctx.context?.globalState.get<string[]>(KEY, []) ?? []
          List<string> persisted = CacheService.GlobalState.Get<List<string>>("kilo.dismissedNotificationIds", []) ?? [];
          var dismissedIds = persisted.Count > 0
            ? cached.DismissedIds.Concat(persisted).Distinct().ToList()
            : cached.DismissedIds;

          var message = new NotificationsLoadedMessage
          {
            Type = cached.Type,
            Notifications = cached.Notifications,
            DismissedIds = dismissedIds,
          };
          // Only update if dismissedIds actually changed
          if (!dismissedIds.SequenceEqual(cached.DismissedIds))
          {
            ctx.Set(message);
          }
          ctx.Post(message);
        }
        return;
      }


      //   try
      //   {
      try {
        //     const { data: all } = await retry(() => ctx.client!.kilo.notifications(undefined, { throwOnError: true }))
        var all = await Retry.RetryAsync(() => ctx.Client.Kilo_notificationsAsync(
          _serviceProvider.GetService<ProjectDirectoryProvider>().GetProjectDirectory(), ""));

        //     const notifications = all.filter((n) => !n.showIn || n.showIn.includes("extension"))
        var notifications = all.Where(n => n.ShowIn == null || n.ShowIn.Contains("extension"));
        //     const existing = ctx.context?.globalState.get<string[]>(KEY, []) ?? []
        var existing = CacheService.GlobalState.Get<List<string>>("kilo.dismissedNotificationIds", []) ?? [];
        //     const active = new Set(notifications.map((n) => n.id))
        var active = notifications.Select(n => n.Id).ToHashSet();
        //     const dismissedIds = notifications.length > 0 ? existing.filter((id) => active.has(id)) : existing
        var dismissedIds = notifications.Count() > 0 ? existing.Where(id => active.Contains(id)).ToList() : existing;
        //     if (dismissedIds.length !== existing.length) await ctx.context?.globalState.update(KEY, dismissedIds)
        if (dismissedIds.Count() != existing.Count())
          await CacheService.GlobalState.UpdateAsync("kilo.dismissedNotificationIds", dismissedIds);
        //     const message = { type: "notificationsLoaded" as const, notifications, dismissedIds }
        var message = new NotificationsLoadedMessage
        {
          Notifications = notifications.Select(EntityConverter.Convert).ToList(),
          DismissedIds = dismissedIds,
        };
        //     ctx.set(message)
        ctx.Set(message);
        //     ctx.post(message)
        ctx.Post(message);
        //   } catch (error) {
        //     console.error("[Kilo New] KiloProvider: Failed to fetch notifications:", error)
        //   }
        // }
      }
      catch (Exception error)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo New] KiloProvider: Failed to fetch notifications: {error}");
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

  /// <summary>
  /// Context for handling notifications with caching and posting capabilities.
  /// Matches the TypeScript NotificationsContext interface.
  /// </summary>
  public class NotificationsContext
  {
    public KiloApiClient Client { get; set; }

    /// <summary>
    /// Gets or sets the cached notifications message.
    /// </summary>
    public Func<NotificationsLoadedMessage?> Cached { get; set; }

    /// <summary>
    /// Action to set the cached notifications message.
    /// </summary>
    public Action<NotificationsLoadedMessage> Set { get; set; }

    /// <summary>
    /// Action to post a notifications message to the webview.
    /// </summary>
    public Action<NotificationsLoadedMessage> Post { get; set; }

    /// <summary>
    /// Action to notify about a specific notification ID.
    /// </summary>
    public Action<string> Notify { get; set; }

    /// <summary>
    /// Gets the cached notifications message.
    /// </summary>
    public NotificationsLoadedMessage? CallGet()
    {
      return Cached?.Invoke();
    }

    /// <summary>
    /// Sets the cached notifications message.
    /// </summary>
    public void CallSet(NotificationsLoadedMessage message)
    {
      Set?.Invoke(message);
    }

    /// <summary>
    /// Posts a notifications message to the webview.
    /// </summary>
    public void CallPost(NotificationsLoadedMessage message)
    {
      Post?.Invoke(message);
    }

    /// <summary>
    /// Notifies about a specific notification ID.
    /// </summary>
    public void CallNotify(string id)
    {
      Notify?.Invoke(id);
    }
  }
}
