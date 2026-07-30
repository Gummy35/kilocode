using System;
using System.Collections.Generic;

namespace KiloVisualStudioExtension.Services
{
    /// <summary>
    /// Represents a terminal associated with a session.
    /// </summary>
    public class SessionTerminal
    {
        public string SessionId { get; set; } = "";
        public bool IsDisposed { get; set; }
        public List<string> Output { get; } = new List<string>();

        public void Write(string text) => Output.Add(text);
        public void Dispose() => IsDisposed = true;
    }

    /// <summary>
    /// Manages terminals for sessions.
    /// </summary>
    public class TerminalManager
    {
        private readonly Dictionary<string, SessionTerminal> _terminals = new Dictionary<string, SessionTerminal>();

        public void CreateTerminal(string sessionId)
        {
            _terminals[sessionId] = new SessionTerminal { SessionId = sessionId };
        }

        public void DisposeTerminal(string sessionId)
        {
            if (_terminals.TryGetValue(sessionId, out var terminal))
            {
                terminal.Dispose();
                _terminals.Remove(sessionId);
            }
        }

        public SessionTerminal? GetTerminal(string sessionId)
        {
            _terminals.TryGetValue(sessionId, out var terminal);
            return terminal;
        }

        public bool HasActiveTerminal() => _terminals.Count > 0;

        public int TerminalCount => _terminals.Count;
    }
}
