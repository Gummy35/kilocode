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
    /// Tests for revert checkpoints - mirrors revert-checkpoints.test.ts from VS Code
    /// </summary>
    public class RevertCheckpointTests
    {
        private class MockClient
        {
            public List<RevertCall> Calls { get; } = new List<RevertCall>();

            public Task<RevertResult> RevertSessionAsync(string sessionID, string messageID)
            {
                Calls.Add(new RevertCall { SessionID = sessionID, MessageID = messageID });
                return Task.FromResult(new RevertResult { 
                    Data = new { Id = sessionID, Revert = new { MessageID = messageID } } 
                });
            }
        }

        private class RevertCall
        {
            public string SessionID { get; set; }
            public string MessageID { get; set; }
        }

        private class RevertResult
        {
            public object Data { get; set; }
        }

        private class Session
        {
            public string Id { get; set; }
            public RevertInfo Revert { get; set; }
        }

        private class RevertInfo
        {
            public string MessageID { get; set; }
        }

        private class VSProviderInternals
        {
            public Session CurrentSession { get; set; }
            public MockClient Client { get; set; } = new MockClient();
            public List<string> PostedMessages { get; } = new List<string>();

            public async Task HandleRevertSessionAsync(string sessionID, string messageID)
            {
                var result = await Client.RevertSessionAsync(sessionID, messageID);
                var data = (dynamic)result.Data;

                CurrentSession = new Session 
                { 
                    Id = data.Id, 
                    Revert = new RevertInfo { MessageID = data.Revert.MessageID } 
                };

                PostedMessages.Add(JsonSerializer.Serialize(new { 
                    type = "sessionUpdated", 
                    session = CurrentSession 
                }));
            }

            public async Task HandleUnrevertSessionAsync(string sessionID)
            {
                CurrentSession = new Session { Id = sessionID, Revert = null };
                PostedMessages.Add(JsonSerializer.Serialize(new { 
                    type = "sessionUpdated", 
                    session = CurrentSession 
                }));
            }
        }

        [Fact]
        public async Task Reverts_session_to_message_and_posts_sessionUpdated()
        {
            // Arrange
            var internals = new VSProviderInternals();
            internals.CurrentSession = new Session { Id = "s1", Revert = null };

            // Act
            await internals.HandleRevertSessionAsync("s1", "m1");

            // Assert
            internals.Client.Calls.Count.Should().Be(1);
            internals.Client.Calls[0].SessionID.Should().Be("s1");
            internals.Client.Calls[0].MessageID.Should().Be("m1");

            internals.CurrentSession.Revert.Should().NotBeNull();
            internals.CurrentSession.Revert.MessageID.Should().Be("m1");

            internals.PostedMessages.Count.Should().Be(1);
            internals.PostedMessages[0].Should().Contain("sessionUpdated");
        }

        [Fact]
        public async Task Unreverts_session_and_clears_revert()
        {
            // Arrange
            var internals = new VSProviderInternals();
            internals.CurrentSession = new Session { Id = "s1", Revert = new RevertInfo { MessageID = "m1" } };

            // Act
            await internals.HandleUnrevertSessionAsync("s1");

            // Assert
            internals.CurrentSession.Revert.Should().BeNull();

            internals.PostedMessages.Count.Should().Be(1);
            internals.PostedMessages[0].Should().Contain("sessionUpdated");
        }
    }
}
