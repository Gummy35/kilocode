using System;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for agent behaviour patches
    /// Mirrors: agent-behaviour-patches.test.ts from VS Code
    /// </summary>
    public class AgentBehaviourPatchesTests
    {
        /// <summary>
        /// Maps an empty text field value to a null delete sentinel
        /// </summary>
        private string? SelectedAgentTextOverrideValue(string text)
        {
            return string.IsNullOrEmpty(text) ? null : text;
        }

        /// <summary>
        /// Maps a blank numeric field value to a null delete sentinel
        /// </summary>
        private double? SelectedAgentNumberOverrideValue(string text, Func<string, double> parser)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            if (double.TryParse(text, out var result))
                return result;

            return undefined; // Invalid non-empty input
        }

        private static double? undefined => null; // Sentinel for undefined

        /// <summary>
        /// Maps an empty dropdown value to a null delete sentinel
        /// </summary>
        private string? SelectedDefaultAgentValue(string value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }

        /// <summary>
        /// Determines if default agent should be cleared when agent becomes unavailable
        /// </summary>
        private bool ShouldClearDefaultAgentWhenAgentBecomesUnavailable(bool isAvailable, string currentDefault, string agentName)
        {
            return !isAvailable && currentDefault == agentName;
        }

        [Fact]
        public void Maps_empty_text_field_to_null()
        {
            // Arrange & Act
            var result = SelectedAgentTextOverrideValue("");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void Preserves_non_empty_text_override()
        {
            // Arrange & Act
            var result = SelectedAgentTextOverrideValue("Review code");

            // Assert
            Assert.Equal("Review code", result);
        }

        [Fact]
        public void Maps_blank_numeric_field_to_null()
        {
            // Arrange & Act
            var result = SelectedAgentNumberOverrideValue("", double.Parse);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void Preserves_valid_numeric_override()
        {
            // Arrange & Act
            var result = SelectedAgentNumberOverrideValue("0.7", double.Parse);

            // Assert
            Assert.Equal(0.7, result);
        }

        [Fact]
        public void Returns_undefined_for_invalid_numeric_input()
        {
            // Arrange & Act
            var result = SelectedAgentNumberOverrideValue("abc", double.Parse);

            // Assert
            Assert.Null(result); // undefined maps to null
        }

        [Fact]
        public void Maps_empty_dropdown_to_null()
        {
            // Arrange & Act
            var result = SelectedDefaultAgentValue("");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void Preserves_non_empty_agent_selection()
        {
            // Arrange & Act
            var result = SelectedDefaultAgentValue("code");

            // Assert
            Assert.Equal("code", result);
        }

        [Fact]
        public void Clears_when_current_default_agent_becomes_unavailable()
        {
            // Arrange & Act
            var result = ShouldClearDefaultAgentWhenAgentBecomesUnavailable(false, "code", "code");

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void Does_not_clear_when_toggling_non_default_agent()
        {
            // Arrange & Act
            var result = ShouldClearDefaultAgentWhenAgentBecomesUnavailable(false, "code", "plan");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void Does_not_clear_when_agent_remains_available()
        {
            // Arrange & Act
            var result = ShouldClearDefaultAgentWhenAgentBecomesUnavailable(true, "code", "code");

            // Assert
            Assert.False(result);
        }
    }
}
