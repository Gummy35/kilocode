using System;
using FluentAssertions;
using KiloVisualStudioExtension.ApiClient.Sse;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace KiloVisualStudioExtension.Tests.ApiClient
{
    /// <summary>
    /// Tests SSE event deserialization using EXACT messages from Wireshark traces.
    /// Verifies both legacy format (session.created) and current format (session.created.1).
    /// </summary>
    public class SseRealMessageTests
    {
        private readonly ITestOutputHelper _output;

        public SseRealMessageTests(ITestOutputHelper output)
        {
            _output = output;
        }

        #region Legacy Format - Stream Events (no .1 suffix)

        [Fact]
        public void Deserialize_SessionCreated_StreamEvent_Trace2b1()
        {
            // Arrange - EXACT data from trace 2b1
            var data = @"{""directory"":""C:\\prog\\kilocode\\kilocode"",""project"":""1964d2a94e8019106135ae62a9554d1484853591"",""payload"":{""id"":""evt_000191a3a001oJQJ0UZ68KHzih"",""type"":""session.created"",""properties"":{""sessionID"":""ses_fffe6e5c7ffeKaWESnqm0mzNHw"",""info"":{""id"":""ses_fffe6e5c7ffeKaWESnqm0mzNHw"",""slug"":""hidden-sailor"",""projectID"":""1964d2a94e8019106135ae62a9554d1484853591"",""directory"":""C:\\prog\\kilocode\\kilocode"",""path"":"""",""cost"":0,""tokens"":{""input"":0,""output"":0,""reasoning"":0,""cache"":{""read"":0,""write"":0}},""title"":""New session - 2026-08-14T11:47:20.248Z"",""version"":""7.4.22"",""metadata"":{""kilocode.sandbox"":{""enabled"":false,""version"":0}},""time"":{""created"":1786708040248,""updated"":1786708040248}}}}}";

            // Act
            var result = SseEventDeserializer.Deserialize(data);

            // Assert
            result.Should().BeOfType<GenericStreamEvent>();
            var streamEvent = (GenericStreamEvent)result;
            
            streamEvent.EventType.Should().Be("session.created");
            streamEvent.Directory.Should().Be("C:\\prog\\kilocode\\kilocode");
            
            var properties = streamEvent.Properties;
            properties["sessionID"].Value<string>().Should().Be("ses_fffe6e5c7ffeKaWESnqm0mzNHw");
            
            var info = properties["info"];
            info["id"].Value<string>().Should().Be("ses_fffe6e5c7ffeKaWESnqm0mzNHw");
            info["slug"].Value<string>().Should().Be("hidden-sailor");
            info["version"].Value<string>().Should().Be("7.4.22");
            info["time"]["created"].Value<long>().Should().Be(1786708040248L);
        }

        [Fact]
        public void Deserialize_MessageUpdated_StreamEvent_Trace396()
        {
            // Arrange - EXACT data from trace 396/40b
            var data = @"{""directory"":""C:\\prog\\kilocode\\kilocode"",""project"":""1964d2a94e8019106135ae62a9554d1484853591"",""payload"":{""id"":""evt_000191e69001rUd2VtqDzcG904"",""type"":""message.updated"",""properties"":{""sessionID"":""ses_fffe6e5c7ffeKaWESnqm0mzNHw"",""info"":{""id"":""msg_0001919b300108vxNativqJTk0"",""sessionID"":""ses_fffe6e5c7ffeKaWESnqm0mzNHw"",""role"":""user"",""time"":{""created"":1786708040838},""agent"":""code"",""model"":{""providerID"":""openrama"",""modelID"":""qwen3.5-122b""},""tools"":{},""editorContext"":{""visibleFiles"":[""packages\\\\kilo-visualstudio\\\\KiloVisualStudioExtension.Tests\\\\SseEventRoutingTests.cs""],""openTabs"":[""packages/opencode/test/kilocode/legacy-sse-event.test.ts"",""packages/kilo-visualstudio/KiloVisualStudioExtension.Tests/SseEventRoutingTests.cs""],""activeFile"":""packages\\\\kilo-visualstudio\\\\KiloVisualStudioExtension.Tests\\\\SseEventRoutingTests.cs"",""shell"":""C:\\\\WINDOWS\\\\System32\\\\WindowsPowerShell\\\\v1.0\\\\powershell.exe""}}}}}";

            // Act
            var result = SseEventDeserializer.Deserialize(data);

            // Assert
            result.Should().BeOfType<MessageUpdatedStreamEvent>();
            var streamEvent = (MessageUpdatedStreamEvent)result;
            
            streamEvent.EventType.Should().Be("message.updated");
            streamEvent.SessionID.Should().Be("ses_fffe6e5c7ffeKaWESnqm0mzNHw");
            
            var info = streamEvent.Properties["info"];
            info["id"].Value<string>().Should().Be("msg_0001919b300108vxNativqJTk0");
            info["role"].Value<string>().Should().Be("user");
            info["agent"].Value<string>().Should().Be("code");
        }

   
        [Fact]
        public void Deserialize_MessagePartUpdated_StreamEvent_Trace1ca()
        {
            // Arrange 
            var data = FixtureLoader.Load("Sse\\message-part-updated\\message.part.updated.json");
            // Act
            var result = SseEventDeserializer.Deserialize(data);

            // Assert
            result.Should().BeOfType<MessagePartUpdatedStreamEvent>();
            var streamEvent = (MessagePartUpdatedStreamEvent)result;
            
            streamEvent.EventType.Should().Be("message.part.updated");
            streamEvent.SessionID.Should().Be("ses_fffe6e5c7ffeKaWESnqm0mzNHw");
            
            var part = streamEvent.Properties["part"];
            part["id"].Value<string>().Should().Be("prt_000191e66001EvgiiWeEGdi8ga");
            part["messageID"].Value<string>().Should().Be("msg_0001919b300108vxNativqJTk0");
            part["type"].Value<string>().Should().Be("text");
            part["text"].Value<string>().Should().Be("list files in current directory");
        }

    [Fact]
    public void Deserialize_MessagePartUpdated1_SyncEvent_Trace()
    {
      // Arrange 
      var data = FixtureLoader.Load("Sse\\message-part-updated\\message.part.updated.1.json");
      // Act
      var result = SseEventDeserializer.Deserialize(data);

      // Assert
      result.Should().BeOfType<MessagePartUpdatedStreamEvent>();
      var streamEvent = (MessagePartUpdatedStreamEvent)result;

      streamEvent.EventType.Should().Be("message.part.updated");
      streamEvent.SessionID.Should().Be("ses_fffe6e5c7ffeKaWESnqm0mzNHw");

      var part = streamEvent.Properties["part"];
      part["id"].Value<string>().Should().Be("prt_000191e66001EvgiiWeEGdi8ga");
      part["messageID"].Value<string>().Should().Be("msg_0001919b300108vxNativqJTk0");
      part["type"].Value<string>().Should().Be("text");
      part["text"].Value<string>().Should().Be("list files in current directory");
    }


    [Fact]
        public void Deserialize_SessionUpdated_StreamEvent_Trace30a()
        {
      // Arrange
      var data = FixtureLoader.Load("Sse\\");// @"{""directory"":""C:\\prog\\kilocode\\kilocode"",""project"":""1964d2a94e8019106135ae62a9554d1484853591"",""payload"":{""id"":""evt_000191c9a001Cw4Be5ssCZZdA1"",""type"":""session.updated"",""properties"":{""sessionID"":""ses_fffe6e5c7ffeKaWESnqm0mzNHw"",""info"":{""id"":""ses_fffe6e5c7ffeKaWESnqm0mzNHw"",""slug"":""hidden-sailor"",""projectID"":""1964d2a94e8019106135ae62a9554d1484853591"",""directory"":""C:\\prog\\kilocode\\kilocode"",""path"":"""",""cost"":0,""tokens"":{""input"":0,""output"":0,""reasoning"":0,""cache"":{""read"":0,""write"":0}},""title"":""New session - 2026-08-14T11:47:20.248Z"",""agent"":""code"",""model"":{""id"":""qwen3.5-122b"",""providerID"":""openrama"",""variant"":""default""},""version"":""7.4.22"",""metadata"":{""kilocode.sandbox"":{""enabled"":false,""version"":0}},""time"":{""created"":1786708040248,""updated"":1786708040838}}}}}";

            // Act
            var result = SseEventDeserializer.Deserialize(data);

            // Assert
            result.Should().BeOfType<GenericStreamEvent>();
            var streamEvent = (GenericStreamEvent)result;
            
            streamEvent.EventType.Should().Be("session.updated");
            streamEvent.SessionID.Should().Be("ses_fffe6e5c7ffeKaWESnqm0mzNHw");
            
            var info = streamEvent.Properties["info"];
            info["agent"].Value<string>().Should().Be("code");
            info["model"]["id"].Value<string>().Should().Be("qwen3.5-122b");
            info["model"]["providerID"].Value<string>().Should().Be("openrama");
        }

        #endregion

        #region Current Format - Sync Events (.1 suffix)

        [Fact]
        public void Deserialize_SessionCreated1_SyncEvent_Trace326()
        {
            // Arrange - EXACT data from trace 326 (sync event wrapper format)
            var data = @"{""directory"":""C:\\prog\\kilocode\\kilocode"",""project"":""1964d2a94e8019106135ae62a9554d1484853591"",""payload"":{""type"":""sync"",""syncEvent"":{""id"":""evt_000191a3a001oJQJ0UZ68KHzih"",""type"":""session.created.1"",""seq"":0,""aggregateID"":""ses_fffe6e5c7ffeKaWESnqm0mzNHw"",""data"":{""sessionID"":""ses_fffe6e5c7ffeKaWESnqm0mzNHw"",""info"":{""id"":""ses_fffe6e5c7ffeKaWESnqm0mzNHw"",""slug"":""hidden-sailor"",""projectID"":""1964d2a94e8019106135ae62a9554d1484853591"",""directory"":""C:\\prog\\kilocode\\kilocode"",""path"":"""",""cost"":0,""tokens"":{""input"":0,""output"":0,""reasoning"":0,""cache"":{""read"":0,""write"":0}},""title"":""New session - 2026-08-14T11:47:20.248Z"",""version"":""7.4.22"",""metadata"":{""kilocode.sandbox"":{""enabled"":false,""version"":0}},""time"":{""created"":1786708040248,""updated"":1786708040248}}}},""id"":""evt_000191a3a001oJQJ0UZ68KHzih""}}";

            // Act
            var result = SseEventDeserializer.Deserialize(data);

            // Assert
            result.Should().BeOfType<SessionCreatedSyncEvent>();
            var syncEvent = (SessionCreatedSyncEvent)result;
            
            syncEvent.Name.Should().Be("session.created.1");
            syncEvent.Id.Should().Be("evt_000191a3a001oJQJ0UZ68KHzih");
            syncEvent.Seq.Should().Be(0);
            
            dynamic eventData = syncEvent.Data;
            eventData.Properties.SessionID.Should().Be("ses_fffe6e5c7ffeKaWESnqm0mzNHw");
            eventData.Properties.Info.Id.Should().Be("ses_fffe6e5c7ffeKaWESnqm0mzNHw");
            eventData.Properties.Info.Slug.Should().Be("hidden-sailor");
        }

        [Fact]
        public void Deserialize_SessionUpdated1_SyncEvent_Trace37f()
        {
            // Arrange - EXACT data from trace 37f (sync event wrapper format)
            var data = @"{""directory"":""C:\\prog\\kilocode\\kilocode"",""project"":""1964d2a94e8019106135ae62a9554d1484853591"",""payload"":{""type"":""sync"",""syncEvent"":{""id"":""evt_000191c9a001Cw4Be5ssCZZdA1"",""type"":""session.updated.1"",""seq"":1,""aggregateID"":""ses_fffe6e5c7ffeKaWESnqm0mzNHw"",""data"":{""sessionID"":""ses_fffe6e5c7ffeKaWESnqm0mzNHw"",""info"":{""id"":""ses_fffe6e5c7ffeKaWESnqm0mzNHw"",""slug"":""hidden-sailor"",""projectID"":""1964d2a94e8019106135ae62a9554d1484853591"",""directory"":""C:\\prog\\kilocode\\kilocode"",""path"":"""",""cost"":0,""tokens"":{""input"":0,""output"":0,""reasoning"":0,""cache"":{""read"":0,""write"":0}},""title"":""New session - 2026-08-14T11:47:20.248Z"",""agent"":""code"",""model"":{""id"":""qwen3.5-122b"",""providerID"":""openrama"",""variant"":""default""},""version"":""7.4.22"",""metadata"":{""kilocode.sandbox"":{""enabled"":false,""version"":0}},""time"":{""created"":1786708040248,""updated"":1786708040838}}}},""id"":""evt_000191c9a001Cw4Be5ssCZZdA1""}}";

            // Act
            var result = SseEventDeserializer.Deserialize(data);

            // Assert
            result.Should().BeOfType<SessionUpdatedSyncEvent>();
            var syncEvent = (SessionUpdatedSyncEvent)result;
            
            syncEvent.Name.Should().Be("session.updated.1");
            syncEvent.Id.Should().Be("evt_000191c9a001Cw4Be5ssCZZdA1");
            syncEvent.Seq.Should().Be(1);
            
            dynamic eventData = syncEvent.Data;
            eventData.Properties.SessionID.Should().Be("ses_fffe6e5c7ffeKaWESnqm0mzNHw");
            eventData.Properties.Info.Agent.Should().Be("code");
            eventData.Properties.Info.Model.Id.Should().Be("qwen3.5-122b");
        }

        #endregion

        #region Data Integrity - Verify No Data Loss

        [Fact]
        public void Deserialize_SessionCreated1_VerifyAllFields_Trace326()
        {
          // Arrange - EXACT data from trace 326
          var data =
"""
{
    "directory": "C:\\prog\\kilocode\\kilocode",
    "project": "1964d2a94e8019106135ae62a9554d1484853591",
    "payload": {
        "type": "sync",
        "syncEvent": {
            "id": "evt_000191a3a001oJQJ0UZ68KHzih",
            "type": "session.created.1",
            "seq": 0,
            "aggregateID": "ses_fffe6e5c7ffeKaWESnqm0mzNHw",
            "data": {
                "sessionID": "ses_fffe6e5c7ffeKaWESnqm0mzNHw",
                "info": {
                    "id": "ses_fffe6e5c7ffeKaWESnqm0mzNHw",
                    "slug": "hidden-sailor",
                    "projectID": "1964d2a94e8019106135ae62a9554d1484853591",
                    "directory": "C:\\prog\\kilocode\\kilocode",
                    "path": "",
                    "cost": 0,
                    "tokens": {
                        "input": 0,
                        "output": 0,
                        "reasoning": 0,
                        "cache": {
                            "read": 0,
                            "write": 0
                        }
                    },
                    "title": "New session - 2026-08-14T11:47:20.248Z",
                    "version": "7.4.22",
                    "metadata": {
                        "kilocode.sandbox": {
                            "enabled": false,
                            "version": 0
                        }
                    },
                    "time": {
                        "created": 1786708040248,
                        "updated": 1786708040248
                    }
                }
            }
        },
        "id": "evt_000191a3a001oJQJ0UZ68KHzih"
    }
}
""";

            // Act
            var result = SseEventDeserializer.Deserialize(data);

            // Assert
            var syncEvent = (SessionCreatedSyncEvent)result;
            dynamic eventData = syncEvent.Data;
            var info = eventData.Properties.Info;

            // Verify all fields from the trace are preserved
            info.Id.Should().Be("ses_fffe6e5c7ffeKaWESnqm0mzNHw");
            info.Slug.Should().Be("hidden-sailor");
            info.ProjectID.Should().Be("1964d2a94e8019106135ae62a9554d1484853591");
            info.Directory.Should().Be("C:\\prog\\kilocode\\kilocode");
            info.Path.Should().BeNullOrEmpty();
            info.Version.Should().Be("7.4.22");
            info.Title.Should().Be("New session - 2026-08-14T11:47:20.248Z");
            info.Time.Created.Should().Be(1786708040248L);
            info.Time.Updated.Should().Be(1786708040248L);
            info.Tokens.Input.Should().Be(0);
            info.Tokens.Output.Should().Be(0);
            info.Tokens.Reasoning.Should().Be(0);
            info.Tokens.Cache.Read.Should().Be(0);
            info.Tokens.Cache.Write.Should().Be(0);
        }

        #endregion
    }
}
