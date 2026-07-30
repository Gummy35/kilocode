using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    public class KiloProviderFollowupTests
    {
        private class SessionInfo
        {
            public string Id { get; set; } = "";
            public string Slug { get; set; } = "";
            public string ProjectID { get; set; } = "";
            public string Directory { get; set; } = "";
            public string Title { get; set; } = "";
            public string Version { get; set; } = "1";
            public SessionTime Time { get; set; } = null!;
            public string? ParentID { get; set; }
        }

        private class SessionTime
        {
            public long Created { get; set; }
            public long Updated { get; set; }
        }

        private class SessionEvent
        {
            public string Type { get; set; } = "";
            public SessionEventProperties Properties { get; set; } = null!;
        }

        private class SessionEventProperties
        {
            public string SessionID { get; set; } = "";
            public SessionInfo Info { get; set; } = null!;
        }

        private class FollowupProvider
        {
            public SessionInfo? CurrentSession { get; set; }
            public HashSet<string> TrackedSessionIds { get; } = new();
            public Dictionary<string, string> SessionDirectories { get; } = new();
            public List<Dictionary<string, object>> PostedMessages { get; } = new();
            public List<string> LoadedSessionIds { get; } = new();
            public Dictionary<string, string>? PendingFollowup { get; set; }

            public void SetSessionDirectory(string sessionID, string directory)
            {
                SessionDirectories[sessionID] = directory;
            }

            public async Task HandleLoadMessagesAsync(string sessionID)
            {
                LoadedSessionIds.Add(sessionID);
                string directory;
                if (SessionDirectories.TryGetValue(sessionID, out var dir))
                {
                    directory = dir;
                }
                else
                {
                    directory = "/repo";
                }
                CurrentSession = new SessionInfo
                {
                    Id = sessionID,
                    Title = "Session",
                    Directory = directory
                };
                TrackedSessionIds.Add(sessionID);
            }

            public void OnSessionCreated(SessionEvent evt)
            {
                var info = evt.Properties.Info;
                var sessionID = evt.Properties.SessionID;

                if (PendingFollowup != null && PendingFollowup.Count > 0)
                {
                    if (info.ParentID != null)
                    {
                        return;
                    }

                    PendingFollowup = null;
                    CurrentSession = info;
                    TrackedSessionIds.Add(sessionID);

                    PostedMessages.Add(new Dictionary<string, object>
                    {
                        ["type"] = "sessionCreated",
                        ["session"] = new
                        {
                            id = info.Id,
                            title = info.Title,
                            createdAt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(info.Time.Created).ToString("O"),
                            updatedAt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(info.Time.Updated).ToString("O"),
                            parentID = info.ParentID,
                            revert = (string?)null,
                            summary = (string?)null
                        },
                        ["activate"] = true
                    });
                }
            }
        }

        private SessionEvent CreateSessionCreated(string id, string directory, string? parentID = null)
        {
            return new SessionEvent
            {
                Type = "session.created",
                Properties = new SessionEventProperties
                {
                    SessionID = id,
                    Info = new SessionInfo
                    {
                        Id = id,
                        Slug = $"{id}-slug",
                        ProjectID = "project-1",
                        Directory = directory,
                        Title = "Session",
                        Version = "1",
                        Time = new SessionTime { Created = 1, Updated = 1 },
                        ParentID = parentID
                    }
                }
            };
        }

        [Fact]
        public async Task Followup_IgnoresSubagentsBeforeAdopting()
        {
            var provider = new FollowupProvider();
            provider.PendingFollowup = new Dictionary<string, string> { ["dir"] = "/repo" };
            provider.SetSessionDirectory("ses-child", "/repo");

            provider.OnSessionCreated(CreateSessionCreated("ses-child", "/repo", "ses-parent"));

            Assert.Null(provider.CurrentSession);
            Assert.False(provider.TrackedSessionIds.Contains("ses-child"));
            Assert.NotNull(provider.PendingFollowup);
            Assert.Empty(provider.LoadedSessionIds);
            Assert.Empty(provider.PostedMessages);

            provider.OnSessionCreated(CreateSessionCreated("ses-followup", "/repo"));

            Assert.Equal("ses-followup", provider.CurrentSession?.Id);
            Assert.True(provider.TrackedSessionIds.Contains("ses-followup"));
            Assert.Contains("ses-followup", provider.LoadedSessionIds);
            Assert.Single(provider.PostedMessages);
            Assert.Equal("sessionCreated", provider.PostedMessages[0]["type"]);
        }

        [Fact]
        public async Task Followup_AdoptsWorktreeFollowupSession()
        {
            var provider = new FollowupProvider();
            provider.PendingFollowup = new Dictionary<string, string> { ["dir"] = "/repo/.kilo/worktrees/feat" };
            provider.SetSessionDirectory("ses-wt", "/repo/.kilo/worktrees/feat");

            provider.OnSessionCreated(CreateSessionCreated("ses-wt", "/repo/.kilo/worktrees/feat"));

            Assert.Equal("ses-wt", provider.CurrentSession?.Id);
            Assert.Equal("/repo/.kilo/worktrees/feat", provider.CurrentSession?.Directory);
            Assert.True(provider.TrackedSessionIds.Contains("ses-wt"));
        }

        [Fact]
        public async Task Followup_ClearsPendingAfterAdoption()
        {
            var provider = new FollowupProvider();
            provider.PendingFollowup = new Dictionary<string, string> { ["dir"] = "/repo" };

            provider.OnSessionCreated(CreateSessionCreated("ses-followup", "/repo"));

            Assert.Null(provider.PendingFollowup);
        }

        [Fact]
        public async Task Followup_NoPendingFollowup_ProcessesNormally()
        {
            var provider = new FollowupProvider();

            provider.OnSessionCreated(CreateSessionCreated("ses-normal", "/repo"));

            Assert.Null(provider.CurrentSession);
            Assert.Empty(provider.TrackedSessionIds);
        }
    }
}
