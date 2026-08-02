using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension
{
    /// <summary>
    /// Wrapper around HttpClient for making authenticated HTTP requests to the Kilo backend.
    /// Handles Basic authentication and provides convenience methods for GET/POST requests
    /// with JSON serialization.
    /// </summary>
    public class HttpClientWrapper : IDisposable
    {
        /// <summary>
        /// The underlying HttpClient instance.
        /// </summary>
        private readonly HttpClient _httpClient;
        
        /// <summary>
        /// The base URL of the backend server.
        /// </summary>
        private readonly string _baseUrl;
        
        /// <summary>
        /// The password used for Basic authentication.
        /// </summary>
        private readonly string _password;
        
        /// <summary>
        /// Flag indicating whether the object has been disposed.
        /// </summary>
        private bool _disposed;
        
        /// <summary>
        /// Current connection state.
        /// </summary>
        private ConnectionState _state = ConnectionState.Disconnected;

        /// <summary>
        /// Creates a new HttpClientWrapper with Basic authentication configured.
        /// </summary>
        /// <param name="baseUrl">The base URL of the backend server.</param>
        /// <param name="password">The password for authentication.</param>
        public HttpClientWrapper(string baseUrl, string password)
        {
            _baseUrl = baseUrl;
            _password = password;
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };
            var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"kilo:{password}"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
            _state = ConnectionState.Connected;
        }

        /// <summary>
        /// Sends an HTTP GET request to the specified endpoint.
        /// </summary>
        /// <param name="endpoint">The API endpoint (e.g., "/session").</param>
        /// <param name="cancellationToken">Token to cancel the request.</param>
        /// <returns>The HTTP response message.</returns>
        public async Task<HttpResponseMessage> GetAsync(string endpoint, CancellationToken cancellationToken = default)
        {
            var url = $"{_baseUrl}{endpoint}";
            System.Diagnostics.Debug.WriteLine($"[Kilo] HTTP GET: {url}");
            return await _httpClient.GetAsync(endpoint, cancellationToken);
        }

        /// <summary>
        /// Sends an HTTP POST request to the specified endpoint with an optional JSON body.
        /// </summary>
        /// <param name="endpoint">The API endpoint.</param>
        /// <param name="body">The object to serialize as JSON body, or null.</param>
        /// <param name="cancellationToken">Token to cancel the request.</param>
        /// <returns>The HTTP response message.</returns>
        public async Task<HttpResponseMessage> PostAsync(string endpoint, object? body, CancellationToken cancellationToken = default)
        {
            var url = $"{_baseUrl}{endpoint}";
            var content = body == null ? null : new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            System.Diagnostics.Debug.WriteLine($"[Kilo] HTTP POST: {url}");
            var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
            System.Diagnostics.Debug.WriteLine($"[Kilo] HTTP POST response: {(int)response.StatusCode}");
            return response;
        }

        /// <summary>
        /// Sends an HTTP GET request and deserializes the JSON response to the specified type.
        /// </summary>
        /// <typeparam name="T">The type to deserialize to.</typeparam>
        /// <param name="endpoint">The API endpoint.</param>
        /// <param name="cancellationToken">Token to cancel the request.</param>
        /// <returns>The deserialized object, or default if the request fails.</returns>
        public async Task<T?> GetJsonAsync<T>(string endpoint, CancellationToken cancellationToken = default)
        {
            var response = await GetAsync(endpoint, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] GET {endpoint} failed: {(int)response.StatusCode}");
                return default;
            }
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(json);
        }

        /// <summary>
        /// Sends an HTTP POST request with JSON body and deserializes the response.
        /// </summary>
        /// <typeparam name="T">The type to deserialize to.</typeparam>
        /// <param name="endpoint">The API endpoint.</param>
        /// <param name="body">The object to serialize as JSON body.</param>
        /// <param name="cancellationToken">Token to cancel the request.</param>
        /// <returns>The deserialized object, or default if the request fails.</returns>
        public async Task<T?> PostJsonAsync<T>(string endpoint, object? body, CancellationToken cancellationToken = default)
        {
            var response = await PostAsync(endpoint, body, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] POST {endpoint} failed: {(int)response.StatusCode}");
                return default;
            }
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(json);
        }

        /// <summary>
        /// Sends an HTTP GET request and returns the response as a JsonDocument.
        /// </summary>
        /// <param name="endpoint">The API endpoint.</param>
        /// <param name="cancellationToken">Token to cancel the request.</param>
        /// <returns>The parsed JsonDocument, or null if the request fails.</returns>
        public async Task<JsonDocument?> GetJsonAsync(string endpoint, CancellationToken cancellationToken = default)
        {
            var response = await GetAsync(endpoint, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] GET {endpoint} failed: {(int)response.StatusCode}");
                return null;
            }
            var json = await response.Content.ReadAsStringAsync();
            return JsonDocument.Parse(json);
        }

        /// <summary>
        /// Sends an HTTP POST request and returns the response as a JsonDocument.
        /// </summary>
        /// <param name="endpoint">The API endpoint.</param>
        /// <param name="body">The object to serialize as JSON body.</param>
        /// <param name="cancellationToken">Token to cancel the request.</param>
        /// <returns>The parsed JsonDocument, or null if the request fails.</returns>
        public async Task<JsonDocument?> PostJsonAsync(string endpoint, object? body, CancellationToken cancellationToken = default)
        {
            var response = await PostAsync(endpoint, body, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] POST {endpoint} failed: {(int)response.StatusCode}");
                return null;
            }
            var json = await response.Content.ReadAsStringAsync();
            return JsonDocument.Parse(json);
        }

        /// <summary>
        /// Checks if the client is currently connected.
        /// </summary>
        /// <returns>True if connected, false otherwise.</returns>
        public bool IsConnected() => _state == ConnectionState.Connected;

        /// <summary>
        /// Disposes of the HttpClient and releases resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _httpClient.Dispose();
        }
    }

    public static class JsonElementExtensions
    {
        /// <summary>
        /// Extension method that returns a default value if the element is null.
        /// </summary>
        /// <param name="element">The optional JsonElement.</param>
        /// <param name="defaultValue">The default value to return if element is null.</param>
        /// <returns>The element or the default value.</returns>
        public static JsonElement OrElse(this JsonElement? element, JsonElement defaultValue)
        {
            return element ?? defaultValue;
        }
    }
}
