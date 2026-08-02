using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension
{
    /// <summary>
    /// Cached HTTP client wrapper with 10-second TTL for CLI API calls.
    /// Prevents excessive API calls during initialization and reduces server load.
    /// 
    /// Features:
    /// - Caches GET responses for 10 seconds
    /// - Deduplicates concurrent requests for the same endpoint
    /// - Automatically cleans up expired cache entries every 30 seconds
    /// - Thread-safe cache operations using ConcurrentDictionary
    /// </summary>
    public class CachedHttpClient : IDisposable
    {
        /// <summary>
        /// The inner HTTP client wrapper for actual API calls.
        /// </summary>
        private readonly HttpClientWrapper _inner;
        
        /// <summary>
        /// Thread-safe cache storing responses by endpoint.
        /// </summary>
        private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
        
        /// <summary>
        /// Time-to-live for cached entries (10 seconds).
        /// </summary>
        private readonly TimeSpan _ttl = TimeSpan.FromSeconds(10);
        
        /// <summary>
        /// Timer for periodic cleanup of expired cache entries.
        /// </summary>
        private readonly Timer _cleanupTimer;
        
        /// <summary>
        /// Flag indicating whether the object has been disposed.
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// Cache entry containing the response data, expiration time, and pending request tracker.
        /// </summary>
        private class CacheEntry
        {
            /// <summary>
            /// The cached response data.
            /// </summary>
            public object? Data { get; set; }
            
            /// <summary>
            /// UTC timestamp when the cache entry expires.
            /// </summary>
            public DateTime ExpiresAt { get; set; }
            
            /// <summary>
            /// Task completion source for deduplicating concurrent requests.
            /// </summary>
            public TaskCompletionSource<object?>? Pending { get; set; }
        }

        /// <summary>
        /// Creates a new CachedHttpClient wrapping the inner HTTP client.
        /// Starts the cleanup timer automatically.
        /// </summary>
        /// <param name="inner">The HttpClientWrapper to wrap.</param>
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

        /// <summary>
        /// Gets cached JSON data from the specified endpoint.
        /// Returns cached data if available and not expired, otherwise fetches from the inner client.
        /// Deduplicates concurrent requests for the same endpoint.
        /// </summary>
        /// <typeparam name="T">The type to deserialize to.</typeparam>
        /// <param name="endpoint">The API endpoint.</param>
        /// <returns>The deserialized object.</returns>
        /// <exception cref="ObjectDisposedException">Thrown if the client has been disposed.</exception>
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

        /// <summary>
        /// Gets cached JSON data as a JsonDocument from the specified endpoint.
        /// Returns cached data if available and not expired, otherwise fetches from the inner client.
        /// Deduplicates concurrent requests for the same endpoint.
        /// </summary>
        /// <param name="endpoint">The API endpoint.</param>
        /// <returns>The parsed JsonDocument.</returns>
        /// <exception cref="ObjectDisposedException">Thrown if the client has been disposed.</exception>
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

        /// <summary>
        /// Cleans up expired cache entries. Called by the cleanup timer every 30 seconds.
        /// </summary>
        /// <param name="state">Unused state parameter.</param>
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

        /// <summary>
        /// Disposes of the cached HTTP client and clears all cache entries.
        /// Stops the cleanup timer.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cleanupTimer.Dispose();
            _cache.Clear();
        }
    }
}
