using System;
using System.Collections.Generic;
using System.Linq;
using KiloVisualStudioExtension.Services;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for InitialMessageHandler service
    /// </summary>
    public class AgentManagerInitialMessageTests
    {
        [Fact]
        public void Forwards_the_selected_variant_to_sendMessage()
        {
            // Arrange
            var input = new SendInitialMessage
            {
                Type = "agentManager.sendInitialMessage",
                SessionId = "session-a",
                WorktreeId = "wt-a",
                Text = "Fix it",
                ProviderId = "anthropic",
                ModelId = "claude-sonnet-4",
                Agent = "code",
                Variant = "high"
            };

            // Act
            var msg = InitialMessageHandler.CreateInitialMessage(input);

            // Assert
            Assert.NotNull(msg);
            Assert.Equal("sendMessage", msg.Type);
            Assert.Equal("Fix it", msg.Text);
            Assert.Equal("session-a", msg.SessionId);
            Assert.Equal("anthropic", msg.ProviderId);
            Assert.Equal("claude-sonnet-4", msg.ModelId);
            Assert.Equal("code", msg.Agent);
            Assert.Equal("high", msg.Variant);
        }

        [Fact]
        public void Does_not_create_an_empty_sendMessage_payload()
        {
            // Arrange
            var input = new SendInitialMessage
            {
                Type = "agentManager.sendInitialMessage",
                SessionId = "session-a",
                WorktreeId = "wt-a"
            };

            // Act
            var msg = InitialMessageHandler.CreateInitialMessage(input);

            // Assert
            Assert.Null(msg);
        }

        [Fact]
        public void Builds_the_initial_session_variant_state()
        {
            // Arrange
            var input = new SendInitialMessage
            {
                Type = "agentManager.sendInitialMessage",
                SessionId = "session-a",
                WorktreeId = "wt-a",
                ProviderId = "anthropic",
                ModelId = "claude-sonnet-4",
                Variant = "medium"
            };

            // Act
            var state = InitialMessageHandler.CreateInitialVariant(input, "code");

            // Assert
            Assert.NotNull(state);
            Assert.Equal("session-a", state.SessionId);
            Assert.Equal("anthropic", state.ProviderId);
            Assert.Equal("claude-sonnet-4", state.ModelId);
            Assert.Equal("code", state.Agent);
            Assert.Equal("medium", state.Value);
        }

        [Fact]
        public void Does_not_build_variant_state_without_a_complete_model_variant()
        {
            // Arrange
            var input = new SendInitialMessage
            {
                Type = "agentManager.sendInitialMessage",
                SessionId = "session-a",
                WorktreeId = "wt-a",
                ProviderId = "anthropic",
                ModelId = "claude-sonnet-4"
            };

            // Act
            var state = InitialMessageHandler.CreateInitialVariant(input, "code");

            // Assert
            Assert.Null(state);
        }
    }

    /// <summary>
    /// Tests for i18n split - Agent Manager keys should be separate from general locale dictionaries
    /// </summary>
    public class AgentManagerI18nSplitTests
    {
        private const string PREFIX = "agentManager.";

        private static readonly Dictionary<string, Dictionary<string, string>> AppLocales = new Dictionary<string, Dictionary<string, string>>
        {
            ["en"] = new Dictionary<string, string> { ["general.key"] = "General" },
            ["zh"] = new Dictionary<string, string> { ["general.key"] = "通用" },
            ["es"] = new Dictionary<string, string> { ["general.key"] = "General" },
        };

        private static readonly Dictionary<string, Dictionary<string, string>> AgentManagerLocales = new Dictionary<string, Dictionary<string, string>>
        {
            ["en"] = new Dictionary<string, string> 
            { 
                [PREFIX + "local"] = "Local",
                [PREFIX + "session.new"] = "New Session"
            },
            ["zh"] = new Dictionary<string, string>
            {
                [PREFIX + "local"] = "本地",
                [PREFIX + "session.new"] = "新会话"
            },
            ["es"] = new Dictionary<string, string>
            {
                [PREFIX + "local"] = "Local",
                [PREFIX + "session.new"] = "Nueva sesión"
            },
        };

        [Fact]
        public void Keeps_agent_manager_keys_out_of_general_locale_dictionaries()
        {
            foreach (var locale in AppLocales)
            {
                var hasAgentManagerKeys = locale.Value.Keys.Any(k => k.StartsWith(PREFIX));
                Assert.False(hasAgentManagerKeys, $"Locale {locale.Key} should not contain agent manager keys");
            }
        }

        [Fact]
        public void Keeps_every_agent_manager_locale_dictionary_scoped_to_agentManager_keys()
        {
            foreach (var locale in AgentManagerLocales)
            {
                Assert.True(locale.Value.Count > 0, $"Locale {locale.Key} should have agent manager keys");
                
                var invalidKeys = locale.Value.Keys.Where(k => !k.StartsWith(PREFIX)).ToList();
                Assert.Empty(invalidKeys);
            }
        }

        [Fact]
        public void Keeps_every_agent_manager_locale_keyset_aligned_with_english()
        {
            var baseKeys = AgentManagerLocales["en"].Keys.ToHashSet();

            foreach (var locale in AgentManagerLocales)
            {
                if (locale.Key == "en") continue;

                var localeKeys = locale.Value.Keys.ToHashSet();
                
                var missing = baseKeys.Where(k => !localeKeys.Contains(k)).ToList();
                var extra = localeKeys.Where(k => !baseKeys.Contains(k)).ToList();

                Assert.Empty(missing);
                Assert.Empty(extra);
            }
        }

        [Fact]
        public void Contains_required_core_keys_in_every_locale()
        {
            var required = new[]
            {
                PREFIX + "local",
                PREFIX + "session.new",
                PREFIX + "apply.error",
                PREFIX + "import.failed"
            };

            foreach (var locale in AgentManagerLocales)
            {
                foreach (var key in required)
                {
                    Assert.True(locale.Value.ContainsKey(key), $"Missing key {key} in locale {locale.Key}");
                }
            }
        }
    }
}
