#nullable enable

using KiloVisualStudioExtension.Utils;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension
{
  /// <summary>
  /// Specifies the scope where a setting is stored or retrieved.
  /// </summary>
  /// <remarks>
  /// Settings follow this precedence order:
  /// <list type="number">
  ///   <item><description>Workspace - Solution-specific settings (highest priority)</description></item>
  ///   <item><description>Global - User-wide settings across all solutions</description></item>
  ///   <item><description>Default - Fallback values when not configured</description></item>
  /// </list>
  /// </remarks>
  public enum SettingScope
  {
    /// <summary>
    /// Default/fallback value (not persisted).
    /// </summary>
    Default,

    /// <summary>
    /// Global user settings stored at %APPDATA%\KiloVisualStudioExtension\settings.json.
    /// </summary>
    Global,

    /// <summary>
    /// Workspace-specific settings stored at &lt;solution-root&gt;\.kilo\vssettings.json.
    /// </summary>
    Workspace
  }

  /// <summary>
  /// Event arguments that indicate which configuration keys have changed.
  /// </summary>
  /// <remarks>
  /// This class supports hierarchical matching: a change to <c>agent.workStyle</c>
  /// will match queries for <c>agent</c>, <c>agent.workStyle</c>, or <c>workStyle</c>.
  /// </remarks>
  public sealed class ConfigurationChangedEventArgs : EventArgs
  {
    private readonly HashSet<string> _keys;

    /// <summary>
    /// Initializes a new instance with the specified changed configuration keys.
    /// </summary>
    /// <param name="keys">The collection of configuration keys that changed.</param>
    public ConfigurationChangedEventArgs(
        IEnumerable<string> keys)
    {
      _keys = new HashSet<string>(
          keys,
          StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the collection of configuration keys that changed.
    /// </summary>
    public IReadOnlyCollection<string> Keys => _keys;

    /// <summary>
    /// Determines whether this event affects the specified configuration.
    /// </summary>
    /// <param name="configuration">The configuration to check.</param>
    /// <returns>
    /// <see langword="true"/> if the event affects the configuration;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Matching is case-insensitive and supports hierarchical prefixes.
    /// For example, a change to <c>agent.workStyle</c> matches:
    /// <list type="bullet">
    ///   <item><description><c>agent</c> (parent prefix)</description></item>
    ///   <item><description><c>agent.workStyle</c> (exact match)</description></item>
    ///   <item><description><c>workStyle</c> (child suffix)</description></item>
    /// </list>
    /// </remarks>
    public bool AffectsConfiguration(
        string configuration)
    {
      if (string.IsNullOrWhiteSpace(configuration))
        return false;

      return _keys.Any(key =>
          string.Equals(
              key,
              configuration,
              StringComparison.OrdinalIgnoreCase) ||
          key.StartsWith(
              configuration + ".",
              StringComparison.OrdinalIgnoreCase) ||
          configuration.StartsWith(
              key + ".",
              StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Determines whether the specified key is in the changed set.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <returns>
    /// <see langword="true"/> if the key changed; otherwise, <see langword="false"/>.
    /// </returns>
    public bool Contains(string key)
    {
      return _keys.Contains(key);
    }
  }

  /// <summary>
  /// Provides a scoped view of configuration settings for a specific domain.
  /// </summary>
  /// <remarks>
  /// <para>
  /// <see cref="DomainConfig"/> prefixes all keys with the domain name, allowing
  /// logical grouping of related settings. For example, a domain of <c>"agent"</c>
  /// will automatically prefix keys like <c>"workStyle"</c> to become <c>"agent.workStyle"</c>.
  /// </para>
  /// <para>
  /// Settings follow the precedence: Workspace &gt; Global &gt; Default.
  /// </para>
  /// </remarks>
  /// <example>
  /// <code>
  /// var config = VSExtensionSettings.GetConfiguration("agent");
  /// var workStyle = config.Get("workStyle", "unset");
  /// config.Update("workStyle", "aggressive", SettingScope.Workspace);
  /// </code>
  /// </example>
  public sealed class DomainConfig
  {
    private readonly string _domain;

    internal DomainConfig(string domain)
    {
      _domain = domain;
    }

    /// <summary>
    /// Gets a setting value from the specified domain.
    /// </summary>
    /// <typeparam name="T">The type to convert the value to.</typeparam>
    /// <param name="key">The setting key (without domain prefix).</param>
    /// <param name="defaultValue">The value to return if the setting is not configured.</param>
    /// <returns>The setting value or the default value.</returns>
    public T Get<T>(
        string key,
        T defaultValue = default!)
    {
      return VSExtensionSettings.Get(
          BuildKey(key),
          defaultValue);
    }

    /// <summary>
    /// Determines whether a setting is configured in any scope.
    /// </summary>
    /// <param name="key">The setting key (without domain prefix).</param>
    /// <returns>
    /// <see langword="true"/> if the setting is configured in Workspace or Global scope;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    public bool IsConfigured(string key)
    {
      return VSExtensionSettings.IsConfigured(
          BuildKey(key));
    }

    /// <summary>
    /// Gets the scope where a setting is configured.
    /// </summary>
    /// <param name="key">The setting key (without domain prefix).</param>
    /// <returns>The scope where the setting is configured, or <see cref="SettingScope.Default"/> if not configured.</returns>
    public SettingScope GetScope(string key)
    {
      return VSExtensionSettings.GetScope(
          BuildKey(key));
    }

    /// <summary>
    /// Updates a setting value at the specified scope.
    /// </summary>
    /// <param name="key">The setting key (without domain prefix).</param>
    /// <param name="value">The value to set. Pass <see langword="null"/> to remove the setting.</param>
    /// <param name="scope">The scope where the setting should be stored.</param>
    /// <remarks>
    /// Updates are persisted asynchronously. The change is reflected immediately in memory.
    /// </remarks>
    public void Update(
        string key,
        object? value,
        SettingScope scope = SettingScope.Workspace)
    {
      VSExtensionSettings.Update(
          BuildKey(key),
          value,
          scope);
    }

    /// <summary>
    /// Updates a setting value asynchronously at the specified scope.
    /// </summary>
    /// <param name="key">The setting key (without domain prefix).</param>
    /// <param name="value">The value to set. Pass <see langword="null"/> to remove the setting.</param>
    /// <param name="scope">The scope where the setting should be stored.</param>
    /// <returns>A task that represents the asynchronous update operation.</returns>
    public Task UpdateAsync(
        string key,
        object? value,
        SettingScope scope = SettingScope.Workspace)
    {
      return VSExtensionSettings.UpdateAsync(
          BuildKey(key),
          value,
          scope);
    }

    /// <summary>
    /// Removes a setting from the specified scope.
    /// </summary>
    /// <param name="key">The setting key (without domain prefix).</param>
    /// <param name="scope">The scope from which to remove the setting.</param>
    /// <remarks>
    /// Removing a setting allows lower-priority scopes to become effective again.
    /// </remarks>
    public void Remove(
        string key,
        SettingScope scope = SettingScope.Workspace)
    {
      VSExtensionSettings.Remove(
          BuildKey(key),
          scope);
    }

    /// <summary>
    /// Creates a snapshot of all settings in this domain.
    /// </summary>
    /// <returns>A string representation of the current settings state.</returns>
    /// <remarks>
    /// Snapshots can be used to detect changes by comparing with previous snapshots.
    /// </remarks>
    public string CreateSnapshot()
    {
      return VSExtensionSettings.CreateSnapshot(
          _domain);
    }

    /// <summary>
    /// Determines whether settings in this domain have changed since the last snapshot.
    /// </summary>
    /// <param name="previousSnapshot">
    /// The previous snapshot to compare against. This parameter is updated with the current snapshot.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if settings have changed; otherwise, <see langword="false"/>.
    /// </returns>
    public bool HasChanged(
        ref string? previousSnapshot)
    {
      var currentSnapshot = CreateSnapshot();

      var changed = !string.Equals(
          previousSnapshot,
          currentSnapshot,
          StringComparison.Ordinal);

      previousSnapshot = currentSnapshot;

      return changed;
    }

    private string BuildKey(string key)
    {
      if (string.IsNullOrWhiteSpace(key))
        return _domain;

      return _domain + "." + key;
    }
  }

  /// <summary>
  /// Thread-safe configuration store with automatic file persistence and change notification.
  /// </summary>
  /// <remarks>
  /// <para>
  /// This class provides a two-tier configuration system that supports:
  /// </para>
  /// <list type="bullet">
  ///   <item>
  ///     <description><b>Workspace settings</b> - Solution-specific configuration stored at
  ///     <c>&lt;solution-root&gt;\.kilo\vssettings.json</c></description>
  ///   </item>
  ///   <item>
  ///     <description><b>Global settings</b> - User-wide configuration stored at
  ///     <c>%APPDATA%\KiloVisualStudioExtension\settings.json</c></description>
  ///   </item>
  /// </list>
  /// <para>
  /// Settings follow this precedence order (highest to lowest):
  /// </para>
  /// <list type="number">
  ///   <item><description>Workspace settings</description></item>
  ///   <item><description>Global settings</description></item>
  ///   <item><description>Default values (code-defined fallbacks)</description></item>
  /// </list>
  /// <para>
  /// <b>Key Features:</b>
  /// </para>
  /// <list type="bullet">
  ///   <item><description>Automatic file watching and hot reload</description></item>
  ///   <item><description>Debounce-coalesced reloads to prevent excessive I/O</description></item>
  ///   <item><description>Atomic file writes using temporary files</description></item>
  ///   <item><description>Thread-safe access with fine-grained locking</description></item>
  ///   <item><description>Configuration change notifications via events</description></item>
  ///   <item><description>Domain-based key namespacing for logical grouping</description></item>
  /// </list>
  /// <para>
  /// <b>Usage Example:</b>
  /// </para>
  /// <code>
  /// // Get a domain-specific configuration
  /// var config = VSExtensionSettings.GetConfiguration("agent");
  /// 
  /// // Read a setting (workspace &gt; global &gt; default)
  /// var workStyle = config.Get("workStyle", "unset");
  /// 
  /// // Check if configured
  /// if (config.IsConfigured("workStyle"))
  /// {
  ///     var scope = config.GetScope("workStyle"); // Workspace or Global
  /// }
  /// 
  /// // Update a setting (defaults to workspace if available)
  /// config.Update("workStyle", "aggressive", SettingScope.Workspace);
  /// 
  /// // Monitor for changes
  /// VSExtensionSettings.ConfigurationChanged += (s, e) =>
  /// {
  ///     if (e.AffectsConfiguration("agent.workStyle"))
  ///     {
  ///         // Handle work style change
  ///     }
  /// };
  /// </code>
  /// </remarks>
  public static class VSExtensionSettings
  {
    private static readonly object _lock = new();

    private static readonly Dictionary<string, JToken>
        _globalSettings =
            new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, JToken>
        _workspaceSettings =
            new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, DomainConfig>
        _domains =
            new(StringComparer.OrdinalIgnoreCase);

    private static readonly SemaphoreSlim _saveSemaphore =
        new(1, 1);

    private static FileSystemWatcher? _globalWatcher;
    private static FileSystemWatcher? _workspaceWatcher;

    private static CancellationTokenSource?
        _globalReloadCancellation;

    private static CancellationTokenSource?
        _workspaceReloadCancellation;

    private static string? _workspaceRoot;
    private static string? _workspaceSettingsPath;

    private static bool _initialized;

    /// <summary>
    /// Occurs when configuration settings have changed.
    /// </summary>
    /// <remarks>
    /// This event is raised when:
    /// <list type="bullet">
    ///   <item><description>A setting is updated via <see cref="Update"/> or <see cref="UpdateAsync"/></description></item>
    ///   <item><description>The settings file is modified externally (file watcher detection)</description></item>
    /// </list>
    /// </remarks>
    public static event EventHandler<
        ConfigurationChangedEventArgs>? ConfigurationChanged;

    /// <summary>
    /// Gets the path to the global settings file.
    /// </summary>
    /// <value>
    /// The full path to <c>%APPDATA%\KiloVisualStudioExtension\settings.json</c>.
    /// </value> 
    public static string GlobalSettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            "KiloVisualStudioExtension",
            "settings.json");

    /// <summary>
    /// Gets the current workspace root directory.
    /// </summary>
    /// <value>
    /// The solution root directory, or <see langword="null"/> if no workspace is open.
    /// </value>
    /// <remarks>
    /// Set this value by calling <see cref="SetWorkspaceRoot"/> when a solution is opened or closed.
    /// </remarks>
   
    public static string? WorkspaceRoot
    {
      get
      {
        lock (_lock)
        {
          return _workspaceRoot;
        }
      }
    }

 
    /// <summary>
    /// Gets the path to the workspace settings file.
    /// </summary>
    /// <value>
    /// The full path to <c>&lt;solution-root&gt;\.kilo\vssettings.json</c>, or <see langword="null"/> if no workspace is open.
    /// </value>
    public static string? WorkspaceSettingsPath
    {
      get
      {
        lock (_lock)
        {
          return _workspaceSettingsPath;
        }
      }
    }

 
    /// <summary>
    /// Gets or creates a domain-specific configuration accessor.
    /// </summary>
    /// <param name="domain">The domain name (e.g., "agent", "autocomplete").</param>
    /// <returns>A <see cref="DomainConfig"/> instance for the specified domain.</returns>
    /// <exception cref="ArgumentException">The domain is null or empty.</exception>
    /// <remarks>
    /// Domains provide logical grouping of related settings. All keys accessed through
    /// the returned <see cref="DomainConfig"/> will be prefixed with the domain name.
    /// </remarks>
    public static DomainConfig GetConfiguration(
        string domain)
    {
      if (string.IsNullOrWhiteSpace(domain))
        throw new ArgumentException(
            "A configuration domain is required.",
            nameof(domain));

      EnsureInitialized();

      lock (_lock)
      {
        if (!_domains.TryGetValue(
                domain,
                out var configuration))
        {
          configuration = new DomainConfig(domain);
          _domains[domain] = configuration;
        }

        return configuration;
      }
    }

    /// <summary>
    /// Sets the workspace root directory and initializes workspace settings.
    /// </summary>
    /// <param name="workspaceRoot">The solution root directory, or <see langword="null"/> to disable workspace settings.</param>
    /// <remarks>
    /// <para>
    /// Call this method when:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>A solution is opened</description></item>
    ///   <item><description>A solution is closed</description></item>
    ///   <item><description>The workspace root changes</description></item>
    /// </list>
    /// <para>
    /// This method automatically:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>Loads workspace settings from the new location</description></item>
    ///   <item><description>Starts file watching for the workspace settings file</description></item>
    ///   <item><description>Raises <see cref="ConfigurationChanged"/> to notify listeners</description></item>
    /// </list>
    /// </remarks>
   
    public static void SetWorkspaceRoot(
        string? workspaceRoot)
    {
      EnsureInitialized();

      workspaceRoot = NormalizeDirectory(
          workspaceRoot);

      string? oldPath;
      string? newPath;

      lock (_lock)
      {
        oldPath = _workspaceSettingsPath;

        _workspaceRoot = workspaceRoot;

        newPath = workspaceRoot == null
            ? null
            : Path.Combine(
                workspaceRoot,
                ".kilo",
                "vssettings.json");

        if (string.Equals(
                oldPath,
                newPath,
                StringComparison.OrdinalIgnoreCase))
        {
          return;
        }

        _workspaceSettingsPath = newPath;

        _workspaceSettings.Clear();

        DisposeWorkspaceWatcher_NoLock();
      }

      if (newPath != null)
      {
        LoadWorkspaceSettings();
        SetupWorkspaceWatcher();
      }

      RaiseConfigurationChanged(
          new[]
          {
                    string.Empty
          });
    }

    /// <summary>
    /// Gets a setting value with workspace/global precedence.
    /// </summary>
    /// <typeparam name="T">The type to convert the value to.</typeparam>
    /// <param name="key">The setting key.</param>
    /// <param name="defaultValue">The value to return if the setting is not configured.</param>
    /// <returns>The setting value from workspace, global, or the default value.</returns>
    /// <remarks>
    /// Settings are resolved in this order:
    /// <list type="number">
    ///   <item><description>Workspace settings (if configured)</description></item>
    ///   <item><description>Global settings (if configured)</description></item>
    ///   <item><description>Default value</description></item>
    /// </list>
    /// </remarks>
   
    public static T Get<T>(
        string key,
        T defaultValue = default!)
    {
      if (string.IsNullOrWhiteSpace(key))
        return defaultValue;

      EnsureInitialized();

      JToken? token;

      lock (_lock)
      {
        if (!_workspaceSettings.TryGetValue(
                key,
                out token))
        {
          _globalSettings.TryGetValue(
              key,
              out token);
        }
      }

      if (token == null)
        return defaultValue;

      return ConvertToken(
          token,
          defaultValue);
    }

    /// <summary>
    /// Determines whether a setting is configured in any scope.
    /// </summary>
    /// <param name="key">The setting key.</param>
    /// <returns>
    /// <see langword="true"/> if the setting is configured in workspace or global scope;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    public static bool IsConfigured(
        string key)
    {
      if (string.IsNullOrWhiteSpace(key))
        return false;

      EnsureInitialized();

      lock (_lock)
      {
        return _workspaceSettings.ContainsKey(key) ||
               _globalSettings.ContainsKey(key);
      }
    }

    /// <summary>
    /// Gets the scope where a setting is configured.
    /// </summary>
    /// <param name="key">The setting key.</param>
    /// <returns>
    /// The scope where the setting is configured:
    /// <list type="table">
    ///   <item><description><see cref="SettingScope.Workspace"/> - if configured in workspace settings</description></item>
    ///   <item><description><see cref="SettingScope.Global"/> - if configured in global settings</description></item>
    ///   <item><description><see cref="SettingScope.Default"/> - if not configured</description></item>
    /// </list>
    /// </returns>
    public static SettingScope GetScope(
        string key)
    {
      if (string.IsNullOrWhiteSpace(key))
        return SettingScope.Default;

      EnsureInitialized();

      lock (_lock)
      {
        if (_workspaceSettings.ContainsKey(key))
          return SettingScope.Workspace;

        if (_globalSettings.ContainsKey(key))
          return SettingScope.Global;

        return SettingScope.Default;
      }
    }

    /// <summary>
    /// Updates a setting value synchronously (persists asynchronously).
    /// </summary>
    /// <param name="key">The setting key.</param>
    /// <param name="value">The value to set. Pass <see langword="null"/> to remove the setting.</param>
    /// <param name="scope">The scope where the setting should be stored.</param>
    /// <exception cref="ArgumentException">The key is null/empty or scope is <see cref="SettingScope.Default"/>.</exception>
    /// <remarks>
    /// <para>
    /// This method updates the in-memory cache immediately and persists to disk asynchronously.
    /// The change is visible to all readers immediately.
    /// </para>
    /// <para>
    /// Passing <see langword="null"/> for the value removes the setting from the specified scope,
    /// allowing lower-priority scopes to become effective again.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Updates a setting value synchronously (persists asynchronously).
    /// </summary>
    /// <param name="key">The setting key.</param>
    /// <param name="value">The value to set. Pass <see langword="null"/> to remove the setting.</param>
    /// <param name="scope">The scope where the setting should be stored.</param>
    /// <exception cref="ArgumentException">The key is null/empty or scope is <see cref="SettingScope.Default"/>.</exception>
    /// <remarks>
    /// <para>
    /// This method updates the in-memory cache immediately and persists to disk asynchronously.
    /// The change is visible to all readers immediately.
    /// </para>
    /// <para>
    /// Passing <see langword="null"/> for the value removes the setting from the specified scope,
    /// allowing lower-priority scopes to become effective again.
    /// </para>
    /// </remarks>
    public static void Update(
        string key,
        object? value,
        SettingScope scope = SettingScope.Workspace)
    {
      _ = UpdateAsync(key, value, scope);
    }

    /// <summary>
    /// Updates a setting value asynchronously.
    /// </summary>
    /// <param name="key">The setting key.</param>
    /// <param name="value">The value to set. Pass <see langword="null"/> to remove the setting.</param>
    /// <param name="scope">The scope where the setting should be stored.</param>
    /// <returns>A task that represents the asynchronous update operation.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the key is null/empty or scope is <see cref="SettingScope.Default"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This method:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>Updates the in-memory cache immediately</description></item>
    ///   <item><description>Writes to disk using atomic file replacement</description></item>
    ///   <item><description>Raises <see cref="ConfigurationChanged"/> event</description></item>
    /// </list>
    /// <para>
    /// Passing <see langword="null"/> for the value removes the setting from the specified scope.
    /// </para>
    /// </remarks>
    public static async Task UpdateAsync(
        string key,
        object? value,
        SettingScope scope = SettingScope.Workspace)
    {
      if (string.IsNullOrWhiteSpace(key))
        throw new ArgumentException(
            "A setting key is required.",
            nameof(key));

      if (scope == SettingScope.Default)
      {
        throw new ArgumentException(
            "Default settings cannot be persisted.",
            nameof(scope));
      }

      EnsureInitialized();

      var token = value == null
          ? null
          : JToken.FromObject(value);

      Dictionary<string, JToken> snapshot;

      lock (_lock)
      {
        var target = GetSettingsDictionary_NoLock(
            scope);

        /*
         * Null means remove the setting. This is preferable to
         * storing a configured null value because it allows the
         * lower-priority scope to become effective again.
         */
        if (token == null)
          target.Remove(key);
        else
          target[key] = token;

        snapshot = new Dictionary<string, JToken>(
            target,
            StringComparer.OrdinalIgnoreCase);
      }

      var path = GetSettingsPath(scope);

      if (path == null)
        return;

      await SaveSettingsAsync(
          path,
          snapshot);

      RaiseConfigurationChanged(
          new[]
          {
                    key
          });
    }

    /// <summary>
    /// Removes a setting from the specified scope.
    /// </summary>
    /// <param name="key">The setting key.</param>
    /// <param name="scope">The scope from which to remove the setting.</param>
    /// <remarks>
    /// Removing a setting allows lower-priority scopes to become effective again.
    /// For example, removing a workspace setting will cause the global setting to take effect.
    /// </remarks>
    /// <summary>
    /// Removes a setting from the specified scope.
    /// </summary>
    /// <param name="key">The setting key.</param>
    /// <param name="scope">The scope from which to remove the setting.</param>
    /// <remarks>
    /// Removing a setting allows lower-priority scopes to become effective again.
    /// For example, removing a workspace setting will cause the global setting to take effect.
    /// </remarks>
    public static void Remove(
        string key,
        SettingScope scope = SettingScope.Workspace)
    {
      _ = RemoveAsync(key, scope);
    }

    /// <summary>
    /// Removes a setting from the specified scope asynchronously.
    /// </summary>
    /// <param name="key">The setting key.</param>
    /// <param name="scope">The scope from which to remove the setting.</param>
    /// <returns>A task that represents the asynchronous remove operation.</returns>
    /// <remarks>
    /// This method is equivalent to calling <see cref="UpdateAsync"/> with a <see langword="null"/> value.
    /// </remarks>
    public static async Task RemoveAsync(
        string key,
        SettingScope scope = SettingScope.Workspace)
    {
      await UpdateAsync(
          key,
          null,
          scope);
    }

    public static Dictionary<string, object?> GetAll(
        SettingScope scope = SettingScope.Workspace)
    {
      EnsureInitialized();

      Dictionary<string, JToken> source;

      lock (_lock)
      {
        source = new Dictionary<string, JToken>(
            GetSettingsDictionary_NoLock(scope),
            StringComparer.OrdinalIgnoreCase);
      }

      return source.ToDictionary(
          pair => pair.Key,
          pair => pair.Value.ToObject<object?>(),
          StringComparer.OrdinalIgnoreCase);
    }

    public static Dictionary<string, object?> GetEffective()
    {
      EnsureInitialized();

      var result =
          new Dictionary<string, object?>(
              StringComparer.OrdinalIgnoreCase);

      lock (_lock)
      {
        foreach (var pair in _globalSettings)
        {
          result[pair.Key] =
              pair.Value.ToObject<object?>();
        }

        foreach (var pair in _workspaceSettings)
        {
          result[pair.Key] =
              pair.Value.ToObject<object?>();
        }
      }

      return result;
    }

    public static string CreateSnapshot(
        string? domain = null)
    {
      EnsureInitialized();

      var effective = GetEffective();

      var selected = effective
          .Where(pair =>
              domain == null ||
              pair.Key.Equals(
                  domain,
                  StringComparison.OrdinalIgnoreCase) ||
              pair.Key.StartsWith(
                  domain + ".",
                  StringComparison.OrdinalIgnoreCase))
          .OrderBy(pair => pair.Key)
          .Select(pair =>
              pair.Key + "=" +
              SerializeValue(pair.Value));

      return string.Join(
          "|",
          selected);
    }

    public static void Reload()
    {
      EnsureInitialized();

      LoadGlobalSettings();
      LoadWorkspaceSettings();
    }

    public static void Dispose()
    {
      lock (_lock)
      {
        _globalWatcher?.Dispose();
        _globalWatcher = null;

        _workspaceWatcher?.Dispose();
        _workspaceWatcher = null;

        _globalReloadCancellation?.Cancel();
        _globalReloadCancellation?.Dispose();
        _globalReloadCancellation = null;

        _workspaceReloadCancellation?.Cancel();
        _workspaceReloadCancellation?.Dispose();
        _workspaceReloadCancellation = null;

        _globalSettings.Clear();
        _workspaceSettings.Clear();
        _domains.Clear();

        _workspaceRoot = null;
        _workspaceSettingsPath = null;
        _initialized = false;
      }
    }

    private static void EnsureInitialized()
    {
      if (_initialized)
        return;

      lock (_lock)
      {
        if (_initialized)
          return;

        _initialized = true;
      }

      LoadGlobalSettings();
    }

    private static Dictionary<string, JToken>
        GetSettingsDictionary_NoLock(
            SettingScope scope)
    {
      return scope switch
      {
        SettingScope.Global => _globalSettings,
        SettingScope.Workspace => _workspaceSettings,
        _ => throw new ArgumentOutOfRangeException(
            nameof(scope))
      };
    }

    private static string? GetSettingsPath(
        SettingScope scope)
    {
      if (scope == SettingScope.Global)
        return GlobalSettingsPath;

      if (scope == SettingScope.Workspace)
      {
        lock (_lock)
        {
          return _workspaceSettingsPath;
        }
      }

      return null;
    }

    private static void LoadGlobalSettings()
    {
      var result = ReadSettingsFile(
          GlobalSettingsPath);

      List<string> changedKeys;

      lock (_lock)
      {
        changedKeys = GetChangedKeys(
            _globalSettings,
            result);

        _globalSettings.Clear();

        foreach (var pair in result)
          _globalSettings[pair.Key] = pair.Value;
      }

      SetupGlobalWatcher();

      if (changedKeys.Count > 0)
      {
        RaiseConfigurationChanged(
            changedKeys);
      }
    }

    private static void LoadWorkspaceSettings()
    {
      string? path;

      lock (_lock)
      {
        path = _workspaceSettingsPath;
      }

      if (path == null)
        return;

      var result = ReadSettingsFile(path);

      List<string> changedKeys;

      lock (_lock)
      {
        changedKeys = GetChangedKeys(
            _workspaceSettings,
            result);

        _workspaceSettings.Clear();

        foreach (var pair in result)
          _workspaceSettings[pair.Key] = pair.Value;
      }

      if (changedKeys.Count > 0)
      {
        RaiseConfigurationChanged(
            changedKeys);
      }
    }

    private static Dictionary<string, JToken>
        ReadSettingsFile(
            string path)
    {
      try
      {
        if (!File.Exists(path))
        {
          return new Dictionary<string, JToken>(
              StringComparer.OrdinalIgnoreCase);
        }

        var json = ReadTextWithRetry(path);

        if (string.IsNullOrWhiteSpace(json))
        {
          return new Dictionary<string, JToken>(
              StringComparer.OrdinalIgnoreCase);
        }

        var parsed =
            JsonConvert.DeserializeObject<
                Dictionary<string, JToken>>(json);

        return parsed == null
            ? new Dictionary<string, JToken>(
                StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, JToken>(
                parsed,
                StringComparer.OrdinalIgnoreCase);
      }
      catch (Exception ex)
      {
        Debug.WriteLine(
            $"[Kilo] Failed to read settings '{path}': {ex}");

        return new Dictionary<string, JToken>(
            StringComparer.OrdinalIgnoreCase);
      }
    }

    private static string? ReadTextWithRetry(
        string path)
    {
      for (var attempt = 0; attempt < 5; attempt++)
      {
        try
        {
          return File.ReadAllText(path);
        }
        catch (IOException)
        {
          Thread.Sleep(50);
        }
        catch (UnauthorizedAccessException)
        {
          Thread.Sleep(50);
        }
      }

      return null;
    }

    private static async Task SaveSettingsAsync(
        string path,
        Dictionary<string, JToken> settings)
    {
      await _saveSemaphore.WaitAsync();

      try
      {
        var directory =
            Path.GetDirectoryName(path);

        if (string.IsNullOrWhiteSpace(directory))
          return;

        Directory.CreateDirectory(directory);

        var json = JsonConvert.SerializeObject(
            settings,
            Formatting.Indented);

        var temporaryPath =
            path + "." +
            Guid.NewGuid().ToString("N") +
            ".tmp";

        try
        {
          await AsyncUtils.WriteAllTextAsync(
              temporaryPath,
              json);

          ReplaceFile(
              temporaryPath,
              path);
        }
        finally
        {
          TryDelete(temporaryPath);
        }
      }
      catch (Exception ex)
      {
        Debug.WriteLine(
            $"[Kilo] Failed to save settings '{path}': {ex}");
      }
      finally
      {
        _saveSemaphore.Release();
      }
    }

    private static void ReplaceFile(
        string temporaryPath,
        string destinationPath)
    {
      if (File.Exists(destinationPath))
      {
        try
        {
          File.Replace(
              temporaryPath,
              destinationPath,
              null);

          return;
        }
        catch
        {
          // Fall back to copy below.
        }
      }

      File.Copy(
          temporaryPath,
          destinationPath,
          true);
    }

    private static void SetupGlobalWatcher()
    {
      lock (_lock)
      {
        if (_globalWatcher != null)
          return;

        var directory =
            Path.GetDirectoryName(
                GlobalSettingsPath);

        if (string.IsNullOrWhiteSpace(directory))
          return;

        Directory.CreateDirectory(directory);

        _globalWatcher = CreateWatcher(
            directory,
            Path.GetFileName(
                GlobalSettingsPath),
            ScheduleGlobalReload);
      }
    }

    private static void SetupWorkspaceWatcher()
    {
      lock (_lock)
      {
        if (_workspaceWatcher != null ||
            _workspaceSettingsPath == null)
        {
          return;
        }

        var directory =
            Path.GetDirectoryName(
                _workspaceSettingsPath);

        if (string.IsNullOrWhiteSpace(directory))
          return;

        Directory.CreateDirectory(directory);

        _workspaceWatcher = CreateWatcher(
            directory,
            Path.GetFileName(
                _workspaceSettingsPath),
            ScheduleWorkspaceReload);
      }
    }

    private static FileSystemWatcher CreateWatcher(
        string directory,
        string fileName,
        Action reload)
    {
      var watcher = new FileSystemWatcher(
          directory,
          fileName)
      {
        NotifyFilter =
              NotifyFilters.FileName |
              NotifyFilters.LastWrite |
              NotifyFilters.Size,
        IncludeSubdirectories = false,
        EnableRaisingEvents = true
      };

      watcher.Changed += (_, _) => reload();
      watcher.Created += (_, _) => reload();
      watcher.Deleted += (_, _) => reload();
      watcher.Renamed += (_, _) => reload();

      return watcher;
    }

    private static void ScheduleGlobalReload()
    {
      CancellationToken token;

      lock (_lock)
      {
        _globalReloadCancellation?.Cancel();
        _globalReloadCancellation?.Dispose();

        _globalReloadCancellation =
            new CancellationTokenSource();

        token =
            _globalReloadCancellation.Token;
      }

      _ = Task.Run(
          async () =>
          {
            try
            {
              await Task.Delay(150, token);

              if (!token.IsCancellationRequested)
                LoadGlobalSettings();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
              Debug.WriteLine(
                        $"[Kilo] Global settings reload failed: {ex}");
            }
          },
          token);
    }

    private static void ScheduleWorkspaceReload()
    {
      CancellationToken token;

      lock (_lock)
      {
        _workspaceReloadCancellation?.Cancel();
        _workspaceReloadCancellation?.Dispose();

        _workspaceReloadCancellation =
            new CancellationTokenSource();

        token =
            _workspaceReloadCancellation.Token;
      }

      _ = Task.Run(
          async () =>
          {
            try
            {
              await Task.Delay(150, token);

              if (!token.IsCancellationRequested)
                LoadWorkspaceSettings();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
              Debug.WriteLine(
                        $"[Kilo] Workspace settings reload failed: {ex}");
            }
          },
          token);
    }

    private static List<string> GetChangedKeys(
        Dictionary<string, JToken> previous,
        Dictionary<string, JToken> current)
    {
      var keys = new HashSet<string>(
          previous.Keys,
          StringComparer.OrdinalIgnoreCase);

      keys.UnionWith(current.Keys);

      return keys
          .Where(key =>
          {
            previous.TryGetValue(
                      key,
                      out var oldValue);

            current.TryGetValue(
                      key,
                      out var newValue);

            return !JToken.DeepEquals(
                      oldValue,
                      newValue);
          })
          .ToList();
    }

    private static T ConvertToken<T>(
        JToken token,
        T defaultValue)
    {
      try
      {
        var value = token.ToObject<T>();

        return value == null
            ? defaultValue
            : value;
      }
      catch (Exception ex)
      {
        Debug.WriteLine(
            $"[Kilo] Invalid setting value: {ex.Message}");

        return defaultValue;
      }
    }

    private static string SerializeValue(
        object? value)
    {
      return JsonConvert.SerializeObject(
          value,
          Formatting.None);
    }

    private static string? NormalizeDirectory(
        string? directory)
    {
      if (string.IsNullOrWhiteSpace(directory))
        return null;

      try
      {
        return Path.GetFullPath(directory);
      }
      catch
      {
        return null;
      }
    }

    private static void DisposeWorkspaceWatcher_NoLock()
    {
      _workspaceWatcher?.Dispose();
      _workspaceWatcher = null;

      _workspaceReloadCancellation?.Cancel();
      _workspaceReloadCancellation?.Dispose();
      _workspaceReloadCancellation = null;
    }

    private static void TryDelete(
        string path)
    {
      try
      {
        if (File.Exists(path))
          File.Delete(path);
      }
      catch
      {
      }
    }

    private static void RaiseConfigurationChanged(
        IEnumerable<string> keys)
    {
      var handler = ConfigurationChanged;

      if (handler == null)
        return;

      try
      {
        handler(
            null,
            new ConfigurationChangedEventArgs(keys));
      }
      catch (Exception ex)
      {
        Debug.WriteLine(
            $"[Kilo] ConfigurationChanged failed: {ex}");
      }
    }
  }
}
