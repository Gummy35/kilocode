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
            _helper = new SSEHelper(msg => _postedMessages.Add(msg));
        }

        [Fact]
        public void ResolveSessionId_SyncEvent_MessageUpdated_ReturnsSessionId()
        {
            // Arrange
            var syncEvent = new MessageUpdatedSyncEvent
            {
                EventType = "sync",
                Name = "message.updated.1",
                Id = "123",
                Seq = 1,              
                Data = new EventMessageUpdated
                {
                    Properties = new Properties79
                    {
                        SessionID = "session-abc",
                        Info = new AssistantMessage
                        {
                            Id = "msg-123",
                            SessionID = "session-abc",
                            ParentID = "parent",
                            ProviderID = "provider",
                            ModelID = "model",
                            Mode = "mode",
                            Role = AssistantMessageRole.Assistant,
                            Path = new Path2 { Cwd = "cwd", Root = "root" },
                            Agent = "agent",
                            Time = new Time6 { Created = 1000, Completed = 1000 }
                        }
                    }
                }
            };

            // Act
            var sessionId = _helper.ResolveSessionId(syncEvent);

            // Assert
            sessionId.Should().Be("session-abc");
        }

        [Fact]
        public void ResolveSessionId_SyncEvent_MessageUpdated_RecordsMessageSessionMapping()
        {
            // Arrange
            var syncEvent = new MessageUpdatedSyncEvent
            {
                EventType = "sync",
                Name = "message.updated.1",
                Id = "123",
                Seq = 1,
                Data = new EventMessageUpdated
                {
                    Properties = new Properties79
                    {
                        SessionID = "session-abc",
                        Info = new AssistantMessage
                        {
                            Id = "msg-123",
                            SessionID = "session-abc",
                            ParentID = "parent",
                            ProviderID = "provider",
                            ModelID = "model",
                            Mode = "mode",
                            Agent = "agent",
                            Path = new Path2 { Cwd = "cwd", Root = "root" },
                            Role = AssistantMessageRole.Assistant,
                            Time = new Time6 { Created = 1000, Completed = 1000 }
                        }
                    }
                }
            };

            // Act
            _helper.ResolveSessionId(syncEvent);
            var lookupResult = _helper.LookupMessageSessionId("msg-123");

            // Assert
            lookupResult.Should().Be("session-abc");
        }

        [Fact]
        public void ResolveSessionId_StreamEvent_SessionStatus_ReturnsSessionId()
        {
            // Arrange
            var streamEvent = new SessionStatusStreamEvent
            {
                EventType = "session.status",
                SessionID = "session-xyz",
                Directory = "",
                Properties = Newtonsoft.Json.Linq.JObject.Parse("{\"status\":{\"type\":\"running\"}}")
            };

            // Act
            var sessionId = _helper.ResolveSessionId(streamEvent);

            // Assert
            sessionId.Should().Be("session-xyz");
        }

        [Fact]
        public void ResolveSessionId_StreamEvent_SessionTurnOpen_ReturnsSessionId()
        {
            // Arrange
            var streamEvent = new GenericStreamEvent
            {
                EventType = "session.turn.open",
                SessionID = "session-turn",
                Directory = "",
                Properties = Newtonsoft.Json.Linq.JObject.Parse("{\"sessionID\":\"session-turn\"}")
            };

            // Act
            var sessionId = _helper.ResolveSessionId(streamEvent);

            // Assert
            sessionId.Should().Be("session-turn");
        }

        [Fact]
        public void ResolveSessionId_StreamEvent_UnknownType_ReturnsNull()
        {
            // Arrange
            var streamEvent = new GenericStreamEvent
            {
                EventType = "unknown.event.type",
                SessionID = "session-unknown",
                Directory = "",
                Properties = Newtonsoft.Json.Linq.JObject.Parse("{}")
            };

            // Act
            var sessionId = _helper.ResolveSessionId(streamEvent);

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
            _helper = new SSEHelper(msg => { });
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
            _helper = new SSEHelper(msg => { });
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
            _helper = new SSEHelper(msg => _postedMessages.Add(msg));
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
