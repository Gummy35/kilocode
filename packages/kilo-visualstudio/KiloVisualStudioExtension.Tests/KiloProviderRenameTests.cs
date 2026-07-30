using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    public class KiloProviderRenameTests
    {
        private const int SESSION_TITLE_LIMIT = 100;

        private class MockClient
        {
            public List<RenameCall> Calls { get; } = new();

            public Task<RenameResult> RenameSessionAsync(string sessionID, string? directory, string title)
            {
                Calls.Add(new RenameCall { SessionID = sessionID, Directory = directory, Title = title });
                return Task.FromResult(new RenameResult
                {
                    Data = new Session
                    {
                        Id = sessionID,
                        Title = title,
                        Time = new SessionTime { Created = 1, Updated = 2 }
                    }
                });
            }
        }

        private class RenameCall
        {
            public string SessionID { get; set; } = "";
            public string? Directory { get; set; }
            public string Title { get; set; } = "";
        }

        private class RenameResult
        {
            public Session Data { get; set; } = null!;
        }

        private class Session
        {
            public string Id { get; set; } = "";
            public string Title { get; set; } = "";
            public SessionTime Time { get; set; } = null!;
        }

        private class SessionTime
        {
            public long Created { get; set; }
            public long Updated { get; set; }
        }

        private class RenameProvider
        {
            public MockClient Client { get; } = new();

            public async Task<Session> RenameSessionAsync(string sessionID, string? directory, string title)
            {
                ValidateTitle(title);
                var result = await Client.RenameSessionAsync(sessionID, directory, NormalizeTitle(title));
                return result.Data;
            }

            private void ValidateTitle(string title)
            {
                if (string.IsNullOrWhiteSpace(title))
                    throw new InvalidOperationException("Invalid session title: title cannot be empty or whitespace");

                if (title.Length > SESSION_TITLE_LIMIT)
                    throw new InvalidOperationException("Invalid session title: exceeds length limit");

                if (title.Contains("\n"))
                    throw new InvalidOperationException("Invalid session title: cannot contain newlines");

                if (title.Contains("\u202e"))
                    throw new InvalidOperationException("Invalid session title: contains unsafe characters");
            }

            private string NormalizeTitle(string title)
            {
                return Regex.Replace(title, @"\s+", " ").Trim();
            }
        }

        [Fact]
        public async Task RenameSession_NormalizesAndPersistsValidTitle()
        {
            var provider = new RenameProvider();
            var updated = await provider.RenameSessionAsync("ses_1", "/repo", "  Rename active session  ");

            Assert.Single(provider.Client.Calls);
            Assert.Equal("ses_1", provider.Client.Calls[0].SessionID);
            Assert.Equal("/repo", provider.Client.Calls[0].Directory);
            Assert.Equal("Rename active session", provider.Client.Calls[0].Title);
            Assert.Equal("Rename active session", updated.Title);
        }

        [Fact]
        public async Task RenameSession_RejectsEmptyTitle()
        {
            var provider = new RenameProvider();
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.RenameSessionAsync("ses_1", "/repo", " "));
            Assert.Empty(provider.Client.Calls);
        }

        [Fact]
        public async Task RenameSession_RejectsTitleExceedingLimit()
        {
            var provider = new RenameProvider();
            var longTitle = new string('a', SESSION_TITLE_LIMIT + 1);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.RenameSessionAsync("ses_1", "/repo", longTitle));
            Assert.Empty(provider.Client.Calls);
        }

        [Fact]
        public async Task RenameSession_RejectsTitleWithNewlines()
        {
            var provider = new RenameProvider();
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.RenameSessionAsync("ses_1", "/repo", "Title\nSecond line"));
            Assert.Empty(provider.Client.Calls);
        }

        [Fact]
        public async Task RenameSession_RejectsTitleWithUnsafeCharacters()
        {
            var provider = new RenameProvider();
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.RenameSessionAsync("ses_1", "/repo", "Title\u202eSpoof"));
            Assert.Empty(provider.Client.Calls);
        }

        [Fact]
        public async Task RenameSession_NormalizesMultipleSpaces()
        {
            var provider = new RenameProvider();
            var updated = await provider.RenameSessionAsync("ses_1", "/repo", "Multiple   spaces   here");
            Assert.Equal("Multiple spaces here", provider.Client.Calls[0].Title);
        }

        [Fact]
        public async Task RenameSession_TrimsLeadingAndTrailingWhitespace()
        {
            var provider = new RenameProvider();
            var updated = await provider.RenameSessionAsync("ses_1", "/repo", "   Trimmed Title   ");
            Assert.Equal("Trimmed Title", provider.Client.Calls[0].Title);
        }
    }
}
