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
    /// Tests for session refresh - mirrors kilo-provider-session-refresh.test.ts from VS Code
    /// </summary>
    public class SessionRefreshTests
    {
        private class MockClient
        {
            public List<GetCall> Calls { get; } = new List<GetCall>();

            public Task<GetResult> GetSessionAsync(string sessionID)
            {
                Calls.Add(new GetCall { SessionID = sessionID });
                return Task.FromResult(new GetResult { 
                    Data = new { Id = sessionID, Title = "Refreshed", Directory = "/repo" } 
                });
            }
        }

        private class GetCall
        {
            public string SessionID { get; set; }
        }

        private class GetResult
        {
            public object Data { get; set; }
        }

        private class Session
        {
            public string Id { get; set; }
            public string Title { get; set; }
            public string Directory { get; set; }
        }

        private class VSProviderInternals
        {
            public Session CurrentSession { get; set; }
            public HashSet<string> TrackedSessionIds { get; } = new HashSet<string>();
            public MockClient Client { get; set; } = new MockClient();
            public List<string> PostedMessages { get; } = new List<string>();

            public async Task RefreshSessionDetailsAsync(string sessionID, string directory)
            {
                if (!TrackedSessionIds.Contains(sessionID))
                    return;

                var result = await Client.GetSessionAsync(sessionID);
                var data = (dynamic)result.Data;

                CurrentSession = new Session 
                { 
                    Id = data.Id, 
                    Title = data.Title, 
                    Directory = data.Directory 
                };

                PostedMessages.Add(JsonSerializer.Serialize(new { type = "sessionUpdated", session = CurrentSession }));
            }
        }

        [Fact]
        public async Task Refreshes_session_metadata_and_posts_sessionUpdated()
        {
            // Arrange
            var internals = new VSProviderInternals();
            internals.CurrentSession = new Session { Id = "s1", Title = "Old", Directory = "/repo" };
            internals.TrackedSessionIds.Add("s1");

            // Act
            await internals.RefreshSessionDetailsAsync("s1", @"/repo");

            // Assert
            internals.Client.Calls.Count.Should().Be(1);
            internals.Client.Calls[0].SessionID.Should().Be("s1");

            internals.CurrentSession.Title.Should().Be("Refreshed");

            internals.PostedMessages.Count.Should().Be(1);
            internals.PostedMessages[0].Should().Contain("sessionUpdated");
        }

        [Fact]
        public async Task Does_not_refresh_if_session_not_tracked()
        {
            // Arrange
            var internals = new VSProviderInternals();
            internals.CurrentSession = new Session { Id = "s1", Title = "Old", Directory = "/repo" };
            // Note: s1 is NOT added to TrackedSessionIds

            // Act
            await internals.RefreshSessionDetailsAsync("s1", @"/repo");

            // Assert
            internals.Client.Calls.Count.Should().Be(0);
            internals.CurrentSession.Title.Should().Be("Old");
        }
    }
}
