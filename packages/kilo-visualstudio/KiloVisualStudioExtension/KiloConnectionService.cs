using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;

namespace KiloVisualStudioExtension
{
    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Error
    }

    public class ConnectionStateEventArgs : EventArgs
    {
        public ConnectionState State { get; }
        public string? ErrorMessage { get; }

        public ConnectionStateEventArgs(ConnectionState state, string? errorMessage = null)
        {
            State = state;
            ErrorMessage = errorMessage;
        }
    }

    public class SseEventReceivedEventArgs : EventArgs
    {
        public string EventType { get; }
        public string Data { get; }

        public SseEventReceivedEventArgs(string eventType, string data)
        {
            EventType = eventType;
            Data = data;
        }
    }

    /// <summary>
    /// Shared connection service for managing CLI backend connection.
    /// Singleton pattern - one instance shared across all providers.
    /// Matches VS Code's KiloConnectionService pattern.
    /// </summary>
    public class KiloConnectionService : IDisposable
    {
        private readonly CliBackendManager _backendManager;
        private HttpClientWrapper? _httpClient;
        private CachedHttpClient? _cachedHttpClient;
        private SseClient? _sseClient;
        private ConnectionState _state = ConnectionState.Disconnected;
        private string? _baseUrl;
        private string? _password;
        private Timer? _healthPollTimer;
        private bool _disposed;
        private readonly object _lock = new object();

        /// <summary>
        /// Singleton instance - set by KiloVisualStudioExtensionPackage
        /// </summary>
        private static KiloConnectionService? _singletonInstance;

        public event EventHandler<ConnectionStateEventArgs>? OnStateChange;
        public event EventHandler<SseEventReceivedEventArgs>? OnSseEvent;

        public ConnectionState State => _state;
        public string? BaseUrl => _baseUrl;
        public string? Password => _password;

        public class ServerInfo
        {
            public int Port { get; set; }
        }

        public ServerInfo? GetServerInfo()
        {
            if (_state != ConnectionState.Connected)
                return null;
            var port = _backendManager.GetPort();
            if (port == null)
                return null;
            return new ServerInfo { Port = port.Value };
        }

        public ServerConfig? GetServerConfig()
        {
            if (_state != ConnectionState.Connected || string.IsNullOrEmpty(_baseUrl))
                return null;
            return new ServerConfig { BaseUrl = _baseUrl, Password = _password ?? "" };
        }

        public class ServerConfig
        {
            public string BaseUrl { get; set; } = "";
            public string Password { get; set; } = "";
        }

        /// <summary>
        /// Get the singleton instance of KiloConnectionService.
        /// Returns null if not initialized yet.
        /// </summary>
        public static KiloConnectionService? GetInstance()
        {
            return _singletonInstance;
        }

        /// <summary>
        /// Set the singleton instance (called from package initialization)
        /// </summary>
        public static void SetInstance(KiloConnectionService instance)
        {
            if (_singletonInstance != null)
            {
                System.Diagnostics.Debug.WriteLine("[Kilo] KiloConnectionService: singleton already set, ignoring");
                return;
            }
            _singletonInstance = instance;
        }

        public KiloConnectionService(CliBackendManager backendManager)
        {
            _backendManager = backendManager;
        }

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

        private string ExtractPasswordFromUrl(string baseUrl)
        {
            var uri = new Uri(baseUrl);
            var userInfo = uri.UserInfo;
            return userInfo ?? "default-password";
        }

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

        private void StopHealthPoll()
        {
            _healthPollTimer?.Dispose();
            _healthPollTimer = null;
        }

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

        private void SseClient_OnConnected(object? sender, EventArgs e)
        {
            SetState(ConnectionState.Connected);
        }

        private void SseClient_OnDisconnected(object? sender, EventArgs e)
        {
            if (_state == ConnectionState.Connected)
            {
                SetState(ConnectionState.Disconnected);
            }
        }

        private void SseClient_OnError(object? sender, Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Kilo] ConnectionService: SSE error - {ex.Message}");
            SetState(ConnectionState.Error, ex.Message);
        }

        private void SseClient_OnEvent(object? sender, SseEventArgs e)
        {
            OnSseEvent?.Invoke(this, new SseEventReceivedEventArgs(e.EventType, e.Data));
        }

        private void SetState(ConnectionState newState, string? errorMessage = null)
        {
            if (_state == newState) return;

            _state = newState;
            System.Diagnostics.Debug.WriteLine($"[Kilo] ConnectionService: state changed to {newState}");
            OnStateChange?.Invoke(this, new ConnectionStateEventArgs(newState, errorMessage));
        }

        public HttpClientWrapper? GetHttpClient()
        {
            if (_state != ConnectionState.Connected)
            {
                throw new InvalidOperationException("Not connected. Call ConnectAsync() first.");
            }
            return _httpClient;
        }

        public CachedHttpClient? GetCachedHttpClient()
        {
            if (_state != ConnectionState.Connected)
            {
                throw new InvalidOperationException("Not connected. Call ConnectAsync() first.");
            }
            return _cachedHttpClient;
        }

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
