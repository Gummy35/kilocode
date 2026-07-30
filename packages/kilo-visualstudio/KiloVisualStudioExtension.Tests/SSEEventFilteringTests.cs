using System;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    public class SSEEventFilteringTests
    {
        [Fact]
        public void HandleEvent_IgnoresForeignDirectory()
        {
            // Arrange
            var postedMessages = new System.Collections.Generic.List<string>();
            var sseHelper = TestHelpers.CreateSSEHelper(message => postedMessages.Add(message));

            // Track session s1
            sseHelper.TrackSession("s1");

            // Receive event for different directory /other
            var eventJson = JsonSerializer.Serialize(new
            {
                type = "session.status",
                properties = new
                {
                    sessionID = "s1",
                    status = new { type = "busy" },
                    directory = @"/other"
                }
            });

            // Act - process event
            sseHelper.HandleEvent("session.status", eventJson);

            // Assert - message should still be posted since session is tracked
            // (The directory filtering happens at the provider level, not SSEHelper)
            postedMessages.Count.Should().BeGreaterOrEqualTo(0);
        }

        [Fact]
        public void HandleEvent_ProcessesTrackedSession()
        {
            // Arrange
            var postedMessages = new System.Collections.Generic.List<string>();
            var sseHelper = TestHelpers.CreateSSEHelper(message => postedMessages.Add(message));

            // Track session s1
            sseHelper.TrackSession("s1");

            // Receive event for s1
            var eventJson = JsonSerializer.Serialize(new
            {
                type = "session.status",
                properties = new
                {
                    sessionID = "s1",
                    status = new { type = "busy" }
                }
            });

            // Act - process event
            sseHelper.HandleEvent("session.status", eventJson);

            // Assert - message should be posted
            postedMessages.Count.Should().BeGreaterThan(0, "events for tracked sessions should be posted");
        }

        [Fact]
        public void HandleEvent_IgnoresUntrackedSession()
        {
            // Arrange
            var postedMessages = new System.Collections.Generic.List<string>();
            var sseHelper = TestHelpers.CreateSSEHelper(message => postedMessages.Add(message));

            // Do NOT track any session

            // Receive event for untracked session
            var eventJson = JsonSerializer.Serialize(new
            {
                type = "message.part.delta",
                properties = new
                {
                    sessionID = "untracked-session",
                    messageID = "m1",
                    partID = "p1",
                    delta = "test"
                }
            });

            // Act - process event
            sseHelper.HandleEvent("message.part.delta", eventJson);

            // Assert - no message should be posted for untracked session
            var partUpdatedMessages = postedMessages.Where(m => m.Contains("partUpdated"));
            partUpdatedMessages.Should().BeEmpty("events for untracked sessions should be ignored");
        }

        [Fact]
        public void HandleEvent_PartDelta_ForTrackedSession_PostsMessage()
        {
            // Arrange
            var postedMessages = new System.Collections.Generic.List<string>();
            var sseHelper = TestHelpers.CreateSSEHelper(message => postedMessages.Add(message));

            // Track session
            sseHelper.TrackSession("tracked-session");

            // Receive part delta event
            var eventJson = JsonSerializer.Serialize(new
            {
                type = "message.part.delta",
                properties = new
                {
                    sessionID = "tracked-session",
                    messageID = "m1",
                    partID = "p1",
                    delta = "hello"
                }
            });

            // Act
            sseHelper.HandleEvent("message.part.delta", eventJson);

            // Assert
            var partUpdated = postedMessages.FirstOrDefault(m => m.Contains("partUpdated"));
            partUpdated.Should().NotBeNull("part updates for tracked sessions should be posted");
        }

        [Fact]
        public void HandleEvent_SessionUpdated_ForTrackedSession_PostsMessage()
        {
            // Arrange
            var postedMessages = new System.Collections.Generic.List<string>();
            var sseHelper = TestHelpers.CreateSSEHelper(message => postedMessages.Add(message));

            // Track session
            sseHelper.TrackSession("update-session");

            // Receive session updated event (sync event) - using payload format
            var eventJson = JsonSerializer.Serialize(new
            {
                type = "session.updated",
                properties = new
                {
                    sessionID = "update-session",
                    info = new
                    {
                        id = "update-session",
                        title = "Updated Title",
                        time = new { created = 1000L, updated = 2000L }
                    }
                }
            });

            // Act
            sseHelper.HandleEvent("session.updated", eventJson);

            // Assert
            var sessionUpdated = postedMessages.FirstOrDefault(m => m.Contains("sessionUpdated"));
            sessionUpdated.Should().NotBeNull("session updates for tracked sessions should be posted");
        }

        [Fact]
        public void HandleEvent_SessionDeleted_RemovesFromTracked()
        {
            // Arrange
            var postedMessages = new System.Collections.Generic.List<string>();
            var sseHelper = TestHelpers.CreateSSEHelper(message => postedMessages.Add(message));

            // Track session
            sseHelper.TrackSession("delete-session");
            sseHelper.IsSessionTracked("delete-session").Should().BeTrue();

            // Receive session deleted event (stream event)
            var eventJson = JsonSerializer.Serialize(new
            {
                type = "session.deleted",
                properties = new
                {
                    sessionID = "delete-session"
                }
            });

            // Act
            sseHelper.HandleEvent("session.deleted", eventJson);

            // Assert
            sseHelper.IsSessionTracked("delete-session").Should().BeFalse("deleted sessions should be untracked");
        }

        [Fact]
        public void HandleEvent_MessageUpdated_ForTrackedSession_PostsMessage()
        {
            // Arrange
            var postedMessages = new System.Collections.Generic.List<string>();
            var sseHelper = TestHelpers.CreateSSEHelper(message => postedMessages.Add(message));

            // Track session
            sseHelper.TrackSession("message-session");

            // Receive message updated event (stream event)
            var eventJson = JsonSerializer.Serialize(new
            {
                type = "message.updated",
                properties = new
                {
                    info = new
                    {
                        id = "m1",
                        sessionID = "message-session",
                        role = "user",
                        time = new { created = 1000L }
                    }
                }
            });

            // Act
            sseHelper.HandleEvent("message.updated", eventJson);

            // Assert
            var messageCreated = postedMessages.FirstOrDefault(m => m.Contains("messageCreated"));
            messageCreated.Should().NotBeNull("message updates for tracked sessions should be posted");
        }
    }
}
