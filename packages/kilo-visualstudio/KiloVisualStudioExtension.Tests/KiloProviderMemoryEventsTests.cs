using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    public class KiloProviderMemoryEventsTests
    {
        private class MemoryStatus
        {
            public string Root { get; set; } = "";
            public MemoryState State { get; set; } = new();
        }

        private class MemoryState
        {
            public bool Enabled { get; set; } = true;
            public bool AutoConsolidate { get; set; } = true;
            public MemoryStats Stats { get; set; } = new();
        }

        private class MemoryStats
        {
            public string LastInjectedSessionID { get; set; } = "";
            public int LastInjectedTokens { get; set; }
            public int LastOperationCount { get; set; }
        }

        private class MockMemoryClient
        {
            public List<string> StatusCalls { get; } = new();

            public Task<MemoryStatus> StatusAsync(string directory)
            {
                StatusCalls.Add(directory);
                return Task.FromResult(new MemoryStatus
                {
                    Root = $"{directory}/.kilo/memory",
                    State = new MemoryState { Enabled = true, AutoConsolidate = true }
                });
            }
        }

        private class MemoryEventProvider
        {
            public MockMemoryClient Client { get; }
            public List<Dictionary<string, object>> PostedMessages { get; } = new();
            public Dictionary<string, string> SessionDirectories { get; } = new();
            public HashSet<string> TrackedSessionIds { get; } = new();
            public string? CurrentSessionId { get; set; }

            public MemoryEventProvider(MockMemoryClient client)
            {
                Client = client;
            }

            public void SetSessionDirectory(string sessionID, string directory)
            {
                SessionDirectories[sessionID] = directory;
            }

            public void TrackSession(string sessionID)
            {
                TrackedSessionIds.Add(sessionID);
            }

            public async Task HandleEvent(string eventType, string sessionID, string directory)
            {
                if (!TrackedSessionIds.Contains(sessionID))
                    return;

                if (eventType == "memory.updated")
                {
                    await Client.StatusAsync(directory);
                    PostedMessages.Add(new Dictionary<string, object>
                    {
                        ["type"] = "memoryEvent",
                        ["sessionID"] = sessionID,
                        ["detail"] = new Dictionary<string, object> { ["type"] = "saved" }
                    });
                    PostedMessages.Add(new Dictionary<string, object>
                    {
                        ["type"] = "memoryLoaded",
                        ["sessionID"] = sessionID
                    });
                }
            }

            public async Task ToggleMemory(string sessionID)
            {
                string directory;
                if (SessionDirectories.TryGetValue(sessionID, out var dir))
                {
                    directory = dir;
                }
                else
                {
                    directory = "/repo";
                }
                await Client.StatusAsync(directory);
                PostedMessages.Add(new Dictionary<string, object>
                {
                    ["type"] = "memoryLoaded",
                    ["sessionID"] = sessionID
                });
            }
        }

        [Fact]
        public async Task HandleEvent_TrackedBackgroundMemoryEvent_RoutesToSessionDirectory()
        {
            var client = new MockMemoryClient();
            var provider = new MemoryEventProvider(client);
            provider.TrackSession("ses_active");
            provider.TrackSession("ses_bg");
            provider.SetSessionDirectory("ses_bg", "/worktree");

            await provider.HandleEvent("memory.updated", "ses_bg", "/worktree");

            Assert.Contains("/worktree", client.StatusCalls);
            Assert.True(provider.PostedMessages.Any(m => m.ContainsKey("type") && m["type"].ToString() == "memoryEvent" && m["sessionID"].ToString() == "ses_bg"));
            Assert.True(provider.PostedMessages.Any(m => m.ContainsKey("type") && m["type"].ToString() == "memoryLoaded" && m["sessionID"].ToString() == "ses_bg"));
            Assert.False(provider.PostedMessages.Any(m => m.ContainsKey("sessionID") && m["sessionID"].ToString() == "ses_active"));
        }

        [Fact]
        public async Task HandleEvent_SameDirectory_AlsoRefreshesActiveSession()
        {
            var client = new MockMemoryClient();
            var provider = new MemoryEventProvider(client);
            provider.CurrentSessionId = "ses_active";
            provider.TrackSession("ses_active");
            provider.TrackSession("ses_bg");
            provider.SetSessionDirectory("ses_active", "/repo");
            provider.SetSessionDirectory("ses_bg", "/repo");

            await provider.HandleEvent("memory.updated", "ses_bg", "/repo");

            Assert.Equal(2, client.StatusCalls.Count);
            Assert.True(provider.PostedMessages.Any(m => m.ContainsKey("type") && m["type"].ToString() == "memoryEvent" && m["sessionID"].ToString() == "ses_bg"));
            Assert.True(provider.PostedMessages.Any(m => m.ContainsKey("type") && m["type"].ToString() == "memoryEvent" && m["sessionID"].ToString() == "ses_active"));
        }

        [Fact]
        public async Task ToggleMemory_UsesProjectDirectory()
        {
            var client = new MockMemoryClient();
            var provider = new MemoryEventProvider(client);
            provider.CurrentSessionId = "ses_active";
            provider.SetSessionDirectory("ses_active", "/repo");

            await provider.ToggleMemory("ses_active");

            Assert.Contains("/repo", client.StatusCalls);
            Assert.True(provider.PostedMessages.Any(m => m.ContainsKey("type") && m["type"].ToString() == "memoryLoaded" && m["sessionID"].ToString() == "ses_active"));
        }

        [Fact]
        public async Task HandleEvent_UntrackedSession_Ignored()
        {
            var client = new MockMemoryClient();
            var provider = new MemoryEventProvider(client);
            provider.TrackSession("ses_active");

            await provider.HandleEvent("memory.updated", "ses_untracked", "/repo");

            Assert.Empty(client.StatusCalls);
            Assert.Empty(provider.PostedMessages);
        }
    }
}
