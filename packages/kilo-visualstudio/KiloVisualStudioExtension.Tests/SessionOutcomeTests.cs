using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    public class SessionOutcomeTests
    {
        private class SessionOutcome
        {
            public string SessionID { get; set; } = "";
            public string Status { get; set; } = "";
            public string? Error { get; set; }
            public long DurationMs { get; set; }
            public int InputTokens { get; set; }
            public int OutputTokens { get; set; }
            public decimal Cost { get; set; }
        }

        private class SessionOutcomeManager
        {
            public Dictionary<string, SessionOutcome> Outcomes { get; } = new();
            public List<Dictionary<string, object>> PostedMessages { get; } = new();

            public void RecordOutcome(SessionOutcome outcome)
            {
                Outcomes[outcome.SessionID] = outcome;
                PostedMessages.Add(new Dictionary<string, object>
                {
                    ["type"] = "sessionOutcome",
                    ["sessionID"] = outcome.SessionID,
                    ["status"] = outcome.Status,
                    ["durationMs"] = outcome.DurationMs
                });
            }

            public SessionOutcome? GetOutcome(string sessionID)
            {
                return Outcomes.TryGetValue(sessionID, out var outcome) ? outcome : null;
            }

            public void RecordSuccess(string sessionID, long durationMs, int inputTokens, int outputTokens, decimal cost)
            {
                RecordOutcome(new SessionOutcome
                {
                    SessionID = sessionID,
                    Status = "success",
                    DurationMs = durationMs,
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens,
                    Cost = cost
                });
            }

            public void RecordError(string sessionID, string error, long durationMs)
            {
                RecordOutcome(new SessionOutcome
                {
                    SessionID = sessionID,
                    Status = "error",
                    Error = error,
                    DurationMs = durationMs
                });
            }

            public void RecordAborted(string sessionID, long durationMs)
            {
                RecordOutcome(new SessionOutcome
                {
                    SessionID = sessionID,
                    Status = "aborted",
                    DurationMs = durationMs
                });
            }
        }

        [Fact]
        public void RecordOutcome_Success_RecordsOutcomeAndPostsMessage()
        {
            var manager = new SessionOutcomeManager();
            manager.RecordSuccess("s1", durationMs: 5000, inputTokens: 100, outputTokens: 200, cost: 0.01m);

            var outcome = manager.GetOutcome("s1");
            Assert.NotNull(outcome);
            Assert.Equal("success", outcome!.Status);
            Assert.Equal(5000, outcome.DurationMs);
            Assert.Equal(100, outcome.InputTokens);
            Assert.Equal(200, outcome.OutputTokens);
            Assert.Equal(0.01m, outcome.Cost);
            Assert.Single(manager.PostedMessages);
            Assert.Equal("sessionOutcome", manager.PostedMessages[0]["type"]);
        }

        [Fact]
        public void RecordOutcome_Error_RecordsError()
        {
            var manager = new SessionOutcomeManager();
            manager.RecordError("s1", "Backend connection failed", durationMs: 3000);

            var outcome = manager.GetOutcome("s1");
            Assert.NotNull(outcome);
            Assert.Equal("error", outcome!.Status);
            Assert.Equal("Backend connection failed", outcome.Error);
            Assert.Equal(3000, outcome.DurationMs);
        }

        [Fact]
        public void RecordOutcome_Aborted_RecordsAbortedStatus()
        {
            var manager = new SessionOutcomeManager();
            manager.RecordAborted("s1", durationMs: 2000);

            var outcome = manager.GetOutcome("s1");
            Assert.NotNull(outcome);
            Assert.Equal("aborted", outcome!.Status);
            Assert.Equal(2000, outcome.DurationMs);
            Assert.Null(outcome.Error);
        }

        [Fact]
        public void GetOutcome_NonExistentSession_ReturnsNull()
        {
            var manager = new SessionOutcomeManager();
            var outcome = manager.GetOutcome("nonexistent");

            Assert.Null(outcome);
        }

        [Fact]
        public void RecordOutcome_OverwritesPreviousOutcome()
        {
            var manager = new SessionOutcomeManager();
            manager.RecordSuccess("s1", durationMs: 1000, inputTokens: 50, outputTokens: 100, cost: 0.005m);
            manager.RecordError("s1", "Updated error", durationMs: 2000);

            var outcome = manager.GetOutcome("s1");
            Assert.NotNull(outcome);
            Assert.Equal("error", outcome!.Status);
            Assert.Equal("Updated error", outcome.Error);
            Assert.Equal(2000, outcome.DurationMs);
        }

        [Fact]
        public void RecordOutcome_MultipleSessions_TracksIndependently()
        {
            var manager = new SessionOutcomeManager();
            manager.RecordSuccess("s1", durationMs: 1000, inputTokens: 50, outputTokens: 100, cost: 0.005m);
            manager.RecordError("s2", "Error in s2", durationMs: 500);
            manager.RecordAborted("s3", durationMs: 100);

            Assert.Equal(3, manager.Outcomes.Count);

            var s1 = manager.GetOutcome("s1");
            Assert.Equal("success", s1!.Status);

            var s2 = manager.GetOutcome("s2");
            Assert.Equal("error", s2!.Status);

            var s3 = manager.GetOutcome("s3");
            Assert.Equal("aborted", s3!.Status);
        }
    }
}
