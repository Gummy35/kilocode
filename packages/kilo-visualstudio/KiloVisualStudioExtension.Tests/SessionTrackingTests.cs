using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using KiloVisualStudioExtension.Services.Handlers.Session;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
  public class SessionTrackingTests
  {
    [Fact]
    public void TrackOpenSessions_AddsSessionsToTrackedSet()
    {
      // Arrange
      var sessionHelper = TestHelpers.serviceProvider.GetService<SessionHandlerService>();

      // Act
      sessionHelper.TrackSession("s1");
      sessionHelper.TrackSession("s2");

      // Assert
      sessionHelper.IsTrackedSession("s1").Should().BeTrue();
      sessionHelper.IsTrackedSession("s2").Should().BeTrue();
    }

    [Fact]
    public void TrackOpenSessions_RemovesSessionsWhenUntracked()
    {
      // Arrange
      var sessionHelper = TestHelpers.serviceProvider.GetService<SessionHandlerService>();
      sessionHelper.TrackSession("s1");
      sessionHelper.TrackSession("s2");

      // Act
      sessionHelper.UntrackSession("s1");

      // Assert
      sessionHelper.IsTrackedSession("s2").Should().BeTrue();
      sessionHelper.IsTrackedSession("s1").Should().BeFalse();
    }

    [Fact]
    public void SetCurrentSession_AddsToTrackedSessions()
    {
      // Arrange
      var sessionHelper = TestHelpers.serviceProvider.GetService<SessionHandlerService>();

      // Act
      //     sessionHelper.SetCurrentSession("new-session");

      // Assert
      sessionHelper.IsTrackedSession("new-session").Should().BeTrue();
      //     sessionHelper.CurrentSessionID.Should().Be("new-session");
      throw new NotImplementedException();
    }

    [Fact]
    public void CurrentSessionID_ReturnsTrackedSession()
    {
      // Arrange
      var sessionHelper = TestHelpers.serviceProvider.GetService<SessionHandlerService>();
      //       sessionHelper.SetCurrentSession("current-session");

      // Act
      //       var current = sessionHelper.CurrentSessionID;

      // Assert
      //       current.Should().Be("current-session");
      throw new NotImplementedException();
    }

    [Fact]
    public void UntrackSession_RemovesFromTrackedSet()
    {
      // Arrange
      var sessionHelper = TestHelpers.serviceProvider.GetService<SessionHandlerService>();
      sessionHelper.TrackSession("session-to-remove");
      sessionHelper.IsTrackedSession("session-to-remove").Should().BeTrue();

      // Act
      sessionHelper.UntrackSession("session-to-remove");

      // Assert
      sessionHelper.IsTrackedSession("session-to-remove").Should().BeFalse();
    }

    [Fact]
    public void TrackSession_DoesNotAddDuplicates()
    {
      // Arrange
      var sessionHelper = TestHelpers.serviceProvider.GetService<SessionHandlerService>();

      // Act
      sessionHelper.TrackSession("duplicate-session");
      sessionHelper.TrackSession("duplicate-session");

      // Assert
      var trackedCount = sessionHelper.GetTrackedSessionIds().Count;
      trackedCount.Should().Be(1, "tracking the same session twice should not create duplicates");
    }
  }
}
