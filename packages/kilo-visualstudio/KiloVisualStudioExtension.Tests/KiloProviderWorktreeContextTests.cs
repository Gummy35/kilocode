using System;
using System.Collections.Generic;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    public class KiloProviderWorktreeContextTests
    {
        private class ContextResolver
        {
            public Dictionary<string, string> SessionDirectories { get; } = new();
            public string WorkspaceDirectory { get; set; } = "/repo";
            public string? CurrentSessionId { get; set; }
            public string? ContextSessionId { get; set; }
            public string? AgentManagerContext { get; set; }

            public string ResolveWorkspaceDirectory()
            {
                if (CurrentSessionId != null && SessionDirectories.TryGetValue(CurrentSessionId, out var dir))
                {
                    return dir;
                }
                return WorkspaceDirectory;
            }

            public string ResolveContextDirectory()
            {
                var sessionId = ContextSessionId ?? CurrentSessionId;
                if (sessionId != null && SessionDirectories.TryGetValue(sessionId, out var dir))
                {
                    return dir;
                }
                return WorkspaceDirectory;
            }

            public string ResolveNewSessionDirectory()
            {
                if (CurrentSessionId != null && SessionDirectories.TryGetValue(CurrentSessionId, out var currentDir))
                {
                    return currentDir;
                }

                if (ContextSessionId != null && SessionDirectories.TryGetValue(ContextSessionId, out var contextDir))
                {
                    return contextDir;
                }

                if (AgentManagerContext != null && AgentManagerContext.StartsWith("wt_"))
                {
                    var worktreeName = AgentManagerContext.Substring(3);
                    return $"{WorkspaceDirectory}/.kilo/worktrees/{worktreeName}";
                }

                return WorkspaceDirectory;
            }
        }

        [Fact]
        public void ResolveWorkspaceDirectory_UsesExplicitSessionWorktreeOverride()
        {
            var resolver = new ContextResolver
            {
                CurrentSessionId = "ses_worktree",
                WorkspaceDirectory = "/repo"
            };
            resolver.SessionDirectories["ses_worktree"] = "/repo/.kilo/worktrees/feature";

            var dir = resolver.ResolveWorkspaceDirectory();

            Assert.Equal("/repo/.kilo/worktrees/feature", dir);
        }

        [Fact]
        public void ResolveWorkspaceDirectory_FallsBackToWorkspaceRootWithoutSessionId()
        {
            var resolver = new ContextResolver
            {
                WorkspaceDirectory = "/repo"
            };
            resolver.SessionDirectories["ses_worktree"] = "/repo/.kilo/worktrees/feature";

            var dir = resolver.ResolveWorkspaceDirectory();

            Assert.Equal("/repo", dir);
        }

        [Fact]
        public void ResolveContextDirectory_UsesActiveSessionWorktree()
        {
            var resolver = new ContextResolver
            {
                ContextSessionId = "ses_worktree",
                WorkspaceDirectory = "/repo"
            };
            resolver.SessionDirectories["ses_worktree"] = "/repo/.kilo/worktrees/feature";

            var dir = resolver.ResolveContextDirectory();

            Assert.Equal("/repo/.kilo/worktrees/feature", dir);
        }

        [Fact]
        public void ResolveContextDirectory_KeepsLastWorktreeAfterClearSession()
        {
            var resolver = new ContextResolver
            {
                ContextSessionId = "ses_worktree",
                WorkspaceDirectory = "/repo"
            };
            resolver.SessionDirectories["ses_worktree"] = "/repo/.kilo/worktrees/feature";

            var dir = resolver.ResolveContextDirectory();

            Assert.Equal("/repo/.kilo/worktrees/feature", dir);
        }

        [Fact]
        public void ResolveContextDirectory_FallsBackToWorkspaceRootWhenNoWorktreeOverride()
        {
            var resolver = new ContextResolver
            {
                ContextSessionId = "ses_local",
                WorkspaceDirectory = "/repo"
            };

            var dir = resolver.ResolveContextDirectory();

            Assert.Equal("/repo", dir);
        }

        [Fact]
        public void ResolveNewSessionDirectory_KeepsExistingWorktreeSessions()
        {
            var resolver = new ContextResolver
            {
                CurrentSessionId = "ses_worktree",
                ContextSessionId = "ses_local",
                WorkspaceDirectory = "/repo"
            };
            resolver.SessionDirectories["ses_worktree"] = "/repo/.kilo/worktrees/feature";

            var dir = resolver.ResolveNewSessionDirectory();

            Assert.Equal("/repo/.kilo/worktrees/feature", dir);
        }

        [Fact]
        public void ResolveNewSessionDirectory_CreatesFollowupInLastWorktree()
        {
            var resolver = new ContextResolver
            {
                ContextSessionId = "ses_worktree",
                AgentManagerContext = "wt_feature",
                WorkspaceDirectory = "/repo"
            };
            resolver.SessionDirectories["ses_worktree"] = "/repo/.kilo/worktrees/feature";

            var dir = resolver.ResolveNewSessionDirectory();

            Assert.Equal("/repo/.kilo/worktrees/feature", dir);
        }

        [Fact]
        public void ResolveNewSessionDirectory_UsesExplicitAgentManagerWorktreeContext()
        {
            var resolver = new ContextResolver
            {
                AgentManagerContext = "wt_feature",
                WorkspaceDirectory = "/repo"
            };

            var dir = resolver.ResolveNewSessionDirectory();

            Assert.Equal("/repo/.kilo/worktrees/feature", dir);
        }

        [Fact]
        public void ResolveNewSessionDirectory_CreatesLocalAgentManagerSessionInWorkspaceRoot()
        {
            var resolver = new ContextResolver
            {
                ContextSessionId = "ses_worktree",
                AgentManagerContext = "local",
                WorkspaceDirectory = "/repo"
            };
            resolver.SessionDirectories["ses_worktree"] = "/repo/.kilo/worktrees/feature";

            var dir = resolver.ResolveNewSessionDirectory();

            Assert.Equal("/repo", dir);
        }
    }
}
