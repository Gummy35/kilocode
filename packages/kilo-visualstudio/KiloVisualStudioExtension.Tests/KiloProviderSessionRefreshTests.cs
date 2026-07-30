using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    public class KiloProviderSessionRefreshTests
    {
        private class SessionInfo
        {
            public string Id { get; set; } = "";
            public string ProjectID { get; set; } = "";
            public string Title { get; set; } = "";
            public string Directory { get; set; } = "";
            public SessionTime Time { get; set; } = null!;
        }

        private class SessionTime
        {
            public long Created { get; set; }
            public long Updated { get; set; }
        }

        private class MockClient
        {
            public List<string> Calls { get; } = new();

            public Task<ListSessionsResult> ListSessionsAsync(string directory)
            {
                Calls.Add(directory);
                return Task.FromResult(new ListSessionsResult { Data = new List<SessionInfo>() });
            }
        }

        private class ListSessionsResult
        {
            public List<SessionInfo> Data { get; set; } = new();
        }

        private class SessionRefreshContext
        {
            public bool PendingSessionRefresh { get; set; } = false;
            public string ConnectionState { get; set; } = "connecting";
            public Func<string, Task<List<SessionInfo>>>? ListSessions { get; set; }
            public Dictionary<string, string> SessionDirectories { get; } = new();
            public string WorkspaceDirectory { get; set; } = "/repo";
            public List<Dictionary<string, object>> PostedMessages { get; } = new();

            public async Task<string?> LoadSessionsAsync()
            {
                if (ListSessions == null) return null;

                PendingSessionRefresh = true;

                var sessions = new List<SessionInfo>();
                var preserveSessionIds = new List<string>();

                try
                {
                    var workspaceSessions = await ListSessions(WorkspaceDirectory);
                    sessions.AddRange(workspaceSessions);
                }
                catch { }

                foreach (var kvp in SessionDirectories)
                {
                    try
                    {
                        var worktreeSessions = await ListSessions(kvp.Value);
                        sessions.AddRange(worktreeSessions);
                    }
                    catch
                    {
                        preserveSessionIds.Add(kvp.Key);
                    }
                }

                PostedMessages.Add(new Dictionary<string, object>
                {
                    ["type"] = "sessionsLoaded",
                    ["sessions"] = sessions.Select(s => new { id = s.Id }),
                    ["preserveSessionIds"] = preserveSessionIds.Any() ? preserveSessionIds : null
                });

                return sessions.FirstOrDefault()?.ProjectID;
            }

            public async Task FlushPendingRefreshAsync()
            {
                if (!PendingSessionRefresh || ListSessions == null) return;

                PendingSessionRefresh = false;

                try
                {
                    await ListSessions(WorkspaceDirectory);
                }
                catch { }

                foreach (var dir in SessionDirectories.Values)
                {
                    try
                    {
                        await ListSessions(dir);
                    }
                    catch { }
                }
            }
        }

        [Fact]
        public async Task LoadSessions_KeepsWorktreeSessionsWithLegacyProjectIds()
        {
            var ctx = new SessionRefreshContext
            {
                ConnectionState = "connected",
                SessionDirectories = { ["ses_worktree"] = "/worktree" },
                ListSessions = async dir =>
                {
                    if (dir == "/repo")
                    {
                        return new List<SessionInfo>
                        {
                            new SessionInfo
                            {
                                Id = "ses_root",
                                ProjectID = "project-new",
                                Title = "root",
                                Directory = "/repo",
                                Time = new SessionTime { Created = 1, Updated = 1 }
                            }
                        };
                    }
                    return new List<SessionInfo>
                    {
                        new SessionInfo
                        {
                            Id = "ses_worktree",
                            ProjectID = "project-old",
                            Title = "worktree",
                            Directory = "/worktree",
                            Time = new SessionTime { Created = 2, Updated = 2 }
                        }
                    };
                }
            };

            var project = await ctx.LoadSessionsAsync();

            Assert.Equal("project-new", project);
            Assert.Single(ctx.PostedMessages);
            var sessions = ((JsonElement)ctx.PostedMessages[0]["sessions"]).EnumerateArray().ToList();
            Assert.Contains("ses_root", sessions.Select(s => s.GetProperty("id").GetString()));
            Assert.Contains("ses_worktree", sessions.Select(s => s.GetProperty("id").GetString()));
        }

        [Fact]
        public async Task LoadSessions_DoesNotUseLegacyWorktreeSessionsAsCanonicalProject()
        {
            var ctx = new SessionRefreshContext
            {
                ConnectionState = "connected",
                SessionDirectories = { ["ses_worktree"] = "/worktree" },
                ListSessions = async dir =>
                {
                    if (dir == "/repo") return new List<SessionInfo>();
                    return new List<SessionInfo>
                    {
                        new SessionInfo
                        {
                            Id = "ses_worktree",
                            ProjectID = "project-old",
                            Title = "worktree",
                            Directory = "/worktree",
                            Time = new SessionTime { Created = 2, Updated = 2 }
                        }
                    };
                }
            };

            var project = await ctx.LoadSessionsAsync();

            Assert.Null(project);
            Assert.Single(ctx.PostedMessages);
        }

        [Fact]
        public async Task LoadSessions_PreservesSessionIdsWhenWorktreeListingFails()
        {
            var ctx = new SessionRefreshContext
            {
                ConnectionState = "connected",
                SessionDirectories =
                {
                    ["ses_wt1"] = "/worktree1",
                    ["ses_wt2"] = "/worktree2"
                },
                ListSessions = async dir =>
                {
                    if (dir == "/repo")
                    {
                        return new List<SessionInfo>
                        {
                            new SessionInfo
                            {
                                Id = "ses_root",
                                ProjectID = "project",
                                Title = "root",
                                Directory = "/repo",
                                Time = new SessionTime { Created = 1, Updated = 1 }
                            }
                        };
                    }
                    if (dir == "/worktree1") throw new Exception("backend not ready");
                    return new List<SessionInfo>
                    {
                        new SessionInfo
                        {
                            Id = "ses_wt2",
                            ProjectID = "project",
                            Title = "wt2",
                            Directory = "/worktree2",
                            Time = new SessionTime { Created = 2, Updated = 2 }
                        }
                    };
                }
            };

            await ctx.LoadSessionsAsync();

            Assert.Single(ctx.PostedMessages);
            var msg = ctx.PostedMessages[0];
            var sessions = ((JsonElement)msg["sessions"]).EnumerateArray().ToList();
            Assert.Contains("ses_root", sessions.Select(s => s.GetProperty("id").GetString()));
            Assert.Contains("ses_wt2", sessions.Select(s => s.GetProperty("id").GetString()));
            var preserveIds = ((JsonElement)msg["preserveSessionIds"]).EnumerateArray().ToList();
            Assert.Contains("ses_wt1", preserveIds.Select(s => s.GetString()));
        }

        [Fact]
        public async Task LoadSessions_OmitsPreserveSessionIdsWhenAllSucceed()
        {
            var ctx = new SessionRefreshContext
            {
                ConnectionState = "connected",
                SessionDirectories = { ["ses_wt"] = "/worktree" },
                ListSessions = async dir =>
                {
                    if (dir == "/repo")
                    {
                        return new List<SessionInfo>
                        {
                            new SessionInfo
                            {
                                Id = "ses_root",
                                ProjectID = "project",
                                Title = "root",
                                Directory = "/repo",
                                Time = new SessionTime { Created = 1, Updated = 1 }
                            }
                        };
                    }
                    return new List<SessionInfo>
                    {
                        new SessionInfo
                        {
                            Id = "ses_wt",
                            ProjectID = "project",
                            Title = "wt",
                            Directory = "/worktree",
                            Time = new SessionTime { Created = 2, Updated = 2 }
                        }
                    };
                }
            };

            await ctx.LoadSessionsAsync();

            Assert.Single(ctx.PostedMessages);
            var msg = ctx.PostedMessages[0];
            Assert.False(msg.ContainsKey("preserveSessionIds") && ((JsonElement)msg["preserveSessionIds"]).ValueKind != JsonValueKind.Null);
        }

        [Fact]
        public async Task FlushPendingRefresh_FlushesDeferredRefresh()
        {
            var calls = new List<string>();
            var ctx = new SessionRefreshContext
            {
                SessionDirectories = { ["ses_1"] = "/worktree" },
                ListSessions = async dir =>
                {
                    calls.Add(dir);
                    return new List<SessionInfo>();
                }
            };

            await ctx.LoadSessionsAsync();
            Assert.True(ctx.PendingSessionRefresh);

            ctx.ConnectionState = "connected";
            await ctx.FlushPendingRefreshAsync();

            Assert.Contains("/repo", calls);
            Assert.Contains("/worktree", calls);
            Assert.False(ctx.PendingSessionRefresh);
        }

        [Fact]
        public async Task LoadSessions_DoesNotPostNotConnectedErrorsWhileConnecting()
        {
            var ctx = new SessionRefreshContext
            {
                ConnectionState = "connecting",
                SessionDirectories = { ["ses_1"] = "/worktree" }
            };

            await ctx.LoadSessionsAsync();

            var errors = ctx.PostedMessages.Where(m => m["type"]?.ToString() == "error").ToList();
            Assert.Empty(errors);
        }
    }
}
