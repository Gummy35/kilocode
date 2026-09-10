using KiloExtensionDTOs.ExtensionMessages;
using KiloExtensionDTOs.KiloConfig;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.Utils;
using Microsoft.Build.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services.Handlers.Config
{
  /// <summary>
  /// Handles configuration-related operations like requestConfig, updateSetting, updateConfig.
  /// This matches the VS Code pattern where config handling is extracted into
  /// separate handler modules.
  /// </summary>
  public class ConfigHandlerService : ServiceProviderServiceBase
  {
    private bool _disposed;
    private int _updateConfigPending = 0;



    private VSProvider Provider => _serviceProvider.GetService<VSProvider>()
            ?? throw new InvalidOperationException("VSProvider not registered in service provider");


    public ConfigHandlerService(ServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    ///// <summary>
    ///// Handles the requestConfig message from the webview.
    ///// Fetches and sends the current configuration to the webview.
    ///// 
    ///// VS Code workflow: Matches the pattern in kilo-provider/handlers/config.ts
    ///// where requestConfig fetches /config and sends configLoaded to webview.
    ///// 
    ///// Workflow steps:
    ///// 1. Get HTTP client from provider
    ///// 2. If no client, send empty config with SendConfigLoadedAsync
    ///// 3. Fetch configuration from /config endpoint
    ///// 4. Extract config and features properties from response
    ///// 5. Send configLoaded message with both config and features
    ///// 
    ///// Messages sent to webview:
    ///// - configLoaded: { config: {...}, features: {...} }
    ///// </summary>
    ///// <param name="payload">The message payload (unused for requestConfig).</param>
    ///// <returns>A task representing the asynchronous operation.</returns>
    //public async Task HandleRequestConfigAsync(JsonElement? payload)
    //{
    //  try
    //  {
    //    var nswagClient = Provider.GetNswagClient();
    //    if (nswagClient == null)
    //    {
    //      await Provider.SendConfigLoadedAsync(new KiloExtensionDTOs.KiloConfig.Config(), new KiloExtensionDTOs.KiloConfig.FeatureFlags());
    //      return;
    //    }

    //    var configResponse = await nswagClient.Global_config_getAsync();
    //    //var config = JsonDocument.Parse("{}").RootElement;
    //    //var features = JsonDocument.Parse("{}").RootElement;

    //    //if (configResponse != null)
    //    //{
    //    //    config = JsonSerializer.SerializeToElement(configResponse);
    //    //    // Features is not a property of Config in the generated model
    //    //    features = JsonDocument.Parse("{}").RootElement;
    //    //}

    //    await Provider.SendConfigLoadedAsync(EntityConverter.Convert(configResponse), new KiloExtensionDTOs.KiloConfig.FeatureFlags());
    //  }
    //  catch (Exception ex)
    //  {
    //    System.Diagnostics.Debug.WriteLine($"[Kilo] ConfigHandler: Error fetching config: {ex.Message}");
    //    await Provider.SendConfigLoadedAsync(new KiloExtensionDTOs.KiloConfig.Config(), new KiloExtensionDTOs.KiloConfig.FeatureFlags());
    //  }
    //}

    /// <summary>
    /// Handles the updateSetting message from the webview.
    /// Updates a specific setting in the configuration via the backend.
    /// 
    /// VS Code workflow: Matches the pattern in kilo-provider/handlers/config.ts
    /// where updateSetting posts to /config with the setting update.
    /// 
    /// Workflow steps:
    /// 1. Validate payload is not null
    /// 2. Get HTTP client from provider
    /// 3. Verify client is connected
    /// 4. POST to /config endpoint with payload
    /// 5. Backend applies the setting update
    /// 
    /// Messages sent to webview:
    /// - error: { message: "Missing payload" | "Not connected to backend" }
    /// </summary>
    /// <param name="payload">The message payload containing setting name and value.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HandleUpdateSettingAsync(JsonElement? payload)
    {
      if (payload == null)
      {
        await Provider.SendErrorAsync("Missing payload", "Payload is required for updateSetting");
        return;
      }

      try
      {
        var nswagClient = Provider.GetNswagClient();
        if (nswagClient == null)
        {
          await Provider.SendErrorAsync("Not connected", "Not connected to backend");
          return;
        }

        var config = JsonSerializer.Deserialize<KiloVisualStudioExtension.ApiClient.Config>(payload.Value.GetRawText());
        if (config != null)
        {
          await nswagClient.Global_config_updateAsync(config);
        }
      }
      catch (Exception ex)
      {
        await Provider.SendErrorAsync("Update setting error", ex.Message);
      }
    }

    /// <summary>
    /// Handles the updateConfig message from the webview.
    /// Updates the entire configuration via the backend.
    /// 
    /// VS Code workflow: Matches the pattern in kilo-provider/handlers/config.ts
    /// where updateConfig posts the full configuration to /config.
    /// 
    /// Workflow steps:
    /// 1. Validate payload is not null
    /// 2. Get HTTP client from provider
    /// 3. Verify client is connected
    /// 4. POST to /config endpoint with full configuration payload
    /// 5. Backend replaces the entire configuration
    /// 
    /// Messages sent to webview:
    /// - error: { message: "Missing payload" | "Not connected to backend" }
    /// </summary>
    /// <param name="payload">The message payload containing the new configuration.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HandleUpdateConfigAsync(JsonElement? payload)
    {
      if (payload == null)
      {
        await Provider.SendErrorAsync("Missing payload", "Payload is required for updateConfig");
        return;
      }

      try
      {
        var nswagClient = Provider.GetNswagClient();
        if (nswagClient == null)
        {
          await Provider.SendErrorAsync("Not connected", "Not connected to backend");
          return;
        }

        var config = JsonSerializer.Deserialize<KiloVisualStudioExtension.ApiClient.Config>(payload.Value.GetRawText());
        if (config != null)
        {
          await nswagClient.Global_config_updateAsync(config);
        }
      }
      catch (Exception ex)
      {
        await Provider.SendErrorAsync("Update config error", ex.Message);
      }
    }

    /// <summary>
    /// Handles the openConfigFile message from the webview.
    /// Opens the configuration file in the system editor.
    /// 
    /// VS Code workflow: Matches the pattern in kilo-provider/handlers/config.ts
    /// where openConfigFile fetches the config file path and opens it externally.
    /// 
    /// Workflow steps:
    /// 1. Extract scope from payload (default: "global")
    /// 2. Get HTTP client from provider
    /// 3. Fetch config file path from /config/file?scope={scope}
    /// 4. Extract path property from response
    /// 5. Launch system process to open the file
    /// 
    /// Messages sent to webview:
    /// - No direct message; file opens in external editor
    /// </summary>
    /// <param name="payload">The message payload containing scope (global or workspace).</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HandleOpenConfigFileAsync(JsonElement? payload)
    {
      if (payload == null) return;

      System.Diagnostics.Debug.WriteLine("[Kilo] ConfigHandler: openConfigFile");

      string scope = "global";
      if (payload.Value.TryGetProperty("scope", out var scopeProp))
      {
        scope = scopeProp.GetString() ?? "global";
      }

      try
      {
        var nswagClient = Provider.GetNswagClient();
        if (nswagClient == null) return;

        // Config model doesn't have a Path property - use default config path
        string filePath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "kilo", "config.json");

        if (!string.IsNullOrEmpty(filePath))
        {
          System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
          {
            FileName = filePath,
            UseShellExecute = true
          });
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] ConfigHandler: error opening config file: {ex.Message}");
      }
    }

    public static string CommitMessageLanguageSetting()
    {
      return VSExtensionSettings.Get<string>("kilo-code.new.languageCommitMessage", "sync");
    }

    public static FeatureFlags GetConfigFeatures(ApiClient.Config? config)
    {
      return new FeatureFlags
      {
        Indexing = Utils.IndexingPluginDetector.HasIndexingPlugin(config?.Plugin),
        SandboxControls = false //process.platform !== "win32"
      };
    }

    /// <summary>
    /// Fetch the latest merged config and push it as configUpdated.
    /// Called when global.config.updated SSE fires (config changed without a full dispose).
    /// </summary>
    internal async Task FetchAndSendConfigUpdatedAsync()
    {
      var _connectionService = _serviceProvider.GetService<KiloConnectionService>();
      var _cacheService = _serviceProvider.GetService<ICacheService>();
      var client = _connectionService.GetNswagClient();
      var connectionState = _connectionService.State;

      if (client == null || connectionState != ConnectionState.Connected) return;

      try
      {
        var workspaceDir = _serviceProvider.GetService<ProjectDirectoryProvider>().GetWorkspaceDirectory();

        // Fetch all three configs in parallel
        var configTask = Retry.RetryAsync(() => client.Config_getAsync(workspaceDir, ""));
        var globalTask = client.Global_config_getAsync();
        var overlayTask = client.Config_overlayAsync(workspaceDir, "", Scope2.Project);

        await Task.WhenAll(configTask, globalTask, overlayTask);
        var config = EntityConverter.Convert(await configTask);
        var global = EntityConverter.Convert(await globalTask);
        var overlay = EntityConverter.Convert((await overlayTask)?.Project);


        //    this.cachedGlobalConfig = global ?? null
        _cacheService.UpdateAsync("globalConfig", global);

        var settings = new ExtensionSettingsOverride
        {
          MaxCost = ServiceProvider.GetService<CostService>().MaxCostSetting(),
          AdditionalProperties = {
              { "languageCommitMessage", CommitMessageLanguageSetting() }
            }
        };

        var features = GetConfigFeatures(configTask.Result);

        var configLoadedMessage = new ConfigLoadedMessage
        {
          Config = config,
          GlobalConfig = global,
          ProjectConfig = overlay,
          Settings = settings,
          Features = features
        };
        _cacheService.UpdateAsync("configLoadedMessage", configLoadedMessage);

        Provider.PostMessage(new ConfigUpdatedMessage
        {
          Config = config,
          GlobalConfig = global,
          ProjectConfig = overlay,
          Settings = settings,
          Features = features
        });
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: Failed to fetch config: {ex.Message}");
      }
    }


    /// <summary>
    /// Fetches configuration from the backend and sends it to the webview.
    /// Matches the VS Code pattern in KiloProvider.ts:fetchAndSendConfig.
    /// 
    /// Workflow:
    /// 1. If not connected and cached config exists, send cached config and return
    /// 2. If handleUpdateConfig is in flight (pending > 0), return to avoid race
    /// 3. Fetch config, global config, and overlay in parallel
    /// 4. Build configLoaded message with settings and features
    /// 5. Cache the message and send to webview
    /// 6. Handle errors gracefully with debug logging
    /// </summary>
    internal async Task FetchAndSendConfigAsync()
    {
      var _connectionService = _serviceProvider.GetService<KiloConnectionService>();
      var _cacheService = _serviceProvider.GetService<ICacheService>();
      var client = _connectionService.GetNswagClient();
      var connectionState = _connectionService.State;

      // If not connected and cached config exists, send cached config
      if (client == null || connectionState != ConnectionState.Connected)
      {
        if (_cacheService.Contains("configMessage"))
          Provider.PostMessage(_cacheService.Get<ConfigLoadedMessage>("configMessage"));
        return;
      }

      // Skip if handleUpdateConfig is in flight — sending a configLoaded now
      // would race with the write and potentially overwrite optimistic webview state.
      if (_updateConfigPending > 0)
      {
        return;
      }

      try
      {
        //  try {
        //    const workspaceDir = this.getWorkspaceDirectory()
        var workspaceDir = _serviceProvider.GetService<ProjectDirectoryProvider>().GetWorkspaceDirectory();

        //    const [{ data: config }, { data: global }, { data: overlay }] = await Promise.all([
        //      retry(() => this.client!.config.get({ directory: workspaceDir }, { throwOnError: true })),
        //      this.client.global.config.get({ throwOnError: true }),
        //      this.client.config.overlay({ directory: workspaceDir, scope: "project" }, { throwOnError: true }),
        //    ])

        // Fetch all three configs in parallel
        var configTask = Retry.RetryAsync(() => client.Config_getAsync(workspaceDir, ""));
        var globalTask = client.Global_config_getAsync();
        var overlayTask = client.Config_overlayAsync(workspaceDir, "", Scope2.Project);

        await Task.WhenAll(configTask, globalTask, overlayTask);
        var config = EntityConverter.Convert(configTask.Result);
        var global = EntityConverter.Convert(globalTask.Result);
        var overlay = EntityConverter.Convert(overlayTask.Result?.Project);


        //    this.cachedGlobalConfig = global ?? null

        _cacheService.UpdateAsync("globalConfig", global);
        
        var message = new ConfigLoadedMessage
        {
          Config = config,
          GlobalConfig = global,
          ProjectConfig = overlay,
          Settings = new ExtensionSettingsOverride
          {
            MaxCost = ServiceProvider.GetService<CostService>().MaxCostSetting(),
            AdditionalProperties = {
              { "languageCommitMessage", CommitMessageLanguageSetting() }
            }
          },
          Features = GetConfigFeatures(configTask.Result)
        };
        _cacheService.UpdateAsync("configLoadedMessage", message);
        Provider.PostMessage(message);
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: Failed to fetch config: {ex.Message}");
      }
    }


    // TypeScript: case "requestGlobalConfig":
    //   this.fetchAndSendGlobalConfig().catch((e) => console.error("[Kilo New] fetchAndSendGlobalConfig failed:", e))
    //   break
    // private async fetchAndSendGlobalConfig(): Promise<void> {
    //   if (!this.client || this.connectionState !== "connected") return
    //   try {
    //     const { data: config } = await this.client.global.config.get({ throwOnError: true })
    //     this.cachedGlobalConfig = config ?? null
    //     this.postMessage({ type: "globalConfigLoaded", config })
    //   } catch (error) {
    //     console.error("[Kilo New] KiloProvider: Failed to fetch global config:", error)
    //   }
    // }
    public async Task HandleRequestGlobalConfigAsync(JsonElement? payload)
    {
      var nswagClient = Provider.GetNswagClient();
      if (nswagClient == null || Provider.GetConnectionState() != "connected")
      {
        return;
      }

      try
      {
        var config = await nswagClient.Global_config_getAsync();
        if (config == null) config = new ApiClient.Config();
        await  _serviceProvider.GetService<ICacheService>().UpdateAsync("globalConfig", config);
        var message = new GlobalConfigLoadedMessage
        {
          Config = EntityConverter.Convert(config)
        };
        Provider.PostMessage(message);
      }
      catch (Exception error)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo New] KiloProvider: Failed to fetch global config: {error}");
      }
    }



    public void Dispose()
    {
      if (_disposed) return;
      _disposed = true;
    }
  }
}

