using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using KiloVisualStudioExtension.Services;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for session utility functions that mirror session-utils.test.ts from VS Code
    /// These tests verify the data structures and logic used in webview session rendering
    /// </summary>
    public class SessionUtilsWebviewTests
    {
        private readonly SessionUtils _sessionUtils;

        public SessionUtilsWebviewTests()
        {
            _sessionUtils = new SessionUtils();
        }

        private string T(string key) => key;

        private SessionUtilsToolPart ToolPart(string tool, string sessionId = null, Dictionary<string, object> input = null)
        {
            return new SessionUtilsToolPart
            {
                Tool = tool,
                State = new SessionUtilsPartState
                {
                    Input = input ?? new Dictionary<string, object>(),
                    Metadata = sessionId != null ? new SessionUtilsPartMetadata { SessionId = sessionId } : null
                }
            };
        }

        private SessionUtilsPart TextPart(string id, string text = "text")
        {
            return new SessionUtilsPart { Type = "text", Id = id, Text = text };
        }

        private Message Msg(string id, string role, double? cost = null)
        {
            return new Message { Id = id, Role = role, Cost = cost };
        }

        private Message MsgWithTokens(string id, string role, TokenUsage tokens = null)
        {
            return new Message { Id = id, Role = role, Tokens = tokens };
        }

        private SessionUtilsPart StepFinish(string id, Metrics metrics = null, TokenUsage tokens = null, TimeInfo time = null)
        {
            return new SessionUtilsPart
            {
                Type = "step-finish",
                Id = id,
                Metrics = metrics,
                Tokens = tokens,
                Time = time
            };
        }

        #region ComputeStatus tests

        [Fact]
        public void Returns_null_for_null_part()
        {
            var result = _sessionUtils.ComputeStatus(null, T);

            Assert.Null(result);
        }

        [Fact]
        public void Maps_task_tool_to_delegating_status()
        {
            var part = new SessionUtilsPart { Type = "tool", Tool = "task", State = new SessionUtilsPartState { Status = "running" } };

            var result = _sessionUtils.ComputeStatus(part, T);

            Assert.Equal("ui.sessionTurn.status.delegating", result);
        }

        [Fact]
        public void Maps_todowrite_tool_to_planning_status()
        {
            var part = new SessionUtilsPart { Type = "tool", Tool = "todowrite", State = new SessionUtilsPartState { Status = "running" } };

            var result = _sessionUtils.ComputeStatus(part, T);

            Assert.Equal("ui.sessionTurn.status.planning", result);
        }

        [Fact]
        public void Maps_read_tool_to_gatheringContext_status()
        {
            var part = new SessionUtilsPart { Type = "tool", Tool = "read", State = new SessionUtilsPartState { Status = "running" } };

            var result = _sessionUtils.ComputeStatus(part, T);

            Assert.Equal("ui.sessionTurn.status.gatheringContext", result);
        }

        [Fact]
        public void Maps_list_grep_glob_tools_to_searchingCodebase_status()
        {
            foreach (var tool in new[] { "list", "grep", "glob" })
            {
                var part = new SessionUtilsPart { Type = "tool", Tool = tool, State = new SessionUtilsPartState { Status = "running" } };

                var result = _sessionUtils.ComputeStatus(part, T);

                Assert.Equal("ui.sessionTurn.status.searchingCodebase", result);
            }
        }

        [Fact]
        public void Maps_webfetch_tool_to_searchingWeb_status()
        {
            var part = new SessionUtilsPart { Type = "tool", Tool = "webfetch", State = new SessionUtilsPartState { Status = "running" } };

            var result = _sessionUtils.ComputeStatus(part, T);

            Assert.Equal("ui.sessionTurn.status.searchingWeb", result);
        }

        [Fact]
        public void Maps_edit_write_tools_to_makingEdits_status()
        {
            foreach (var tool in new[] { "edit", "write" })
            {
                var part = new SessionUtilsPart { Type = "tool", Tool = tool, State = new SessionUtilsPartState { Status = "running" } };

                var result = _sessionUtils.ComputeStatus(part, T);

                Assert.Equal("ui.sessionTurn.status.makingEdits", result);
            }
        }

        [Fact]
        public void Maps_bash_tool_to_runningCommands_status()
        {
            var part = new SessionUtilsPart { Type = "tool", Tool = "bash", State = new SessionUtilsPartState { Status = "running" } };

            var result = _sessionUtils.ComputeStatus(part, T);

            Assert.Equal("ui.sessionTurn.status.runningCommands", result);
        }

        [Fact]
        public void Returns_null_for_unknown_tool()
        {
            var part = new SessionUtilsPart { Type = "tool", Tool = "unknown_tool", State = new SessionUtilsPartState { Status = "running" } };

            var result = _sessionUtils.ComputeStatus(part, T);

            Assert.Null(result);
        }

        [Fact]
        public void Maps_reasoning_part_to_thinking_status()
        {
            var part = new SessionUtilsPart { Type = "reasoning", Text = "thinking..." };

            var result = _sessionUtils.ComputeStatus(part, T);

            Assert.Equal("ui.sessionTurn.status.thinking", result);
        }

        [Fact]
        public void Maps_text_part_to_writingResponse_status()
        {
            var part = new SessionUtilsPart { Type = "text", Text = "hello" };

            var result = _sessionUtils.ComputeStatus(part, T);

            Assert.Equal("session.status.writingResponse", result);
        }

        #endregion

        #region RecentSessions tests

        [Fact]
        public void Keeps_the_newest_root_sessions_after_removing_sub_agents()
        {
            var sessions = new List<SessionInfo>
            {
                new SessionInfo { Id = "old-root", UpdatedAt = "2026-01-01T00:00:00.000Z" },
                new SessionInfo { Id = "child", UpdatedAt = "2026-01-06T00:00:00.000Z", ParentID = "old-root" },
                new SessionInfo { Id = "new-root", UpdatedAt = "2026-01-05T00:00:00.000Z" },
                new SessionInfo { Id = "blank-parent", UpdatedAt = "2026-01-04T00:00:00.000Z", ParentID = "" },
                new SessionInfo { Id = "mid-root", UpdatedAt = "2026-01-03T00:00:00.000Z", ParentID = null },
                new SessionInfo { Id = "fourth-root", UpdatedAt = "2026-01-02T00:00:00.000Z" }
            };

            var result = _sessionUtils.RecentSessions(sessions);

            Assert.Equal(new[] { "new-root", "mid-root", "fourth-root" }, result.Select(s => s.Id));
        }

        [Fact]
        public void Does_not_mutate_the_session_list_while_sorting_recents()
        {
            var sessions = new List<SessionInfo>
            {
                new SessionInfo { Id = "old", UpdatedAt = "2026-01-01T00:00:00.000Z" },
                new SessionInfo { Id = "new", UpdatedAt = "2026-01-03T00:00:00.000Z" },
                new SessionInfo { Id = "mid", UpdatedAt = "2026-01-02T00:00:00.000Z" }
            };

            _sessionUtils.RecentSessions(sessions);

            Assert.Equal(new[] { "old", "new", "mid" }, sessions.Select(s => s.Id));
        }

        #endregion

        #region CalcTotalCost tests

        [Fact]
        public void Returns_0_for_empty_messages()
        {
            var result = _sessionUtils.CalcTotalCost(new List<Message>());

            Assert.Equal(0, result);
        }

        [Fact]
        public void Sums_costs_from_assistant_messages_only()
        {
            var msgs = new List<Message>
            {
                new Message { Role = "user", Cost = 1 },
                new Message { Role = "assistant", Cost = 0.05 },
                new Message { Role = "assistant", Cost = 0.03 }
            };

            var result = _sessionUtils.CalcTotalCost(msgs);

            Assert.Equal(0.08, result, 2);
        }

        [Fact]
        public void Ignores_user_messages()
        {
            var msgs = new List<Message>
            {
                new Message { Role = "user", Cost = 999 },
                new Message { Role = "assistant", Cost = 0.01 }
            };

            var result = _sessionUtils.CalcTotalCost(msgs);

            Assert.Equal(0.01, result, 2);
        }

        #endregion

        #region CalcTokenUsage tests

        [Fact]
        public void Sums_assistant_message_input_output_and_cache_read_tokens()
        {
            var msgs = new List<Message>
            {
                new Message { Role = "assistant", Tokens = new TokenUsage { Input = 100, Output = 40, Reasoning = 8, Cache = new CacheUsage { Read = 10, Write = 5 } } },
                new Message { Role = "assistant", Tokens = new TokenUsage { Input = 25, Output = 15, Cache = new CacheUsage { Read = 7, Write = 3 } } }
            };

            var result = _sessionUtils.CalcTokenUsage(msgs);

            Assert.Equal(125, result.input);
            Assert.Equal(55, result.output);
            Assert.Equal(17, result.cached);
        }

        [Fact]
        public void Ignores_user_messages_and_reasoning_tokens()
        {
            var msgs = new List<Message>
            {
                new Message { Role = "user", Tokens = new TokenUsage { Input = 999, Output = 999, Cache = new CacheUsage { Read = 999, Write = 999 } } },
                new Message { Role = "assistant" },
                new Message { Role = "assistant", Tokens = new TokenUsage { Input = 10, Output = 4, Reasoning = 30, Cache = new CacheUsage { Read = 2, Write = 20 } } }
            };

            var result = _sessionUtils.CalcTokenUsage(msgs);

            Assert.Equal(10, result.input);
            Assert.Equal(4, result.output);
            Assert.Equal(2, result.cached);
        }

        #endregion

        #region ChildID tests

        [Fact]
        public void Reads_session_ID_from_top_level_metadata()
        {
            var part = new SessionUtilsToolPart { Tool = "task", Metadata = new SessionUtilsPartMetadata { SessionId = "child1" } };

            var result = _sessionUtils.ChildID(part);

            Assert.Equal("child1", result);
        }

        [Fact]
        public void Reads_session_ID_from_state_metadata()
        {
            var part = new SessionUtilsToolPart { Tool = "task", State = new SessionUtilsPartState { Metadata = new SessionUtilsPartMetadata { SessionId = "child2" } } };

            var result = _sessionUtils.ChildID(part);

            Assert.Equal("child2", result);
        }

        [Fact]
        public void Prefers_top_level_metadata_over_state_metadata()
        {
            var part = new SessionUtilsToolPart
            {
                Tool = "task",
                Metadata = new SessionUtilsPartMetadata { SessionId = "top" },
                State = new SessionUtilsPartState { Metadata = new SessionUtilsPartMetadata { SessionId = "nested" } }
            };

            var result = _sessionUtils.ChildID(part);

            Assert.Equal("top", result);
        }

        [Fact]
        public void Ignores_non_task_tool_parts()
        {
            var part = new SessionUtilsToolPart { Tool = "read", State = new SessionUtilsPartState { Metadata = new SessionUtilsPartMetadata { SessionId = "child3" } } };

            var result = _sessionUtils.ChildID(part);

            Assert.Null(result);
        }

        #endregion

        #region FormatTG tests

        [Fact]
        public void Renders_the_value_with_a_t_s_suffix()
        {
            Assert.Equal("412 t/s", _sessionUtils.FormatTG(412, "en-US"));
            Assert.Equal("28 t/s", _sessionUtils.FormatTG(28, "en-US"));
        }

        [Fact]
        public void Falls_back_to_dash_for_missing_or_bogus_values()
        {
            Assert.Equal("–", _sessionUtils.FormatTG(null, "en-US"));
            Assert.Equal("–", _sessionUtils.FormatTG(0, "en-US"));
            Assert.Equal("–", _sessionUtils.FormatTG(-5, "en-US"));
        }

        #endregion
    }
}
