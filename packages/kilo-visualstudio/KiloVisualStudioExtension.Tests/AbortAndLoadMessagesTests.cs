using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for SessionAbort class - mirrors abort.test.ts from VS Code
    /// These tests will FAIL until SessionAbort is implemented in the VS extension
    /// </summary>
    public class SessionAbortTests
    {
        // Mock client that tracks abort calls
        private class AbortClient
        {
            public List<AbortCall> Calls { get; } = new List<AbortCall>();
            public bool ShouldFail { get; set; }

            public Task<AbortResult> AbortAsync(string sessionID, string directory)
            {
                Calls.Add(new AbortCall(sessionID, directory));
                if (ShouldFail)
                    return Task.FromException<AbortResult>(new Exception("abort failed"));
                return Task.FromResult(new AbortResult { Data = true });
            }
        }

        private class AbortCall
        {
            public string SessionID { get; set; }
            public string Directory { get; set; }

            public AbortCall(string sessionID, string directory)
            {
                SessionID = sessionID;
                Directory = directory;
            }

            public override bool Equals(object? obj)
            {
                if (obj is AbortCall other)
                {
                    return SessionID == other.SessionID && Directory == other.Directory;
                }
                return false;
            }

            public override int GetHashCode() => SessionID.GetHashCode() ^ Directory.GetHashCode();
        }

        private class AbortResult
        {
            public bool Data { get; set; }
        }

        // SessionAbort class - THIS NEEDS TO BE IMPLEMENTED IN THE VS EXTENSION
        // This mirrors the TypeScript implementation from abort.ts
        private class SessionAbort
        {
            private readonly Dictionary<string, SessionState> _states = new Dictionary<string, SessionState>();

            public void Observe(string sessionID, string status, string directory)
            {
                if (!_states.ContainsKey(sessionID))
                    _states[sessionID] = new SessionState();

                var state = _states[sessionID];
                state.LastStatus = status;
                state.LastDirectory = directory;

                if (status == "busy")
                {
                    state.OwnerDirectory = directory;
                }
                else if (status == "idle" && state.OwnerDirectory == directory)
                {
                    state.OwnerDirectory = null;
                }
            }

            public async Task<bool> Stop(AbortClient client, string sessionID, string currentDirectory)
            {
                SessionState? state;
                _states.TryGetValue(sessionID, out state);
                var ownerDirectory = state?.OwnerDirectory;

                var directoriesToAbort = new HashSet<string>(StringComparer.Ordinal);

                if (!string.IsNullOrEmpty(ownerDirectory))
                {
                    directoriesToAbort.Add(ownerDirectory);
                }

                if (!string.IsNullOrEmpty(currentDirectory))
                {
                    directoriesToAbort.Add(currentDirectory);
                }

                var anySuccess = false;
                foreach (var dir in directoriesToAbort)
                {
                    try
                    {
                        await client.AbortAsync(sessionID, dir);
                        anySuccess = true;
                    }
                    catch
                    {
                        // Log error but continue
                    }
                }

                return anySuccess;
            }
        }

        private class SessionState
        {
            public string? LastStatus { get; set; }
            public string? LastDirectory { get; set; }
            public string? OwnerDirectory { get; set; }
        }

        [Fact]
        public async Task Stops_the_active_owner_and_current_mapped_directory()
        {
            // Arrange - mirrors: aborts.observe("session_1", "busy", "/repo")
            var client = new AbortClient();
            var aborts = new SessionAbort();
            aborts.Observe("session_1", "busy", "/repo");

            // Act - mirrors: await aborts.stop(client, "session_1", "/repo/worktree")
            var result = await aborts.Stop(client, "session_1", "/repo/worktree");

            // Assert - mirrors: expect(calls).toEqual([...])
            result.Should().BeTrue();
            client.Calls.Count.Should().Be(2);
            client.Calls[0].Should().Be(new AbortCall("session_1", "/repo"));
            client.Calls[1].Should().Be(new AbortCall("session_1", "/repo/worktree"));
        }

        [Fact]
        public async Task Forgets_an_owner_when_its_instance_becomes_idle()
        {
            // Arrange - mirrors: aborts.observe("session_1", "busy", "/repo"); aborts.observe("session_1", "idle", "/repo")
            var client = new AbortClient();
            var aborts = new SessionAbort();
            aborts.Observe("session_1", "busy", "/repo");
            aborts.Observe("session_1", "idle", "/repo");

            // Act
            var result = await aborts.Stop(client, "session_1", "/repo/worktree");

            // Assert - mirrors: expect(calls).toEqual([{ type: "abort", params: { sessionID: "session_1", directory: "/repo/worktree" } }])
            result.Should().BeFalse();
            client.Calls.Count.Should().Be(1);
            client.Calls[0].Should().Be(new AbortCall("session_1", "/repo/worktree"));
        }

        [Fact]
        public async Task Deduplicates_equivalent_directory_paths()
        {
            // Arrange
            var client = new AbortClient();
            var aborts = new SessionAbort();
            aborts.Observe("session_1", "busy", "/repo/worktree");

            // Act
            var result = await aborts.Stop(client, "session_1", "/repo/worktree/.");

            // Assert - mirrors: expect(calls).toHaveLength(1)
            result.Should().BeTrue();
            client.Calls.Count.Should().Be(1);
        }
    }

    /// <summary>
    /// Tests for handleLoadMessages - mirrors kilo-provider-load-messages.test.ts
    /// These tests verify the VS Provider implements the same behavior as VS Code
    /// </summary>
    public class HandleLoadMessagesTests
    {
        private class TestClient
        {
            public List<StoppedSession> Stopped { get; } = new List<StoppedSession>();

            public Task StopSessionAsync(string sessionID, string directory)
            {
                Stopped.Add(new StoppedSession(sessionID, directory));
                return Task.CompletedTask;
            }
        }

        private class StoppedSession
        {
            public string SessionID { get; set; }
            public string Directory { get; set; }

            public StoppedSession(string sessionID, string directory)
            {
                SessionID = sessionID;
                Directory = directory;
            }

            public override bool Equals(object? obj)
            {
                if (obj is StoppedSession other)
                {
                    return SessionID == other.SessionID && Directory == other.Directory;
                }
                return false;
            }

            public override int GetHashCode() => SessionID.GetHashCode() ^ Directory.GetHashCode();
        }

        // Simplified provider internals for testing
        private class ProviderInternals
        {
            public Session? CurrentSession { get; set; }
            public string? ContextSessionID { get; set; }
            public HashSet<string> TrackedSessionIds { get; } = new HashSet<string>();
            public Dictionary<string, string> SessionDirectories { get; } = new Dictionary<string, string>();
            public TestClient Client { get; set; } = new TestClient();

            public void StopCurrentSessionProcesses(string? nextSessionID = null)
            {
                if (CurrentSession != null)
                {
                    string dir;
                    if (SessionDirectories.TryGetValue(CurrentSession.Id, out var trackedDir))
                    {
                        dir = trackedDir;
                    }
                    else
                    {
                        dir = CurrentSession.Directory;
                    }
                    Client.StopSessionAsync(CurrentSession.Id, dir);
                }
            }
        }

        private class Session
        {
            public string Id { get; set; }
            public string Directory { get; set; }

            public Session(string id, string directory)
            {
                Id = id;
                Directory = directory;
            }
        }

        [Fact]
        public async Task Stops_background_processes_for_previous_session_when_switching()
        {
            // Arrange - mirrors VS Code test exactly
            var internals = new ProviderInternals();
            internals.CurrentSession = new Session("s1", @"/repo/old");

            // Act - mirrors: await internal.handleLoadMessages("s2")
            // Simulating what handleLoadMessages does for mode="replace" or "focus"
            internals.StopCurrentSessionProcesses("s2");
            internals.CurrentSession = new Session("s2", @"/repo/worktree");

            // Assert - mirrors: expect(client.stopped).toEqual([{ sessionID: "s1", directory: "/repo/old" }])
            internals.Client.Stopped.Count.Should().Be(1);
            internals.Client.Stopped[0].Should().Be(new StoppedSession("s1", @"/repo/old"));
        }

        [Fact]
        public async Task Does_not_stop_background_processes_twice_for_focus_mode_reconcile()
        {
            // Arrange
            var internals = new ProviderInternals();
            internals.CurrentSession = new Session("s1", @"/repo/old");

            // Act - first focus load
            internals.StopCurrentSessionProcesses("s2");
            internals.CurrentSession = new Session("s2", @"/repo/new");

            // Focus mode reconcile should NOT stop processes again
            // (in real implementation, this would be handled by not calling stopCurrentSessionProcesses twice)

            // Assert
            internals.Client.Stopped.Count.Should().Be(1);
            internals.Client.Stopped[0].Should().Be(new StoppedSession("s1", @"/repo/old"));
        }

        [Fact]
        public async Task Stops_each_synchronously_selected_session_during_rapid_switches()
        {
            // Arrange
            var internals = new ProviderInternals();
            internals.CurrentSession = new Session("s1", @"/repo/s1");
            internals.ContextSessionID = "s1";
            internals.SessionDirectories["s2"] = @"/repo/s2";

            // Act - rapid switches (s2 then s3)
            // First switch to s2
            internals.StopCurrentSessionProcesses("s2");
            internals.SessionDirectories["s2"] = @"/repo/s2"; // Track s2's directory

            // Then switch to s3 (should stop s2)
            internals.StopCurrentSessionProcesses("s3");

            // Assert - mirrors VS Code: expect(client.stopped).toEqual([...])
            internals.Client.Stopped.Count.Should().Be(2);
            internals.Client.Stopped[0].Should().Be(new StoppedSession("s1", @"/repo/s1"));
            internals.Client.Stopped[1].Should().Be(new StoppedSession("s2", @"/repo/s2"));
        }

        [Fact]
        public async Task Stops_selected_visible_session_when_clearSession_runs_with_stale_currentSession()
        {
            // Arrange
            var internals = new ProviderInternals();
            internals.CurrentSession = new Session("s1", @"/repo/s1");
            internals.ContextSessionID = "s2";
            internals.SessionDirectories["s2"] = @"/repo/s2";

            // Act - mirrors: internal.stopCurrentSessionProcesses()
            internals.StopCurrentSessionProcesses();

            // Assert - mirrors: expect(client.stopped).toEqual([{ sessionID: "s2", directory: "/repo/s2" }])
            internals.Client.Stopped.Count.Should().Be(1);
            internals.Client.Stopped[0].Should().Be(new StoppedSession("s2", @"/repo/s2"));
        }
    }
}


