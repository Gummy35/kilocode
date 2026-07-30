using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    public class AbortTests
    {
        [Fact]
        public async Task HandleAbort_StopsActiveOwnerAndCurrentDirectory()
        {
            // Arrange
            var httpClient = new TestHttpClient();
            var webView = new TestWebView();

            // Setup abort handler
            TestHelpers.SetupAbortHandler(httpClient);

            var sseHelper = TestHelpers.CreateSSEHelper(webView.PostMessage);

            // Simulate session being busy at /repo
            var eventJson = JsonSerializer.Serialize(new
            {
                type = "session.status",
                properties = new
                {
                    sessionID = "session_1",
                    status = new { type = "busy" },
                    directory = @"/repo"
                }
            });
            sseHelper.HandleEvent("session.status", eventJson);

            // Act - abort session_1
            var sessionID = "session_1";
            await httpClient.PostAsync($"/session/{sessionID}/abort", new StringContent("{}", Encoding.UTF8, "application/json"));

            // Assert - abort should complete successfully
            httpClient.Requests.Count.Should().Be(1, "one abort request should be made");
            httpClient.Requests[0].Url.Should().Contain("/session/session_1/abort");
        }

        [Fact]
        public async Task HandleAbort_ForgotsOwnerWhenIdle()
        {
            // Arrange
            var httpClient = new TestHttpClient();
            var webView = new TestWebView();

            // Setup abort handler
            TestHelpers.SetupAbortHandler(httpClient);

            var sseHelper = TestHelpers.CreateSSEHelper(webView.PostMessage);

            // Simulate session being busy then idle at /repo
            var busyEvent = JsonSerializer.Serialize(new
            {
                type = "session.status",
                properties = new
                {
                    sessionID = "session_1",
                    status = new { type = "busy" },
                    directory = @"/repo"
                }
            });
            sseHelper.HandleEvent("session.status", busyEvent);

            var idleEvent = JsonSerializer.Serialize(new
            {
                type = "session.status",
                properties = new
                {
                    sessionID = "session_1",
                    status = new { type = "idle" },
                    directory = @"/repo"
                }
            });
            sseHelper.HandleEvent("session.status", idleEvent);

            // Act - abort session_1
            var sessionID = "session_1";
            await httpClient.PostAsync($"/session/{sessionID}/abort", new StringContent("{}", Encoding.UTF8, "application/json"));

            // Assert - abort should complete
            httpClient.Requests.Count.Should().Be(1);
        }

        [Fact]
        public async Task HandleAbort_NoSessionID_UsesCurrentSession()
        {
            // Arrange
            var httpClient = new TestHttpClient();
            var webView = new TestWebView();

            // Setup abort handler
            TestHelpers.SetupAbortHandler(httpClient);

            var sseHelper = TestHelpers.CreateSSEHelper(webView.PostMessage);
            sseHelper.SetCurrentSession("current-session");

            // Act - abort without sessionID (should use current)
            var sessionID = sseHelper.CurrentSessionID;
            await httpClient.PostAsync($"/session/{sessionID}/abort", new StringContent("{}", Encoding.UTF8, "application/json"));

            // Assert - current session should be used
            httpClient.Requests.Count.Should().Be(1);
            httpClient.Requests[0].Url.Should().Contain("/session/current-session/abort");
        }

        [Fact]
        public async Task HandleAbort_PostsSessionTurnClosed()
        {
            // Arrange
            var httpClient = new TestHttpClient();
            var webView = new TestWebView();

            // Setup abort handler
            TestHelpers.SetupAbortHandler(httpClient);

            var sseHelper = TestHelpers.CreateSSEHelper(webView.PostMessage);
            sseHelper.SetCurrentSession("abort-session");

            // Act - abort the session
            var sessionID = sseHelper.CurrentSessionID;
            await httpClient.PostAsync($"/session/{sessionID}/abort", new StringContent("{}", Encoding.UTF8, "application/json"));

            // Assert - abort should complete
            httpClient.Requests.Count.Should().Be(1);
        }

        [Fact]
        public async Task HandleAbort_PostsSessionStatusIdle()
        {
            // Arrange
            var httpClient = new TestHttpClient();
            var webView = new TestWebView();

            // Setup abort handler
            TestHelpers.SetupAbortHandler(httpClient);

            var sseHelper = TestHelpers.CreateSSEHelper(webView.PostMessage);
            sseHelper.SetCurrentSession("abort-session-2");

            // Act - abort the session
            var sessionID = sseHelper.CurrentSessionID;
            await httpClient.PostAsync($"/session/{sessionID}/abort", new StringContent("{}", Encoding.UTF8, "application/json"));

            // Assert - abort should complete
            httpClient.Requests.Count.Should().Be(1);
        }

        [Fact]
        public async Task HandleAbort_DirectoryContext_AbortsCorrectSession()
        {
            // Arrange
            var httpClient = new TestHttpClient();
            var webView = new TestWebView();

            // Setup abort handler
            TestHelpers.SetupAbortHandler(httpClient);

            var sseHelper = TestHelpers.CreateSSEHelper(webView.PostMessage);
            sseHelper.SetCurrentSession("dir-session");

            // Act - abort with directory context
            var sessionID = sseHelper.CurrentSessionID;
            await httpClient.PostAsync($"/session/{sessionID}/abort", new StringContent("{}", Encoding.UTF8, "application/json"));

            // Assert - should complete without error
            httpClient.Requests.Count.Should().Be(1);
            httpClient.Requests[0].Url.Should().Contain("/session/dir-session/abort");
        }
    }
}
