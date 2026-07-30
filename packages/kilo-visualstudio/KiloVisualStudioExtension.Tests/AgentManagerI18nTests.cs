using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Localization lint: Agent Manager
    /// 
    /// Ensures all user-visible strings in the Agent Manager webview go through
    /// the i18n t() function rather than being hardcoded in English.
    /// 
    /// Mirrors: agent-manager-i18n.test.ts from VS Code
    /// </summary>
    public class AgentManagerI18nTests
    {
        private readonly ITestOutputHelper _output;
        private readonly string _rootPath;
        private readonly string[] _tsxFiles;

        public AgentManagerI18nTests(ITestOutputHelper output)
        {
            _output = output;
            _rootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../webview-ui"));
            _tsxFiles = new[]
            {
                Path.Combine(_rootPath, "agent-manager/AgentManagerApp.tsx"),
                Path.Combine(_rootPath, "agent-manager/sortable-tab.tsx"),
            };
        }

        /// <summary>
        /// Props whose string values are user-visible and must be localized
        /// </summary>
        private static readonly HashSet<string> UserFacingProps = new HashSet<string>
        {
            "title", "label", "placeholder", "aria-label"
        };

        /// <summary>
        /// Strings that are clearly programmatic and should never be flagged
        /// </summary>
        private bool IsProgrammatic(string text)
        {
            var trimmed = text.Trim();
            if (string.IsNullOrEmpty(trimmed)) return true;

            // Single characters, pure numbers, pure punctuation/whitespace
            if (trimmed.Length <= 1) return true;
            if (Regex.IsMatch(trimmed, @"^\d+$")) return true;
            if (Regex.IsMatch(trimmed, @"^[^a-zA-Z]*$")) return true;

            // CSS class names
            if (Regex.IsMatch(trimmed, @"^am-")) return true;

            // Message type strings
            if (Regex.IsMatch(trimmed, @"^agentManager\.")) return true;
            if (Regex.IsMatch(trimmed, @"^(sendMessage|loadMessages|clearSession|sessionsLoaded|sessionCreated|action|setLanguage|webviewReady)$")) return true;

            // Known programmatic identifiers
            if (Regex.IsMatch(trimmed, @"^(local|pending:|kilo-vscode|data-theme|use:sortable)")) return true;

            // navigator/platform detection
            if (Regex.IsMatch(trimmed, @"^(Mac|iPhone|iPad)")) return true;

            // Keyboard modifier symbols
            if (Regex.IsMatch(trimmed, @"^[⌘⇧⌃⌥]+$")) return true;

            // Direction/action constants
            if (Regex.IsMatch(trimmed, @"^(up|down|left|right|horizontal|new|import|bottom|top|right-start|bottom-start|bottom-end|top-start)$")) return true;

            // Variant/size/icon names
            if (Regex.IsMatch(trimmed, @"^(ghost|primary|secondary|small|large|fit)$")) return true;

            // Icon names
            if (Regex.IsMatch(trimmed, @"^(branch|plus|close-small|settings-gear|chevron-down|chevron-right|trash|info|circle-x|console|magnifying-glass|layers|selector)$")) return true;

            // HTML tag names / type attribute values
            if (Regex.IsMatch(trimmed, @"^(button|text|submit|checkbox|root)$")) return true;

            // CSS selector strings
            if (Regex.IsMatch(trimmed, @"^\[data-") || Regex.IsMatch(trimmed, @"^\.am-")) return true;

            // Log prefixes
            if (Regex.IsMatch(trimmed, @"^\[Kilo")) return true;

            return false;
        }

        [Fact]
        public void Should_have_no_hardcoded_user_facing_strings_in_agent_manager_TSX_files()
        {
            // Arrange
            var violations = new List<string>();

            foreach (var filePath in _tsxFiles.Where(File.Exists))
            {
                var content = File.ReadAllText(filePath);
                var fileName = Path.GetFileName(filePath);

                // Check for JSX text content (simplified check)
                var textMatches = Regex.Matches(content, @"><([^<>]+)>(?=/?)");
                foreach (var match in textMatches.Cast<Match>())
                {
                    var text = match.Groups[1].Value.Trim();
                    if (string.IsNullOrEmpty(text)) continue;
                    if (Regex.IsMatch(text, @"^[^a-zA-Z]*$")) continue;
                    if (IsProgrammatic(text)) continue;

                    // Check if it's wrapped in t()
                    if (!Regex.IsMatch(match.Value, @"t\("))
                    {
                        violations.Add($"{fileName}: Found potential hardcoded string: \"{text}\"");
                    }
                }
            }

            // Assert
            Assert.Empty(violations);
        }

        [Fact]
        public void Should_not_shadow_the_t_translation_function_in_callbacks()
        {
            // Arrange
            var violations = new List<string>();

            foreach (var filePath in _tsxFiles.Where(File.Exists))
            {
                var content = File.ReadAllText(filePath);
                var fileName = Path.GetFileName(filePath);

                // Check for arrow functions with parameter 't' that contain JSX
                // Pattern: .map((t) => <...>)
                var shadowMatches = Regex.Matches(content, @"\.map\(\(t\)\s*=>\s*<");
                foreach (var match in shadowMatches.Cast<Match>())
                {
                    violations.Add($"{fileName}: Parameter 't' shadows i18n function in JSX context");
                }
            }

            // Assert
            Assert.Empty(violations);
        }
    }
}
