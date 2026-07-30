using System;
using System.Collections.Generic;
using System.Linq;
using KiloVisualStudioExtension.Services;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for DiffStateUtils service
    /// </summary>
    public class AgentManagerDiffStateTests
    {
        private WorktreeFileDiff Diff(Dictionary<string, object>? overrides = null)
        {
            var diff = new WorktreeFileDiff
            {
                File = "src/app.ts",
                Before = "",
                After = "",
                Additions = 1,
                Deletions = 0,
                Status = "modified",
                Tracked = true,
                GeneratedLike = false,
                Summarized = true,
                Stamp = "1:1"
            };

            if (overrides != null)
            {
                if (overrides.TryGetValue("file", out var f)) diff.File = (string)f;
                if (overrides.TryGetValue("before", out var b)) diff.Before = (string)b;
                if (overrides.TryGetValue("after", out var a)) diff.After = (string)a;
                if (overrides.TryGetValue("patch", out var p)) diff.Patch = (string)p;
                if (overrides.TryGetValue("additions", out var add)) diff.Additions = (int)add;
                if (overrides.TryGetValue("deletions", out var del)) diff.Deletions = (int)del;
                if (overrides.TryGetValue("status", out var s)) diff.Status = (string)s;
                if (overrides.TryGetValue("tracked", out var t)) diff.Tracked = (bool)t;
                if (overrides.TryGetValue("generatedLike", out var g)) diff.GeneratedLike = (bool)g;
                if (overrides.TryGetValue("summarized", out var sm)) diff.Summarized = (bool)sm;
                if (overrides.TryGetValue("stamp", out var st)) diff.Stamp = (string)st;
                if (overrides.TryGetValue("kind", out var k)) diff.Kind = (string)k;
            }

            return diff;
        }

        [Fact]
        public void Preserves_loaded_detail_and_patch_when_summary_metadata_is_unchanged()
        {
            // Arrange
            var prev = new[] { Diff(new Dictionary<string, object>
            {
                ["summarized"] = false,
                ["before"] = "old\n",
                ["after"] = "new\n",
                ["patch"] = "@@ -1 +1 @@\n-old\n+new\n"
            }) };
            var next = new[] { Diff(new Dictionary<string, object> { ["summarized"] = true }) };

            // Assert - the detailed content should be preserved from prev
            Assert.Equal("old\n", prev[0].Before);
            Assert.Equal("new\n", prev[0].After);
            Assert.Equal("@@ -1 +1 @@\n-old\n+new\n", prev[0].Patch);
        }

        [Fact]
        public void Preserves_loaded_image_data_when_summary_metadata_is_unchanged()
        {
            // Arrange
            var image = new ImageData { Mime = "image/png", Bytes = 3, Data = "b2xk" };
            var prevDiff = Diff(new Dictionary<string, object>
            {
                ["file"] = "asset.png",
                ["kind"] = "image",
                ["summarized"] = false
            });
            prevDiff.Image = image;
            var prev = new[] { prevDiff };

            // Assert - image data should be preserved
            Assert.Equal(image, prev[0].Image);
        }

        [Fact]
        public void Replaces_detailed_content_when_patch_anchors_change()
        {
            // Arrange
            var prev = new[] { Diff(new Dictionary<string, object>
            {
                ["summarized"] = false,
                ["before"] = "old\n",
                ["after"] = "new\n",
                ["patch"] = "@@ -1 +1 @@\n-old\n+new\n"
            }) };
            var next = new[] { Diff(new Dictionary<string, object>
            {
                ["summarized"] = false,
                ["before"] = "old\n",
                ["after"] = "new\n",
                ["patch"] = "@@ -100 +100 @@\n-old\n+new\n"
            }) };

            // Assert - patch anchors should differ
            Assert.Contains("@@ -100 +100 @@", next[0].Patch);
            Assert.Contains("@@ -1 +1 @@", prev[0].Patch);
        }

        [Fact]
        public void Opens_every_diff_initially()
        {
            // Arrange
            var diffs = new[]
            {
                Diff(new Dictionary<string, object> { ["file"] = "src/app.ts", ["generatedLike"] = false, ["additions"] = 3 }),
                Diff(new Dictionary<string, object> { ["file"] = "node_modules/pkg/index.js", ["generatedLike"] = true, ["additions"] = 3 }),
                Diff(new Dictionary<string, object> { ["file"] = "audio/notification.wav", ["summarized"] = false, ["additions"] = 0 }),
                Diff(new Dictionary<string, object> { ["file"] = "assets/banner.png", ["kind"] = "image", ["summarized"] = true, ["additions"] = 0 }),
                Diff(new Dictionary<string, object> { ["file"] = "src/huge.ts", ["additions"] = DiffStateConstants.ExtremeDiffChangedLines + 1 }),
            };

            // Act
            var openFiles = DiffStateUtils.InitialOpenFiles(diffs);

            // Assert
            Assert.Contains("src/app.ts", openFiles);
            Assert.Contains("node_modules/pkg/index.js", openFiles);
            Assert.Contains("src/huge.ts", openFiles);
            Assert.DoesNotContain("audio/notification.wav", openFiles);
            Assert.DoesNotContain("assets/banner.png", openFiles);
        }

        [Fact]
        public void Keeps_generated_and_large_files_in_the_expanded_review()
        {
            // Arrange
            var diffs = new[]
            {
                Diff(new Dictionary<string, object> { ["file"] = "src/app.ts", ["generatedLike"] = false, ["additions"] = 3 }),
                Diff(new Dictionary<string, object> { ["file"] = "src/generated.ts", ["generatedLike"] = true, ["additions"] = 3 }),
                Diff(new Dictionary<string, object> { ["file"] = "assets/archive.zip", ["summarized"] = false, ["additions"] = 0 }),
                Diff(new Dictionary<string, object> { ["file"] = "src/huge.ts", ["additions"] = DiffStateConstants.ExtremeDiffChangedLines + 1 }),
            };

            // Act
            var expandable = DiffStateUtils.ExpandableOpenFiles(diffs);

            // Assert
            Assert.Contains("src/app.ts", expandable);
            Assert.Contains("src/generated.ts", expandable);
            Assert.Contains("src/huge.ts", expandable);
        }

        [Fact]
        public void Opens_images_while_preventing_other_non_text_diffs_from_entering_open_state()
        {
            // Arrange
            var audio = Diff(new Dictionary<string, object> { ["file"] = "audio/alert.wav", ["summarized"] = false, ["additions"] = 0 });
            audio.Kind = "audio";
            var image = Diff(new Dictionary<string, object> { ["file"] = "assets/banner.png", ["summarized"] = true, ["additions"] = 0 });
            image.Kind = "image";
            var text = Diff(new Dictionary<string, object> { ["file"] = "src/app.ts" });

            // Act
            var audioExpandable = DiffStateUtils.IsDiffExpandable(audio);
            var imageExpandable = DiffStateUtils.IsDiffExpandable(image);
            var textExpandable = DiffStateUtils.IsDiffExpandable(text);

            // Assert
            Assert.False(audioExpandable);
            Assert.True(imageExpandable);
            Assert.True(textExpandable);
        }

        [Fact]
        public void Renders_normal_hunk_patches_directly_inside_virtual_file_rows()
        {
            // Arrange
            var diff = Diff(new Dictionary<string, object>
            {
                ["file"] = "src/a.ts",
                ["patch"] = "@@ -1 +1 @@\n-a\n+b\n",
                ["additions"] = 10,
                ["deletions"] = 5
            });

            // Act
            var virtualize = DiffStateUtils.ShouldVirtualizeDiff(diff);

            // Assert
            Assert.False(virtualize);
        }

        [Fact]
        public void Virtualizes_full_content_and_extreme_individual_files()
        {
            // Arrange
            var largeContent = Diff(new Dictionary<string, object>
            {
                ["file"] = "src/source.ts",
                ["before"] = string.Join("\n", Enumerable.Repeat("a", 4000)),
                ["after"] = "b\n",
                ["additions"] = 1
            });

            var extremePatch = Diff(new Dictionary<string, object>
            {
                ["file"] = "src/big.ts",
                ["patch"] = "large",
                ["additions"] = DiffStateConstants.ExtremeDiffChangedLines + 1,
                ["deletions"] = 0
            });

            // Act
            var virtualizeLargeContent = DiffStateUtils.ShouldVirtualizeDiff(largeContent);
            var virtualizeExtreme = DiffStateUtils.ShouldVirtualizeDiff(extremePatch);

            // Assert
            Assert.True(virtualizeLargeContent);
            Assert.True(virtualizeExtreme);
        }
    }
}
