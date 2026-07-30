using System;
using System.Text.RegularExpressions;

namespace KiloVisualStudioExtension.Services
{
    /// <summary>
    /// Terminal font configuration.
    /// </summary>
    public class TerminalFont
    {
        public string FontFamily { get; set; } = "";
        public int FontSize { get; set; }
    }

    /// <summary>
    /// Utility methods for terminal font resolution.
    /// </summary>
    public static class TerminalFontResolver
    {
        private const string DefaultFontFamily = "Menlo, Monaco, 'Courier New', monospace";
        private const int DefaultFontSizeUnix = 12;
        private const int DefaultFontSizeWindows = 14;

        /// <summary>
        /// Resolves terminal font settings from configuration.
        /// Does not inherit editor font size.
        /// </summary>
        public static TerminalFont Resolve(string? fontFamily, int? fontSize, string? editorFontFamily)
        {
            var defaultFontSize = Environment.OSVersion.Platform == PlatformID.Unix ? DefaultFontSizeUnix : DefaultFontSizeWindows;

            return new TerminalFont
            {
                FontFamily = fontFamily ?? editorFontFamily ?? DefaultFontFamily,
                FontSize = fontSize ?? defaultFontSize
            };
        }

        /// <summary>
        /// Checks if a settings event affects terminal font configuration.
        /// </summary>
        public static bool AffectsTerminalFont(string settingKey)
        {
            return settingKey == "terminal.integrated.fontFamily" ||
                   settingKey == "terminal.integrated.fontSize" ||
                   settingKey == "editor.fontFamily";
        }
    }

    /// <summary>
    /// Utility methods for branch name sanitization.
    /// </summary>
    public static class BranchNameSanitizer
    {
        /// <summary>
        /// Sanitizes a branch name by replacing invalid characters with hyphens.
        /// </summary>
        public static string Sanitize(string branchName)
        {
            return Regex.Replace(branchName, @"[^a-zA-Z0-9-]", "-").Replace("--", "-").Trim('-');
        }
    }
}
