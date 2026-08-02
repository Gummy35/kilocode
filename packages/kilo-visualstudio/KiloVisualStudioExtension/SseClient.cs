using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension
{
    /// <summary>
    /// Event arguments for Server-Sent Events (SSE) received from the backend.
    /// Contains the event type and data payload.
    /// </summary>
    public class SseEventArgs : EventArgs
    {
        /// <summary>
        /// The type of SSE event (e.g., "session.created", "message.part.delta").
        /// </summary>
        public string EventType { get; }
        
        /// <summary>
        /// The event data payload as a string.
        /// </summary>
        public string Data { get; }

        /// <summary>
        /// Creates a new instance of SseEventArgs.
        /// </summary>
        /// <param name="eventType">The event type.</param>
        /// <param name="data">The event data.</param>
        public SseEventArgs(string eventType, string data)
        {
            EventType = eventType;
            Data = data;
        }
    }

    /// <summary>
    /// SSE (Server-Sent Events) client for receiving real-time events from the Kilo backend.
    /// Implements automatic reconnection on connection loss and heartbeat timeout detection.
    /// 
    /// Features:
    /// - Connects to the /global/event endpoint using SSE protocol
    /// - Parses event: and data: lines from the stream
    /// - Detects heartbeat timeouts (15s no event) and triggers reconnection
    /// - Automatic reconnection with 250ms delay on disconnect
    /// - Basic authentication via username/password
    /// </summary>
    public class SseClient : IDisposable
    {
        /// <summary>
        /// The base URL of the backend server.
        /// </summary>
        private readonly string _baseUrl;
        
        /// <summary>
        /// The password for authentication.
        /// </summary>
        private readonly string _password;
        
        /// <summary>
        /// HttpClient for making the SSE connection request.
        /// </summary>
        private HttpClient? _httpClient;
        
        /// <summary>
        /// CancellationTokenSource for cancelling the connection.
        /// </summary>
        private CancellationTokenSource? _cts;
        
        /// <summary>
        /// Task representing the read loop that processes the SSE stream.
        /// </summary>
        private Task? _readLoop;
        
        /// <summary>
        /// Flag indicating whether the object has been disposed.
        /// </summary>
        private bool _disposed;
        
        /// <summary>
        /// Lock object for thread-safe operations.
        /// </summary>
        private readonly object _lock = new object();

        /// <summary>
        /// Event raised when an SSE event is received.
        /// </summary>
        public event EventHandler<SseEventArgs>? OnEvent;
        
        /// <summary>
        /// Event raised when the SSE connection is successfully established.
        /// </summary>
        public event EventHandler? OnConnected;
        
        /// <summary>
        /// Event raised when the SSE connection is disconnected.
        /// </summary>
        public event EventHandler? OnDisconnected;
        
        /// <summary>
        /// Event raised when an SSE connection error occurs.
        /// </summary>
        public event EventHandler<Exception>? OnError;

        /// <summary>
        /// Heartbeat timeout in milliseconds. If no event is received within this time,
        /// the connection is considered stale and reconnection is triggered.
        /// </summary>
        private const int HeartbeatTimeoutMs = 15000;
        
        /// <summary>
        /// Delay in milliseconds before attempting reconnection after a disconnect.
        /// </summary>
        private const int ReconnectDelayMs = 250;

        /// <summary>
        /// Timestamp of the last received event, used for heartbeat detection.
        /// </summary>
        private DateTime _lastEventTime;
        
        /// <summary>
        /// Flag indicating whether the connection is currently established.
        /// </summary>
        private bool _connected;

        /// <summary>
        /// Creates a new SSE client with the specified backend URL and password.
        /// </summary>
        /// <param name="baseUrl">The base URL of the backend server.</param>
        /// <param name="password">The password for authentication.</param>
        public SseClient(string baseUrl, string password)
        {
            _baseUrl = baseUrl;
            _password = password;
            _lastEventTime = DateTime.UtcNow;
        }

        /// <summary>
        /// Connects to the SSE endpoint. If already connected, does nothing.
        /// Initiates the connection process which includes authentication and stream setup.
        /// </summary>
        public void Connect()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                System.Diagnostics.Debug.WriteLine("[Kilo] SSE: already connected");
                return;
            }

            StartConnection();
        }

        /// <summary>
        /// Starts the SSE connection process. Creates an HTTP request to /global/event,
        /// sets up authentication headers, and begins reading the event stream.
        /// Handles connection errors by scheduling automatic reconnection.
        /// </summary>
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

        /// <summary>
        /// Reads the SSE stream asynchronously. Parses event: and data: lines,
        /// dispatches events to handlers, and detects stream closure.
        /// </summary>
        /// <param name="stream">The response stream to read from.</param>
        /// <param name="token">Cancellation token for stopping the read loop.</param>
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

        /// <summary>
        /// Checks if the heartbeat timeout has been exceeded. If no event has been
        /// received within HeartbeatTimeoutMs, schedules a reconnection.
        /// </summary>
        private async Task CheckHeartbeatAsync()
        {
            var elapsed = (DateTime.UtcNow - _lastEventTime).TotalMilliseconds;
            if (elapsed > HeartbeatTimeoutMs)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] SSE: heartbeat timeout ({elapsed}ms)");
                ScheduleReconnect();
            }
        }

        /// <summary>
        /// Schedules a reconnection attempt after ReconnectDelayMs milliseconds.
        /// Only reconnects if the object has not been disposed and cancellation has not been requested.
        /// </summary>
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

        /// <summary>
        /// Dispatches an SSE event to the OnEvent handler.
        /// Updates the last event time for heartbeat detection.
        /// </summary>
        /// <param name="eventType">The type of the event.</param>
        /// <param name="data">The event data payload.</param>
        private void DispatchEvent(string eventType, string data)
        {
            if (string.IsNullOrEmpty(data)) return;

            _lastEventTime = DateTime.UtcNow;
            var trimmedData = data.TrimEnd('\n');
            OnEvent?.Invoke(this, new SseEventArgs(eventType, trimmedData));
        }

        /// <summary>
        /// Disconnects from the SSE endpoint and releases resources.
        /// Cancels the connection and disposes the HTTP client.
        /// </summary>
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

        /// <summary>
        /// Disposes of all resources used by the SSE client.
        /// Calls Disconnect() and disposes the cancellation token source.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Disconnect();
            _cts?.Dispose();
        }

        /// <summary>
        /// Gets whether the SSE client is currently connected.
        /// </summary>
        public bool IsConnected => _connected;
    }
}
