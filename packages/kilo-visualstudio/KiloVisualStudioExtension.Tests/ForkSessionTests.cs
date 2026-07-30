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
    /// Tests for fork session - mirrors fork-session.test.ts from VS Code
    /// </summary>
    public class ForkSessionTests
    {
        private class MockClient
        {
            public List<ForkCall> Calls { get; } = new List<ForkCall>();

            public Task<ForkResult> ForkSessionAsync(string sessionID, string messageID)
            {
                Calls.Add(new ForkCall { SessionID = sessionID, MessageID = messageID });
                return Task.FromResult(new ForkResult { 
                    Data = new { Id = "forked-" + sessionID, ParentID = sessionID } 
                });
            }
        }

        private class ForkCall
        {
            public string SessionID { get; set; }
            public string MessageID { get; set; }
        }

        private class ForkResult
        {
            public object Data { get; set; }
        }

        private class VSProviderInternals
        {
            public MockClient Client { get; set; } = new MockClient();
            public List<string> PostedMessages { get; } = new List<string>();

            public async Task HandleForkSessionAsync(string sessionID, string messageID)
            {
                var result = await Client.ForkSessionAsync(sessionID, messageID);
                var data = (dynamic)result.Data;

                PostedMessages.Add(JsonSerializer.Serialize(new { 
                    type = "sessionCreated", 
                    session = new { Id = data.Id, ParentID = data.ParentID } 
                }));
            }
        }

        [Fact]
        public async Task Forks_session_and_posts_sessionCreated_with_parent()
        {
            // Arrange
            var internals = new VSProviderInternals();

            // Act
            await internals.HandleForkSessionAsync("s1", "m1");

            // Assert
            internals.Client.Calls.Count.Should().Be(1);
            internals.Client.Calls[0].SessionID.Should().Be("s1");
            internals.Client.Calls[0].MessageID.Should().Be("m1");

            internals.PostedMessages.Count.Should().Be(1);
            internals.PostedMessages[0].Should().Contain("sessionCreated");
            internals.PostedMessages[0].Should().Contain("forked-s1");
            internals.PostedMessages[0].Should().Contain("ParentID");
        }
    }
}
