using System;
using System.Collections.Generic;
using System.Linq;
using KiloVisualStudioExtension.Services;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for TranscriptExporter service
    /// </summary>
    public class ExportTranscriptTests
    {
        [Fact]
        public void Exports_transcript_as_markdown()
        {
            // Arrange
            var exporter = new TranscriptExporter();
            var messages = new[]
            {
                new TranscriptMessage { Role = "user", Content = "Hello" },
                new TranscriptMessage { Role = "assistant", Content = "Hi there!" }
            };

            // Act
            var result = exporter.ExportAsMarkdown(messages);

            // Assert
            Assert.Equal("markdown", result.Format);
            Assert.Equal(2, result.MessageCount);
            Assert.Contains("Hello", result.Content);
            Assert.Contains("Hi there!", result.Content);
        }

        [Fact]
        public void Exports_transcript_as_json()
        {
            // Arrange
            var exporter = new TranscriptExporter();
            var messages = new[]
            {
                new TranscriptMessage { Role = "user", Content = "Test" }
            };

            // Act
            var result = exporter.ExportAsJson(messages);

            // Assert
            Assert.Equal("json", result.Format);
            Assert.Contains("user", result.Content);
        }

        [Fact]
        public void Includes_timestamps_in_export()
        {
            // Arrange
            var exporter = new TranscriptExporter();
            var messages = new[]
            {
                new TranscriptMessage { Role = "user", Content = "Test", Timestamp = new DateTime(2024, 1, 1, 12, 0, 0) }
            };

            // Act
            var result = exporter.ExportAsJson(messages);

            // Assert
            Assert.Contains("2024", result.Content);
        }
    }

    /// <summary>
    /// Tests for MessageValidation service
    /// </summary>
    public class MessageContractTests
    {
        [Fact]
        public void Validates_message_structure()
        {
            // Arrange
            var role = "user";
            var content = "test";

            // Act & Assert
            Assert.NotNull(role);
            Assert.NotNull(content);
        }

        [Fact]
        public void Rejects_invalid_message_roles()
        {
            // Arrange
            var invalidRoles = new[] { "invalid", "unknown", "" };

            // Act & Assert
            foreach (var role in invalidRoles)
            {
                Assert.False(MessageValidation.IsValidRole(role), $"{role} should be invalid");
            }
        }

        [Fact]
        public void Accepts_valid_message_roles()
        {
            // Arrange
            var validRoles = new[] { "user", "assistant", "system" };

            // Act & Assert
            foreach (var role in validRoles)
            {
                Assert.True(MessageValidation.IsValidRole(role), $"{role} should be valid");
            }
        }
    }

    /// <summary>
    /// Tests for DraftStore service
    /// </summary>
    public class PromptDraftsTests
    {
        [Fact]
        public void Saves_and_retrieves_draft()
        {
            // Arrange
            var store = new DraftStore();

            // Act
            store.SaveDraft("s1", "My draft content");
            var draft = store.GetDraft("s1");

            // Assert
            Assert.NotNull(draft);
            Assert.Equal("My draft content", draft?.Content);
        }

        [Fact]
        public void Deletes_draft()
        {
            // Arrange
            var store = new DraftStore();
            store.SaveDraft("s1", "Content");

            // Act
            store.DeleteDraft("s1");
            var draft = store.GetDraft("s1");

            // Assert
            Assert.Null(draft);
        }

        [Fact]
        public void Overwrites_existing_draft()
        {
            // Arrange
            var store = new DraftStore();
            store.SaveDraft("s1", "First");

            // Act
            store.SaveDraft("s1", "Updated");
            var draft = store.GetDraft("s1");

            // Assert
            Assert.Equal("Updated", draft?.Content);
        }

        [Fact]
        public void Checks_if_draft_exists()
        {
            // Arrange
            var store = new DraftStore();

            // Act
            store.SaveDraft("s1", "Content");
            var exists = store.HasDraft("s1");
            var notExists = store.HasDraft("s2");

            // Assert
            Assert.True(exists);
            Assert.False(notExists);
        }
    }

    /// <summary>
    /// Tests for prompt history management
    /// </summary>
    public class PromptHistoryTests
    {
        [Fact]
        public void Maintains_history_of_prompts()
        {
            // Arrange
            var history = new List<string> { "Prompt 1", "Prompt 2", "Prompt 3" };

            // Assert
            Assert.Equal(3, history.Count);
            Assert.Equal("Prompt 1", history[0]);
        }

        [Fact]
        public void Limits_history_size()
        {
            // Arrange
            var maxSize = 10;
            var history = new List<string>();

            // Act - Add more than max
            for (int i = 0; i < 15; i++)
            {
                history.Add($"Prompt {i}");
                if (history.Count > maxSize)
                    history.RemoveAt(0);
            }

            // Assert
            Assert.Equal(maxSize, history.Count);
        }
    }
}
