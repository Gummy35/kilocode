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
    /// Tests for session rename - mirrors kilo-provider-rename.test.ts from VS Code
    /// </summary>
    public class SessionRenameTests
    {
        private class MockClient
        {
            public List<RenameCall> Calls { get; } = new List<RenameCall>();

            public Task<RenameResult> RenameSessionAsync(string sessionID, string title)
            {
                Calls.Add(new RenameCall { SessionID = sessionID, Title = title });
                return Task.FromResult(new RenameResult { Data = true });
            }
        }

        private class RenameCall
        {
            public string SessionID { get; set; }
            public string Title { get; set; }
        }

        private class RenameResult
        {
            public bool Data { get; set; }
        }

        private class Session
        {
            public string Id { get; set; }
            public string Title { get; set; }
        }

        private class VSProviderInternals
        {
            public Session CurrentSession { get; set; }
            public MockClient Client { get; set; } = new MockClient();
            public List<string> PostedMessages { get; } = new List<string>();

            public async Task HandleRenameSessionAsync(string sessionID, string title)
            {
                await Client.RenameSessionAsync(sessionID, title);

                if (CurrentSession != null && CurrentSession.Id == sessionID)
                {
                    CurrentSession.Title = title;
                }

                PostedMessages.Add(JsonSerializer.Serialize(new { type = "sessionUpdated", session = new { Id = sessionID, Title = title } }));
            }
        }

        [Fact]
        public async Task Renames_session_and_posts_sessionUpdated()
        {
            // Arrange
            var internals = new VSProviderInternals();
            internals.CurrentSession = new Session { Id = "s1", Title = "Old Title" };

            // Act
            await internals.HandleRenameSessionAsync("s1", "New Title");

            // Assert
            internals.Client.Calls.Count.Should().Be(1);
            internals.Client.Calls[0].SessionID.Should().Be("s1");
            internals.Client.Calls[0].Title.Should().Be("New Title");

            internals.CurrentSession.Title.Should().Be("New Title");

            internals.PostedMessages.Count.Should().Be(1);
            internals.PostedMessages[0].Should().Contain("sessionUpdated");
        }

        [Fact]
        public async Task Does_not_update_currentSession_if_sessionID_does_not_match()
        {
            // Arrange
            var internals = new VSProviderInternals();
            internals.CurrentSession = new Session { Id = "s1", Title = "Old Title" };

            // Act
            await internals.HandleRenameSessionAsync("s2", "New Title");

            // Assert
            internals.CurrentSession.Title.Should().Be("Old Title");
        }
    }
}
