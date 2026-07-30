using System;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    public class SessionTitleTests
    {
        private const int SESSION_TITLE_LIMIT = 100;

        private class SessionTitleParser
        {
            public static readonly char[] BlockedControlChars = new char[]
            {
                '\u0000', '\u001b', '\u001f', '\u007f', '\u009f',
                '\u061c', '\u200e', '\u200f', '\u2028', '\u2029',
                '\u202a', '\u202e', '\u2066', '\u2069'
            };

            public (bool Valid, string? Value, string? Error) Parse(string? title)
            {
                if (title == null)
                    return (false, null, "invalid");

                var trimmed = title.Trim();

                if (trimmed.Length == 0)
                    return (false, null, "required");

                if (trimmed.Length > SESSION_TITLE_LIMIT)
                    return (false, null, "too_long");

                foreach (var c in trimmed)
                {
                    if (Array.IndexOf(BlockedControlChars, c) >= 0)
                        return (false, null, "control");
                }

                return (true, trimmed, null);
            }
        }

        [Fact]
        public void ParseSessionTitle_RejectsNullInput()
        {
            var parser = new SessionTitleParser();
            var result = parser.Parse(null);

            Assert.False(result.Valid);
            Assert.Equal("invalid", result.Error);
        }

        [Fact]
        public void ParseSessionTitle_TrimValidTitle()
        {
            var parser = new SessionTitleParser();
            var result = parser.Parse("  Review authentication flow  ");

            Assert.True(result.Valid);
            Assert.Equal("Review authentication flow", result.Value);
        }

        [Fact]
        public void ParseSessionTitle_RejectsEmptyTitles()
        {
            var parser = new SessionTitleParser();
            var result = parser.Parse("  \t \n ");

            Assert.False(result.Valid);
            Assert.Equal("required", result.Error);
        }

        [Fact]
        public void ParseSessionTitle_AcceptsDisplayLimit()
        {
            var parser = new SessionTitleParser();
            var atLimit = new string('a', SESSION_TITLE_LIMIT);
            var result = parser.Parse(atLimit);

            Assert.True(result.Valid);
            Assert.Equal(atLimit, result.Value);
        }

        [Fact]
        public void ParseSessionTitle_RejectsLongerThanLimit()
        {
            var parser = new SessionTitleParser();
            var tooLong = new string('a', SESSION_TITLE_LIMIT + 1);
            var result = parser.Parse(tooLong);

            Assert.False(result.Valid);
            Assert.Equal("too_long", result.Error);
        }

        [Fact]
        public void ParseSessionTitle_RejectsControlCharacters()
        {
            var parser = new SessionTitleParser();
            var blockedValues = new[]
            {
                "Task\u0000suffix",
                "Task\u001bsuffix",
                "Task\u001fsuffix",
                "Task\u007fsuffix",
                "Task\u009fsuffix",
                "Task\u061csuffix",
                "Task\u200esuffix",
                "Task\u200fsuffix",
                "Task\u2028suffix",
                "Task\u2029suffix",
                "Task\u202asuffix",
                "Task\u202esuffix",
                "Task\u2066suffix",
                "Task\u2069suffix"
            };

            foreach (var value in blockedValues)
            {
                var result = parser.Parse(value);
                Assert.False(result.Valid, $"Value containing \\u{(int)value[4]:X4} should be invalid");
                Assert.Equal("control", result.Error);
            }
        }

        [Fact]
        public void ParseSessionTitle_AcceptsNormalUnicodeDisplayText()
        {
            var parser = new SessionTitleParser();
            var result = parser.Parse("Analyse de la session - 修正");

            Assert.True(result.Valid);
            Assert.Equal("Analyse de la session - 修正", result.Value);
        }

        [Fact]
        public void ParseSessionTitle_TrimRemovesWhitespace()
        {
            var parser = new SessionTitleParser();
            var result = parser.Parse("   Trimmed   ");

            Assert.True(result.Valid);
            Assert.Equal("Trimmed", result.Value);
        }
    }
}
