using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for SSE event mapping and message confirmation
    /// Mirrors: kilo-provider-utils.test.ts from VS Code (MessageConfirmation and event mapping)
    /// </summary>
    public class KiloProviderUtilsTests
    {
        /// <summary>
        /// Message confirmation state for tracking webview message delivery
        /// </summary>
        private class MessageConfirmation
        {
            private readonly HashSet<string> _tracked = new HashSet<string>();
            private readonly HashSet<string> _confirmed = new HashSet<string>();
            private readonly Dictionary<string, List<TaskCompletionSource<bool>>> _waiters = new Dictionary<string, List<TaskCompletionSource<bool>>>();

            public void Track(string messageId)
            {
                _tracked.Add(messageId);
                _waiters[messageId] = new List<TaskCompletionSource<bool>>();
            }

            public void Confirm(string messageId)
            {
                if (_tracked.Contains(messageId))
                {
                    _confirmed.Add(messageId);
                    if (_waiters.TryGetValue(messageId, out var sources))
                    {
                        foreach (var source in sources)
                            source.SetResult(true);
                        sources.Clear();
                    }
                }
            }

            public bool Has(string messageId) => _confirmed.Contains(messageId);

            public async Task<bool> Wait(string messageId, int timeoutMs)
            {
                if (_confirmed.Contains(messageId))
                    return true;

                var tcs = new TaskCompletionSource<bool>();
                if (_waiters.TryGetValue(messageId, out var sources))
                    sources.Add(tcs);

                var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
                return completed == tcs.Task;
            }

            public void Release(string messageId)
            {
                _tracked.Remove(messageId);
                _confirmed.Remove(messageId);
                _waiters.Remove(messageId);
            }
        }

        [Fact]
        public void Tracks_confirmed_messages()
        {
            // Arrange
            var state = new MessageConfirmation();

            // Act
            state.Track("msg-1");
            state.Confirm("msg-1");

            // Assert
            Assert.True(state.Has("msg-1"));
        }

        [Fact]
        public async Task Resolves_waiters_when_message_is_confirmed()
        {
            // Arrange
            var state = new MessageConfirmation();
            state.Track("msg-1");
            var wait = state.Wait("msg-1", 50);

            // Act
            state.Confirm("msg-1");

            // Assert
            Assert.True(await wait);
        }

        [Fact]
        public async Task Returns_false_when_confirmation_does_not_arrive()
        {
            // Arrange
            var state = new MessageConfirmation();
            state.Track("msg-1");

            // Act
            var result = await state.Wait("msg-1", 10);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void Forgets_confirmations_after_release()
        {
            // Arrange
            var state = new MessageConfirmation();
            state.Track("msg-1");
            state.Confirm("msg-1");

            // Act
            state.Release("msg-1");

            // Assert
            Assert.False(state.Has("msg-1"));
        }
    }

    /// <summary>
    /// Tests for session to webview conversion
    /// Mirrors: sessionToWebview from kilo-provider-utils.test.ts
    /// </summary>
    public class SessionToWebviewTests
    {
        private class Session
        {
            public string Id { get; set; } = "";
            public string Title { get; set; } = "";
            public long CreatedAt { get; set; }
            public long UpdatedAt { get; set; }
        }

        private class WebviewSession
        {
            public string Id { get; set; } = "";
            public string Title { get; set; } = "";
            public string CreatedAt { get; set; } = "";
            public string UpdatedAt { get; set; } = "";
        }

        private WebviewSession SessionToWebview(Session session)
        {
            return new WebviewSession
            {
                Id = session.Id,
                Title = session.Title,
                CreatedAt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(session.CreatedAt).ToString("o"),
                UpdatedAt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(session.UpdatedAt).ToString("o")
            };
        }

        [Fact]
        public void Converts_epoch_timestamps_to_ISO_strings()
        {
            // Arrange
            var session = new Session
            {
                Id = "sess-1",
                Title = "Test",
                CreatedAt = 1700000000000,
                UpdatedAt = 1700001000000
            };

            // Act
            var result = SessionToWebview(session);

            // Assert
            Assert.Equal(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(1700000000000).ToString("o"), result.CreatedAt);
            Assert.Equal(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(1700001000000).ToString("o"), result.UpdatedAt);
        }

        [Fact]
        public void Preserves_id_and_title()
        {
            // Arrange
            var session = new Session { Id = "abc", Title = "My Session", CreatedAt = 0, UpdatedAt = 0 };

            // Act
            var result = SessionToWebview(session);

            // Assert
            Assert.Equal("abc", result.Id);
            Assert.Equal("My Session", result.Title);
        }

        [Fact]
        public void Produces_valid_ISO_format()
        {
            // Arrange
            var session = new Session { Id = "sess-1", Title = "Test", CreatedAt = 1700000000000, UpdatedAt = 1700001000000 };

            // Act
            var result = SessionToWebview(session);

            // Assert
            var parsed = DateTime.Parse(result.CreatedAt);
            Assert.Equal(1700000000000, parsed.Ticks / 10000L - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks / 10000L);
        }
    }

    /// <summary>
    /// Tests for agent filtering
    /// Mirrors: filterVisibleAgents from kilo-provider-utils.test.ts
    /// </summary>
    public class AgentFilteringTests
    {
        private class Agent
        {
            public string Name { get; set; } = "";
            public string Mode { get; set; } = "";
            public bool Hidden { get; set; }
        }

        private (List<Agent> Visible, string DefaultAgent) FilterVisibleAgents(List<Agent> agents)
        {
            var visible = agents.Where(a => a.Mode != "subagent" && !a.Hidden).ToList();
            var defaultAgent = visible.Count > 0 ? visible[0].Name : "code";
            return (visible, defaultAgent);
        }

        [Fact]
        public void Filters_out_subagent_mode()
        {
            // Arrange
            var agents = new List<Agent>
            {
                new Agent { Name = "code", Mode = "primary" },
                new Agent { Name = "sub", Mode = "subagent" }
            };

            // Act
            var (visible, _) = FilterVisibleAgents(agents);

            // Assert
            Assert.Single(visible);
            Assert.Equal("code", visible[0].Name);
        }

        [Fact]
        public void Filters_out_hidden_agents()
        {
            // Arrange
            var agents = new List<Agent>
            {
                new Agent { Name = "code", Mode = "primary" },
                new Agent { Name = "hidden", Mode = "primary", Hidden = true }
            };

            // Act
            var (visible, _) = FilterVisibleAgents(agents);

            // Assert
            Assert.Single(visible);
            Assert.Equal("code", visible[0].Name);
        }

        [Fact]
        public void Uses_first_visible_agent_as_default()
        {
            // Arrange
            var agents = new List<Agent>
            {
                new Agent { Name = "first", Mode = "primary" },
                new Agent { Name = "second", Mode = "primary" }
            };

            // Act
            var (_, defaultAgent) = FilterVisibleAgents(agents);

            // Assert
            Assert.Equal("first", defaultAgent);
        }

        [Fact]
        public void Falls_back_to_code_when_no_visible_agents()
        {
            // Arrange
            var agents = new List<Agent>
            {
                new Agent { Name = "sub", Mode = "subagent" },
                new Agent { Name = "hidden", Mode = "primary", Hidden = true }
            };

            // Act
            var (_, defaultAgent) = FilterVisibleAgents(agents);

            // Assert
            Assert.Equal("code", defaultAgent);
        }
    }

    /// <summary>
    /// Tests for error message extraction
    /// Mirrors: getErrorMessage from kilo-provider-utils.test.ts
    /// </summary>
    public class ErrorMessageExtractionTests
    {
        private string GetErrorMessage(object? error)
        {
            if (error == null)
                return "null";

            if (error is Exception ex)
                return ex.Message;

            if (error is string str)
                return str;

            var dict = error as System.Collections.IDictionary;
            if (dict != null)
            {
                if (dict.Contains("message"))
                    return dict["message"]?.ToString() ?? "[object Object]";
                if (dict.Contains("error"))
                    return dict["error"]?.ToString() ?? "[object Object]";
            }

            return error.ToString() ?? "[object Object]";
        }

        [Fact]
        public void Extracts_message_from_Exception_instance()
        {
            // Arrange
            var error = new Exception("boom");

            // Act
            var result = GetErrorMessage(error);

            // Assert
            Assert.Equal("boom", result);
        }

        [Fact]
        public void Returns_string_as_is()
        {
            // Arrange
            var error = "plain text failure";

            // Act
            var result = GetErrorMessage(error);

            // Assert
            Assert.Equal("plain text failure", result);
        }

        [Fact]
        public void Reads_direct_message_field()
        {
            // Arrange
            var error = new { message = "bad input" };

            // Act
            var result = GetErrorMessage(error);

            // Assert
            Assert.Equal("bad input", result);
        }

        [Fact]
        public void Reads_direct_error_field()
        {
            // Arrange
            var error = new { error = "nope" };

            // Act
            var result = GetErrorMessage(error);

            // Assert
            Assert.Equal("nope", result);
        }

        [Fact]
        public void Handles_null()
        {
            // Act
            var result = GetErrorMessage(null);

            // Assert
            Assert.Equal("null", result);
        }
    }
}
