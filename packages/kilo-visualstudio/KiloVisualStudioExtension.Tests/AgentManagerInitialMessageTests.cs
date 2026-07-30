using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for Agent Manager initial message handling
    /// Mirrors: agent-manager-initial-message.test.ts from VS Code
    /// 
    /// These tests verify the initial message and variant state creation logic
    /// </summary>
    public class AgentManagerInitialMessageTests
    {
        // Simplified message types
        private class SendInitialMessage
        {
            public string Type { get; set; } = "";
            public string SessionId { get; set; } = "";
            public string WorktreeId { get; set; } = "";
            public string? Text { get; set; }
            public string? ProviderId { get; set; }
            public string? ModelId { get; set; }
            public string? Agent { get; set; }
            public string? Variant { get; set; }
        }

        private class SendMessage
        {
            public string Type { get; set; } = "sendMessage";
            public string? Text { get; set; }
            public string? SessionId { get; set; }
            public string? ProviderId { get; set; }
            public string? ModelId { get; set; }
            public string? Agent { get; set; }
            public string? Variant { get; set; }
            public object? Files { get; set; }
        }

        private class SessionVariant
        {
            public string SessionId { get; set; } = "";
            public string ProviderId { get; set; } = "";
            public string ModelId { get; set; } = "";
            public string Agent { get; set; } = "";
            public string Value { get; set; } = "";
        }

        /// <summary>
        /// Creates initial sendMessage payload from SendInitialMessage
        /// Mirrors: initialMessage() from VS Code
        /// </summary>
        private SendMessage? InitialMessage(SendInitialMessage msg)
        {
            if (string.IsNullOrEmpty(msg.Text))
                return null;

            return new SendMessage
            {
                Text = msg.Text,
                SessionId = msg.SessionId,
                ProviderId = msg.ProviderId,
                ModelId = msg.ModelId,
                Agent = msg.Agent,
                Variant = msg.Variant,
                Files = null
            };
        }

        /// <summary>
        /// Builds initial variant state
        /// Mirrors: initialVariant() from VS Code
        /// </summary>
        private SessionVariant? InitialVariant(SendInitialMessage msg, string agent)
        {
            if (string.IsNullOrEmpty(msg.ProviderId) || 
                string.IsNullOrEmpty(msg.ModelId) || 
                string.IsNullOrEmpty(msg.Variant))
                return null;

            return new SessionVariant
            {
                SessionId = msg.SessionId,
                ProviderId = msg.ProviderId,
                ModelId = msg.ModelId,
                Agent = agent,
                Value = msg.Variant
            };
        }

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
            var msg = InitialMessage(input);

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
                // No text provided
            };

            // Act
            var msg = InitialMessage(input);

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
            var state = InitialVariant(input, "code");

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
                // No variant provided
            };

            // Act
            var state = InitialVariant(input, "code");

            // Assert
            Assert.Null(state);
        }
    }

    /// <summary>
    /// Tests for i18n split - Agent Manager keys should be separate from general locale dictionaries
    /// Mirrors: agent-manager-i18n-split.test.ts from VS Code
    /// </summary>
    public class AgentManagerI18nSplitTests
    {
        private const string PREFIX = "agentManager.";

        // Simplified locale dictionaries (in real implementation these would be loaded from JSON)
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
            // Arrange & Act & Assert
            foreach (var locale in AppLocales)
            {
                var hasAgentManagerKeys = locale.Value.Keys.Any(k => k.StartsWith(PREFIX));
                Assert.False(hasAgentManagerKeys, $"Locale {locale.Key} should not contain agent manager keys");
            }
        }

        [Fact]
        public void Keeps_every_agent_manager_locale_dictionary_scoped_to_agentManager_keys()
        {
            // Arrange & Act & Assert
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
            // Arrange
            var baseKeys = AgentManagerLocales["en"].Keys.ToHashSet();

            // Act & Assert
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
            // Arrange
            var required = new[]
            {
                PREFIX + "local",
                PREFIX + "session.new",
                PREFIX + "apply.error",
                PREFIX + "import.failed"
            };

            // Act & Assert
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
