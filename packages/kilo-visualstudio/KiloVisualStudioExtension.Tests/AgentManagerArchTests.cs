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
    /// Architecture tests: Agent Manager
    /// 
    /// The agent manager runs in the same webview context as other UI.
    /// All its CSS classes must be prefixed with "am-" to avoid conflicts.
    /// These tests also verify consistency between CSS definitions and JS usage,
    /// and that the provider sends correct message types for each action.
    /// 
    /// Mirrors: agent-manager-arch.test.ts from VS Code
    /// </summary>
    public class AgentManagerArchTests
    {
        private readonly ITestOutputHelper _output;
        private readonly string _rootPath;
        private readonly string[] _cssFiles;
        private readonly string[] _jsFiles;

        public AgentManagerArchTests(ITestOutputHelper output)
        {
            _output = output;
            _rootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../webview-ui"));
            _cssFiles = new[]
            {
                Path.Combine(_rootPath, "agent-manager/agent-manager.css"),
                Path.Combine(_rootPath, "agent-manager/agent-manager-review.css"),
            };
            _jsFiles = new[]
            {
                Path.Combine(_rootPath, "agent-manager/AgentManagerApp.tsx"),
                Path.Combine(_rootPath, "agent-manager/UnassignedSessionsSection.tsx"),
                Path.Combine(_rootPath, "agent-manager/NewWorktreeDialog.tsx"),
                Path.Combine(_rootPath, "agent-manager/sortable-tab.tsx"),
                Path.Combine(_rootPath, "agent-manager/DiffPanel.tsx"),
                Path.Combine(_rootPath, "diff-viewer/FullScreenDiffView.tsx"),
                Path.Combine(_rootPath, "diff-viewer/ImageDiffView.tsx"),
                Path.Combine(_rootPath, "diff-viewer/MarkdownDiffView.tsx"),
                Path.Combine(_rootPath, "diff-viewer/MarkdownAnnotationLayer.tsx"),
                Path.Combine(_rootPath, "diff-viewer/markdown-comment-ranges.ts"),
                Path.Combine(_rootPath, "diff-viewer/DiffEndMarker.tsx"),
                Path.Combine(_rootPath, "diff-viewer/FileTree.tsx"),
                Path.Combine(_rootPath, "diff-viewer/review-annotations.ts"),
                Path.Combine(_rootPath, "diff-viewer/review-annotation-speech.tsx"),
                Path.Combine(_rootPath, "agent-manager/MultiModelSelector.tsx"),
                Path.Combine(_rootPath, "agent-manager/ApplyDialog.tsx"),
                Path.Combine(_rootPath, "agent-manager/WorktreeItem.tsx"),
                Path.Combine(_rootPath, "agent-manager/SectionHeader.tsx"),
                Path.Combine(_rootPath, "agent-manager/SidebarSearchMenu.tsx"),
                Path.Combine(_rootPath, "agent-manager/SidebarToggleButton.tsx"),
                Path.Combine(_rootPath, "agent-manager/WorktreeSectionActions.tsx"),
                Path.Combine(_rootPath, "agent-manager/tab-rendering.tsx"),
                Path.Combine(_rootPath, "agent-manager/terminal/TerminalTab.tsx"),
                Path.Combine(_rootPath, "agent-manager/terminal/SortableTerminalTab.tsx"),
                Path.Combine(_rootPath, "agent-manager/terminal/render.tsx"),
                Path.Combine(_rootPath, "diff-virtual/DiffVirtualApp.tsx"),
                Path.Combine(_rootPath, "src/components/shared/BranchSelect.tsx"),
                Path.Combine(_rootPath, "src/components/chat/TabDnd.tsx"),
                Path.Combine(_rootPath, "diff-viewer/BaseBranchPicker.tsx"),
            };
        }

        private string ReadAllCss()
        {
            return string.Join("\n", _cssFiles.Where(File.Exists).Select(File.ReadAllText));
        }

        private string ReadAllJs()
        {
            return string.Join("\n", _jsFiles.Where(File.Exists).Select(File.ReadAllText));
        }

        [Fact]
        public void All_CSS_class_selectors_should_use_am_prefix()
        {
            // Arrange
            var css = ReadAllCss();
            var matches = Regex.Matches(css, @"\.([a-z][a-z0-9-]*)", RegexOptions.IgnoreCase);
            var names = matches.Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();

            // Exceptions:
            // - VS Code sets these body classes on webview elements
            // - `kilo-diff-theme` is the shared Pierre diff theme utility
            // - `css` is matched from `@import "./diff.css"` file extension
            var host = new HashSet<string> { "vscode-high-contrast", "vscode-high-contrast-light", "kilo-diff-theme", "css" };
            var invalid = names.Where(n => !n.StartsWith("am-") && !host.Contains(n)).ToList();

            // Assert
            Assert.Empty(invalid);
        }

        [Fact]
        public void All_CSS_custom_properties_should_use_am_prefix()
        {
            // Arrange
            var css = ReadAllCss();
            var matches = Regex.Matches(css, @"--([a-z][a-z0-9-]*)\s*:", RegexOptions.IgnoreCase);
            var names = matches.Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();

            // Allow kilo-ui design tokens, vscode theme variables, and third-party library tokens
            var allowed = new[] { "am-", "vscode-", "surface-", "text-", "border-", "diffs-", "sticky-", "syntax-" };
            var invalid = names.Where(n => !allowed.Any(p => n.StartsWith(p))).ToList();

            // Assert
            Assert.Empty(invalid);
        }

        [Fact]
        public void All_keyframes_should_use_am_prefix()
        {
            // Arrange
            var css = ReadAllCss();
            var matches = Regex.Matches(css, @"@keyframes\s+([a-z][a-z0-9-]*)", RegexOptions.IgnoreCase);
            var names = matches.Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            var invalid = names.Where(n => !n.StartsWith("am-")).ToList();

            // Assert
            Assert.Empty(invalid);
        }

        [Fact]
        public void All_classes_used_in_TSX_should_be_defined_in_CSS()
        {
            // Arrange
            var css = ReadAllCss();
            var tsx = ReadAllJs();

            // Extract am- classes defined in CSS
            var cssMatches = Regex.Matches(css, @"\.([a-z][a-z0-9-]*)", RegexOptions.IgnoreCase);
            var defined = new HashSet<string>(cssMatches.Cast<Match>().Select(m => m.Groups[1].Value));

            // Extract am- classes referenced in TSX
            var tsxMatches = Regex.Matches(tsx, @"\bam-[a-z0-9-]+");
            var used = tsxMatches.Cast<Match>().Select(m => m.Value).Distinct().ToList();

            var missing = used.Where(c => !defined.Contains(c)).ToList();

            // Assert
            Assert.Empty(missing);
        }

        [Fact]
        public void All_am_classes_defined_in_CSS_should_be_used_in_TSX()
        {
            // Arrange
            var css = ReadAllCss();
            var tsx = ReadAllJs();

            // Extract am- classes defined in CSS
            var cssMatches = Regex.Matches(css, @"\.([a-z][a-z0-9-]*)", RegexOptions.IgnoreCase);
            var defined = cssMatches.Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .Where(n => n.StartsWith("am-"))
                .ToList();

            var unused = defined.Where(c => !tsx.Contains(c)).ToList();

            // Assert
            Assert.Empty(unused);
        }
    }
}
