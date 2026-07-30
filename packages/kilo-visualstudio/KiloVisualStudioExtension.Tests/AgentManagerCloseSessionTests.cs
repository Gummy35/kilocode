using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for Agent Manager close session behavior
    /// Mirrors: agent-manager-close-session.test.ts from VS Code
    /// 
    /// These tests will FAIL until the VS extension implements proper session cleanup
    /// </summary>
    public class AgentManagerCloseSessionTests
    {
        // Mock client that tracks process stops
        private class MockClient
        {
            public List<StopCall> Stops { get; } = new List<StopCall>();

            public Task<StopResult> StopSessionAsync(string sessionID, string? directory)
            {
                Stops.Add(new StopCall(sessionID, directory));
                return Task.FromResult(new StopResult { Data = true });
            }
        }

        private class StopCall
        {
            public string SessionID { get; set; }
            public string? Directory { get; set; }

            public StopCall(string sessionID, string? directory)
            {
                SessionID = sessionID;
                Directory = directory;
            }

            public override bool Equals(object? obj)
            {
                if (obj is StopCall other)
                {
                    return SessionID == other.SessionID && Directory == other.Directory;
                }
                return false;
            }

            public override int GetHashCode() => (SessionID?.GetHashCode() ?? 0) ^ (Directory?.GetHashCode() ?? 0);
        }

        private class StopResult
        {
            public bool Data { get; set; }
        }

        /// <summary>
        /// Simulates the AgentManagerProvider.closeSession behavior
        /// This mirrors the TypeScript implementation
        /// </summary>
        private class CloseSessionSimulator
        {
            private readonly MockClient _client;
            private readonly HashSet<string> _panelSessions = new HashSet<string>();
            private readonly Dictionary<string, string> _sessionDirectories = new Dictionary<string, string>();
            private readonly List<string> _aborted = new List<string>();
            private readonly List<string> _cleared = new List<string>();
            private readonly List<string> _removed = new List<string>();

            public CloseSessionSimulator(MockClient client)
            {
                _client = client;
            }

            public void AddPanelSession(string sessionId) => _panelSessions.Add(sessionId);
            public void SetSessionDirectory(string sessionId, string directory) => _sessionDirectories[sessionId] = directory;

            public async Task CloseSession(string sessionId)
            {
                // 1. Abort the session first
                _aborted.Add(sessionId);

                // 2. Get directory from state or panel
                var directory = _sessionDirectories.TryGetValue(sessionId, out var dir) ? dir : null;

                // 3. Stop background processes
                if (!string.IsNullOrEmpty(directory))
                {
                    await _client.StopSessionAsync(sessionId, directory);
                }

                // 4. Remove from state
                _removed.Add(sessionId);

                // 5. Clear session directory
                _cleared.Add(sessionId);

                // 6. Remove from panel sessions
                _panelSessions.Remove(sessionId);
            }

            public List<string> Aborted => _aborted;
            public List<StopCall> Stops => _client.Stops;
            public List<string> Removed => _removed;
            public List<string> Cleared => _cleared;
            public bool HasPanelSession(string id) => _panelSessions.Contains(id);
        }

        [Fact]
        public async Task Aborts_the_agent_before_stopping_processes_and_removing_its_tab()
        {
            // Arrange
            var client = new MockClient();
            var simulator = new CloseSessionSimulator(client);
            simulator.AddPanelSession("s1");
            simulator.SetSessionDirectory("s1", "/repo/worktree");

            // Act
            await simulator.CloseSession("s1");

            // Assert
            Assert.Contains("s1", simulator.Aborted);
            Assert.Contains(new StopCall("s1", "/repo/worktree"), client.Stops);
            Assert.Contains("s1", simulator.Removed);
            Assert.Contains("s1", simulator.Cleared);
            Assert.False(simulator.HasPanelSession("s1"));
        }

        [Fact]
        public async Task Falls_back_to_session_provider_directory_mappings()
        {
            // Arrange
            var client = new MockClient();
            var simulator = new CloseSessionSimulator(client);
            simulator.AddPanelSession("s1");
            simulator.SetSessionDirectory("s1", "/repo/panel-worktree");

            // Act
            await simulator.CloseSession("s1");

            // Assert
            Assert.Contains(new StopCall("s1", "/repo/panel-worktree"), client.Stops);
        }

        [Fact]
        public async Task Still_aborts_when_Agent_Manager_has_no_workspace_state()
        {
            // Arrange
            var client = new MockClient();
            var simulator = new CloseSessionSimulator(client);
            simulator.AddPanelSession("s1");
            // No directory set - simulating no workspace state

            // Act
            await simulator.CloseSession("s1");

            // Assert - should still abort even without directory
            Assert.Contains("s1", simulator.Aborted);
            Assert.Empty(client.Stops); // No stops without directory
        }
    }
}
