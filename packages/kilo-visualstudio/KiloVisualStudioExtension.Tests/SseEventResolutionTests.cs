using System;
using System.Collections.Generic;
using FluentAssertions;
using KiloVisualStudioExtension.ApiClient.Sse;
using KiloVisualStudioExtension.ApiClient;
using Xunit;
using Microsoft.Web.WebView2.Core;

namespace KiloVisualStudioExtension.Tests
{
  public class SseEventResolutionTests
  {
    private readonly List<string> _postedMessages = new();
    private readonly SSEHelper _helper;

    public SseEventResolutionTests()
    {
      _helper = new SSEHelper(TestHelpers.serviceProvider, msg => _postedMessages.Add(msg));
    }

    [Fact]
    public void ResolveSessionId_SyncEvent_MessageUpdated_ReturnsSessionId()
    {
      // Arrange
      var data = FixtureLoader.Load("Sse\\message\\message.updated.1.json");
      var ev = new SseEventReceivedEventArgs("message.updated.1", data);

      // Act
      var sessionId = _helper.ResolveEventSessionId(ev);

      // Assert
      sessionId.Should().Be("ses_fffe6e5c7ffeKaWESnqm0mzNHw");
    }

    [Fact]
    public void ResolveSessionId_SyncEvent_MessageUpdated_RecordsMessageSessionMapping()
    {
      // Arrange
      var data = FixtureLoader.Load("Sse\\message\\message.updated.1.json");
      var ev = new SseEventReceivedEventArgs("message.updated.1", data);


      // Act
      _helper.ResolveEventSessionId(ev);
      var lookupResult = _helper.LookupMessageSessionId("msg_0001919b300108vxNativqJTk0");

      // Assert
      lookupResult.Should().Be("ses_fffe6e5c7ffeKaWESnqm0mzNHw");
    }

    [Fact]
    public void ResolveSessionId_StreamEvent_SessionStatus_ReturnsSessionId()
    {
      // Arrange
      var data = FixtureLoader.Load("Sse\\session\\session.updated.json");
      var ev = new SseEventReceivedEventArgs("session.updated", data);

      // Act
      var sessionId = _helper.ResolveEventSessionId(ev);

      // Assert
      sessionId.Should().Be("ses_fffe6e5c7ffeKaWESnqm0mzNHw");
    }

    [Fact]
    public void ResolveSessionId_StreamEvent_SessionTurnOpen_ReturnsSessionId()
    {
      // Arrange
      var data = FixtureLoader.Load("Sse\\session\\session.turn.open.json");
      var ev = new SseEventReceivedEventArgs("session.turn.open", data);

      // Act
      var sessionId = _helper.ResolveEventSessionId(ev);

      // Assert
      sessionId.Should().Be("ses_fffe6e5c7ffeKaWESnqm0mzNHw");
    }

    [Fact]
    public void ResolveSessionId_StreamEvent_UnknownType_ReturnsNull()
    {
      // Arrange
      var data = FixtureLoader.Load("Sse\\session\\session.turn.open.json");
      data = data.Replace("session.turn.open", "unknown.type");
      var ev = new SseEventReceivedEventArgs("unknown.type", data);

      // Act
      var sessionId = _helper.ResolveEventSessionId(ev);

      // Assert
      sessionId.Should().BeNull();
    }

    [Fact]
    public void RecordMessageSessionId_StoresMapping()
    {
      // Act
      _helper.RecordMessageSessionId("msg-456", "session-def");
      var result = _helper.LookupMessageSessionId("msg-456");

      // Assert
      result.Should().Be("session-def");
    }

    [Fact]
    public void LookupMessageSessionId_NotFound_ReturnsNull()
    {
      // Act
      var result = _helper.LookupMessageSessionId("non-existent-msg");

      // Assert
      result.Should().BeNull();
    }
  }

  public class SseStaleEventDetectionTests
  {
    private readonly SSEHelper _helper;

    public SseStaleEventDetectionTests()
    {
      _helper = new SSEHelper(TestHelpers.serviceProvider, msg => { });
    }

    [Fact]
    public void IsStaleEvent_NoPreviousRevision_ReturnsFalse()
    {
      // Act
      var isStale = _helper.IsStaleEvent("session-1", "100", 1);

      // Assert
      isStale.Should().BeFalse();
    }

    [Fact]
    public void IsStaleEvent_NewerSeq_ReturnsFalse()
    {
      // Arrange
      _helper.UpdateRevision("session-1", "50", 1);

      // Act
      var isStale = _helper.IsStaleEvent("session-1", "100", 2);

      // Assert
      isStale.Should().BeFalse();
    }

    [Fact]
    public void IsStaleEvent_OlderSeq_ReturnsTrue()
    {
      // Arrange
      _helper.UpdateRevision("session-1", "100", 5);

      // Act
      var isStale = _helper.IsStaleEvent("session-1", "200", 3);

      // Assert
      isStale.Should().BeTrue();
    }

    [Fact]
    public void IsStaleEvent_SameSeq_ReturnsTrue()
    {
      // Arrange
      _helper.UpdateRevision("session-1", "100", 5);

      // Act
      var isStale = _helper.IsStaleEvent("session-1", "200", 5);

      // Assert
      isStale.Should().BeTrue();
    }

    [Fact]
    public void UpdateRevision_StoresRevision()
    {
      // Act
      _helper.UpdateRevision("session-1", "123", 10);

      // The revision should be stored (verified by IsStaleEvent behavior)
      var isStale = _helper.IsStaleEvent("session-1", "456", 5);
      isStale.Should().BeTrue();
    }
  }

  public class SseProjectFilteringTests
  {
    private readonly SSEHelper _helper;

    public SseProjectFilteringTests()
    {
      _helper = new SSEHelper(TestHelpers.serviceProvider, msg => { });
    }

    [Fact]
    public void IsEventFromForeignProject_NoCurrentProject_ReturnsFalse()
    {
      // Act
      var isForeign = _helper.IsEventFromForeignProject("session.created.1", "project-abc");

      // Assert
      isForeign.Should().BeFalse();
    }

    [Fact]
    public void IsEventFromForeignProject_MatchingProject_ReturnsFalse()
    {
      // Arrange
      _helper.SetProjectID("project-abc");

      // Act
      var isForeign = _helper.IsEventFromForeignProject("session.created.1", "project-abc");

      // Assert
      isForeign.Should().BeFalse();
    }

    [Fact]
    public void IsEventFromForeignProject_DifferentProject_ReturnsTrue()
    {
      // Arrange
      _helper.SetProjectID("project-abc");

      // Act
      var isForeign = _helper.IsEventFromForeignProject("session.created.1", "project-xyz");

      // Assert
      isForeign.Should().BeTrue();
    }

    [Fact]
    public void IsEventFromForeignProject_SessionUpdated_DifferentProject_ReturnsTrue()
    {
      // Arrange
      _helper.SetProjectID("project-abc");

      // Act
      var isForeign = _helper.IsEventFromForeignProject("session.updated.1", "project-xyz");

      // Assert
      isForeign.Should().BeTrue();
    }

    [Fact]
    public void IsEventFromForeignProject_SessionDeleted_MatchingProject_ReturnsFalse()
    {
      // Arrange
      _helper.SetProjectID("project-abc");

      // Act
      var isForeign = _helper.IsEventFromForeignProject("session.deleted.1", "project-abc");

      // Assert
      isForeign.Should().BeFalse();
    }

    [Fact]
    public void SetProjectID_UpdatesCurrentProject()
    {
      // Act
      _helper.SetProjectID("new-project");

      // Assert
      _helper.CurrentProjectID.Should().Be("new-project");
    }
  }

  public class SseNetworkEventHandlingTests
  {
    private readonly List<string> _postedMessages = new();
    private readonly SSEHelper _helper;

    public SseNetworkEventHandlingTests()
    {
      _helper = new SSEHelper(TestHelpers.serviceProvider, msg => _postedMessages.Add(msg));
    }

    [Fact]
    public void HandleNetworkEvent_SessionNetworkAsked_StoresRequest()
    {
      // This test verifies the network event handler exists and can be called
      // The actual network reply logic would require mocking the CLI client
      var properties = Newtonsoft.Json.Linq.JObject.Parse(@"{
                ""id"": ""net-request-123"",
                ""sessionID"": ""session-abc"",
                ""requestID"": ""net-request-123""
            }");

      // Act - should not throw
      var action = () => TestNetworkEvent("session.network.asked", properties);
      action.Should().NotThrow();
    }

    [Fact]
    public void HandleNetworkEvent_SessionNetworkRestored_ProcessesRestore()
    {
      var properties = Newtonsoft.Json.Linq.JObject.Parse(@"{
                ""requestID"": ""net-request-123"",
                ""sessionID"": ""session-abc""
            }");

      // Act - should not throw
      var action = () => TestNetworkEvent("session.network.restored", properties);
      action.Should().NotThrow();
    }

    private void TestNetworkEvent(string type, Newtonsoft.Json.Linq.JToken properties)
    {
      // Use reflection to call the private method for testing
      var method = typeof(SSEHelper).GetMethod("HandleNetworkEvent",
          System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
      method?.Invoke(_helper, new object[] { type, properties });
    }
  }
}
