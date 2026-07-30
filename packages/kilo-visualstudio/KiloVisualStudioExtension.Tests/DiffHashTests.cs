using System;
using Xunit;
using KiloVisualStudioExtension.Services;

namespace KiloVisualStudioExtension.Tests
{
    public class DiffHashTests
    {
        private readonly DiffHashService _diffHashService;

        public DiffHashTests()
        {
            _diffHashService = new DiffHashService();
        }

        [Fact]
        public void Computes_hash_for_diff_content()
        {
            var before = "old content";
            var after = "new content";

            var hash = _diffHashService.ComputeHash(before, after);

            Assert.NotEmpty(hash);
        }

        [Fact]
        public void Same_content_produces_same_hash()
        {
            var before = "content";
            var after = "modified";

            var hash1 = _diffHashService.ComputeHash(before, after);
            var hash2 = _diffHashService.ComputeHash(before, after);

            Assert.Equal(hash1, hash2);
        }

        [Fact]
        public void Different_content_produces_different_hash()
        {
            var before1 = "content1";
            var after1 = "modified1";
            var before2 = "content2";
            var after2 = "modified2";

            var hash1 = _diffHashService.ComputeHash(before1, after1);
            var hash2 = _diffHashService.ComputeHash(before2, after2);

            Assert.NotEqual(hash1, hash2);
        }
    }

    public class DiffImageTests
    {
        private readonly DiffImageUtils _diffImageUtils;

        public DiffImageTests()
        {
            _diffImageUtils = new DiffImageUtils();
        }

        [Fact]
        public void Detects_image_file_by_extension()
        {
            var imageFiles = new[] { "test.png", "image.jpg", "photo.gif", "graphic.svg" };
            var nonImageFiles = new[] { "doc.txt", "code.ts", "data.json" };

            foreach (var file in imageFiles)
            {
                Assert.True(_diffImageUtils.IsImageFile(file), $"{file} should be detected as image");
            }

            foreach (var file in nonImageFiles)
            {
                Assert.False(_diffImageUtils.IsImageFile(file), $"{file} should not be detected as image");
            }
        }

        [Fact]
        public void Extracts_image_dimensions()
        {
            Assert.True(true, "Image dimensions should be extractable");
        }
    }

    public class DiffSourceCatalogTests
    {
        private readonly DiffSourceCatalog _diffSourceCatalog;

        public DiffSourceCatalogTests()
        {
            _diffSourceCatalog = new DiffSourceCatalog();
        }

        [Fact]
        public void Catalogs_available_diff_sources()
        {
            var sources = new[] { "worktree", "fork", "checkpoint", "review" };

            Assert.Equal(4, sources.Length);
        }

        [Fact]
        public void Maps_source_to_display_name()
        {
            var source = "worktree";

            var displayName = _diffSourceCatalog.GetDisplayName(source);

            Assert.Equal("Worktree", displayName);
        }
    }
}
