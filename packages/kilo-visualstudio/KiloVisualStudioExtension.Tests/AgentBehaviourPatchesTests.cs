using System;
using Xunit;
using KiloVisualStudioExtension.Services;

namespace KiloVisualStudioExtension.Tests
{
    public class AgentBehaviourPatchesTests
    {
        private readonly AgentBehaviourPatches _patches;

        public AgentBehaviourPatchesTests()
        {
            _patches = new AgentBehaviourPatches();
        }

        [Fact]
        public void Maps_empty_text_field_to_null()
        {
            var result = _patches.SelectedAgentTextOverrideValue("");

            Assert.Null(result);
        }

        [Fact]
        public void Preserves_non_empty_text_override()
        {
            var result = _patches.SelectedAgentTextOverrideValue("Review code");

            Assert.Equal("Review code", result);
        }

        [Fact]
        public void Maps_blank_numeric_field_to_null()
        {
            var result = _patches.SelectedAgentNumberOverrideValue("", double.Parse);

            Assert.Null(result);
        }

        [Fact]
        public void Preserves_valid_numeric_override()
        {
            var result = _patches.SelectedAgentNumberOverrideValue("0.7", double.Parse);

            Assert.Equal(0.7, result);
        }

        [Fact]
        public void Returns_undefined_for_invalid_numeric_input()
        {
            var result = _patches.SelectedAgentNumberOverrideValue("abc", double.Parse);

            Assert.Null(result);
        }

        [Fact]
        public void Maps_empty_dropdown_to_null()
        {
            var result = _patches.SelectedDefaultAgentValue("");

            Assert.Null(result);
        }

        [Fact]
        public void Preserves_non_empty_agent_selection()
        {
            var result = _patches.SelectedDefaultAgentValue("code");

            Assert.Equal("code", result);
        }

        [Fact]
        public void Clears_when_current_default_agent_becomes_unavailable()
        {
            var result = _patches.ShouldClearDefaultAgentWhenAgentBecomesUnavailable(false, "code", "code");

            Assert.True(result);
        }

        [Fact]
        public void Does_not_clear_when_toggling_non_default_agent()
        {
            var result = _patches.ShouldClearDefaultAgentWhenAgentBecomesUnavailable(false, "code", "plan");

            Assert.False(result);
        }

        [Fact]
        public void Does_not_clear_when_agent_remains_available()
        {
            var result = _patches.ShouldClearDefaultAgentWhenAgentBecomesUnavailable(true, "code", "code");

            Assert.False(result);
        }
    }
}
