using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension
{
    public class SseEventArgs : EventArgs
    {
        public string EventType { get; }
        public string Data { get; }

        public SseEventArgs(string eventType, string data)
        {
            EventType = eventType;
            Data = data;
        }
    }

    public class SseClient : IDisposable
    {
        private readonly string _baseUrl;
        private readonly string _password;
        private HttpClient? _httpClient;
        private CancellationTokenSource? _cts;
        private Task? _readLoop;
        private bool _disposed;
        private readonly object _lock = new object();

        public event EventHandler<SseEventArgs>? OnEvent;
        public event EventHandler? OnConnected;
        public event EventHandler? OnDisconnected;
        public event EventHandler<Exception>? OnError;

        private const int HeartbeatTimeoutMs = 15000;
        private const int ReconnectDelayMs = 250;

        private DateTime _lastEventTime;
        private bool _connected;

        public SseClient(string baseUrl, string password)
        {
            _baseUrl = baseUrl;
            _password = password;
            _lastEventTime = DateTime.UtcNow;
        }

        public void Connect()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                System.Diagnostics.Debug.WriteLine("[Kilo] SSE: already connected");
                return;
            }

            StartConnection();
        }

        private async void StartConnection()
        {
            if (_disposed) return;

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            try
            {
                _httpClient = new HttpClient
                {
                    Timeout = TimeSpan.FromMilliseconds(Timeout.Infinite)
                };
                var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"kilo:{_password}"));
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
                _httpClient.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("text/event-stream"));

                var url = $"{_baseUrl}/global/event";
                System.Diagnostics.Debug.WriteLine($"[Kilo] SSE: connecting to {url}");

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"SSE connection failed with status {(int)response.StatusCode}");
                }

                lock (_lock)
                {
                    if (_disposed)
                    {
                        response.Dispose();
                        return;
                    }
                    _connected = true;
                    _lastEventTime = DateTime.UtcNow;
                }

                OnConnected?.Invoke(this, EventArgs.Empty);
                System.Diagnostics.Debug.WriteLine("[Kilo] SSE: connected");

                using var stream = await response.Content.ReadAsStreamAsync();
                await ReadStreamAsync(stream, token);
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("[Kilo] SSE: connection cancelled");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] SSE: connection error - {ex.Message}");
                OnError?.Invoke(this, ex);
                ScheduleReconnect();
            }
        }

        private async Task ReadStreamAsync(Stream stream, CancellationToken token)
        {
            using var reader = new System.IO.StreamReader(stream, Encoding.UTF8, true, 8192, true);

            while (!token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (line == null)
                {
                    System.Diagnostics.Debug.WriteLine("[Kilo] SSE: stream closed");
                    break;
                }

                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                var eventType = "";
                var data = "";

                if (line.StartsWith("event: "))
                {
                    eventType = line.Substring("event: ".Length).Trim();
                }
                else if (line.StartsWith("data: "))
                {
                    data = line.Substring("data: ".Length) + "\n";
                }

                if (!string.IsNullOrEmpty(eventType) || !string.IsNullOrEmpty(data))
                {
                    var nextLine = await reader.ReadLineAsync();
                    while (nextLine != null && !string.IsNullOrEmpty(nextLine))
                    {
                        if (nextLine.StartsWith("data: "))
                        {
                            data += nextLine.Substring("data: ".Length) + "\n";
                        }
                        nextLine = await reader.ReadLineAsync();
                    }
                    DispatchEvent(eventType, data);
                }
            }

            lock (_lock)
            {
                _connected = false;
            }
            OnDisconnected?.Invoke(this, EventArgs.Empty);
        }

        private async Task CheckHeartbeatAsync()
        {
            var elapsed = (DateTime.UtcNow - _lastEventTime).TotalMilliseconds;
            if (elapsed > HeartbeatTimeoutMs)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] SSE: heartbeat timeout ({elapsed}ms)");
                ScheduleReconnect();
            }
        }

        private void ScheduleReconnect()
        {
            if (_disposed) return;

            Task.Delay(ReconnectDelayMs).ContinueWith(_ =>
            {
                if (!_disposed && _cts != null && !_cts.IsCancellationRequested)
                {
                    StartConnection();
                }
            });
        }

        private void DispatchEvent(string eventType, string data)
        {
            if (string.IsNullOrEmpty(data)) return;

            _lastEventTime = DateTime.UtcNow;
            var trimmedData = data.TrimEnd('\n');

            System.Diagnostics.Debug.WriteLine($"[Kilo] SSE: event={eventType}");
            OnEvent?.Invoke(this, new SseEventArgs(eventType, trimmedData));
        }

        public void Disconnect()
        {
            _cts?.Cancel();
            _httpClient?.Dispose();
            _httpClient = null;
            lock (_lock)
            {
                _connected = false;
            }
            OnDisconnected?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Disconnect();
            _cts?.Dispose();
        }

        public bool IsConnected => _connected;
    }
}
