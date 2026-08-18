using System;
using FluentAssertions;
using KiloVisualStudioExtension.ApiClient;
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
      // Arrange 
      var data = FixtureLoader.Load("Sse\\session\\session.created.json");
      var ev = new SseEventReceivedEventArgs("session.created", data);

      // Act
      var result = SseEventDeserializer.Deserialize(ev);

      // Assert
      ev.EventType.Should().Be("session.created");
      ev.Directory.Should().Be("C:\\prog\\kilocode\\kilocode");
      ev.Project.Should().Be("1964d2a94e8019106135ae62a9554d1484853591");
      result.AggregateID.Should().Be("");
      result.Seq.Should().Be(0);
      result.Id.Should().Be("evt_000191a3a001oJQJ0UZ68KHzih");
      result.Data.Should().BeOfType<EventSessionCreated>();
      var streamEvent = (EventSessionCreated)result.Data;

      streamEvent.Type.Should().Be(EventSessionCreatedType.Session_created);
      var p = streamEvent.Properties;
      p.SessionID.Should().Be("ses_fffe6e5c7ffeKaWESnqm0mzNHw");
      p.Info.Should().BeOfType<Session>();
      var info = (Session)p.Info;
      info.Id.Should().Be("ses_fffe6e5c7ffeKaWESnqm0mzNHw");
      info.Slug.Should().Be("hidden-sailor");
      info.ProjectID.Should().Be("1964d2a94e8019106135ae62a9554d1484853591");
      info.Directory.Should().Be("C:\\prog\\kilocode\\kilocode");
      info.Path.Should().Be("path");
      info.Cost.Should().Be(1);
      info.Tokens.Input.Should().Be(2);
      info.Tokens.Output.Should().Be(3);
      info.Tokens.Reasoning.Should().Be(4);
      info.Tokens.Cache.Read.Should().Be(5);
      info.Tokens.Cache.Write.Should().Be(6);
      info.Title.Should().Be("New session - 2026-08-14T11:47:20.248Z");
      info.Version.Should().Be("7.4.22");      
      info.Time.Created.Should().Be(1786708040248);
      info.Time.Updated.Should().Be(1786708040248);
    }

    [Fact]
    public void Deserialize_MessageUpdated_StreamEvent_Trace396()
    {
      // Arrange - EXACT data from trace 396/40b
      var data = FixtureLoader.Load("Sse\\message\\message.updated.json");
      var ev = new SseEventReceivedEventArgs("message.updated", data);

      // Act
      var result = SseEventDeserializer.Deserialize(ev);

      // Assert
      result.Should().BeOfType<MessageUpdatedStreamEvent>();
      //var streamEvent = (MessageUpdatedStreamEvent)result;

      //streamEvent.EventType.Should().Be("message.updated");
      //streamEvent.SessionID.Should().Be("ses_fffe6e5c7ffeKaWESnqm0mzNHw");

      //var info = streamEvent.Properties["info"];
      //info["id"].Value<string>().Should().Be("msg_0001919b300108vxNativqJTk0");
      //info["role"].Value<string>().Should().Be("user");
      //info["agent"].Value<string>().Should().Be("code");
    }


    [Fact]
    public void Deserialize_MessagePartUpdated_StreamEvent_Trace1ca()
    {
      // Arrange 
      var data = FixtureLoader.Load("Sse\\message-part\\message.part.updated.json");
      var ev = new SseEventReceivedEventArgs("message.part.updated", data);

      // Act
      var result = SseEventDeserializer.Deserialize(ev);

      // Assert
      ev.EventType.Should().Be("message.part.updated");
      ev.Directory.Should().Be("C:\\prog\\kilocode\\kilocode");
      ev.Project.Should().Be("1964d2a94e8019106135ae62a9554d1484853591");
      result.AggregateID.Should().Be("");
      result.Seq.Should().Be(0);
      result.Id.Should().Be("evt_000191e76001j68utheQHccIJv");
      result.Data.Should().BeOfType<EventMessagePartUpdated>();
      var streamEvent = (EventMessagePartUpdated)result.Data;

      streamEvent.Type.Should().Be(EventMessagePartUpdatedType.Message_part_updated);
      var p = streamEvent.Properties;
      p.SessionID.Should().Be("ses_fffe6e5c7ffeKaWESnqm0mzNHw");
      p.Part.Should().BeOfType<TextPart>();
      var part = (TextPart)p.Part;
      part.Id.Should().Be("prt_000191e66001EvgiiWeEGdi8ga");
      part.MessageID.Should().Be("msg_0001919b300108vxNativqJTk0");
      part.Text.Should().Be("list files in current directory");
    }

    [Fact]
    public void Deserialize_MessagePartUpdated1_SyncEvent_Trace()
    {
      // Arrange 
      var data = FixtureLoader.Load("Sse\\message-part\\message.part.updated.1.json");
      var ev = new SseEventReceivedEventArgs("message.part.updated.1", data);

      // Act
      var result = SseEventDeserializer.Deserialize(ev);

      // Assert
      ev.EventType.Should().Be("message.part.updated.1");
      ev.Directory.Should().Be("C:\\prog\\kilocode\\kilocode");
      ev.Project.Should().Be("1964d2a94e8019106135ae62a9554d1484853591");
      result.AggregateID.Should().Be("ses_fffe6e5c7ffeKaWESnqm0mzNHw");
      result.Seq.Should().Be(3);
      result.Id.Should().Be("evt_000191e76001j68utheQHccIJv");
      result.Data.Should().BeOfType<EventMessagePartUpdated>();

      var streamEvent = (EventMessagePartUpdated)result.Data;

      streamEvent.Type.Should().Be(EventMessagePartUpdatedType.Message_part_updated);
      var p = streamEvent.Properties;
      p.SessionID.Should().Be("ses_fffe6e5c7ffeKaWESnqm0mzNHw");
      p.Part.Should().BeOfType<TextPart>();
      var part = (TextPart)p.Part;
      part.Id.Should().Be("prt_000191e66001EvgiiWeEGdi8ga");
      part.MessageID.Should().Be("msg_0001919b300108vxNativqJTk0");
      part.Text.Should().Be("list files in current directory");
    }


    [Fact]
    public void Deserialize_SessionUpdated_StreamEvent_Trace30a()
    {
      // Arrange
      var data = FixtureLoader.Load("Sse\\session\\session.updated.json");

      var ev = new SseEventReceivedEventArgs("session.updated", data);

      // Act
      var result = SseEventDeserializer.Deserialize(ev);

      // Assert
      result.Should().BeOfType<GenericStreamEvent>();
      //var streamEvent = (GenericStreamEvent)result;

      //streamEvent.EventType.Should().Be("session.updated");
      //streamEvent.SessionID.Should().Be("ses_fffe6e5c7ffeKaWESnqm0mzNHw");

      //var info = streamEvent.Properties["info"];
      //info["agent"].Value<string>().Should().Be("code");
      //info["model"]["id"].Value<string>().Should().Be("qwen3.5-122b");
      //info["model"]["providerID"].Value<string>().Should().Be("openrama");
    }

    #endregion

    #region Current Format - Sync Events (.1 suffix)

    [Fact]
    public void Deserialize_SessionCreated1_SyncEvent_Trace326()
    {
      // Arrange 
      var data = FixtureLoader.Load("Sse\\session\\session.created.1.json");
      var ev = new SseEventReceivedEventArgs("session.created.1", data);

      // Act
      var result = SseEventDeserializer.Deserialize(ev);

      // Assert
      ev.EventType.Should().Be("session.created.1");
      ev.Directory.Should().Be("C:\\prog\\kilocode\\kilocode");
      ev.Project.Should().Be("1964d2a94e8019106135ae62a9554d1484853591");
      result.AggregateID.Should().Be("ses_fffe6e5c7ffeKaWESnqm0mzNHw");
      result.Seq.Should().Be(1);
      result.Id.Should().Be("evt_000191a3a001oJQJ0UZ68KHzih");
      result.Data.Should().BeOfType<EventSessionCreated>();
      var streamEvent = (EventSessionCreated)result.Data;

      streamEvent.Type.Should().Be(EventSessionCreatedType.Session_created);
      var p = streamEvent.Properties;
      p.SessionID.Should().Be("ses_fffe6e5c7ffeKaWESnqm0mzNHw");
      p.Info.Should().BeOfType<Session>();
      var info = (Session)p.Info;
      info.Id.Should().Be("ses_fffe6e5c7ffeKaWESnqm0mzNHw");
      info.Slug.Should().Be("hidden-sailor");
      info.ProjectID.Should().Be("1964d2a94e8019106135ae62a9554d1484853591");
      info.Directory.Should().Be("C:\\prog\\kilocode\\kilocode");
      info.Path.Should().Be("path");
      info.Cost.Should().Be(1);
      info.Tokens.Input.Should().Be(2);
      info.Tokens.Output.Should().Be(3);
      info.Tokens.Reasoning.Should().Be(4);
      info.Tokens.Cache.Read.Should().Be(5);
      info.Tokens.Cache.Write.Should().Be(6);
      info.Title.Should().Be("New session - 2026-08-14T11:47:20.248Z");
      info.Version.Should().Be("7.4.22");
      info.Time.Created.Should().Be(1786708040248);
      info.Time.Updated.Should().Be(1786708040248);
    }

    [Fact]
    public void Deserialize_SessionUpdated1_SyncEvent_Trace37f()
    {
      // Arrange - EXACT data from trace 37f (sync event wrapper format)
      var data = FixtureLoader.Load("Sse\\session\\session.updated.1.json");
      var ev = new SseEventReceivedEventArgs("session.updated.1", data);

      // Act
      var result = SseEventDeserializer.Deserialize(ev);

      // Assert
      result.Should().BeOfType<SessionUpdatedSyncEvent>();
      //var syncEvent = (SessionUpdatedSyncEvent)result;

      //syncEvent.Name.Should().Be("session.updated.1");
      //syncEvent.Id.Should().Be("evt_000191c9a001Cw4Be5ssCZZdA1");
      //syncEvent.Seq.Should().Be(1);

      //dynamic eventData = syncEvent.Data;
      //eventData.Properties.SessionID.Should().Be("ses_fffe6e5c7ffeKaWESnqm0mzNHw");
      //eventData.Properties.Info.Agent.Should().Be("code");
      //eventData.Properties.Info.Model.Id.Should().Be("qwen3.5-122b");
    }

    #endregion


  }
}
