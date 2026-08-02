using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace KiloVisualStudioExtension.AgentManager
{
    /// <summary>
    /// Agent Manager provider for multi-session orchestration.
    /// Matches VS Code's AgentManagerProvider pattern.
    /// 
    /// This is a simplified implementation that provides:
    /// - Panel visibility tracking
    /// - Session state management
    /// - Message posting to webview
    /// - Integration with shared KiloConnectionService
    /// </summary>
    public class AgentManagerProvider : IDisposable
    {
        private IPanelContext? _panel;
        private readonly IHost _host;
        private readonly KiloConnectionService _connectionService;
        private readonly WorktreeStateManager _stateManager;
        private readonly List<IDisposable> _disposables = new List<IDisposable>();
        private bool _disposed;
        private Action<bool>? _onPanelVisibilityChange;
        
        /// <summary>
        /// Current active session index for tab switching.
        /// </summary>
        private int _currentSessionIndex = -1;

        /// <summary>
        /// Constructor.
        /// </summary>
        public AgentManagerProvider(IHost host, KiloConnectionService connectionService, string workspaceRoot)
        {
            _host = host;
            _connectionService = connectionService;
            _stateManager = new WorktreeStateManager(workspaceRoot);
        }

        /// <summary>
        /// Open the Agent Manager panel.
        /// </summary>
        public void OpenPanel()
        {
            if (_panel != null)
            {
                _panel.Reveal();
                return;
            }

            _panel = _host.OpenPanel(
                onBeforeMessage: HandleBeforeMessage,
                worktreeDirectories: () => _stateManager.GetWorktrees().ConvertAll(w => w.Path).ToArray()
            );

            _panel.OnDidChangeVisibility += OnPanelVisibilityChanged;
            _panel.OnDidDispose += OnPanelDisposed;
            System.Diagnostics.Debug.WriteLine("[Kilo] Agent Manager panel opened");
        }

        /// <summary>
        /// Send a message to the Agent Manager webview.
        /// </summary>
        public void PostMessage(object message)
        {
            _panel?.PostMessage(message);
        }

        /// <summary>
        /// Check if the panel is currently active.
        /// </summary>
        public bool IsActive()
        {
            return _panel?.Active ?? false;
        }

        /// <summary>
        /// Check if the panel is visible.
        /// </summary>
        public bool IsVisible()
        {
            return _panel?.Visible ?? false;
        }

        /// <summary>
        /// Set callback for panel visibility changes.
        /// </summary>
        public void OnPanelVisibilityChange(Action<bool> callback)
        {
            _onPanelVisibilityChange = callback;
        }

        /// <summary>
        /// Get session directories managed by this panel.
        /// </summary>
        public Dictionary<string, string> GetSessionDirectories()
        {
            var sessions = _stateManager.GetSessions();
            var result = new Dictionary<string, string>();
            foreach (var session in sessions)
            {
                if (!string.IsNullOrEmpty(session.Directory))
                {
                    result[session.Id] = session.Directory;
                }
            }
            return result;
        }

        /// <summary>
        /// Register a session with the panel.
        /// </summary>
        public void RegisterSession(string sessionId, string directory)
        {
            _stateManager.AddOrUpdateSession(new AgentManagerSession
            {
                Id = sessionId,
                Directory = directory,
                CreatedAt = DateTime.UtcNow,
                LastActiveAt = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Close a session.
        /// </summary>
        public async Task CloseSessionAsync(string sessionId)
        {
            _stateManager.RemoveSession(sessionId);
            PostMessage(new { type = "agentManager.sessionClosed", sessionId = sessionId });
            
            // Adjust current index if needed
            AdjustCurrentIndex();
        }

        /// <summary>
        /// Navigate to the previous session.
        /// </summary>
        public void SessionPrevious()
        {
            var sessions = _stateManager.GetSessions();
            var activeSessions = sessions.FindAll(s => !s.IsClosed);
            
            if (activeSessions.Count == 0) return;
            
            _currentSessionIndex = (_currentSessionIndex - 1 + activeSessions.Count) % activeSessions.Count;
            var session = activeSessions[_currentSessionIndex];
            
            _stateManager.SetActiveSessionId(session.Id);
            PostMessage(new { type = "action", action = "sessionSwitched", sessionId = session.Id });
        }

        /// <summary>
        /// Navigate to the next session.
        /// </summary>
        public void SessionNext()
        {
            var sessions = _stateManager.GetSessions();
            var activeSessions = sessions.FindAll(s => !s.IsClosed);
            
            if (activeSessions.Count == 0) return;
            
            _currentSessionIndex = (_currentSessionIndex + 1) % activeSessions.Count;
            var session = activeSessions[_currentSessionIndex];
            
            _stateManager.SetActiveSessionId(session.Id);
            PostMessage(new { type = "action", action = "sessionSwitched", sessionId = session.Id });
        }

        /// <summary>
        /// Navigate to the previous tab.
        /// </summary>
        public void TabPrevious()
        {
            // For now, same as session previous
            SessionPrevious();
        }

        /// <summary>
        /// Navigate to the next tab.
        /// </summary>
        public void TabNext()
        {
            // For now, same as session next
            SessionNext();
        }

        /// <summary>
        /// Jump to a specific tab (1-9).
        /// </summary>
        public void JumpToTab(int tabNumber)
        {
            var sessions = _stateManager.GetSessions();
            var activeSessions = sessions.FindAll(s => !s.IsClosed);
            
            if (activeSessions.Count == 0) return;
            
            var index = Math.Min(tabNumber - 1, activeSessions.Count - 1);
            if (index >= 0)
            {
                _currentSessionIndex = index;
                var session = activeSessions[_currentSessionIndex];
                _stateManager.SetActiveSessionId(session.Id);
                PostMessage(new { type = "action", action = "sessionSwitched", sessionId = session.Id });
            }
        }

        /// <summary>
        /// Adjust current index when sessions are removed.
        /// </summary>
        private void AdjustCurrentIndex()
        {
            var sessions = _stateManager.GetSessions();
            var activeSessions = sessions.FindAll(s => !s.IsClosed);
            
            if (activeSessions.Count == 0)
            {
                _currentSessionIndex = -1;
                return;
            }
            
            if (_currentSessionIndex >= activeSessions.Count)
            {
                _currentSessionIndex = activeSessions.Count - 1;
            }
            
            if (_currentSessionIndex >= 0)
            {
                var session = activeSessions[_currentSessionIndex];
                _stateManager.SetActiveSessionId(session.Id);
            }
        }

        /// <summary>
        /// Handle message interceptor.
        /// </summary>
        private async Task<Dictionary<string, object>?> HandleBeforeMessage(Dictionary<string, object> message)
        {
            // Can be used to intercept and modify messages before they're sent to the webview
            return null; // Allow message to proceed
        }

        /// <summary>
        /// Handle panel visibility change.
        /// </summary>
        private void OnPanelVisibilityChanged(bool visible)
        {
            System.Diagnostics.Debug.WriteLine($"[Kilo] Agent Manager panel visibility changed: {visible}");
            _onPanelVisibilityChange?.Invoke(visible);
        }

        /// <summary>
        /// Handle panel disposal.
        /// </summary>
        private void OnPanelDisposed()
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] Agent Manager panel disposed");
            _panel = null;
        }

        /// <summary>
        /// Shutdown the Agent Manager (called during extension deactivation).
        /// </summary>
        public async Task ShutdownAsync()
        {
            if (_disposed) return;
            
            // Save state before shutdown
            _stateManager.Save();
            
            _disposed = true;
            foreach (var disposable in _disposables)
            {
                disposable.Dispose();
            }
            _disposables.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            _stateManager.Dispose();
            foreach (var disposable in _disposables)
            {
                disposable.Dispose();
            }
            _disposables.Clear();
        }
    }
}
