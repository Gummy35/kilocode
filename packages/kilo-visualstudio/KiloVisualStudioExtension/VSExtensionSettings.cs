#nullable enable

namespace KiloVisualStudioExtension;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;


/// <summary>
/// Manages user settings stored in .kilo/vssettings.json at the solution root.
/// Provides thread-safe cached access with async file persistence and file watcher support.
/// </summary>
public static class VSExtensionSettings
{
  private static readonly object _lock = new object();
  private static readonly Dictionary<string, object?> _cache = new Dictionary<string, object?>();
  private static bool _isLoaded = false;
  private static FileSystemWatcher? _fileWatcher;
  private static string? _settingsPath;

  /// <summary>
  /// Gets the settings file path (.kilo/vssettings.json in solution root).
  /// </summary>
  private static string SettingsPath
  {
    get
    {
      if (_settingsPath != null)
        return _settingsPath;

      // Find solution root by looking for .sln file or use current directory
      var currentDir = Directory.GetCurrentDirectory();
      var searchDir = currentDir;

      while (searchDir != null)
      {
        var slnFiles = Directory.GetFiles(searchDir, "*.sln");
        if (slnFiles.Length > 0)
          break;

        var parent = Directory.GetParent(searchDir);
        if (parent == null)
          break;

        searchDir = parent.FullName;
      }

      var rootDir = searchDir ?? currentDir;
      var kiloDir = Path.Combine(rootDir, ".kilo");
      if (!Directory.Exists(kiloDir))
        Directory.CreateDirectory(kiloDir);

      _settingsPath = Path.Combine(kiloDir, "vssettings.json");
      return _settingsPath;
    }
  }

  /// <summary>
  /// Initializes the settings store and starts file watcher.
  /// Called automatically on first Get or Update.
  /// </summary>
  private static void EnsureInitialized()
  {
    if (_isLoaded)
      return;

    lock (_lock)
    {
      if (_isLoaded)
        return;

      LoadSettings();
      SetupFileWatcher();
      _isLoaded = true;
    }
  }

  /// <summary>
  /// Loads settings from file into cache.
  /// </summary>
  private static void LoadSettings()
  {
    try
    {
      if (File.Exists(SettingsPath))
      {
        var json = File.ReadAllText(SettingsPath);
        var settings = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
        if (settings != null)
        {
          lock (_lock)
          {
            _cache.Clear();
            foreach (var kvp in settings)
            {
              _cache[kvp.Key] = kvp.Value;
            }
          }
        }
      }
    }
    catch (Exception ex)
    {
      System.Diagnostics.Debug.WriteLine($"[Kilo] ExtensionSettingsStore: Failed to load settings: {ex.Message}");
    }
  }

  /// <summary>
  /// Sets up file watcher to reload settings when file changes.
  /// </summary>
  private static void SetupFileWatcher()
  {
    try
    {
      var kiloDir = Path.GetDirectoryName(SettingsPath);
      if (kiloDir == null)
        return;

      _fileWatcher?.Dispose();
      _fileWatcher = new FileSystemWatcher(kiloDir, "vssettings.json")
      {
        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
      };

      _fileWatcher.Changed += (sender, e) =>
      {
        try
        {
          System.Threading.Thread.Sleep(100); // Debounce file writes
          LoadSettings();
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"[Kilo] ExtensionSettingsStore: File watcher error: {ex.Message}");
        }
      };

      _fileWatcher.EnableRaisingEvents = true;
    }
    catch (Exception ex)
    {
      System.Diagnostics.Debug.WriteLine($"[Kilo] ExtensionSettingsStore: Failed to setup file watcher: {ex.Message}");
    }
  }

  /// <summary>
  /// Gets a setting value by key with optional default.
  /// </summary>
  /// <typeparam name="T">The type of the setting value.</typeparam>
  /// <param name="key">The setting key.</param>
  /// <param name="defaultValue">The default value if key not found.</param>
  /// <returns>The setting value or default value.</returns>
  public static T Get<T>(string key, T defaultValue = default!)
  {
    EnsureInitialized();

    lock (_lock)
    {
      if (_cache.TryGetValue(key, out var value))
      {
        if (value == null)
          return defaultValue!;

        // Handle type conversion
        if (value is T typedValue)
          return typedValue;

        // Handle JSON deserialization types
        if (value is long lng && typeof(T) == typeof(int))
          return (T)(object)(int)lng;

        if (value is double dbl && typeof(T) == typeof(int))
          return (T)(object)(int)dbl;

        if (value is int intl && typeof(T) == typeof(double))
          return (T)(object)(double)intl;

        if (value is string str)
        {
          if (typeof(T) == typeof(bool))
            return (T)(object)(str.ToLowerInvariant() == "true" || str == "1");

          if (typeof(T) == typeof(int))
            return (T)(object)int.Parse(str);

          if (typeof(T) == typeof(double))
            return (T)(object)double.Parse(str);
        }

        return defaultValue!;
      }
      return defaultValue;
    }
  }

  /// <summary>
  /// Updates a setting value by key.
  /// Writes to file asynchronously (fire and forget).
  /// </summary>
  /// <param name="key">The setting key.</param>
  /// <param name="value">The setting value.</param>
  public static void Update(string key, object? value)
  {
    EnsureInitialized();

    lock (_lock)
    {
      _cache[key] = value;
    }

    // Fire and forget async write
    _ = SaveSettingsAsync();
  }

  /// <summary>
  /// Saves settings to file asynchronously.
  /// </summary>
  private static async Task SaveSettingsAsync()
  {
    try
    {
      await Task.Yield(); // Ensure we're on a background thread

      Dictionary<string, object> settings;
      lock (_lock)
      {
        settings = new Dictionary<string, object>(_cache);
      }

      var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
      File.WriteAllText(SettingsPath, json);
    }
    catch (Exception ex)
    {
      System.Diagnostics.Debug.WriteLine($"[Kilo] ExtensionSettingsStore: Failed to save settings: {ex.Message}");
    }
  }

  /// <summary>
  /// Clears the cache and reloads from file.
  /// </summary>
  public static void Reload()
  {
    lock (_lock)
    {
      _cache.Clear();
    }
    LoadSettings();
  }

  /// <summary>
  /// Gets all settings as a dictionary.
  /// </summary>
  public static Dictionary<string, object?> GetAll()
  {
    EnsureInitialized();

    lock (_lock)
    {
      return new Dictionary<string, object?>(_cache);
    }
  }
}
