using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using KiloVisualStudioExtension.Services.Handlers.Session;
using Microsoft.VisualStudio.Shell;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
  public class MessageLoadingTests
  {
    [Fact]
    public async Task HandleLoadMessages_StopsProcessesForPreviousSession()
    {
      // Arrange
      var httpClient = new TestHttpClient();
      var webView = new TestWebView();
      var connectionService = new TestConnectionService(httpClient);

      // Setup mock for s2 messages
      var message1 = TestHelpers.CreateMessage("m1", "user", 1000);
      var message2 = TestHelpers.CreateMessage("m2", "assistant", 2000);
      httpClient.RegisterJsonHandler("GET", "/session", _ => TestHelpers.CreateMessagesResponse(message1, message2));

      // Create a provider-like scenario
      var sessionHelper = TestHelpers.serviceProvider.GetService<SessionHandlerService>();
      //sessionHelper.SetCurrentSession("s1");

      // Act - simulate loading messages for s2 (which would stop s1 processes)
      var loadPayload = JsonDocument.Parse("{\"sessionID\": \"s2\", \"mode\": \"replace\"}").RootElement;
      var sessionID = loadPayload.TryGetProperty("sessionID", out var sid) ? sid.GetString() : "";
      // sessionHelper.SetCurrentSession(sessionID);

      // Simulate the HTTP call
      var response = await httpClient.GetAsync($"/session/{sessionID}/message");

      // Assert - current session should be s2 now
      // sessionHelper.CurrentSessionID.Should().Be("s2");
      throw new NotImplementedException();
    }

    [Fact]
    public async Task HandleLoadMessages_FocusMode_ReconcilesTail()
    {
      // Arrange
      var httpClient = new TestHttpClient();
      var webView = new TestWebView();
      var connectionService = new TestConnectionService(httpClient);

      // Setup mock to return 3 messages
      var message1 = TestHelpers.CreateMessage("m1", "user", 1000);
      var message2 = TestHelpers.CreateMessage("m2", "assistant", 2000);
      var message3 = TestHelpers.CreateMessage("m3", "user", 3000);
      httpClient.RegisterJsonHandler("GET", "/session", _ => TestHelpers.CreateMessagesResponse(message1, message2, message3));

      var sseHelper = TestHelpers.CreateSSEHelper(webView.PostMessage);

      // Act - load messages with focus mode
      var loadPayload = JsonDocument.Parse("{\"sessionID\": \"s1\", \"mode\": \"focus\"}").RootElement;
      var sessionID = loadPayload.TryGetProperty("sessionID", out var sid) ? sid.GetString() : "";
      // sseHelper.SetCurrentSession(sessionID);

      // Assert - session should be tracked
      //sseHelper.IsSessionTracked("s1").Should().BeTrue();
      throw new NotImplementedException();
    }

    [Fact]
    public async Task HandleLoadMessages_ReplaceMode_LoadsMessages()
    {
      // Arrange
      var httpClient = new TestHttpClient();
      var webView = new TestWebView();
      var connectionService = new TestConnectionService(httpClient);

      // Setup mock to return messages
      var message1 = TestHelpers.CreateMessage("m1", "user", 1000);
      var message2 = TestHelpers.CreateMessage("m2", "assistant", 2000);
      httpClient.RegisterJsonHandler("GET", "/session", _ => TestHelpers.CreateMessagesResponse(message1, message2));

      var sseHelper = TestHelpers.CreateSSEHelper(webView.PostMessage);

      // Act - load messages with replace mode
      var loadPayload = JsonDocument.Parse("{\"sessionID\": \"s1\", \"mode\": \"replace\"}").RootElement;
      var sessionID = loadPayload.TryGetProperty("sessionID", out var sid) ? sid.GetString() : "";
      //    sseHelper.SetCurrentSession(sessionID);

      // Assert
      //    sseHelper.CurrentSessionID.Should().Be("s1");
      //    sseHelper.IsSessionTracked("s1").Should().BeTrue();
      throw new NotImplementedException();
    
    }

    [Fact]
    public async Task HandleLoadMessages_PrependMode_DoesNotAbort()
    {
      // Arrange
      var httpClient = new TestHttpClient();
      var webView = new TestWebView();
      var connectionService = new TestConnectionService(httpClient);

      // Setup mock to return messages
      var message1 = TestHelpers.CreateMessage("m1", "user", 1000);
      httpClient.RegisterJsonHandler("GET", "/session", _ => TestHelpers.CreateMessagesResponse(message1));

      var sseHelper = TestHelpers.CreateSSEHelper(webView.PostMessage);

      // Act - load messages with prepend mode
      var loadPayload = JsonDocument.Parse("{\"sessionID\": \"s1\", \"mode\": \"prepend\", \"before\": \"m0\"}").RootElement;
      var sessionID = loadPayload.TryGetProperty("sessionID", out var sid) ? sid.GetString() : "";
      //    sseHelper.SetCurrentSession(sessionID);

      // Assert - should complete without error
      //    sseHelper.CurrentSessionID.Should().Be("s1");
      throw new NotImplementedException();
    }

    [Fact]
    public async Task HandleLoadMessages_ReplaceMode_AbortsPreviousLoad()
    {
      // Arrange
      var httpClient = new TestHttpClient();
      var webView = new TestWebView();
      var connectionService = new TestConnectionService(httpClient);

      // Setup mock to return messages
      var message1 = TestHelpers.CreateMessage("m1", "user", 1000);
      httpClient.RegisterJsonHandler("GET", "/session", _ => TestHelpers.CreateMessagesResponse(message1));

      var sseHelper = TestHelpers.CreateSSEHelper(webView.PostMessage);
  //    sseHelper.SetCurrentSession("s1");

      // Act - start load for s1, then start load for s2 with mode="replace"
      var loadPayload1 = JsonDocument.Parse("{\"sessionID\": \"s1\", \"mode\": \"replace\"}").RootElement;
      var sessionID1 = loadPayload1.TryGetProperty("sessionID", out var sid1) ? sid1.GetString() : "";
   //   sseHelper.SetCurrentSession(sessionID1);

      var loadPayload2 = JsonDocument.Parse("{\"sessionID\": \"s2\", \"mode\": \"replace\"}").RootElement;
      var sessionID2 = loadPayload2.TryGetProperty("sessionID", out var sid2) ? sid2.GetString() : "";
      //    sseHelper.SetCurrentSession(sessionID2);

      // Assert - s2 should be current (s1 was "aborted" by switching)
      //    sseHelper.CurrentSessionID.Should().Be("s2");
      throw new NotImplementedException();
    }

    [Fact]
    public void HandleLoadMessages_DeletedSession_DropsResponse()
    {
      // Arrange
      var sessionHelper = TestHelpers.serviceProvider.GetService<SessionHandlerService>();

      // Create and track a session
      sessionHelper.TrackSession("session-to-delete");
      sessionHelper.IsTrackedSession("session-to-delete").Should().BeTrue();

      // Delete the session (untrack it)
      sessionHelper.UntrackSession("session-to-delete");

      // Act - try to load messages for deleted session
      var isTracked = sessionHelper.IsTrackedSession("session-to-delete");

      // Assert - session should not be tracked
      isTracked.Should().BeFalse("deleted sessions should be untracked");
    }

    [Fact]
    public async Task HandleLoadMessages_WithLimit_RespectsLimit()
    {
      // Arrange
      var httpClient = new TestHttpClient();
      var webView = new TestWebView();
      var connectionService = new TestConnectionService(httpClient);

      // Setup mock to return messages
      var message1 = TestHelpers.CreateMessage("m1", "user", 1000);
      var message2 = TestHelpers.CreateMessage("m2", "assistant", 2000);
      httpClient.RegisterJsonHandler("GET", "/session", _ => TestHelpers.CreateMessagesResponse(message1, message2));

      var sseHelper = TestHelpers.CreateSSEHelper(webView.PostMessage);

      // Act - load with limit=2
      var loadPayload = JsonDocument.Parse("{\"sessionID\": \"s1\", \"mode\": \"replace\", \"limit\": 2}").RootElement;
      var sessionID = loadPayload.TryGetProperty("sessionID", out var sid) ? sid.GetString() : "";
      //    sseHelper.SetCurrentSession(sessionID);

      // Assert
      //   sseHelper.CurrentSessionID.Should().Be("s1");
      throw new NotImplementedException();
    }

    [Fact]
    public async Task HandleLoadMessages_WithBefore_Cursor_Paginates()
    {
      // Arrange
      var httpClient = new TestHttpClient();
      var webView = new TestWebView();
      var connectionService = new TestConnectionService(httpClient);

      // Setup mock to return messages
      var message1 = TestHelpers.CreateMessage("m1", "user", 1000);
      httpClient.RegisterJsonHandler("GET", "/session", _ => TestHelpers.CreateMessagesResponse(message1));

      var sessionHelper = TestHelpers.serviceProvider.GetService<SessionHandlerService>();

      // Act - load with before cursor
      var loadPayload = JsonDocument.Parse("{\"sessionID\": \"s1\", \"mode\": \"prepend\", \"before\": \"m-cursor-123\"}").RootElement;
      var sessionID = loadPayload.TryGetProperty("sessionID", out var sid) ? sid.GetString() : "";
      // sessionHelper.SetCurrentSession(sessionID);

      // Assert
      //   sseHelper.CurrentSessionID.Should().Be("s1");
      throw new NotImplementedException();
    }
  }
}
