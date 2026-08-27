using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services
{
  /// <summary>
  /// Cache service that provides Memento-like storage for extension state.
  /// Matches the VS Code extensionContext.globalState pattern.
  /// </summary>
  public interface ICacheService:IServiceProviderService
  {
    /// <summary>
    /// Get a cached value by key. Returns null if not found.
    /// </summary>
    T? Get<T>(string key) where T : class;

    /// <summary>
    /// Get a cached value by key with default fallback.
    /// </summary>
    T Get<T>(string key, T defaultValue) where T : class;

    /// <summary>
    /// Get a JsonElement from cache. Returns null if not found.
    /// </summary>
    JsonElement? GetJson(string key);

    /// <summary>
    /// Get a JsonElement from cache with default fallback.
    /// </summary>
    JsonElement GetJson(string key, JsonElement defaultValue);

    /// <summary>
    /// Update a cached value. Persists to storage asynchronously.
    /// </summary>
    Task UpdateAsync(string key, object? value);

    /// <summary>
    /// Remove a cached value.
    /// </summary>
    void Remove(string key);

    /// <summary>
    /// Check if a key exists in cache.
    /// </summary>
    bool Contains(string key);

    Task ClearAsync();
  }

  /// <summary>
  /// Implementation of ICacheService that provides in-memory caching with async persistence.
  /// </summary>
  public class CacheService : ServiceProviderServiceBase, ICacheService
  {
    public static CacheService GlobalState { get; } = new CacheService(null, null);
    public static CacheService WorkspaceState { get; } = new CacheService(null, null);

    private readonly Dictionary<string, object?> _cache = new Dictionary<string, object?>();
    private readonly Dictionary<string, JsonElement> _storage = new Dictionary<string, JsonElement>();
    private readonly Func<string, JsonElement?, Task> _onSave = (key, value) => Task.CompletedTask;

    /// <summary>
    /// Creates a new CacheService instance.
    /// </summary>
    /// <param name="onSave">Callback invoked when cache values are persisted.</param>
    public CacheService(ServiceProvider serviceProvider, Func<string, JsonElement?, Task>? onSave = null) : base(serviceProvider)
    {
      _onSave = onSave ?? ((key, value) => Task.CompletedTask);
    }

    public T? Get<T>(string key) where T : class
    {
      if (_cache.TryGetValue(key, out var value))
      {
        return value as T;
      }

      // Try to load from storage
      if (_storage.TryGetValue(key, out var json))
      {
        var deserialized = json.Deserialize<T>();
        if (deserialized != null)
        {
          _cache[key] = deserialized;
          return deserialized;
        }
      }

      return default;
    }

    public T Get<T>(string key, T defaultValue) where T : class
    {
      return Get<T>(key) ?? defaultValue;
    }

    public JsonElement? GetJson(string key)
    {
      if (_cache.TryGetValue(key, out var value))
      {
        if (value is JsonElement json)
        {
          return json;
        }
      }

      if (_storage.TryGetValue(key, out var stored))
      {
        return stored;
      }

      return null;
    }

    public JsonElement GetJson(string key, JsonElement defaultValue)
    {
      var result = GetJson(key);
      return result.HasValue ? result.Value : defaultValue;
    }

    public async Task UpdateAsync(string key, object? value)
    {
      if (value == null)
      {
        _cache.Remove(key);
        _storage.Remove(key);
        await _onSave(key, null);
      }
      else
      {
        var json = JsonSerializer.SerializeToElement(value);
        _cache[key] = json;
        _storage[key] = json;
        await _onSave(key, json);
      }
    }

    public void Remove(string key)
    {
      _cache.Remove(key);
      _storage.Remove(key);
    }

    public bool Contains(string key)
    {
      return _cache.ContainsKey(key) || _storage.ContainsKey(key);
    }

    public async Task ClearAsync()
    {
      var keys = _cache.Keys.ToList();
      foreach (var key in keys)
      {
        Remove(key);
        await _onSave(key, null);
      }
      keys = _storage.Keys.ToList();
      foreach (var key in keys)
      {
        Remove(key);
        await _onSave(key, null);
      }
    }
  }
}
