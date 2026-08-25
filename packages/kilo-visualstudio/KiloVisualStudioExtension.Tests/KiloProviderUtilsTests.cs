using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KiloVisualStudioExtension.Services;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for MessageConfirmation service
    /// </summary>
    public class MessageConfirmationTests
    {
        [Fact]
        public void Tracks_confirmed_messages()
        {
            // Arrange
            var state = new MessageConfirmation(new ServiceProvider());

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
            var state = new MessageConfirmation(new ServiceProvider());
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
            var state = new MessageConfirmation(new ServiceProvider());
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
            var state = new MessageConfirmation(new ServiceProvider());
            var release = state.Track("msg-1");
            state.Confirm("msg-1");

            // Act
            release.Invoke();

            // Assert
            Assert.False(state.Has("msg-1"));
        }
    }

    /// <summary>
    /// Tests for AgentFiltering service
    /// </summary>
    public class AgentFilteringTests
    {
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
            var (visible, _) = AgentFiltering.FilterVisibleAgents(agents);

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
            var (visible, _) = AgentFiltering.FilterVisibleAgents(agents);

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
            var (_, defaultAgent) = AgentFiltering.FilterVisibleAgents(agents);

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
            var (_, defaultAgent) = AgentFiltering.FilterVisibleAgents(agents);

            // Assert
            Assert.Equal("code", defaultAgent);
        }
    }

    /// <summary>
    /// Tests for ErrorMessageExtraction service
    /// </summary>
    public class ErrorMessageExtractionTests
    {
        [Fact]
        public void Extracts_message_from_Exception_instance()
        {
            // Arrange
            var error = new Exception("boom");

            // Act
            var result = ErrorMessageExtraction.GetErrorMessage(error);

            // Assert
            Assert.Equal("boom", result);
        }

        [Fact]
        public void Returns_string_as_is()
        {
            // Arrange
            var error = "plain text failure";

            // Act
            var result = ErrorMessageExtraction.GetErrorMessage(error);

            // Assert
            Assert.Equal("plain text failure", result);
        }

        [Fact]
        public void Reads_direct_message_field()
        {
            // Arrange
            var error = new { message = "bad input" };

            // Act
            var result = ErrorMessageExtraction.GetErrorMessage(error);

            // Assert
            Assert.Equal("bad input", result);
        }

        [Fact]
        public void Reads_direct_error_field()
        {
            // Arrange
            var error = new { error = "nope" };

            // Act
            var result = ErrorMessageExtraction.GetErrorMessage(error);

            // Assert
            Assert.Equal("nope", result);
        }

        [Fact]
        public void Handles_null()
        {
            // Act
            var result = ErrorMessageExtraction.GetErrorMessage(null);

            // Assert
            Assert.Equal("null", result);
        }
    }
}
