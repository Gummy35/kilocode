using System;
using KiloVisualStudioExtension.Services;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for TerminalFontResolver service
    /// </summary>
    public class AgentManagerTerminalFontTests
    {
        [Fact]
        public void Resolves_terminal_settings_without_inheriting_editor_size()
        {
            // Arrange & Act
            var result1 = TerminalFontResolver.Resolve(null, null, null);
            var result2 = TerminalFontResolver.Resolve("MesloLGS NF", 16, "Menlo");
            var result3 = TerminalFontResolver.Resolve(null, 16, "Menlo");

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
            Assert.True(TerminalFontResolver.AffectsTerminalFont("terminal.integrated.fontFamily"));
            Assert.True(TerminalFontResolver.AffectsTerminalFont("terminal.integrated.fontSize"));
            Assert.True(TerminalFontResolver.AffectsTerminalFont("editor.fontFamily"));
            Assert.False(TerminalFontResolver.AffectsTerminalFont("editor.fontSize"));
            Assert.False(TerminalFontResolver.AffectsTerminalFont("terminal.integrated.letterSpacing"));
        }
    }

    /// <summary>
    /// Tests for BranchNameSanitizer service
    /// </summary>
    public class AgentManagerToolStartTests
    {
        [Fact]
        public void Sanitizes_branch_names()
        {
            // Arrange
            var branchName = "fix command permissions @#$ persistence";

            // Act
            var sanitized = BranchNameSanitizer.Sanitize(branchName);

            // Assert
            Assert.Equal("fix-command-permissions-persistence", sanitized);
        }
    }
}
