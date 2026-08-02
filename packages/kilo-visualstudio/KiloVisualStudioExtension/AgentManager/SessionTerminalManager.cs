using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.AgentManager
{
    /// <summary>
    /// Manages terminals for Agent Manager sessions.
    /// Creates and tracks VS Code terminals per session, routing output to session state.
    /// Matches VS Code's SessionTerminalManager pattern.
    /// </summary>
    public class SessionTerminalManager : IDisposable
    {
        private readonly Dictionary<string, TerminalInfo> _terminals = new Dictionary<string, TerminalInfo>();
        private readonly Action<string> _log;
        private readonly IHost _host;
        private bool _disposed;

        /// <summary>
        /// Terminal information.
        /// </summary>
        public class TerminalInfo
        {
            public string SessionId { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string? WorkingDirectory { get; set; }
            public Process? Process { get; set; }
            public DateTime CreatedAt { get; set; }
            public bool IsRunning { get; set; }
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        public SessionTerminalManager(Action<string> log, IHost host)
        {
            _log = log;
            _host = host;
        }

        /// <summary>
        /// Create a new terminal for a session.
        /// </summary>
        public async Task<string> CreateTerminalAsync(string sessionId, string name, string? workingDirectory = null)
        {
            _log($"[Terminal] Creating terminal '{name}' for session {sessionId}");
            
            var terminalId = $"{sessionId}:{name}";
            
            var terminalInfo = new TerminalInfo
            {
                SessionId = sessionId,
                Name = name,
                WorkingDirectory = workingDirectory,
                CreatedAt = DateTime.UtcNow,
                IsRunning = false
            };

            _terminals[terminalId] = terminalInfo;
            
            _log($"[Terminal] Terminal '{terminalId}' created");
            return terminalId;
        }

        /// <summary>
        /// Run a command in a terminal.
        /// </summary>
        public async Task<bool> RunCommandAsync(string terminalId, string command, string[]? args = null)
        {
            if (!_terminals.TryGetValue(terminalId, out var terminalInfo))
            {
                _log($"[Terminal] Terminal not found: {terminalId}");
                return false;
            }

            _log($"[Terminal] Running command in '{terminalId}': {command} {string.Join(" ", args ?? Array.Empty<string>())}");

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = args != null ? string.Join(" ", args) : "",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    WorkingDirectory = terminalInfo.WorkingDirectory ?? _host.WorkspacePath() ?? Directory.GetCurrentDirectory()
                };

                var process = new Process { StartInfo = startInfo };
                
                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        _log($"[Terminal:{terminalId}] {e.Data}");
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        _log($"[Terminal:{terminalId} ERROR] {e.Data}");
                    }
                };

                process.EnableRaisingEvents = true;
                process.Exited += (sender, e) =>
                {
                    terminalInfo.IsRunning = false;
                    _log($"[Terminal:{terminalId}] Process exited");
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                terminalInfo.Process = process;
                terminalInfo.IsRunning = true;

                return true;
            }
            catch (Exception ex)
            {
                _log($"[Terminal:{terminalId}] Error running command: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Send input to a terminal.
        /// </summary>
        public bool SendInput(string terminalId, string input)
        {
            if (!_terminals.TryGetValue(terminalId, out var terminalInfo))
            {
                return false;
            }

            if (terminalInfo.Process?.StandardInput == null || !terminalInfo.IsRunning)
            {
                return false;
            }

            try
            {
                terminalInfo.Process.StandardInput.Write(input);
                terminalInfo.Process.StandardInput.Flush();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Kill a terminal.
        /// </summary>
        public void KillTerminal(string terminalId)
        {
            if (!_terminals.TryGetValue(terminalId, out var terminalInfo))
            {
                return;
            }

            _log($"[Terminal:{terminalId}] Killing terminal");

            try
            {
                if (terminalInfo.Process != null && !terminalInfo.Process.HasExited)
                {
                    terminalInfo.Process.Kill();
                    terminalInfo.Process.WaitForExit(1000);
                }
            }
            catch (Exception ex)
            {
                _log($"[Terminal:{terminalId}] Error killing process: {ex.Message}");
            }

            terminalInfo.IsRunning = false;
            terminalInfo.Process?.Dispose();
            terminalInfo.Process = null;
        }

        /// <summary>
        /// Get list of active terminals for a session.
        /// </summary>
        public List<string> GetSessionTerminals(string sessionId)
        {
            var result = new List<string>();
            foreach (var kvp in _terminals)
            {
                if (kvp.Value.SessionId == sessionId && kvp.Value.IsRunning)
                {
                    result.Add(kvp.Key);
                }
            }
            return result;
        }

        /// <summary>
        /// Check if a terminal exists.
        /// </summary>
        public bool HasTerminal(string terminalId)
        {
            return _terminals.ContainsKey(terminalId);
        }

        /// <summary>
        /// Get terminal info.
        /// </summary>
        public TerminalInfo? GetTerminal(string terminalId)
        {
            _terminals.TryGetValue(terminalId, out var info);
            return info;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Kill all running terminals
            foreach (var kvp in _terminals)
            {
                try
                {
                    if (kvp.Value.Process != null && !kvp.Value.Process.HasExited)
                    {
                        kvp.Value.Process.Kill();
                    }
                    kvp.Value.Process?.Dispose();
                }
                catch { }
            }

            _terminals.Clear();
        }
    }
}
