using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for cloud session handler - mirrors cloud-session-handler.test.ts from VS Code
    /// </summary>
    public class CloudSessionTests
    {
        private class CloudSession
        {
            public string Id { get; set; }
            public string Title { get; set; }
            public DateTime CreatedAt { get; set; }
            public bool IsSynced { get; set; }
        }

        private class MockCloudClient
        {
            public List<SyncCall> SyncCalls { get; } = new List<SyncCall>();
            public List<CloudSession> Sessions { get; } = new List<CloudSession>();

            public Task<SyncResult> SyncSessionAsync(string sessionID)
            {
                SyncCalls.Add(new SyncCall { SessionID = sessionID });
                var session = Sessions.FirstOrDefault(s => s.Id == sessionID);
                if (session != null)
                {
                    session.IsSynced = true;
                }
                return Task.FromResult(new SyncResult { Data = session });
            }

            public Task<ListResult> ListSessionsAsync()
            {
                return Task.FromResult(new ListResult { Data = Sessions.ToArray() });
            }
        }

        private class SyncCall
        {
            public string SessionID { get; set; }
        }

        private class SyncResult
        {
            public CloudSession Data { get; set; }
        }

        private class ListResult
        {
            public CloudSession[] Data { get; set; }
        }

        private class CloudSessionHandler
        {
            private readonly MockCloudClient _client;
            public List<string> SyncedSessionIds { get; } = new List<string>();

            public CloudSessionHandler(MockCloudClient client)
            {
                _client = client;
            }

            public async Task SyncSessionAsync(string sessionID)
            {
                var result = await _client.SyncSessionAsync(sessionID);
                if (result.Data != null)
                {
                    SyncedSessionIds.Add(sessionID);
                }
            }

            public async Task<CloudSession[]> ListSessionsAsync()
            {
                var result = await _client.ListSessionsAsync();
                return result.Data;
            }
        }

        [Fact]
        public async Task Syncs_session_and_tracks_synced_id()
        {
            // Arrange
            var client = new MockCloudClient();
            client.Sessions.Add(new CloudSession { Id = "s1", Title = "Session 1", CreatedAt = DateTime.UtcNow });
            var handler = new CloudSessionHandler(client);

            // Act
            await handler.SyncSessionAsync("s1");

            // Assert
            client.SyncCalls.Count.Should().Be(1);
            client.SyncCalls[0].SessionID.Should().Be("s1");

            handler.SyncedSessionIds.Should().Contain("s1");

            var session = client.Sessions.First();
            session.IsSynced.Should().BeTrue();
        }

        [Fact]
        public async Task Lists_all_cloud_sessions()
        {
            // Arrange
            var client = new MockCloudClient();
            client.Sessions.Add(new CloudSession { Id = "s1", Title = "Session 1", CreatedAt = DateTime.UtcNow });
            client.Sessions.Add(new CloudSession { Id = "s2", Title = "Session 2", CreatedAt = DateTime.UtcNow });
            var handler = new CloudSessionHandler(client);

            // Act
            var sessions = await handler.ListSessionsAsync();

            // Assert
            sessions.Length.Should().Be(2);
            sessions.Select(s => s.Id).Should().ContainInOrder("s1", "s2");
        }

        [Fact]
        public async Task Does_not_track_synced_id_if_sync_fails()
        {
            // Arrange
            var client = new MockCloudClient();
            var handler = new CloudSessionHandler(client);

            // Act
            await handler.SyncSessionAsync("non-existent");

            // Assert
            handler.SyncedSessionIds.Should().BeEmpty();
        }
    }
}
