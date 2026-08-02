using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;

namespace KiloVisualStudioExtension
{
    /// <summary>
    /// Represents the connection state of the Kilo backend service.
    /// </summary>
    public enum ConnectionState
    {
        /// <summary>
        /// Not connected to the backend.
        /// </summary>
        Disconnected,
        
        /// <summary>
        /// Connection in progress.
        /// </summary>
        Connecting,
        
        /// <summary>
        /// Successfully connected to the backend.
        /// </summary>
        Connected,
        
        /// <summary>
        /// Connection error occurred.
        /// </summary>
        Error
    }

    /// <summary>
    /// Event arguments for connection state changes.
    /// Contains the new state and optional error message.
    /// </summary>
    public class ConnectionStateEventArgs : EventArgs
    {
        /// <summary>
        /// The new connection state.
        /// </summary>
        public ConnectionState State { get; }
        
        /// <summary>
        /// Optional error message if the state is Error.
        /// </summary>
        public string? ErrorMessage { get; }

        /// <summary>
        /// Creates a new instance of ConnectionStateEventArgs.
        /// </summary>
        /// <param name="state">The new connection state.</param>
        /// <param name="errorMessage">Optional error message.</param>
        public ConnectionStateEventArgs(ConnectionState state, string? errorMessage = null)
        {
            State = state;
            ErrorMessage = errorMessage;
        }
    }

    /// <summary>
    /// Event arguments for SSE (Server-Sent Events) received from the backend.
    /// </summary>
    public class SseEventReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// The type of SSE event (e.g., "session.created", "message.part.delta").
        /// </summary>
        public string EventType { get; }
        
        /// <summary>
        /// The event data payload as a JSON string.
        /// </summary>
        public string Data { get; }

        /// <summary>
        /// Creates a new instance of SseEventReceivedEventArgs.
        /// </summary>
        /// <param name="eventType">The event type.</param>
        /// <param name="data">The event data.</param>
        public SseEventReceivedEventArgs(string eventType, string data)
        {
            EventType = eventType;
            Data = data;
        }
    }

    /// <summary>
    /// Shared connection service for managing the CLI backend connection.
    /// Implements the singleton pattern - one instance shared across all providers.
    /// Matches the VS Code extension's KiloConnectionService pattern.
    /// 
    /// Responsibilities:
    /// - Manages the lifecycle of the CLI backend process via CliBackendManager
    /// - Creates and manages HTTP client for REST API calls
    /// - Creates and manages SSE client for real-time event streaming
    /// - Polls health endpoint to detect backend failures
    /// - Provides connection state changes to subscribers
    /// - Handles reconnection on errors
    /// 
    /// The service uses lazy startup - the backend only starts when ConnectAsync()
    /// is first called, reducing extension startup time.
    /// </summary>
    public class KiloConnectionService : IDisposable
    {
        /// <summary>
        /// Manages the CLI backend process.
        /// </summary>
        private readonly CliBackendManager _backendManager;
        
        /// <summary>
        /// HTTP client wrapper for REST API calls.
        /// </summary>
        private HttpClientWrapper? _httpClient;
        
        /// <summary>
        /// Cached HTTP client with 10s TTL for reducing API calls.
        /// </summary>
        private CachedHttpClient? _cachedHttpClient;
        
        /// <summary>
        /// SSE client for receiving real-time events from the backend.
        /// </summary>
        private SseClient? _sseClient;
        
        /// <summary>
        /// Current connection state.
        /// </summary>
        private ConnectionState _state = ConnectionState.Disconnected;
        
        /// <summary>
        /// Base URL of the backend server (e.g., "http://127.0.0.1:9999").
        /// </summary>
        private string? _baseUrl;
        
        /// <summary>
        /// Password extracted from the base URL for authentication.
        /// </summary>
        private string? _password;
        
        /// <summary>
        /// Timer for periodic health checks.
        /// </summary>
        private Timer? _healthPollTimer;
        
        /// <summary>
        /// Flag indicating whether the object has been disposed.
        /// </summary>
        private bool _disposed;
        
        /// <summary>
        /// Lock object for thread-safe operations.
        /// </summary>
        private readonly object _lock = new object();

        /// <summary>
        /// Singleton instance - set by KiloVisualStudioExtensionPackage.
        /// Access via GetInstance() and SetInstance() methods.
        /// </summary>
        private static KiloConnectionService? _singletonInstance;

        /// <summary>
        /// Event raised when the connection state changes.
        /// </summary>
        public event EventHandler<ConnectionStateEventArgs>? OnStateChange;
        
        /// <summary>
        /// Event raised when an SSE event is received from the backend.
        /// </summary>
        public event EventHandler<SseEventReceivedEventArgs>? OnSseEvent;

        /// <summary>
        /// Gets the current connection state.
        /// </summary>
        public ConnectionState State => _state;
        
        /// <summary>
        /// Gets the base URL of the backend server.
        /// Returns null if not connected.
        /// </summary>
        public string? BaseUrl => _baseUrl;
        
        /// <summary>
        /// Gets the password used for authentication.
        /// Returns null if not connected.
        /// </summary>
        public string? Password => _password;

        /// <summary>
        /// Server information containing the port number.
        /// Matches VS Code's { port: number } structure.
        /// </summary>
        public class ServerInfo
        {
            /// <summary>
            /// The port number the backend is listening on (lowercase to match VS Code).
            /// </summary>
            public int port { get; set; }
        }

        /// <summary>
        /// Gets server information if connected.
        /// </summary>
        /// <returns>ServerInfo with port number, or null if not connected.</returns>
        public ServerInfo? GetServerInfo()
        {
            if (_state != ConnectionState.Connected)
                return null;
            var port = _backendManager.GetPort();
            if (port == null)
                return null;
            return new ServerInfo { port = port.Value };
        }

        /// <summary>
        /// Server configuration containing base URL and password.
        /// </summary>
        public class ServerConfig
        {
            /// <summary>
            /// The base URL of the backend server.
            /// </summary>
            public string BaseUrl { get; set; } = "";
            
            /// <summary>
            /// The password for authentication.
            /// </summary>
            public string Password { get; set; } = "";
        }

        /// <summary>
        /// Gets the server configuration if connected.
        /// </summary>
        /// <returns>ServerConfig with URL and password, or null if not connected.</returns>
        public ServerConfig? GetServerConfig()
        {
            if (_state != ConnectionState.Connected || string.IsNullOrEmpty(_baseUrl))
                return null;
            return new ServerConfig { BaseUrl = _baseUrl, Password = _password ?? "" };
        }

        /// <summary>
        /// Get the singleton instance of KiloConnectionService.
        /// Returns null if not initialized yet.
        /// </summary>
        /// <returns>The singleton instance, or null if not initialized.</returns>
        public static KiloConnectionService? GetInstance()
        {
            return _singletonInstance;
        }

        /// <summary>
        /// Set the singleton instance (called from package initialization).
        /// Only the first call takes effect; subsequent calls are ignored.
        /// </summary>
        /// <param name="instance">The KiloConnectionService instance to set as singleton.</param>
        public static void SetInstance(KiloConnectionService instance)
        {
            if (_singletonInstance != null)
            {
                System.Diagnostics.Debug.WriteLine("[Kilo] KiloConnectionService: singleton already set, ignoring");
                return;
            }
            _singletonInstance = instance;
        }

        /// <summary>
        /// Creates a new instance of KiloConnectionService.
        /// </summary>
        /// <param name="backendManager">The CLI backend manager instance.</param>
        public KiloConnectionService(CliBackendManager backendManager)
        {
            _backendManager = backendManager;
        }

        /// <summary>
        /// Asynchronously connects to the CLI backend.
        /// Starts the backend process if not already running, creates HTTP and SSE clients,
        /// and begins health polling. Idempotent - subsequent calls are ignored if already connected.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the connection attempt.</param>
        /// <returns>A task representing the asynchronous connection operation.</returns>
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (_state == ConnectionState.Connected)
            {
                System.Diagnostics.Debug.WriteLine("[Kilo] ConnectionService: already connected");
                return;
            }

            if (_state == ConnectionState.Connecting)
            {
                System.Diagnostics.Debug.WriteLine("[Kilo] ConnectionService: connection already in progress");
                return;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            SetState(ConnectionState.Connecting);

            try
            {
                await _backendManager.StartAsync(cancellationToken);

                _baseUrl = _backendManager.BaseUrl;
                if (string.IsNullOrEmpty(_baseUrl))
                {
                    throw new Exception("Backend manager did not provide a valid base URL");
                }

                var password = ExtractPasswordFromUrl(_baseUrl);
                _password = password;

                System.Diagnostics.Debug.WriteLine($"[Kilo] ConnectionService: creating HTTP client for {_baseUrl}");
                _httpClient = new HttpClientWrapper(_baseUrl, password);
                _cachedHttpClient = new CachedHttpClient(_httpClient);

                System.Diagnostics.Debug.WriteLine("[Kilo] ConnectionService: creating SSE client");
                _sseClient = new SseClient(_baseUrl, password);
                _sseClient.OnConnected += SseClient_OnConnected;
                _sseClient.OnDisconnected += SseClient_OnDisconnected;
                _sseClient.OnError += SseClient_OnError;
                _sseClient.OnEvent += SseClient_OnEvent;

                System.Diagnostics.Debug.WriteLine("[Kilo] ConnectionService: connecting SSE");
                _sseClient.Connect();

                StartHealthPoll();

                SetState(ConnectionState.Connected);
                System.Diagnostics.Debug.WriteLine("[Kilo] ConnectionService: connected successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] ConnectionService: connection failed - {ex.Message}");
                SetState(ConnectionState.Error, ex.Message);
            }
        }

        /// <summary>
        /// Extracts the password from the base URL's user info section.
        /// The URL format is expected to be "http://user:password@host:port".
        /// </summary>
        /// <param name="baseUrl">The base URL containing authentication info.</param>
        /// <returns>The extracted password, or a default value if not found.</returns>
        private string ExtractPasswordFromUrl(string baseUrl)
        {
            var uri = new Uri(baseUrl);
            var userInfo = uri.UserInfo;
            return userInfo ?? "default-password";
        }

        /// <summary>
        /// Starts the health polling timer that checks backend connectivity every 10 seconds.
        /// If the health check fails, forces an SSE reconnection.
        /// </summary>
        private void StartHealthPoll()
        {
            StopHealthPoll();
            _healthPollTimer = new Timer(async _ =>
            {
                if (_state != ConnectionState.Connected) return;

                var healthy = await CheckHealthAsync();
                if (!healthy)
                {
                    System.Diagnostics.Debug.WriteLine("[Kilo] ConnectionService: health check failed, forcing reconnect");
                    if (_sseClient != null)
                    {
                        _sseClient.Disconnect();
                        _sseClient.Connect();
                    }
                }
            }, null, 10000, 10000);
        }

        /// <summary>
        /// Stops the health polling timer.
        /// </summary>
        private void StopHealthPoll()
        {
            _healthPollTimer?.Dispose();
            _healthPollTimer = null;
        }

        /// <summary>
        /// Checks the health of the backend by calling the /global/health endpoint.
        /// </summary>
        /// <returns>True if the backend is healthy, false otherwise.</returns>
        private async Task<bool> CheckHealthAsync()
        {
            if (_httpClient == null) return false;

            try
            {
                var response = await _httpClient.GetAsync("/global/health");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Event handler for SSE connected event. Updates connection state.
        /// </summary>
        private void SseClient_OnConnected(object? sender, EventArgs e)
        {
            SetState(ConnectionState.Connected);
        }

        /// <summary>
        /// Event handler for SSE disconnected event. Updates connection state if was connected.
        /// </summary>
        private void SseClient_OnDisconnected(object? sender, EventArgs e)
        {
            if (_state == ConnectionState.Connected)
            {
                SetState(ConnectionState.Disconnected);
            }
        }

        /// <summary>
        /// Event handler for SSE error event. Logs error and updates connection state to Error.
        /// </summary>
        private void SseClient_OnError(object? sender, Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Kilo] ConnectionService: SSE error - {ex.Message}");
            SetState(ConnectionState.Error, ex.Message);
        }

        /// <summary>
        /// Event handler for SSE event received. Raises the OnSseEvent event with the event data.
        /// </summary>
        private void SseClient_OnEvent(object? sender, SseEventArgs e)
        {
            OnSseEvent?.Invoke(this, new SseEventReceivedEventArgs(e.EventType, e.Data));
        }

        /// <summary>
        /// Sets the connection state and raises the OnStateChange event.
        /// </summary>
        /// <param name="newState">The new connection state.</param>
        /// <param name="errorMessage">Optional error message for Error state.</param>
        private void SetState(ConnectionState newState, string? errorMessage = null)
        {
            if (_state == newState) return;

            _state = newState;
            System.Diagnostics.Debug.WriteLine($"[Kilo] ConnectionService: state changed to {newState}");
            OnStateChange?.Invoke(this, new ConnectionStateEventArgs(newState, errorMessage));
        }

        /// <summary>
        /// Gets the HTTP client for making REST API calls.
        /// </summary>
        /// <returns>The HttpClientWrapper instance.</returns>
        /// <exception cref="InvalidOperationException">Thrown if not connected.</exception>
        public HttpClientWrapper? GetHttpClient()
        {
            if (_state != ConnectionState.Connected)
            {
                throw new InvalidOperationException("Not connected. Call ConnectAsync() first.");
            }
            return _httpClient;
        }

        /// <summary>
        /// Gets the cached HTTP client for making REST API calls with 10s TTL caching.
        /// </summary>
        /// <returns>The CachedHttpClient instance.</returns>
        /// <exception cref="InvalidOperationException">Thrown if not connected.</exception>
        public CachedHttpClient? GetCachedHttpClient()
        {
            if (_state != ConnectionState.Connected)
            {
                throw new InvalidOperationException("Not connected. Call ConnectAsync() first.");
            }
            return _cachedHttpClient;
        }

        /// <summary>
        /// Disconnects from the backend and cleans up all resources.
        /// Stops health polling, disconnects SSE, and disposes HTTP clients.
        /// </summary>
        public void Disconnect()
        {
            StopHealthPoll();
            _sseClient?.Disconnect();
            _cachedHttpClient?.Dispose();
            _httpClient?.Dispose();
            _httpClient = null;
            _cachedHttpClient = null;
            _sseClient = null;
            SetState(ConnectionState.Disconnected);
        }

        /// <summary>
        /// Disposes of all resources used by the connection service.
        /// Calls Disconnect() and clears the singleton instance.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Disconnect();
            _healthPollTimer?.Dispose();
            
            // Clear singleton instance on disposal
            if (_singletonInstance == this)
            {
                _singletonInstance = null;
            }
        }
    }
}
