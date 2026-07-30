using System;
using System.Collections.Generic;
using System.Linq;

namespace KiloVisualStudioExtension.Services
{
    /// <summary>
    /// State for a single tab in the session tab switcher.
    /// </summary>
    public class TabState
    {
        public string SessionId { get; set; } = "";
        public bool IsActive { get; set; }
        public List<string> MessageIds { get; set; } = new List<string>();
    }

    /// <summary>
    /// Manages session tabs - activation, filtering, and state.
    /// </summary>
    public class TabSwitcher
    {
        private readonly Dictionary<string, TabState> _tabs = new Dictionary<string, TabState>();
        private string? _activeTabId;

        public void CreateTab(string sessionId)
        {
            _tabs[sessionId] = new TabState { SessionId = sessionId, IsActive = false };
        }

        public void ActivateTab(string sessionId)
        {
            if (_tabs.ContainsKey(sessionId))
            {
                foreach (var tab in _tabs.Values)
                    tab.IsActive = false;

                _tabs[sessionId].IsActive = true;
                _activeTabId = sessionId;
            }
        }

        public TabState? GetActiveTab()
        {
            if (_activeTabId != null && _tabs.TryGetValue(_activeTabId, out var tab))
                return tab;
            return null;
        }

        public List<string> GetTabIds() => _tabs.Keys.ToList();
    }
}
