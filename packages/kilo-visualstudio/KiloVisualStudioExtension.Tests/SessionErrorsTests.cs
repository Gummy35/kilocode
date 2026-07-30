using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for session errors - mirrors session-errors.test.ts from VS Code
    /// These tests verify error filtering and visibility logic
    /// </summary>
    public class SessionErrorsTests
    {
        private class Message
        {
            public string Id { get; set; }
            public string SessionID { get; set; }
            public string Role { get; set; }
            public ErrorInfo Error { get; set; }
        }

        private class ErrorInfo
        {
            public string Name { get; set; }
        }

        // Error utilities - THIS NEEDS TO BE IMPLEMENTED IN THE VS EXTENSION
        private static class ErrorUtils
        {
            public static List<string> ErrorIds(List<Message> messages)
            {
                return messages
                    .Where(m => m.Error != null)
                    .Select(m => m.Id)
                    .ToList();
            }

            public static ErrorInfo VisibleError(List<Message> messages, Func<string, bool> isHidden)
            {
                foreach (var message in messages.AsEnumerable().Reverse())
                {
                    if (message.Role != "assistant")
                        continue;

                    if (isHidden(message.Id))
                        continue;

                    if (message.Error?.Name == "MessageAbortedError")
                        continue;

                    if (message.Error != null)
                        return message.Error;
                }

                return null;
            }
        }

        private static Message Assistant(string id, string errorName = null)
        {
            return new Message
            {
                Id = id,
                SessionID = "session",
                Role = "assistant",
                Error = errorName != null ? new ErrorInfo { Name = errorName } : null
            };
        }

        [Fact]
        public void Returns_only_message_ids_with_errors()
        {
            // Arrange
            var messages = new List<Message>
            {
                Assistant("message_1"),
                Assistant("message_2", "ProviderError"),
                Assistant("message_3", "RateLimitError")
            };

            // Act
            var errorIds = ErrorUtils.ErrorIds(messages);

            // Assert
            errorIds.Should().BeEquivalentTo("message_2", "message_3");
        }

        [Fact]
        public void Hides_only_selected_error_messages()
        {
            // Arrange
            var hidden = new HashSet<string> { "message_2" };
            var messages = new List<Message>
            {
                Assistant("message_1"),
                Assistant("message_2", "ProviderError"),
                Assistant("message_3", "RateLimitError")
            };

            // Act
            var visible = ErrorUtils.VisibleError(messages, id => hidden.Contains(id));

            // Assert
            visible.Should().NotBeNull();
            visible.Name.Should().Be("RateLimitError");
        }

        [Fact]
        public void Ignores_aborted_assistant_messages()
        {
            // Arrange
            var messages = new List<Message>
            {
                Assistant("message_1", "MessageAbortedError")
            };

            // Act
            var visible = ErrorUtils.VisibleError(messages, id => false);

            // Assert
            visible.Should().BeNull();
        }
    }
}
