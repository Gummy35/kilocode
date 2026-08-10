using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KiloVisualStudioExtension.ApiClient;

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

        public async Task InitializeAsync(KiloApiClient? nswagClient)
        {
            if (_initialized) return;

            lock (_lock)
            {
                if (_initialized) return;
            }

            var client = nswagClient ?? throw new InvalidOperationException("No NSwag client provided");

            try
            {
                var configTask = client.Global_config_getAsync();
                var providersTask = client.Provider_listAsync(System.Environment.CurrentDirectory, "");
                var agentsTask = client.App_agentsAsync(System.Environment.CurrentDirectory, "");
                var mcpTask = client.Mcp_statusAsync(System.Environment.CurrentDirectory, "");
                var sessionsTask = client.Session_listAsync(System.Environment.CurrentDirectory, "", null, "", null, null, null, null);

                await Task.WhenAll(configTask, providersTask, agentsTask, mcpTask, sessionsTask);

                lock (_lock)
                {
                    _config = JsonSerializer.SerializeToElement(configTask.Result);
                    _providers = JsonSerializer.SerializeToElement(providersTask.Result);
                    _agents = agentsTask.Result?.Select(a => a.Name ?? "").ToArray();
                    _mcpStatus = JsonSerializer.SerializeToElement(mcpTask.Result);
                    _sessions = sessionsTask.Result?.Select(s => JsonSerializer.SerializeToElement(s)).ToArray();
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
