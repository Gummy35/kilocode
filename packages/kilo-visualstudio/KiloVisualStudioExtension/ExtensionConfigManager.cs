using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension
{
    /// <summary>
    /// Extension configuration manager that keeps extension, CLI, and webview in sync.
    /// Loads config once from CLI and updates via SSE events.
    /// </summary>
    public class ExtensionConfigManager
    {
        private JsonElement? _config;
        private JsonElement? _providers;
        private string[]? _agents;
        private JsonElement? _mcpStatus;
        private JsonElement[]? _sessions;
        private readonly object _lock = new object();
        private bool _initialized;

        public bool IsInitialized => _initialized;

        public async Task InitializeAsync(HttpClientWrapper? httpClient)
        {
            if (_initialized) return;

            lock (_lock)
            {
                if (_initialized) return;
            }

            var client = httpClient ?? throw new InvalidOperationException("No HTTP client provided");

            try
            {
                var configTask = client.GetJsonAsync<JsonElement>("/config");
                var providersTask = client.GetJsonAsync<JsonElement>("/provider");
                var agentsTask = client.GetJsonAsync<string[]>("/experimental/tool/ids");
                var mcpTask = client.GetJsonAsync<JsonElement>("/mcp");
                var sessionsTask = client.GetJsonAsync<JsonElement[]>("/session");

                await Task.WhenAll(configTask, providersTask, agentsTask, mcpTask, sessionsTask);

                lock (_lock)
                {
                    _config = configTask.Result;
                    _providers = providersTask.Result;
                    _agents = agentsTask.Result;
                    _mcpStatus = mcpTask.Result;
                    _sessions = sessionsTask.Result;
                    _initialized = true;
                }

                System.Diagnostics.Debug.WriteLine("[Kilo] ConfigManager: initialized");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] ConfigManager init error: {ex.Message}");
                throw;
            }
        }

        public void UpdateFromSse(string eventType, string data)
        {
            // Handle SSE events that update config
            // For now, just log - can be extended to parse and update specific fields
            System.Diagnostics.Debug.WriteLine($"[Kilo] ConfigManager: SSE event {eventType}");
            
            // TODO: Parse data and update relevant config fields based on event type
            // e.g., "project.updated" -> update config, "indexing.status" -> update indexing status
        }

        public JsonElement? GetConfig()
        {
            lock (_lock)
            {
                return _config;
            }
        }

        public JsonElement? GetProviders()
        {
            lock (_lock)
            {
                return _providers;
            }
        }

        public string[]? GetAgents()
        {
            lock (_lock)
            {
                return _agents;
            }
        }

        public JsonElement? GetMcpStatus()
        {
            lock (_lock)
            {
                return _mcpStatus;
            }
        }

        public JsonElement[]? GetSessions()
        {
            lock (_lock)
            {
                return _sessions;
            }
        }
    }
}
