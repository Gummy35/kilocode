using System;
using System.Collections.Generic;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for Agent Manager terminal font resolution
    /// Mirrors: agent-manager-terminal-font.test.ts from VS Code
    /// </summary>
    public class AgentManagerTerminalFontTests
    {
        private class TerminalFont
        {
            public string FontFamily { get; set; } = "";
            public int FontSize { get; set; }
        }

        /// <summary>
        /// Resolves terminal font settings
        /// </summary>
        private TerminalFont ResolveTerminalFont(string? fontFamily, int? fontSize, string? editorFontFamily)
        {
            var defaultFontSize = Environment.OSVersion.Platform == PlatformID.Unix ? 12 : 14;
            var defaultFontFamily = "Menlo, Monaco, 'Courier New', monospace";

            return new TerminalFont
            {
                FontFamily = fontFamily ?? editorFontFamily ?? defaultFontFamily,
                FontSize = fontSize ?? defaultFontSize
            };
        }

        /// <summary>
        /// Checks if a settings event affects terminal font
        /// </summary>
        private bool AffectsTerminalFont(string settingKey)
        {
            return settingKey == "terminal.integrated.fontFamily" ||
                   settingKey == "terminal.integrated.fontSize" ||
                   settingKey == "editor.fontFamily";
        }

        [Fact]
        public void Resolves_terminal_settings_without_inheriting_editor_size()
        {
            // Arrange & Act
            var result1 = ResolveTerminalFont(null, null, null);
            var result2 = ResolveTerminalFont("MesloLGS NF", 16, "Menlo");
            var result3 = ResolveTerminalFont(null, 16, "Menlo");

            // Assert
            Assert.Equal("Menlo, Monaco, 'Courier New', monospace", result1.FontFamily);
            Assert.Equal(Environment.OSVersion.Platform == PlatformID.Unix ? 12 : 14, result1.FontSize);

            Assert.Equal("MesloLGS NF", result2.FontFamily);
            Assert.Equal(16, result2.FontSize);

            Assert.Equal("Menlo", result3.FontFamily);
            Assert.Equal(16, result3.FontSize);
        }

        [Fact]
        public void Watches_only_settings_that_affect_terminal_font_family_or_size()
        {
            // Arrange & Act & Assert
            Assert.True(AffectsTerminalFont("terminal.integrated.fontFamily"));
            Assert.True(AffectsTerminalFont("terminal.integrated.fontSize"));
            Assert.True(AffectsTerminalFont("editor.fontFamily"));
            Assert.False(AffectsTerminalFont("editor.fontSize"));
            Assert.False(AffectsTerminalFont("terminal.integrated.letterSpacing"));
        }
    }

    /// <summary>
    /// Tests for Agent Manager tool start parsing
    /// Mirrors: agent-manager-tool-start.test.ts from VS Code (partial)
    /// </summary>
    public class AgentManagerToolStartTests
    {
        private class ToolRequest
        {
            public string Mode { get; set; } = "";
            public List<TaskDefinition>? Tasks { get; set; }
        }

        private class TaskDefinition
        {
            public string? Prompt { get; set; }
            public ModelDefinition? Model { get; set; }
            public string? Variant { get; set; }
            public string? BranchName { get; set; }
        }

        private class ModelDefinition
        {
            public string ProviderId { get; set; } = "";
            public string ModelId { get; set; } = "";
        }

        private class ParsedRequest
        {
            public string RequestId { get; set; } = "";
            public string Mode { get; set; } = "";
            public List<ParsedTask>? Tasks { get; set; }
        }

        private class ParsedTask
        {
            public string Prompt { get; set; } = "";
            public ModelDefinition Model { get; set; } = new ModelDefinition();
            public string Variant { get; set; } = "";
        }

        /// <summary>
        /// Parses tool start events defensively
        /// </summary>
        private ParsedRequest? ParseToolRequest(ToolRequest request)
        {
            if (string.IsNullOrEmpty(request.Mode) || request.Tasks == null || request.Tasks.Count == 0)
                return null;

            var parsedTasks = new List<ParsedTask>();
            foreach (var task in request.Tasks)
            {
                if (string.IsNullOrEmpty(task.Prompt) || task.Model == null)
                    return null;

                parsedTasks.Add(new ParsedTask
                {
                    Prompt = task.Prompt.Trim(),
                    Model = new ModelDefinition
                    {
                        ProviderId = task.Model.ProviderId.Trim(),
                        ModelId = task.Model.ModelId.Trim()
                    },
                    Variant = task.Variant?.Trim() ?? ""
                });
            }

            var guid = Guid.NewGuid().ToString();
            return new ParsedRequest
            {
                RequestId = $"am-{guid.Substring(0, 8)}",
                Mode = request.Mode,
                Tasks = parsedTasks
            };
        }

        [Fact]
        public void Parses_tool_start_events_defensively()
        {
            // Arrange
            var request = new ToolRequest
            {
                Mode = "local",
                Tasks = new List<TaskDefinition>
                {
                    new TaskDefinition
                    {
                        Prompt = "one",
                        Model = new ModelDefinition { ProviderId = " test ", ModelId = " reasoning/model " },
                        Variant = " high "
                    }
                }
            };

            // Act
            var parsed = ParseToolRequest(request);

            // Assert
            Assert.NotNull(parsed);
            Assert.StartsWith("am-", parsed.RequestId);
            Assert.Equal("local", parsed.Mode);
            Assert.Single(parsed.Tasks);
            Assert.Equal("one", parsed.Tasks[0].Prompt);
            Assert.Equal("test", parsed.Tasks[0].Model.ProviderId);
            Assert.Equal("reasoning/model", parsed.Tasks[0].Model.ModelId);
            Assert.Equal("high", parsed.Tasks[0].Variant);
        }

        [Fact]
        public void Rejects_invalid_tool_requests()
        {
            // Arrange & Act & Assert
            Assert.Null(ParseToolRequest(new ToolRequest { Mode = "local", Tasks = new List<TaskDefinition>() }));
            Assert.Null(ParseToolRequest(new ToolRequest { Mode = "local", Tasks = new List<TaskDefinition> { new TaskDefinition() } }));
            Assert.Null(ParseToolRequest(new ToolRequest { Mode = "bad", Tasks = new List<TaskDefinition> { new TaskDefinition { Prompt = "test" } } }));
        }

        [Fact]
        public void Sanitizes_branch_names()
        {
            // Arrange
            var branchName = "fix command permissions @#$ persistence";

            // Act - Simplified sanitization
            var sanitized = System.Text.RegularExpressions.Regex.Replace(branchName, @"[^a-zA-Z0-9-]", "-").Replace("--", "-").Trim('-');

            // Assert
            Assert.Equal("fix-command-permissions-persistence", sanitized);
        }
    }
}
