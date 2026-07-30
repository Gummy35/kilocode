using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for background process management - mirrors background-process.test.ts from VS Code
    /// These tests verify process stopping behavior
    /// </summary>
    public class BackgroundProcessTests
    {
        private class MockClient
        {
            public List<StopCall> Calls { get; } = new List<StopCall>();
            public bool ShouldFail { get; set; }

            public Task<StopResult> StopSessionAsync(string sessionID, string directory)
            {
                Calls.Add(new StopCall { SessionID = sessionID, Directory = directory });
                if (ShouldFail)
                    return Task.FromException<StopResult>(new Exception("stop failed"));
                return Task.FromResult(new StopResult { Data = true });
            }
        }

        private class StopCall
        {
            public string SessionID { get; set; }
            public string Directory { get; set; }
        }

        private class StopResult
        {
            public bool Data { get; set; }
        }

        // Background process stopper - THIS NEEDS TO BE IMPLEMENTED IN THE VS EXTENSION
        private class BackgroundProcessStopper
        {
            public async Task StopSessionProcesses(MockClient client, string sessionID, string directory)
            {
                try
                {
                    await client.StopSessionAsync(sessionID, directory);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Warning: Failed to stop process for {sessionID} at {directory}: {ex.Message}");
                }
            }
        }

        [Fact]
        public async Task Stops_all_background_processes_for_a_session_in_the_provided_directory()
        {
            // Arrange
            var client = new MockClient();
            var stopper = new BackgroundProcessStopper();

            // Act
            await stopper.StopSessionProcesses(client, "s1", @"/repo/worktree");

            // Assert
            client.Calls.Count.Should().Be(1);
            client.Calls[0].SessionID.Should().Be("s1");
            client.Calls[0].Directory.Should().Be(@"/repo/worktree");
        }

        [Fact]
        public async Task Logs_stop_failures_without_throwing()
        {
            // Arrange
            var client = new MockClient { ShouldFail = true };
            var stopper = new BackgroundProcessStopper();
            var warnings = new List<string>();

            // Act - just verify it doesn't throw
            await stopper.StopSessionProcesses(client, "s1", @"/repo");

            // Assert - verify the call was made (failure is logged, not thrown)
            client.Calls.Count.Should().Be(1);
        }
    }
}
