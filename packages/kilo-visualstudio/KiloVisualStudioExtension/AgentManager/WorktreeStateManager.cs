using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.AgentManager
{
    /// <summary>
    /// Represents a worktree in the Agent Manager state.
    /// Matches VS Code's Worktree type from WorktreeStateManager.ts
    /// </summary>
    public class Worktree
    {
        public string Id { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public string? BaseBranch { get; set; }
        public string? PrNumber { get; set; }
        public string? PrUrl { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsOpen { get; set; }
    }

    /// <summary>
    /// Represents a session in the Agent Manager state.
    /// </summary>
    public class AgentManagerSession
    {
        public string Id { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? Directory { get; set; }
        public string? WorktreeId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastActiveAt { get; set; }
        public bool IsClosed { get; set; }
    }

    /// <summary>
    /// State file structure for Agent Manager.
    /// Stored in .kilo/agent-manager.json
    /// </summary>
    public class AgentManagerState
    {
        public List<Worktree> Worktrees { get; set; } = new List<Worktree>();
        public List<AgentManagerSession> Sessions { get; set; } = new List<AgentManagerSession>();
        public string? ActiveSessionId { get; set; }
        public string? ActiveWorktreeId { get; set; }
        public Dictionary<string, object> TabState { get; set; } = new Dictionary<string, object>();
        public int Version { get; set; } = 1;
    }

    /// <summary>
    /// Manages Agent Manager state persistence to .kilo/agent-manager.json.
    /// Matches VS Code's WorktreeStateManager.ts pattern.
    /// </summary>
    public class WorktreeStateManager : IDisposable
    {
        private readonly string _stateFilePath;
        private AgentManagerState _state;
        private readonly object _lock = new object();
        private bool _disposed;
        private readonly JsonSerializerOptions _jsonOptions;

        public WorktreeStateManager(string workspaceRoot)
        {
            var kiloDir = Path.Combine(workspaceRoot, ".kilo");
            if (!Directory.Exists(kiloDir))
            {
                Directory.CreateDirectory(kiloDir);
            }
            _stateFilePath = Path.Combine(kiloDir, "agent-manager.json");
            _state = LoadState();
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
        }

        /// <summary>
        /// Load state from disk, or return empty state if file doesn't exist.
        /// </summary>
        private AgentManagerState LoadState()
        {
            try
            {
                if (File.Exists(_stateFilePath))
                {
                    var json = File.ReadAllText(_stateFilePath);
                    return JsonSerializer.Deserialize<AgentManagerState>(json, _jsonOptions) ?? new AgentManagerState();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] WorktreeStateManager: failed to load state: {ex.Message}");
            }
            return new AgentManagerState();
        }

        /// <summary>
        /// Save state to disk.
        /// </summary>
        public void Save()
        {
            lock (_lock)
            {
                try
                {
                    var json = JsonSerializer.Serialize(_state, _jsonOptions);
                    File.WriteAllText(_stateFilePath, json);
                    System.Diagnostics.Debug.WriteLine($"[Kilo] WorktreeStateManager: state saved to {_stateFilePath}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Kilo] WorktreeStateManager: failed to save state: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Get all worktrees.
        /// </summary>
        public List<Worktree> GetWorktrees()
        {
            lock (_lock)
            {
                return new List<Worktree>(_state.Worktrees);
            }
        }

        /// <summary>
        /// Get a worktree by ID.
        /// </summary>
        public Worktree? GetWorktree(string id)
        {
            lock (_lock)
            {
                return _state.Worktrees.Find(w => w.Id == id);
            }
        }

        /// <summary>
        /// Add or update a worktree.
        /// </summary>
        public void AddOrUpdateWorktree(Worktree worktree)
        {
            lock (_lock)
            {
                var existing = _state.Worktrees.Find(w => w.Id == worktree.Id);
                if (existing != null)
                {
                    existing.Branch = worktree.Branch;
                    existing.BaseBranch = worktree.BaseBranch;
                    existing.PrNumber = worktree.PrNumber;
                    existing.PrUrl = worktree.PrUrl;
                    existing.Path = worktree.Path;
                }
                else
                {
                    _state.Worktrees.Add(worktree);
                }
                Save();
            }
        }

        /// <summary>
        /// Remove a worktree by ID.
        /// </summary>
        public void RemoveWorktree(string id)
        {
            lock (_lock)
            {
                _state.Worktrees.RemoveAll(w => w.Id == id);
                Save();
            }
        }

        /// <summary>
        /// Get all sessions.
        /// </summary>
        public List<AgentManagerSession> GetSessions()
        {
            lock (_lock)
            {
                return new List<AgentManagerSession>(_state.Sessions);
            }
        }

        /// <summary>
        /// Get a session by ID.
        /// </summary>
        public AgentManagerSession? GetSession(string id)
        {
            lock (_lock)
            {
                return _state.Sessions.Find(s => s.Id == id);
            }
        }

        /// <summary>
        /// Add or update a session.
        /// </summary>
        public void AddOrUpdateSession(AgentManagerSession session)
        {
            lock (_lock)
            {
                var existing = _state.Sessions.Find(s => s.Id == session.Id);
                if (existing != null)
                {
                    existing.Title = session.Title;
                    existing.Directory = session.Directory;
                    existing.WorktreeId = session.WorktreeId;
                    existing.LastActiveAt = session.LastActiveAt;
                    existing.IsClosed = session.IsClosed;
                }
                else
                {
                    _state.Sessions.Add(session);
                }
                Save();
            }
        }

        /// <summary>
        /// Remove a session by ID.
        /// </summary>
        public void RemoveSession(string id)
        {
            lock (_lock)
            {
                _state.Sessions.RemoveAll(s => s.Id == id);
                if (_state.ActiveSessionId == id)
                {
                    _state.ActiveSessionId = null;
                }
                Save();
            }
        }

        /// <summary>
        /// Get the active session ID.
        /// </summary>
        public string? GetActiveSessionId()
        {
            lock (_lock)
            {
                return _state.ActiveSessionId;
            }
        }

        /// <summary>
        /// Set the active session ID.
        /// </summary>
        public void SetActiveSessionId(string? id)
        {
            lock (_lock)
            {
                _state.ActiveSessionId = id;
                Save();
            }
        }

        /// <summary>
        /// Get the active worktree ID.
        /// </summary>
        public string? GetActiveWorktreeId()
        {
            lock (_lock)
            {
                return _state.ActiveWorktreeId;
            }
        }

        /// <summary>
        /// Set the active worktree ID.
        /// </summary>
        public void SetActiveWorktreeId(string? id)
        {
            lock (_lock)
            {
                _state.ActiveWorktreeId = id;
                Save();
            }
        }

        /// <summary>
        /// Update worktree PR information.
        /// </summary>
        public void UpdateWorktreePR(string id, string? number, string? url, string? state)
        {
            lock (_lock)
            {
                var worktree = _state.Worktrees.Find(w => w.Id == id);
                if (worktree != null)
                {
                    worktree.PrNumber = number;
                    worktree.PrUrl = url;
                }
                Save();
            }
        }

        /// <summary>
        /// Get all open worktrees.
        /// </summary>
        public List<Worktree> GetOpenWorktrees()
        {
            lock (_lock)
            {
                return _state.Worktrees.FindAll(w => w.IsOpen);
            }
        }

        /// <summary>
        /// Set worktree open/closed state.
        /// </summary>
        public void SetWorktreeOpen(string id, bool isOpen)
        {
            lock (_lock)
            {
                var worktree = _state.Worktrees.Find(w => w.Id == id);
                if (worktree != null)
                {
                    worktree.IsOpen = isOpen;
                    Save();
                }
            }
        }

        /// <summary>
        /// Clear all state (for new workspace).
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _state = new AgentManagerState();
                Save();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Save();
        }
    }
}
