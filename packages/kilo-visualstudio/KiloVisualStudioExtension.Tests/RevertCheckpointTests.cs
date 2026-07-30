using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using KiloVisualStudioExtension.Services;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for RevertCheckpointService
    /// </summary>
    public class RevertCheckpointTests
    {
        private class MockRevertClient : ISessionRevertClient
        {
            public List<RevertCall> Calls { get; } = new List<RevertCall>();

            public Task<RevertResult> RevertSessionAsync(string sessionID, string messageID)
            {
                Calls.Add(new RevertCall { SessionID = sessionID, MessageID = messageID });
                return Task.FromResult(new RevertResult 
                { 
                    SessionId = sessionID, 
                    MessageId = messageID 
                });
            }
        }

        private class RevertCall
        {
            public string SessionID { get; set; } = "";
            public string MessageID { get; set; } = "";
        }

        [Fact]
        public async Task Reverts_session_to_message()
        {
            // Arrange
            var client = new MockRevertClient();
            var service = new RevertCheckpointService(client);

            // Act
            await service.RevertSessionAsync("s1", "m1");

            // Assert
            client.Calls.Count.Should().Be(1);
            client.Calls[0].SessionID.Should().Be("s1");
            client.Calls[0].MessageID.Should().Be("m1");

            service.CurrentSession.Should().NotBeNull();
            service.CurrentSession?.Revert.Should().NotBeNull();
            service.CurrentSession?.Revert?.MessageID.Should().Be("m1");
        }

        [Fact]
        public async Task Unreverts_session_and_clears_revert()
        {
            // Arrange
            var client = new MockRevertClient();
            var service = new RevertCheckpointService(client);
            await service.RevertSessionAsync("s1", "m1");

            // Act
            service.UnrevertSession("s1");

            // Assert
            service.CurrentSession?.Revert.Should().BeNull();
        }
    }
}
