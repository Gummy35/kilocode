using System;
using System.Threading.Tasks;
using Xunit;
using Moq;
using KiloVisualStudioExtension.Services;

namespace KiloVisualStudioExtension.Tests
{
    public class ForkHandoffTests
    {
        [Fact]
        public void ForkText_DescribesRetainedContextWithoutAssumingNewTask()
        {
            var text = ForkHandoff.GetForkText("/repo/.kilo/worktrees/feature");

            Assert.Contains("This session was forked from an existing session in the current repository or worktree.", text);
            Assert.Contains("Use this as the current working directory: /repo/.kilo/worktrees/feature", text);
            Assert.Contains("this location supersedes any earlier repository or worktree location", text);
            Assert.Contains("The prior conversation context was retained intentionally.", text);
            Assert.Contains("continue the same task, explore an alternative approach, or provide new instructions", text);
            Assert.Contains("Follow the user's next instruction as the direction for this fork", text);
        }

        [Fact]
        public async Task RecordForkHandoff_RecordsHiddenNoReplyHandoffInForkedSession()
        {
            var mockClient = new Mock<KiloVisualStudioExtension.Services.IKiloClient>();
            var mockSession = new Mock<KiloVisualStudioExtension.Services.ISessionService>();
            mockClient.Setup(c => c.Session).Returns(mockSession.Object);

            await ForkHandoff.RecordForkHandoff(mockClient.Object, "session-fork", "/repo/.kilo/worktrees/feature");

            mockSession.Verify(s => s.PromptAsync(
                It.Is<KiloVisualStudioExtension.Services.SessionPromptRequest>(r =>
                    r.SessionID == "session-fork" &&
                    r.Directory == "/repo/.kilo/worktrees/feature" &&
                    r.NoReply == true &&
                    r.Parts.Count == 1 &&
                    r.Parts[0].Type == "text" &&
                    r.Parts[0].Synthetic == true &&
                    r.Parts[0].Text.Contains("This session was forked")
                ),
                It.Is<KiloVisualStudioExtension.Services.PromptOptions>(o => o.ThrowOnError == true)
            ), Times.Once);
        }
    }
}
