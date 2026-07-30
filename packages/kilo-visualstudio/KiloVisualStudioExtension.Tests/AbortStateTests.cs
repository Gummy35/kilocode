using System;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for abort state management - mirrors abort-state.test.ts from VS Code
    /// These tests verify the abort state machine for pending prompts and sessions
    /// </summary>
    public class AbortStateTests
    {
        // Abort state machine - THIS NEEDS TO BE IMPLEMENTED IN THE VS EXTENSION
        private class AbortState
        {
            private readonly Dictionary<string, AbortRequest> _requests = new Dictionary<string, AbortRequest>();

            public bool Request(string draftId, string status, string messageId = null)
            {
                if (status != "idle" || string.IsNullOrEmpty(messageId))
                    return false;

                _requests[draftId] = new AbortRequest { MessageId = messageId };
                return true;
            }

            public bool Update(string sessionId, string status)
            {
                if (!_requests.TryGetValue(sessionId, out var request))
                    return false;

                if (status == "busy")
                    return true;

                if (status == "idle")
                {
                    _requests.Remove(sessionId);
                    return false;
                }

                return false;
            }

            public void Move(string fromId, string toId)
            {
                if (_requests.TryGetValue(fromId, out var request))
                {
                    _requests.Remove(fromId);
                    _requests[toId] = request;
                }
            }

            public void Finish(string messageId)
            {
                var keysToRemove = new List<string>();
                foreach (var kvp in _requests)
                {
                    if (kvp.Value.MessageId == messageId)
                        keysToRemove.Add(kvp.Key);
                }
                foreach (var key in keysToRemove)
                {
                    _requests.Remove(key);
                }
            }
        }

        private class AbortRequest
        {
            public string MessageId { get; set; }
        }

        [Fact]
        public void Waits_for_the_same_submission_to_become_cancellable()
        {
            // Arrange
            var aborts = new AbortState();

            // Act & Assert
            aborts.Request("draft", "idle", "message").Should().BeFalse();
            aborts.Update("draft", "busy").Should().BeTrue();
            aborts.Update("draft", "busy").Should().BeFalse();
            aborts.Update("draft", "idle").Should().BeFalse();
            aborts.Update("draft", "busy").Should().BeFalse();
        }

        [Fact]
        public void Moves_pending_cancellation_to_the_created_session()
        {
            // Arrange
            var aborts = new AbortState();

            // Act
            aborts.Request("draft", "idle", "message").Should().BeFalse();
            aborts.Move("draft", "session");

            // Assert
            aborts.Update("draft", "busy").Should().BeFalse();
            aborts.Update("session", "busy").Should().BeTrue();
        }

        [Fact]
        public void Does_not_retain_cancellation_after_an_idle_terminal_status()
        {
            // Arrange
            var aborts = new AbortState();

            // Act & Assert
            aborts.Request("session", "idle", "message").Should().BeFalse();
            aborts.Update("session", "idle").Should().BeFalse();
            aborts.Update("session", "busy").Should().BeFalse();
        }

        [Fact]
        public void Allows_retrying_an_abort_while_the_session_remains_active()
        {
            // Arrange
            var aborts = new AbortState();

            // Act & Assert
            aborts.Request("session", "busy").Should().BeTrue();
            aborts.Request("session", "busy").Should().BeTrue();
            aborts.Update("session", "idle").Should().BeFalse();
            aborts.Request("session", "busy").Should().BeTrue();
        }

        [Fact]
        public void Clears_cancellation_when_the_matching_submission_finishes()
        {
            // Arrange
            var aborts = new AbortState();

            // Act
            aborts.Request("session", "idle", "message").Should().BeFalse();
            aborts.Finish("other");
            aborts.Update("session", "busy").Should().BeTrue();

            // Assert
            aborts.Update("session", "idle").Should().BeFalse();
            aborts.Request("session", "idle", "message").Should().BeFalse();

            // Act
            aborts.Finish("message");

            // Assert
            aborts.Update("session", "busy").Should().BeFalse();
        }

        [Fact]
        public void Preserves_active_destination_state_during_duplicate_draft_migration()
        {
            // Arrange
            var aborts = new AbortState();

            // Act
            aborts.Request("draft", "idle", "message").Should().BeFalse();
            aborts.Request("session", "busy").Should().BeTrue();
            aborts.Move("draft", "session");

            // Assert
            aborts.Request("session", "busy").Should().BeTrue();
        }
    }
}
