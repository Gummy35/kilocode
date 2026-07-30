using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    public class SessionAgentTests
    {
        private class Agent
        {
            public string Name { get; set; } = "";
            public string Mode { get; set; } = "primary";
            public bool Hidden { get; set; }
        }

        private class Message
        {
            public string Id { get; set; } = "";
            public string SessionID { get; set; } = "";
            public string Role { get; set; } = "user";
            public string? Agent { get; set; }
        }

        private class SessionAgentManager
        {
            public string? ResolveSessionAgent(List<Message> messages, HashSet<string> validAgents)
            {
                string? result = null;
                foreach (var msg in messages)
                {
                    if (string.IsNullOrWhiteSpace(msg.Agent))
                        continue;

                    if (!validAgents.Contains(msg.Agent))
                        continue;

                    result = msg.Agent;
                }
                return result;
            }

            public string? CycleAgent(List<Agent> agents, string current, int direction, HashSet<string> visibleAgents)
            {
                var visibleList = agents.Where(a => !a.Hidden && visibleAgents.Contains(a.Name)).ToList();
                if (visibleList.Count == 0) return null;

                var currentIndex = visibleList.FindIndex(a => a.Name == current);
                if (currentIndex == -1)
                {
                    return visibleList[0].Name;
                }

                var nextIndex = (currentIndex + direction + visibleList.Count) % visibleList.Count;
                if (nextIndex == currentIndex && visibleList.Count == 1)
                {
                    return null;
                }

                return visibleList[nextIndex].Name;
            }
        }

        [Fact]
        public void ResolveSessionAgent_ReturnsLatestValidUserAgent()
        {
            var manager = new SessionAgentManager();
            var messages = new List<Message>
            {
                new Message { Id = "1", Agent = "plan" },
                new Message { Id = "2", Role = "assistant", Agent = "ask" },
                new Message { Id = "3", Agent = "code" }
            };
            var validAgents = new HashSet<string> { "plan", "code", "ask" };

            var result = manager.ResolveSessionAgent(messages, validAgents);

            Assert.Equal("code", result);
        }

        [Fact]
        public void ResolveSessionAgent_ReturnsLatestAssistantAgentWhenLast()
        {
            var manager = new SessionAgentManager();
            var messages = new List<Message>
            {
                new Message { Id = "1", Agent = "plan" },
                new Message { Id = "2", Role = "assistant", Agent = "code" }
            };
            var validAgents = new HashSet<string> { "plan", "code" };

            var result = manager.ResolveSessionAgent(messages, validAgents);

            Assert.Equal("code", result);
        }

        [Fact]
        public void ResolveSessionAgent_IgnoresUnknownAgentNames()
        {
            var manager = new SessionAgentManager();
            var messages = new List<Message>
            {
                new Message { Id = "1", Agent = "code" },
                new Message { Id = "2", Role = "assistant", Agent = "task" }
            };
            var validAgents = new HashSet<string> { "code" };

            var result = manager.ResolveSessionAgent(messages, validAgents);

            Assert.Equal("code", result);
        }

        [Fact]
        public void ResolveSessionAgent_IgnoresEmptyAgentValues()
        {
            var manager = new SessionAgentManager();
            var messages = new List<Message>
            {
                new Message { Id = "1", Agent = "  " }
            };
            var validAgents = new HashSet<string> { "code" };

            var result = manager.ResolveSessionAgent(messages, validAgents);

            Assert.Null(result);
        }

        [Fact]
        public void CycleAgent_CyclesForward()
        {
            var manager = new SessionAgentManager();
            var agents = new List<Agent>
            {
                new Agent { Name = "ask", Mode = "primary" },
                new Agent { Name = "plan", Mode = "primary" },
                new Agent { Name = "code", Mode = "primary" }
            };
            var visibleAgents = new HashSet<string> { "ask", "plan", "code" };

            var result = manager.CycleAgent(agents, "ask", 1, visibleAgents);

            Assert.Equal("plan", result);
        }

        [Fact]
        public void CycleAgent_CyclesBackward()
        {
            var manager = new SessionAgentManager();
            var agents = new List<Agent>
            {
                new Agent { Name = "ask", Mode = "primary" },
                new Agent { Name = "plan", Mode = "primary" },
                new Agent { Name = "code", Mode = "primary" }
            };
            var visibleAgents = new HashSet<string> { "ask", "plan", "code" };

            var result = manager.CycleAgent(agents, "ask", -1, visibleAgents);

            Assert.Equal("code", result);
        }

        [Fact]
        public void CycleAgent_WrapsToFirstWhenUnknown()
        {
            var manager = new SessionAgentManager();
            var agents = new List<Agent>
            {
                new Agent { Name = "ask", Mode = "primary" },
                new Agent { Name = "plan", Mode = "primary" },
                new Agent { Name = "code", Mode = "primary" }
            };
            var visibleAgents = new HashSet<string> { "ask", "plan", "code" };

            var result = manager.CycleAgent(agents, "code", 1, visibleAgents);

            Assert.Equal("ask", result);
        }

        [Fact]
        public void CycleAgent_DoesNothingWhenNoAlternative()
        {
            var manager = new SessionAgentManager();
            var agents = new List<Agent>
            {
                new Agent { Name = "code", Mode = "primary" }
            };
            var visibleAgents = new HashSet<string> { "code" };

            var result = manager.CycleAgent(agents, "code", 1, visibleAgents);

            Assert.Null(result);
        }

        [Fact]
        public void CycleAgent_IgnoresHiddenAgents()
        {
            var manager = new SessionAgentManager();
            var agents = new List<Agent>
            {
                new Agent { Name = "ask", Mode = "primary" },
                new Agent { Name = "hidden", Mode = "primary", Hidden = true },
                new Agent { Name = "code", Mode = "primary" }
            };
            var visibleAgents = new HashSet<string> { "ask", "hidden", "code" };

            var result = manager.CycleAgent(agents, "ask", 1, visibleAgents);

            Assert.Equal("code", result);
        }
    }
}
