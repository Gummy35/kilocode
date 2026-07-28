using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace KiloVisualStudioExtension
{
    public class StateStorage
    {
        private static StateStorage? _instance;
        private readonly string _stateFile;
        private readonly object _lock = new object();
        private Dictionary<string, object> _state = new Dictionary<string, object>();

        public static StateStorage Instance
        {
            get
            {
                if (_instance == null)
                {
                    var appData = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "KiloCode");
                    
                    if (!Directory.Exists(appData))
                    {
                        Directory.CreateDirectory(appData);
                    }

                    _instance = new StateStorage(Path.Combine(appData, "state.json"));
                }
                return _instance;
            }
        }

        private StateStorage(string stateFile)
        {
            _stateFile = stateFile;
            Load();
        }

        private void Load()
        {
            lock (_lock)
            {
                if (File.Exists(_stateFile))
                {
                    try
                    {
                        var json = File.ReadAllText(_stateFile);
                        _state = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Kilo] StateStorage load error: {ex.Message}");
                        _state = new Dictionary<string, object>();
                    }
                }
                else
                {
                    _state = new Dictionary<string, object>();
                }
            }
        }

        private void Save()
        {
            lock (_lock)
            {
                try
                {
                    var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_stateFile, json);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Kilo] StateStorage save error: {ex.Message}");
                }
            }
        }

        public T? Get<T>(string key)
        {
            lock (_lock)
            {
                if (_state.TryGetValue(key, out var value))
                {
                    var json = JsonSerializer.Serialize(value);
                    return JsonSerializer.Deserialize<T>(json);
                }
                return default;
            }
        }

        public void Set<T>(string key, T value)
        {
            lock (_lock)
            {
                _state[key] = value ?? new object();
                Save();
            }
        }

        public void Remove(string key)
        {
            lock (_lock)
            {
                _state.Remove(key);
                Save();
            }
        }
    }

    public class FavoriteModel
    {
        public string providerID { get; set; } = "";
        public string modelID { get; set; } = "";
        public string label { get; set; } = "";
    }

    public class ModelSelection
    {
        public string providerID { get; set; } = "";
        public string modelID { get; set; } = "";
    }

    public class Notification
    {
        public string id { get; set; } = "";
        public string type { get; set; } = "";
        public string title { get; set; } = "";
        public string message { get; set; } = "";
        public long timestamp { get; set; }
        public string[] actions { get; set; } = Array.Empty<string>();
    }
}
