using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    public class SessionTrackingTests
    {
        [Fact]
        public void TrackOpenSessions_AddsSessionsToTrackedSet()
        {
            // Arrange
            var sseHelper = TestHelpers.CreateSSEHelper();

            // Act
            sseHelper.TrackSession("s1");
            sseHelper.TrackSession("s2");

            // Assert
            sseHelper.IsSessionTracked("s1").Should().BeTrue();
            sseHelper.IsSessionTracked("s2").Should().BeTrue();
        }

        [Fact]
        public void TrackOpenSessions_RemovesSessionsWhenUntracked()
        {
            // Arrange
            var sseHelper = TestHelpers.CreateSSEHelper();
            sseHelper.TrackSession("s1");
            sseHelper.TrackSession("s2");

            // Act
            sseHelper.UntrackSession("s1");

            // Assert
            sseHelper.IsSessionTracked("s2").Should().BeTrue();
            sseHelper.IsSessionTracked("s1").Should().BeFalse();
        }

        [Fact]
        public void SetCurrentSession_AddsToTrackedSessions()
        {
            // Arrange
            var sseHelper = TestHelpers.CreateSSEHelper();

            // Act
            sseHelper.SetCurrentSession("new-session");

            // Assert
            sseHelper.IsSessionTracked("new-session").Should().BeTrue();
            sseHelper.CurrentSessionID.Should().Be("new-session");
        }

        [Fact]
        public void CurrentSessionID_ReturnsTrackedSession()
        {
            // Arrange
            var sseHelper = TestHelpers.CreateSSEHelper();
            sseHelper.SetCurrentSession("current-session");

            // Act
            var current = sseHelper.CurrentSessionID;

            // Assert
            current.Should().Be("current-session");
        }

        [Fact]
        public void UntrackSession_RemovesFromTrackedSet()
        {
            // Arrange
            var sseHelper = TestHelpers.CreateSSEHelper();
            sseHelper.TrackSession("session-to-remove");
            sseHelper.IsSessionTracked("session-to-remove").Should().BeTrue();

            // Act
            sseHelper.UntrackSession("session-to-remove");

            // Assert
            sseHelper.IsSessionTracked("session-to-remove").Should().BeFalse();
        }

        [Fact]
        public void TrackSession_DoesNotAddDuplicates()
        {
            // Arrange
            var sseHelper = TestHelpers.CreateSSEHelper();

            // Act
            sseHelper.TrackSession("duplicate-session");
            sseHelper.TrackSession("duplicate-session");

            // Assert
            var trackedCount = sseHelper.TrackedSessionIds.Count;
            trackedCount.Should().Be(1, "tracking the same session twice should not create duplicates");
        }
    }
}
