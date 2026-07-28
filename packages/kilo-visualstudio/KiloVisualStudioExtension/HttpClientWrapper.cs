using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension
{
    public class HttpClientWrapper : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _password;
        private bool _disposed;

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
        }

        public async Task<HttpResponseMessage> GetAsync(string endpoint, CancellationToken cancellationToken = default)
        {
            var url = $"{_baseUrl}{endpoint}";
            System.Diagnostics.Debug.WriteLine($"[Kilo] HTTP GET: {url}");
            return await _httpClient.GetAsync(endpoint, cancellationToken);
        }

        public async Task<HttpResponseMessage> PostAsync(string endpoint, object? body, CancellationToken cancellationToken = default)
        {
            var url = $"{_baseUrl}{endpoint}";
            var content = body == null ? null : new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            System.Diagnostics.Debug.WriteLine($"[Kilo] HTTP POST: {url}");
            var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
            System.Diagnostics.Debug.WriteLine($"[Kilo] HTTP POST response: {(int)response.StatusCode}");
            return response;
        }

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

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _httpClient.Dispose();
        }
    }
}
