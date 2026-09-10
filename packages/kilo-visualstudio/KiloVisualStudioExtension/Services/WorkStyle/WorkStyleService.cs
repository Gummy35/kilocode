using KiloExtensionDTOs;
using KiloExtensionDTOs.ExtensionMessages;
using KiloExtensionDTOs.WebviewMessages;
using KiloVisualStudioExtension.ApiClient;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Config = KiloVisualStudioExtension.ApiClient.Config;

namespace KiloVisualStudioExtension.Services.WorkStyle
{
  /// <summary>
  /// Service responsible for managing work style configuration and applying presets.
  /// Handles initialization, updates, and rollback on failure.
  /// </summary>
  public class WorkStyleService : ServiceProviderServiceBase
  {
    // ========================================================================
    // Constants
    // ========================================================================

    /// <summary>
    /// The configuration key for the work style setting.
    /// </summary>
    public const string AGENT_WORK_STYLE_KEY = "agentWorkStyle";

    /// <summary>
    /// The configuration key for showing the task timeline.
    /// </summary>
    public const string SHOW_TASK_TIMELINE_KEY = "showTaskTimeline";

    /// <summary>
    /// Array of work style setting keys that trigger configuration change events.
    /// </summary>
    public readonly string[] WORK_STYLE_SETTING_KEYS = { SHOW_TASK_TIMELINE_KEY };

    // ========================================================================
    // Permission Level Definitions for Bash Commands
    // ========================================================================

    /// <summary>
    /// Default permission levels for common bash commands.
    /// Safe commands are allowed, potentially dangerous ones require approval.
    /// </summary>
    private static readonly Dictionary<string, PermissionLevelEnum> BASH =
        new()
        {
          ["*"] = PermissionLevelEnum.Ask,
          ["cat *"] = PermissionLevelEnum.Allow,
          ["head *"] = PermissionLevelEnum.Allow,
          ["tail *"] = PermissionLevelEnum.Allow,
          ["less *"] = PermissionLevelEnum.Allow,
          ["ls *"] = PermissionLevelEnum.Allow,
          ["tree *"] = PermissionLevelEnum.Allow,
          ["pwd *"] = PermissionLevelEnum.Allow,
          ["echo *"] = PermissionLevelEnum.Allow,
          ["wc *"] = PermissionLevelEnum.Allow,
          ["which *"] = PermissionLevelEnum.Allow,
          ["type *"] = PermissionLevelEnum.Allow,
          ["file *"] = PermissionLevelEnum.Allow,
          ["diff *"] = PermissionLevelEnum.Allow,
          ["du *"] = PermissionLevelEnum.Allow,
          ["df *"] = PermissionLevelEnum.Allow,
          ["date *"] = PermissionLevelEnum.Allow,
          ["uname *"] = PermissionLevelEnum.Allow,
          ["whoami *"] = PermissionLevelEnum.Allow,
          ["printenv *"] = PermissionLevelEnum.Allow,
          ["man *"] = PermissionLevelEnum.Allow,
          ["grep *"] = PermissionLevelEnum.Allow,
          ["rg *"] = PermissionLevelEnum.Allow,
          ["ag *"] = PermissionLevelEnum.Allow,
          ["uniq *"] = PermissionLevelEnum.Allow,
          ["cut *"] = PermissionLevelEnum.Allow,
          ["tr *"] = PermissionLevelEnum.Allow,
          ["jq *"] = PermissionLevelEnum.Allow,
          [">*"] = PermissionLevelEnum.Ask
        };

    // ========================================================================
    // Work Style Presets
    // ========================================================================

    /// <summary>
    /// Predefined work style configurations with their associated permissions and settings.
    /// </summary>
    public static readonly Dictionary<WorkStyleEnum, WorkStylePreset>
        WORK_STYLE_PRESETS =
        new()
        {
          [WorkStyleEnum.HumanInTheLoop] =
                new WorkStylePreset
                {
                  Style = WorkStyleEnum.HumanInTheLoop,
                  Config = new WorkStyleConfig
                  {
                    Terminal_command_display = ExpandedCollapsedEnum.Expanded,
                    Auto_collapse_reasoning = false,
                    Permission = new Dictionary<string, object?>
                    {
                      ["*"] = PermissionLevelEnum.Ask,
                      ["read"] = new Dictionary<string, PermissionLevelEnum?>
                      {
                        ["*"] = PermissionLevelEnum.Allow,
                        ["*.env"] = PermissionLevelEnum.Ask,
                        ["*.env.*"] = PermissionLevelEnum.Ask,
                        ["*.env.example"] = PermissionLevelEnum.Allow
                      },
                      ["grep"] = PermissionLevelEnum.Allow,
                      ["glob"] = PermissionLevelEnum.Allow,
                      ["list"] = PermissionLevelEnum.Allow,
                      ["question"] = PermissionLevelEnum.Allow,
                      ["webfetch"] = PermissionLevelEnum.Allow,
                      ["websearch"] = PermissionLevelEnum.Allow,
                      ["codesearch"] = PermissionLevelEnum.Allow,
                      ["external_directory"] = PermissionLevelEnum.Ask,
                      ["edit"] = PermissionLevelEnum.Ask,
                      ["bash"] = BASH,
                      ["doom_loop"] = PermissionLevelEnum.Ask
                    }
                  },
                  Settings = new WorkStyleSettings
                  {
                    ShowTaskTimeline = true
                  }
                },

          [WorkStyleEnum.Autonomous] =
                new WorkStylePreset
                {
                  Style = WorkStyleEnum.Autonomous,
                  Config = new WorkStyleConfig
                  {
                    Terminal_command_display = ExpandedCollapsedEnum.Collapsed,
                    Auto_collapse_reasoning = true
                  },
                  Settings = new WorkStyleSettings
                  {
                    ShowTaskTimeline = false
                  }
                }
        };

    // ========================================================================
    // Private Fields
    // ========================================================================

    private bool _disposed;

    /// <summary>
    /// Gets the VSProvider instance from the service provider.
    /// </summary>
    private VSProvider Provider => _serviceProvider.GetService<VSProvider>()
        ?? throw new InvalidOperationException("VSProvider not registered in service provider");

    // ========================================================================
    // Constructor
    // ========================================================================

    /// <summary>
    /// Creates a new WorkStyleService instance.
    /// </summary>
    /// <param name="serviceProvider">The service provider for dependency injection.</param>
    public WorkStyleService(ServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    // ========================================================================
    // Configuration Accessors
    // ========================================================================

    /// <summary>
    /// Gets the domain configuration for kilo-code.new settings.
    /// </summary>
    /// <returns>The domain configuration instance.</returns>
    public DomainConfig GetConfig()
    {
      return VSExtensionSettings.GetConfiguration("kilo-code.new");
    }

    /// <summary>
    /// Checks if the work style has been configured at the global scope.
    /// </summary>
    /// <returns>True if configured, false otherwise.</returns>
    public bool IsWorkStyleConfigured()
    {
      return GetConfig().IsConfigured(AGENT_WORK_STYLE_KEY, SettingScope.Global);
    }

    /// <summary>
    /// Gets the array of keys to watch for configuration changes.
    /// </summary>
    /// <returns>Array of configuration keys.</returns>
    public string[] GetWatchKeys()
    {
      return new[] { AGENT_WORK_STYLE_KEY }.Concat(WORK_STYLE_SETTING_KEYS).ToArray();
    }

    /// <summary>
    /// Checks if a given key is a work style related setting.
    /// </summary>
    /// <param name="key">The configuration key to check.</param>
    /// <returns>True if the key is a work style setting, false otherwise.</returns>
    public bool IsWorkStyleSetting(string key)
    {
      return WORK_STYLE_SETTING_KEYS.Contains(key) || key == AGENT_WORK_STYLE_KEY;
    }

    // ========================================================================
    // Configuration Change Watching
    // ========================================================================

    /// <summary>
    /// Sets up a watcher for work style configuration changes.
    /// When a relevant setting changes, posts the current work style payload.
    /// </summary>
    /// <param name="post">Action to post messages to the webview.</param>
    /// <returns>A handler that can be unsubscribed from the configuration changed event.</returns>
    public EventHandler<ConfigurationChangedEventArgs> WatchWorkStyleConfig(Action<IWebviewMessage?> post)
    {
      var handler = new EventHandler<ConfigurationChangedEventArgs>((_, e) =>
      {
        var keys = new[] { AGENT_WORK_STYLE_KEY }.Concat(WORK_STYLE_SETTING_KEYS);
        if (keys.Any(key => e.AffectsConfiguration(key)))
        {
          post(GetWorkStylePayload());
        }
      });

      VSExtensionSettings.ConfigurationChanged += handler;
      return handler;
    }

    // ========================================================================
    // Message Handling
    // ========================================================================

    /// <summary>
    /// Gets the current work style payload for sending to the webview.
    /// </summary>
    /// <returns>A WorkStyleLoadedMessage with the current style.</returns>
    public WorkStyleLoadedMessage GetWorkStylePayload()
    {
      var style = GetConfig().Get<string>(AGENT_WORK_STYLE_KEY, "unset");
      return new WorkStyleLoadedMessage { Style = style.ToEnum(WorkStyleStateEnum.Unset) };
    }

    /// <summary>
    /// Routes and handles work style related webview messages.
    /// </summary>
    /// <param name="message">The incoming webview message.</param>
    /// <param name="connection">The Kilo connection service.</param>
    /// <param name="directory">The working directory.</param>
    /// <param name="post">Action to post responses back to the webview.</param>
    /// <returns>True if the message was handled, false otherwise.</returns>
    public async Task<bool> HandleMessageAsync(
        IWebviewMessage message,
        KiloConnectionService connection,
        string directory,
        Action<IWebviewMessage?> post)
    {
      // Handle requestWorkStyle message
      if (message is RequestWorkStyleMessage)
      {
        bool initialized;

        try
        {
          await InitializeWorkStyleAsync(connection, directory);
          initialized = true;
        }
        catch (Exception ex)
        {
          Debug.WriteLine($"[Kilo New] Failed to initialize work style: {ex}");
          initialized = false;
        }

        var payload = GetWorkStylePayload();

        if (!initialized)
        {
          payload.Style = WorkStyleStateEnum.Skipped;
        }

        post(payload);
        return true;
      }

      // Handle applyWorkStyle message
      if (message is ApplyWorkStyleMessage applyWorkStyle)
      {
        return await HandleApplyWorkStyleMessageAsync(
            applyWorkStyle,
            connection,
            directory,
            post);
      }

      // Handle setWorkStyle message
      if (message is not SetWorkStyleMessage setWorkStyle)
      {
        return false;
      }

      if (setWorkStyle.Style == null)
      {
        Debug.WriteLine("[Kilo New] Missing style in setWorkStyle message");
        return true;
      }

      await SetWorkStyleAsync(setWorkStyle.Style);
      post(GetWorkStylePayload());
      return true;
    }

    // ========================================================================
    // Initialization
    // ========================================================================

    /// <summary>
    /// Initializes the work style on first use if not already configured.
    /// Skips initialization if sessions already exist.
    /// </summary>
    /// <param name="connection">The Kilo connection service.</param>
    /// <param name="directory">The working directory.</param>
    private async Task InitializeWorkStyleAsync(KiloConnectionService connection, string directory)
    {
      // Return if already configured
      if (IsWorkStyleConfigured())
        return;

      var client = connection.GetNswagClient();
      if (client == null)
        return;

      // Check if any sessions exist
      var hasSessions = await HasAnySessionAsync(client, directory);

      // Double-check after async operation
      if (IsWorkStyleConfigured())
        return;

      // Set initial style based on session existence
      var initialStyle = GetInitialWorkStyle(hasSessions);
      await SetWorkStyleAsync(initialStyle);
    }

    /// <summary>
    /// Checks if any sessions exist in the given directory.
    /// </summary>
    /// <param name="client">The NSwag API client.</param>
    /// <param name="directory">The directory to check.</param>
    /// <returns>True if sessions exist, false otherwise.</returns>
    private async Task<bool> HasAnySessionAsync(KiloApiClient client, string directory)
    {
      try
      {
        var result = await client.Experimental_session_listAsync(
            directory: directory,
            workspace: directory,
            projectID: null,
            worktrees: null,
            current: null,
            roots: true,
            start: null,
            cursor: null,
            search: null,
            limit: 1,
            archived: true);
        return result != null && result.Count > 0;
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Determines the initial work style based on whether sessions exist.
    /// </summary>
    /// <param name="hasSessions">True if sessions exist, false otherwise.</param>
    /// <returns>"skipped" if sessions exist, "unset" otherwise.</returns>
    private WorkStyleStateEnum GetInitialWorkStyle(bool hasSessions)
    {
      return hasSessions ? WorkStyleStateEnum.Skipped : WorkStyleStateEnum.Unset;
    }

    /// <summary>
    /// Sets the work style configuration globally.
    /// </summary>
    /// <param name="style">The work style to set.</param>
    private async Task SetWorkStyleAsync(WorkStyleStateEnum style)
    {
      var config = VSExtensionSettings.GetConfiguration("agent");
      await config.UpdateAsync(AGENT_WORK_STYLE_KEY, style, SettingScope.Global);
    }

    // ========================================================================
    // Apply Work Style
    // ========================================================================

    /// <summary>
    /// Handles the applyWorkStyle message by applying the selected preset.
    /// </summary>
    /// <param name="message">The apply work style message.</param>
    /// <param name="connection">The Kilo connection service.</param>
    /// <param name="directory">The working directory.</param>
    /// <param name="post">Action to post responses.</param>
    /// <returns>True always (message is handled).</returns>
    private async Task<bool> HandleApplyWorkStyleMessageAsync(
        ApplyWorkStyleMessage message,
        KiloConnectionService connection,
        string directory,
        Action<IWebviewMessage?> post)
    {
      // Validate style
      if (message.Style != WorkStyleEnum.HumanInTheLoop && message.Style != WorkStyleEnum.Autonomous)
      {
        post(new WorkStyleApplyFailedMessage
        {
          Message = "Invalid work style",
          RollbackFailed = false
        });
        return true;
      }

      // Apply the work style
      var result = await ApplyWorkStyleAsync(message.Style, connection, directory);

      if (result.Ok)
      {
        post(new WorkStyleAppliedMessage { Style = message.Style });
      }
      else
      {
        post(new WorkStyleApplyFailedMessage
        {
          Message = result.Error,
          RollbackFailed = result.Rollback.Length > 0
        });
      }

      return true;
    }

    /// <summary>
    /// Applies a work style preset with rollback support on failure.
    /// </summary>
    /// <param name="style">The work style to apply.</param>
    /// <param name="connection">The Kilo connection service.</param>
    /// <param name="directory">The working directory.</param>
    /// <returns>A tuple containing success status, error message, and rollback keys.</returns>
    private async Task<(bool Ok, string Error, string[] Rollback)> ApplyWorkStyleAsync(
        WorkStyleEnum style,
        KiloConnectionService? connection,
        string directory)
    {
      var settings = GetConfig();

      // Read current config from API
      Func<Task<WorkStyleConfig>> ReadAsync = async () =>
      {
        var client = connection.GetNswagClient();
        var data = await client.Config_getAsync(directory, string.Empty);

        return data == null
                  ? new WorkStyleConfig()
                  : new WorkStyleConfig
              {
                Auto_collapse_reasoning = data.Auto_collapse_reasoning,
                Permission = data.Permission,
                Terminal_command_display = data.Terminal_command_display ==
                          ConfigTerminal_command_display.Expanded
                              ? ExpandedCollapsedEnum.Expanded
                              : ExpandedCollapsedEnum.Collapsed
              };
      };

      // Inspect setting scope
      Func<string, Task<SettingsSnapshot>> InspectAsync = async (string key) =>
      {
        var info = settings.GetAtScope<object>(key, SettingScope.Global);
        return new SettingsSnapshot
        {
          Global = info,
          Customized = info != null
                      || settings.GetAtScope<object>(key, SettingScope.Workspace) != null
        };
      };

      // Write setting
      Func<string, object?, Task> WriteAsync = async (string key, object? value) =>
      {
        await settings.UpdateAsync(key, value, SettingScope.Global);
      };

      // Patch API config
      Func<WorkStyleConfig, Task> PatchAsync = async (WorkStyleConfig config) =>
      {
        var client = connection.GetNswagClient();
        await client.Global_config_updateAsync(new Config
        {
          Auto_collapse_reasoning = config.Auto_collapse_reasoning == true,
          Permission = new PermissionConfig
          {
            AdditionalProperties = (IDictionary<string, object>)config.Permission
          },
          Terminal_command_display = config.Terminal_command_display ==
                      ExpandedCollapsedEnum.Expanded
                          ? ConfigTerminal_command_display.Expanded
                          : ConfigTerminal_command_display.Collapsed
        });
      };

      var store = new Store
      {
        InspectAsync = InspectAsync,
        PatchAsync = PatchAsync,
        ReadAsync = ReadAsync,
        WriteAsync = WriteAsync
      };

      return await ApplyWorkStyleAsync(style, store);
    }

    /// <summary>
    /// Core apply logic with rollback support.
    /// </summary>
    /// <param name="style">The work style to apply.</param>
    /// <param name="store">The store interface for reading/writing settings.</param>
    /// <returns>A tuple containing success status, error message, and rollback keys.</returns>
    private async Task<(bool Ok, string Error, string[] Rollback)> ApplyWorkStyleAsync(
        WorkStyleEnum style,
        Store store)
    {
      var completed = new List<(string Key, object? Value)>();

      try
      {
        var config = await store.ReadAsync();
        var plan = BuildWorkStyleApplyPlan(
            style,
            config,
            (key) => !(store.InspectAsync(key).Result.Customized));

        var writes = new List<(string Key, object? Value)>();
        foreach (var kvp in (Dictionary<string, object?>)plan.Settings)
        {
          writes.Add((kvp.Key, kvp.Value));
        }
        writes.Add((AGENT_WORK_STYLE_KEY, style));

        // Save current values and apply new ones
        foreach (var write in writes)
        {
          var currentValue = GetGlobalValue(write.Key);
          completed.Add((write.Key, currentValue));
          await store.WriteAsync(write.Key, write.Value);
        }

        // Apply config patch if needed
        if (plan.Config != null)
        {
          await store.PatchAsync(config);
        }

        return (true, "", Array.Empty<string>());
      }
      catch (Exception err)
      {
        // Rollback on failure
        var rollback = new List<string>();
        for (var i = completed.Count - 1; i >= 0; i--)
        {
          var write = completed[i];
          try
          {
            await store.WriteAsync(write.Key, write.Value);
          }
          catch
          {
            rollback.Add(write.Key);
          }
        }
        return (false, err.Message, rollback.ToArray());
      }
    }

    /// <summary>
    /// Gets the global value for a setting key.
    /// </summary>
    /// <param name="key">The setting key.</param>
    /// <returns>The current global value or null.</returns>
    private object? GetGlobalValue(string key)
    {
      var config = GetConfig();
      return key == AGENT_WORK_STYLE_KEY
          ? config.GetAtScope<string>(AGENT_WORK_STYLE_KEY, SettingScope.Global)
          : config.GetAtScope<bool>(SHOW_TASK_TIMELINE_KEY, SettingScope.Global);
    }

    // ========================================================================
    // Build Apply Plan
    // ========================================================================

    /// <summary>
    /// Builds an apply plan based on the selected style and current config.
    /// Only includes settings that should be changed.
    /// </summary>
    /// <param name="style">The work style to apply.</param>
    /// <param name="config">The current work style config.</param>
    /// <param name="settingDefault">Function to check if a setting uses its default value.</param>
    /// <returns>A WorkStyleApplyPlan with config and settings to apply.</returns>
    public static WorkStyleApplyPlan BuildWorkStyleApplyPlan(
        WorkStyleEnum style,
        WorkStyleConfig config,
        Func<string, bool>? settingDefault = null)
    {
      var preset = GetWorkStylePreset(style);
      var next = new WorkStyleConfig();

      // Add permission config if preset has it and current doesn't
      if (preset.Config.Permission is not null && !HasPermissionConfig(config))
      {
        next.Permission = StripPermission(
            (Dictionary<string, object?>)preset.Config.Permission);
      }

      // Add terminal_command_display if not set
      if (config.Terminal_command_display is null)
      {
        next.Terminal_command_display = preset.Config.Terminal_command_display;
      }

      // Add auto_collapse_reasoning if not set
      if (config.Auto_collapse_reasoning is null)
      {
        next.Auto_collapse_reasoning = preset.Config.Auto_collapse_reasoning;
      }

      settingDefault ??= _ => true;

      var settings = new Dictionary<string, object?>();

      if (settingDefault("showTaskTimeline"))
      {
        settings["showTaskTimeline"] = preset.Settings.ShowTaskTimeline;
      }

      return new WorkStyleApplyPlan
      {
        Config = next,
        Settings = settings
      };
    }

    /// <summary>
    /// Gets the preset configuration for a given work style.
    /// </summary>
    /// <param name="style">The work style.</param>
    /// <returns>The work style preset.</returns>
    public static WorkStylePreset GetWorkStylePreset(WorkStyleEnum style)
    {
      return WORK_STYLE_PRESETS[style];
    }

    /// <summary>
    /// Checks if a work style config has permission settings.
    /// </summary>
    /// <param name="config">The work style config.</param>
    /// <returns>True if permission is configured, false otherwise.</returns>
    public static bool HasPermissionConfig(WorkStyleConfig config)
    {
      return config.Permission is not null &&
             config.Permission is ICollection coll &&
             coll.Count > 0;
    }

    /// <summary>
    /// Strips null and empty permission rules from a permission config.
    /// </summary>
    /// <param name="config">The permission config to strip.</param>
    /// <returns>A cleaned permission config.</returns>
    private static Dictionary<string, object?> StripPermission(
        Dictionary<string, object?> config)
    {
      var result = new Dictionary<string, object?>();

      foreach (var entry in config)
      {
        var key = entry.Key;
        var rule = entry.Value;

        if (rule is null)
        {
          continue;
        }

        if (rule is PermissionLevelEnum permissionLevel)
        {
          result[key] = permissionLevel;
          continue;
        }

        if (rule is Dictionary<string, PermissionLevelEnum?> nestedRules)
        {
          var next = new Dictionary<string, PermissionLevelEnum?>();

          foreach (var nestedEntry in nestedRules)
          {
            if (nestedEntry.Value is not null)
            {
              next[nestedEntry.Key] = nestedEntry.Value;
            }
          }

          if (next.Count > 0)
          {
            result[key] = next;
          }
        }
      }

      return result;
    }
  }
}
