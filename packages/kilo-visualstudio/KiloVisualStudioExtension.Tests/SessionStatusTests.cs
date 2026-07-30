using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for session status - mirrors session-status.test.ts from VS Code
    /// These tests verify session status seeding and stale entry cleanup
    /// </summary>
    public class SessionStatusTests
    {
        private class SessionStatus
        {
            public string Type { get; set; }
            public int Attempt { get; set; }
            public string Message { get; set; }
            public long? Next { get; set; }
        }

        private class MockClient
        {
            public Dictionary<string, SessionStatus> Response { get; set; }
            public bool ShouldThrow { get; set; }

            public Task<Dictionary<string, SessionStatus>> StatusAsync(string directory)
            {
                if (ShouldThrow)
                    return Task.FromException<Dictionary<string, SessionStatus>>(new Exception("Server error"));
                return Task.FromResult(Response);
            }
        }

        // Session status seeding - THIS NEEDS TO BE IMPLEMENTED IN THE VS EXTENSION
        private static class SessionStatusUtils
        {
            public static async Task SeedSessionStatuses(MockClient client, string directory, 
                Dictionary<string, string> statusMap, Action<object> postMessage)
            {
                var response = await client.StatusAsync(directory);

                // Clear all existing statuses first
                foreach (var key in new List<string>(statusMap.Keys))
                {
                    if (!response.ContainsKey(key))
                    {
                        statusMap[key] = "idle";
                        postMessage(new { type = "sessionStatus", sessionID = key, status = "idle" });
                    }
                }

                // Update with new statuses
                foreach (var kvp in response)
                {
                    statusMap[kvp.Key] = kvp.Value.Type;
                    postMessage(new 
                    { 
                        type = "sessionStatus", 
                        sessionID = kvp.Key, 
                        status = kvp.Value.Type,
                        attempt = kvp.Value.Attempt,
                        message = kvp.Value.Message,
                        next = kvp.Value.Next
                    });
                }
            }
        }

        [Fact]
        public async Task Seeds_map_and_posts_messages_for_non_idle_sessions()
        {
            // Arrange
            var client = new MockClient
            {
                Response = new Dictionary<string, SessionStatus>
                {
                    { "s1", new SessionStatus { Type = "busy" } },
                    { "s2", new SessionStatus { Type = "retry", Attempt = 3, Message = "rate limited", Next = 5000 } }
                }
            };
            var statusMap = new Dictionary<string, string>();
            var messages = new List<object>();

            // Act
            await SessionStatusUtils.SeedSessionStatuses(client, @"/repo", statusMap, msg => messages.Add(msg));

            // Assert
            statusMap["s1"].Should().Be("busy");
            statusMap["s2"].Should().Be("retry");
            messages.Count.Should().Be(2);
        }

        [Fact]
        public async Task Clears_stale_busy_entries_absent_from_server_response()
        {
            // Arrange
            var client = new MockClient { Response = new Dictionary<string, SessionStatus>() };
            var statusMap = new Dictionary<string, string> { { "s1", "busy" } };
            var messages = new List<object>();

            // Act
            await SessionStatusUtils.SeedSessionStatuses(client, @"/repo", statusMap, msg => messages.Add(msg));

            // Assert
            statusMap["s1"].Should().Be("idle");
            messages.Count.Should().Be(1);
            ((dynamic)messages[0]).sessionID.Should().Be("s1");
            ((dynamic)messages[0]).status.Should().Be("idle");
        }
    }
}
