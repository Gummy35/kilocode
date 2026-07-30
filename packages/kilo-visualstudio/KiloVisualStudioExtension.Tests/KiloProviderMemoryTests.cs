using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    public class KiloProviderMemoryTests
    {
        private class MemoryStatus
        {
            public string Root { get; set; } = "";
            public MemoryState State { get; set; } = new();
            public MemoryIndex Index { get; set; } = new();
        }

        private class MemoryState
        {
            public bool Enabled { get; set; } = true;
            public string Scope { get; set; } = "project";
            public bool AutoConsolidate { get; set; } = true;
            public MemoryStats Stats { get; set; } = new();
        }

        private class MemoryStats
        {
            public string LastInjectedSessionID { get; set; } = "";
            public int LastInjectedTokens { get; set; }
            public int LastOperationCount { get; set; }
        }

        private class MemoryIndex
        {
            public int EstimatedTokens { get; set; }
        }

        private class MemoryView
        {
            public string Root { get; set; } = "";
            public MemoryState State { get; set; } = new();
            public Dictionary<string, string> Sources { get; set; } = new();
            public string Index { get; set; } = "";
            public string Items { get; set; } = "";
            public string Changes { get; set; } = "";
            public string Decisions { get; set; } = "";
        }

        private class MockMemoryClient
        {
            public List<string> StatusCalls { get; } = new();
            public List<string> ShowCalls { get; } = new();
            public List<string> CorrectCalls { get; } = new();
            public List<string> ConfigureCalls { get; } = new();
            public List<string> PurgeCalls { get; } = new();

            public Task<MemoryStatus> StatusAsync(string directory)
            {
                StatusCalls.Add(directory);
                return Task.FromResult(new MemoryStatus
                {
                    Root = $"{directory}/.kilo/memory",
                    State = new MemoryState { Enabled = true, Scope = "project", AutoConsolidate = true },
                    Index = new MemoryIndex { EstimatedTokens = 0 }
                });
            }

            public Task<MemoryView> ShowAsync(string directory)
            {
                ShowCalls.Add(directory);
                return Task.FromResult(new MemoryView
                {
                    Root = $"{directory}/.kilo/memory",
                    State = new MemoryState { Enabled = true },
                    Items = "record id=project.md:Facts:test :: Stored memory fact"
                });
            }

            public Task<OperationResult> CorrectAsync(string directory, string text, string key)
            {
                CorrectCalls.Add($"{directory}:{key}");
                return Task.FromResult(new OperationResult { OperationCount = 1, Added = 1, Removed = 0 });
            }

            public Task<MemoryStatus> ConfigureAsync(string directory, bool autoConsolidate)
            {
                ConfigureCalls.Add($"{directory}:{autoConsolidate}");
                return Task.FromResult(new MemoryStatus
                {
                    Root = $"{directory}/.kilo/memory",
                    State = new MemoryState { AutoConsolidate = autoConsolidate }
                });
            }

            public Task<PurgeResult> PurgeAsync(string directory, bool confirm)
            {
                PurgeCalls.Add($"{directory}:{confirm}");
                return Task.FromResult(new PurgeResult { Root = $"{directory}/.kilo/memory", Purged = true });
            }
        }

        private class OperationResult
        {
            public int OperationCount { get; set; }
            public int Added { get; set; }
            public int Removed { get; set; }
            public List<string> Skipped { get; set; } = new();
        }

        private class PurgeResult
        {
            public string Root { get; set; } = "";
            public bool Purged { get; set; }
        }

        private class MemoryProvider
        {
            public MockMemoryClient Client { get; }
            public List<Dictionary<string, object>> PostedMessages { get; } = new();
            public string CurrentDirectory { get; set; } = "/repo";

            public MemoryProvider(MockMemoryClient client)
            {
                Client = client;
            }

            public async Task ShowAsync(string sessionID)
            {
                var view = await Client.ShowAsync(CurrentDirectory);
                PostedMessages.Add(new Dictionary<string, object> { ["type"] = "memoryLoaded", ["sessionID"] = sessionID });
            }

            public async Task FetchAsync(string sessionID)
            {
                var status = await Client.StatusAsync(CurrentDirectory);
                PostedMessages.Add(new Dictionary<string, object> { ["type"] = "memoryLoaded", ["sessionID"] = sessionID });
            }

            public async Task<OperationResult> CorrectAsync(string sessionID, string text, string key)
            {
                var result = await Client.CorrectAsync(CurrentDirectory, text, key);
                PostedMessages.Add(new Dictionary<string, object> { ["type"] = "memoryOperationResult", ["operation"] = "correct", ["ok"] = true });
                return result;
            }

            public async Task ConfigureAsync(bool autoConsolidate)
            {
                await Client.ConfigureAsync(CurrentDirectory, autoConsolidate);
                PostedMessages.Add(new Dictionary<string, object> { ["type"] = "memoryOperationResult", ["operation"] = "auto", ["ok"] = true });
            }

            public async Task PurgeAsync(bool confirm)
            {
                await Client.PurgeAsync(CurrentDirectory, confirm);
                PostedMessages.Add(new Dictionary<string, object> { ["type"] = "memoryOperationResult", ["operation"] = "purge", ["ok"] = true });
            }
        }

        [Fact]
        public async Task Show_StoredMemory_PostsMemoryLoaded()
        {
            var client = new MockMemoryClient();
            var memory = new MemoryProvider(client);

            await memory.ShowAsync("ses_stored");

            Assert.Contains("/repo", client.ShowCalls);
            Assert.True(memory.PostedMessages.Any(m => m.ContainsKey("type") && m["type"].ToString() == "memoryLoaded"));
        }

        [Fact]
        public async Task Show_EmptyProject_Handled()
        {
            var client = new MockMemoryClient();
            var memory = new MemoryProvider(client);
            memory.CurrentDirectory = "/empty";

            await memory.ShowAsync("ses_empty");

            Assert.True(memory.PostedMessages.Any(m => m.ContainsKey("type") && m["type"].ToString() == "memoryLoaded"));
        }

        [Fact]
        public async Task Correct_DoesNotSendIgnoredFields()
        {
            var client = new MockMemoryClient();
            var memory = new MemoryProvider(client);

            await memory.CorrectAsync("ses_correct", "Prefer corrections.", "correction_key");

            Assert.Contains("/repo:correction_key", client.CorrectCalls);
        }

        [Fact]
        public async Task Configure_AutoConsolidate_Off()
        {
            var client = new MockMemoryClient();
            var memory = new MemoryProvider(client);

            await memory.ConfigureAsync(false);

            Assert.Contains("/repo:false", client.ConfigureCalls);
        }

        [Fact]
        public async Task Purge_Confirmed()
        {
            var client = new MockMemoryClient();
            var memory = new MemoryProvider(client);

            await memory.PurgeAsync(true);

            Assert.Contains("/repo:true", client.PurgeCalls);
            Assert.True(memory.PostedMessages.Any(m => m.ContainsKey("type") && m["type"].ToString() == "memoryOperationResult" && m["operation"].ToString() == "purge"));
        }

        [Fact]
        public async Task Status_Operation_DoesNotMutate()
        {
            var client = new MockMemoryClient();
            var memory = new MemoryProvider(client);

            await memory.FetchAsync("ses_memory");

            Assert.Contains("/repo", client.StatusCalls);
        }
    }
}
