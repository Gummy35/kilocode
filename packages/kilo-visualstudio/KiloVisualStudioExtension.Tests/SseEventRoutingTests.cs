using FluentAssertions;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.ApiClient.Sse;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using SessionCreateRequest = KiloVisualStudioExtension.ApiClient.Body18;
using SessionPromptRequest = KiloVisualStudioExtension.ApiClient.Body20;

namespace KiloVisualStudioExtension.Tests
{
  /// <summary>
  /// Tests SSE event routing from the real CLI backend.
  /// Ports the behavioral contract from VS Code's connection-utils.test.ts
  /// Uses the real CLI via CliLauncher and strongly-typed SSE event classes.
  /// </summary>
  public class SseEventRoutingTests : IDisposable
  {
    private readonly ITestOutputHelper _output;
    private CliLauncher? _cliLauncher;
    private SseClient? _sseClient;

    private SSEHelper? _sseHelper;
    private KiloApiClient? _apiClient;
    private KiloConnectionService? _connectionService;
    private readonly System.Collections.Generic.List<Events> _receivedEvents;
    private readonly object _lock = new object();

    public SseEventRoutingTests(ITestOutputHelper output)
    {
      _output = output;
      _receivedEvents = new System.Collections.Generic.List<Events>();
    }

    public void Dispose()
    {
      _sseClient?.Dispose();
      _cliLauncher?.Dispose();
    }

    private async Task StartCliAndConnectSseAsync(CancellationToken cancellationToken)
    {
      // Start the real CLI
      _cliLauncher = new CliLauncher(_output);
      await _cliLauncher.StartAsync(cancellationToken);

      _cliLauncher.Should().NotBeNull("CLI should have started");
      _cliLauncher!.BaseUrl.Should().NotBeNullOrEmpty("Base URL should be set");
      _cliLauncher.Password.Should().NotBeNullOrEmpty("Password should be set");

      _output.WriteLine($"CLI started at {_cliLauncher.BaseUrl}");

      _apiClient = new KiloApiClient(_cliLauncher.BaseUrl, _cliLauncher.Password);

      _sseHelper = new SSEHelper(TestHelpers.serviceProvider, msg => { });

      // Connect SSE client to the real backend
      _sseClient = new SseClient(_cliLauncher.BaseUrl!, _cliLauncher.Password!);

      _sseClient.OnEvent += (sender, args) =>
      {
        lock (_lock)
        {
          try
          {
            var sseEvent = new SseEventReceivedEventArgs(args.EventType, args.Data);
            if (_sseHelper.FilterSSEEvent(sseEvent))
            {
              var evt = SseEventDeserializer.Deserialize(sseEvent);
              if (evt != null)
              {
                _receivedEvents.Add(evt);
                _output.WriteLine($"Received SSE event: {args.EventType} - {args.Data.Substring(0, Math.Min(100, args.Data.Length))}");
              }
              else
              {
                _output.WriteLine($"** Unhandled SSE event: {args.EventType} - {args.Data.Substring(0, Math.Min(100, args.Data.Length))}");
              }
            } else
            {
              _output.WriteLine($"** SSE event {args.EventType} filtered out");
            }
          }
          catch (Exception ex)
          {
            _output.WriteLine($"Failed to deserialize SSE event: {ex.Message}");
          }
        }
      };

      _sseClient.Connect();
      await Task.Delay(1000, cancellationToken); // Wait for connection to stabilize
    }

    [Fact]
    public async Task Sse_SessionUpdated_ReturnsSessionUpdatedSyncEvent()
    {
      // Arrange
      var cancellationToken = CancellationToken.None;
      await StartCliAndConnectSseAsync(cancellationToken);
      lock (_lock)
      {
        _receivedEvents.Clear();
      }
      // Create a session

      await _apiClient.Session_createAsync(AppContext.BaseDirectory, null,
        new SessionCreateRequest {
              
        });

      await Task.Delay(2000, cancellationToken);

      // Act - send a message that will generate parts
      object? createdEvent;
      lock (_lock)
      {
        createdEvent = _receivedEvents.FindLast(e =>
            e is Events sync && sync.Type == "session.created.1")?.Data;
      }

      if (createdEvent is EventSessionCreated createdSync)
      {
        var sessionID = createdSync.Properties.SessionID;
        // Send a message
        _apiClient.Session_promptAsync(sessionID, AppContext.BaseDirectory, null,
          new SessionPromptRequest
          {

            Parts = new List<Parts> {
              new TextPartInput
              {
                  Type = TextPartInputType.Text,
                  Text = "Hello"
              }
            }
          });

        // Wait for part updates (assistant response)
        await Task.Delay(5000, cancellationToken);

        // Assert
        List<Events> sessionUpdatedEvents;
        lock (_lock)
        {
          sessionUpdatedEvents = _receivedEvents.FindAll(e =>
              e is Events sync && sync.Type == "session.updated.1");
        }

        sessionUpdatedEvents.Should().NotBeEmpty("session.updated.1 events should be received during assistant response");

        var syncEvent = sessionUpdatedEvents[0].Data as EventSessionUpdated;
        syncEvent.Should().NotBeNull("message.part.updated.1 should deserialize to EventMessagePartUpdated");
      }
    }

    [Fact]
    public async Task Sse_MessagePartUpdated_ReturnsMessagePartUpdatedSyncEvent()
    {
      // Arrange
      var cancellationToken = CancellationToken.None;
      await StartCliAndConnectSseAsync(cancellationToken);
      lock (_lock)
      {
        _receivedEvents.Clear();
      }
      // Create a session

      await _apiClient.Session_createAsync(AppContext.BaseDirectory, null,
        new SessionCreateRequest { });

      await Task.Delay(2000, cancellationToken);

      // Act - send a message that will generate parts
      object? createdEvent;
      lock (_lock)
      {
        createdEvent = _receivedEvents.FindLast(e =>
            e is Events sync && sync.Type == "session.created.1")?.Data;
      }

      if (createdEvent is EventSessionCreated createdSync)
      {
        var sessionID = createdSync.Properties.SessionID;
        // Send a message
        _apiClient.Session_promptAsync(sessionID, AppContext.BaseDirectory, null,
          new SessionPromptRequest
          {
            
            Parts = new List<Parts> {
              new TextPartInput
              {
                  Type = TextPartInputType.Text,
                  Text = "Hello"
              }
            }
          });

        // Wait for part updates (assistant response)
        await Task.Delay(5000, cancellationToken);

        // Assert
        List<Events> partUpdatedEvents;
        lock (_lock)
        {
          partUpdatedEvents = _receivedEvents.FindAll(e =>
              e is Events sync && sync.Type == "message.part.updated.1");
        }

        partUpdatedEvents.Should().NotBeEmpty("message.part.updated.1 events should be received during assistant response");

        var syncEvent = partUpdatedEvents[0].Data as EventMessagePartUpdated;
        syncEvent.Should().NotBeNull("message.part.updated.1 should deserialize to EventMessagePartUpdated");
      }
    }

    [Fact]
    public async Task Sse_SessionStatus_ReturnsStreamEvent()
    {
      // Arrange
      var cancellationToken = CancellationToken.None;
      await StartCliAndConnectSseAsync(cancellationToken);
      lock (_lock)
      {
        _receivedEvents.Clear();
      }
      // Create a session

      await _apiClient.Session_createAsync(AppContext.BaseDirectory, null,
        new SessionCreateRequest { });

      await Task.Delay(2000, cancellationToken);

      // Act - send a message that will generate parts
      object? createdEvent;
      lock (_lock)
      {
        createdEvent = _receivedEvents.FindLast(e =>
            e is Events sync && sync.Type == "session.created.1")?.Data;
      }

      if (createdEvent is EventSessionCreated createdSync)
      {
        var sessionID = createdSync.Properties.SessionID;

        // Abort the session
        await _apiClient.Session_abortAsync(sessionID, AppContext.BaseDirectory, null);
        await Task.Delay(2000, cancellationToken);

        // Assert - session.status events are stream events
        List<Events> statusEvents;
        lock (_lock)
        {
          statusEvents = _receivedEvents.FindAll(e =>
            e is Events ev &&
            (ev.Type == "session.status" ||
             ev.Type == "session.aborted"));
        }

        statusEvents.Should().NotBeEmpty("session.status or session.aborted stream events should be received");
      }
    }

    [Fact]
    public async Task Sse_EventsReceived_VerifySseConnectionWorks()
    {
      // Arrange
      var cancellationToken = CancellationToken.None;
      await StartCliAndConnectSseAsync(cancellationToken);
      lock (_lock)
      {
        _receivedEvents.Clear();
      }
      // Create a session

      await _apiClient.Session_createAsync(AppContext.BaseDirectory, null,
        new SessionCreateRequest { });

      await Task.Delay(2000, cancellationToken);

      // Act - send a message that will generate parts
      object? createdEvent;
      lock (_lock)
      {
        _receivedEvents.Should().NotBeEmpty("SSE connection should receive events from session creation");
      }
    }
  }
}
