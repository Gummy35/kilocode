using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension
{
    /// <summary>
    /// Cached HTTP client wrapper with 10s TTL for CLI API calls.
    /// Prevents excessive API calls during initialization and reduces load.
    /// </summary>
    public class CachedHttpClient : IDisposable
    {
        private readonly HttpClientWrapper _inner;
        private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
        private readonly TimeSpan _ttl = TimeSpan.FromSeconds(10);
        private readonly Timer _cleanupTimer;
        private bool _disposed;

        public CachedHttpClient(HttpClientWrapper inner)
        {
            _inner = inner;
            // Cleanup expired cache entries every 30 seconds
            _cleanupTimer = new Timer(CleanupExpiredEntries, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        private class CacheEntry
        {
            public object? Data { get; set; }
            public DateTime ExpiresAt { get; set; }
            public TaskCompletionSource<object?>? Pending { get; set; }
        }

        public async Task<T?> GetJsonAsync<T>(string endpoint)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CachedHttpClient));

            // Check cache first
            if (_cache.TryGetValue(endpoint, out var entry) && DateTime.UtcNow < entry.ExpiresAt)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] Cache HIT for {endpoint}");
                return (T?)entry.Data;
            }

            // Check if there's a pending request
            if (entry?.Pending != null)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] Waiting for pending request for {endpoint}");
                var result = await entry.Pending.Task;
                return (T?)result;
            }

            // Create new pending request
            var tcs = new TaskCompletionSource<object?>();
            var newEntry = new CacheEntry { Pending = tcs };
            
            // Only add if not already present (race condition)
            if (!_cache.TryAdd(endpoint, newEntry))
            {
                // Another request won the race, wait for it
                if (_cache.TryGetValue(endpoint, out var raceEntry) && raceEntry.Pending != null)
                {
                    var result = await raceEntry.Pending.Task;
                    return (T?)result;
                }
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] Cache MISS for {endpoint}, fetching...");
                var data = await _inner.GetJsonAsync<T>(endpoint);
                
                // Update cache entry
                newEntry.Data = data;
                newEntry.ExpiresAt = DateTime.UtcNow + _ttl;
                newEntry.Pending = null;
                
                tcs.SetResult(data);
                System.Diagnostics.Debug.WriteLine($"[Kilo] Cached {endpoint} for {_ttl.TotalSeconds}s");
                
                return data;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] Cache error for {endpoint}: {ex.Message}");
                newEntry.Pending = null;
                tcs.SetException(ex);
                throw;
            }
        }

        public async Task<JsonDocument?> GetJsonAsync(string endpoint)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CachedHttpClient));

            if (_cache.TryGetValue(endpoint, out var entry) && DateTime.UtcNow < entry.ExpiresAt)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] Cache HIT for {endpoint}");
                return (JsonDocument?)entry.Data;
            }

            if (entry?.Pending != null)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] Waiting for pending request for {endpoint}");
                var result = await entry.Pending.Task;
                return (JsonDocument?)result;
            }

            var tcs = new TaskCompletionSource<object?>();
            var newEntry = new CacheEntry { Pending = tcs };
            
            if (!_cache.TryAdd(endpoint, newEntry))
            {
                if (_cache.TryGetValue(endpoint, out var raceEntry) && raceEntry.Pending != null)
                {
                    var result = await raceEntry.Pending.Task;
                    return (JsonDocument?)result;
                }
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] Cache MISS for {endpoint}, fetching...");
                var data = await _inner.GetJsonAsync(endpoint);
                
                newEntry.Data = data;
                newEntry.ExpiresAt = DateTime.UtcNow + _ttl;
                newEntry.Pending = null;
                
                tcs.SetResult(data);
                System.Diagnostics.Debug.WriteLine($"[Kilo] Cached {endpoint} for {_ttl.TotalSeconds}s");
                
                return data;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] Cache error for {endpoint}: {ex.Message}");
                newEntry.Pending = null;
                tcs.SetException(ex);
                throw;
            }
        }

        private void CleanupExpiredEntries(object? state)
        {
            if (_disposed) return;

            var now = DateTime.UtcNow;
            foreach (var key in _cache.Keys)
            {
                if (_cache.TryGetValue(key, out var entry) && now >= entry.ExpiresAt)
                {
                    _cache.TryRemove(key, out _);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cleanupTimer.Dispose();
            _cache.Clear();
        }
    }
}
