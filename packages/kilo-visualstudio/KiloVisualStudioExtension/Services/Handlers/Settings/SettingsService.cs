using KiloExtensionDTOs.ExtensionMessages;
using KiloVisualStudioExtension.ApiClient;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace KiloVisualStudioExtension.Services.Handlers.Settings
{
  /// <summary>
  /// Handles settings-related operations like indexing settings, chat settings, throughput settings, autocomplete settings.
  /// This matches the VS Code pattern where settings handling is extracted into separate handler modules.
  /// </summary>
  public class SettingsService : ServiceProviderServiceBase
  {
    private bool _disposed;    

    private VSProvider Provider => _serviceProvider.GetService<VSProvider>()
        ?? throw new InvalidOperationException("VSProvider not registered in service provider");

    /// <summary>
    /// Creates a new SettingsHandlerService instance.
    /// </summary>
    /// <param name="serviceProvider">The service provider for dependency injection.</param>
    public SettingsService(ServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    /// <summary>
    /// Handles the requestIndexingSettings message from the webview.
    /// Sends the current indexing settings to the webview.
    /// </summary>
    /// <param name="payload">The message payload (unused).</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task SendIndexingSettingsAsync()
    {
      Provider.PostMessage(BuildIndexingSettingsMessage());
    }

    private IndexingSettingsLoadedMessage BuildIndexingSettingsMessage()
    {
      var config = VSExtensionSettings.Get("kilo-code.new.indexing.showButtonWhenDisabled", true);
      return new IndexingSettingsLoadedMessage
      {
        Settings = new IndexingSettingsLoadedMessageSettingsType
        {
          ShowButtonWhenDisabled = config
        }
      };
    }

    /// <summary>
    /// Handles the requestChatSettings message from the webview.
    /// Sends the current chat settings to the webview.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task SendChatSettingsAsync()
    {
      Provider.PostMessage(BuildChatSettingsMessage());
    }

    public ChatSettingsLoadedMessage BuildChatSettingsMessage()
    {
      return new ChatSettingsLoadedMessage
      {
        Settings = new ChatSettingsLoadedMessageSettingsType
        {
          ShiftTabCyclesVariant = true
        }
      };
    }

    /// <summary>
    /// Handles the requestThroughputSetting message from the webview.
    /// Sends the current throughput setting to the webview.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task SendThroughputSettingAsync()
    {
      Provider.PostMessage(BuildThroughputSettingMessage());
    }

    public ThroughputSettingLoadedMessage BuildThroughputSettingMessage()
    {
      return new ThroughputSettingLoadedMessage
      {
        Visible = true
      };
    }

    /// <summary>
    /// Handles the requestAutocompleteSettings message from the webview.
    /// Sends the current autocomplete settings to the webview.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task SendAutocompleteSettingsAsync()
    {
      Provider.PostMessage(BuildAutocompleteSettingsMessage());
    }

    private AutocompleteSettingsLoadedMessage BuildAutocompleteSettingsMessage()
    {
      var config = VSExtensionSettings.GetConfiguration("kilo-code.new.indexing");
      // Pass through provider/model as-is (null when unset) so the webview can
      // distinguish "user hasn't picked" from "user picked the current default."
      // The runtime resolves null → DEFAULT_AUTOCOMPLETE_MODEL via getAutocompleteModel().

      return new AutocompleteSettingsLoadedMessage
      {
        Settings = new AutocompleteSettingsLoadedMessageSettingsType
        {
          EnableAutoTrigger = config.Get("enableAutoTrigger", true),
          EnableSmartInlineTaskKeybinding = config.Get("enableSmartInlineTaskKeybinding", false),
          EnableChatAutocomplete = config.Get("enableChatAutocomplete", false),
          Provider = config.Get<string>("provider", null),
          Model = config.Get<string>("model", null)
        }
      };
    }

    /// <summary>
    /// Handles the requestIndexingStatus message from the webview.
    /// Fetches and sends the current indexing status to the webview.
    /// </summary>
    /// <param name="payload">The message payload (unused).</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HandleRequestIndexingStatusAsync(JsonElement? payload)
    {
      try
      {
        var nswagClient = Provider.GetNswagClient();
        if (nswagClient == null)
        {
          await Provider.SendIndexingStatusAsync(JsonDocument.Parse("{}").RootElement);
          return;
        }

        var status = await nswagClient.Indexing_statusAsync("", "");
        var statusData = status != null ? JsonSerializer.SerializeToElement(status) : JsonDocument.Parse("{}").RootElement;

        await Provider.SendIndexingStatusAsync(statusData);
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] SettingsHandler: Error fetching indexing status: {ex.Message}");
        await Provider.SendIndexingStatusAsync(JsonDocument.Parse("{}").RootElement);
      }
    }

    public void Dispose()
    {
      if (_disposed) return;
      DisposeWatchers();
      _disposed = true;
    }

    internal void DisposeWatchers()
    {
      _autocompleteConfigDisposable?.Dispose();
      _indexingConfigDisposable?.Dispose();
      _chatConfigDisposable?.Dispose();
      _throughputConfigDisposable?.Dispose();
      _telemetryStateDisposable?.Dispose();
    }

    private IDisposable? _autocompleteConfigDisposable;
    private IDisposable? _indexingConfigDisposable;
    private IDisposable? _chatConfigDisposable;
    private IDisposable? _throughputConfigDisposable;
    private IDisposable? _telemetryStateDisposable;

    internal void StartWatchers(Action<object> action)
    {
      _autocompleteConfigDisposable = watchAutocompleteConfig(action);
      _indexingConfigDisposable = watchIndexingConfig(action);
      _chatConfigDisposable = watchChatConfig(action);
      _throughputConfigDisposable = watchThroughputConfig(action);
      _telemetryStateDisposable = watchTelemetryState(action);
    }

    private IDisposable watchAutocompleteConfig(Action<object> action)
    {
      EventHandler<ConfigurationChangedEventArgs> handler = (sender, e) =>
      {
        if (e.AffectsConfiguration("kilo-code.new.autocomplete"))
        {
          var msg = BuildAutocompleteSettingsMessage();
          action(msg);
        }
      };
      VSExtensionSettings.ConfigurationChanged += handler;
      return new DelegateDisposable(
        () =>
        {
          VSExtensionSettings.ConfigurationChanged -=
              handler;
        });
    }

    private IDisposable watchIndexingConfig(Action<object> action)
    {
      EventHandler<ConfigurationChangedEventArgs> handler = (sender, e) =>
      {
        if (e.AffectsConfiguration("kilo-code.new.indexing"))
        {
          var msg = BuildIndexingSettingsMessage();
          action(msg);
        }
      };
      VSExtensionSettings.ConfigurationChanged += handler;
      return new DelegateDisposable(
        () =>
        {
          VSExtensionSettings.ConfigurationChanged -=
              handler;
        });
    }

    private IDisposable watchChatConfig(Action<object> action)
    {
      EventHandler<ConfigurationChangedEventArgs> handler = (sender, e) =>
      {
        if (e.AffectsConfiguration("kilo-code.new.chat"))
        {
          var msg = BuildChatSettingsMessage();
          action(msg);
        }
      };
      VSExtensionSettings.ConfigurationChanged += handler;
      return new DelegateDisposable(
        () =>
        {
          VSExtensionSettings.ConfigurationChanged -=
              handler;
        });
    }

    private IDisposable watchThroughputConfig(Action<object> action)
    {
      EventHandler<ConfigurationChangedEventArgs> handler = (sender, e) =>
      {
        if (e.AffectsConfiguration("kilo-code.new.showTokenThroughput"))
        {
          var msg = BuildThroughputSettingMessage();
          action(msg);
        }
      };
      VSExtensionSettings.ConfigurationChanged += handler;
      return new DelegateDisposable(
        () =>
        {
          VSExtensionSettings.ConfigurationChanged -=
              handler;
        });
    }

    private IDisposable watchTelemetryState(Action<object> action)
    {
      return null;
    //  return vscode.env.onDidChangeTelemetryEnabled((enabled) => {
    //      post({ type: "telemetryState", enabled })
    //  })
    }
  }
}

