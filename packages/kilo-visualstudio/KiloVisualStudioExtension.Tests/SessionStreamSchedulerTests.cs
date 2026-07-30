using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for SessionStreamScheduler - mirrors session-stream-scheduler.test.ts from VS Code
    /// These tests verify part update throttling and coalescing behavior
    /// </summary>
    public class SessionStreamSchedulerTests
    {
        // Part update message structure
        private class PartUpdate
        {
            public string Type { get; set; }
            public string SessionID { get; set; }
            public string MessageID { get; set; }
            public Part Part { get; set; }
            public DeltaData Delta { get; set; }

            public PartUpdate(string sessionID, string messageID, string text, string delta = null, string partID = "p1")
            {
                Type = "partUpdated";
                SessionID = sessionID;
                MessageID = messageID;
                Part = new Part { Id = partID, Type = "text", MessageID = messageID, Text = text };
                if (delta != null)
                {
                    Delta = new DeltaData { Type = "text-delta", TextDelta = delta };
                }
            }
        }

        private class Part
        {
            public string Id { get; set; }
            public string Type { get; set; }
            public string MessageID { get; set; }
            public string Text { get; set; }
            public Dictionary<string, object> ExtraMetadata { get; set; }
        }

        private class DeltaData
        {
            public string Type { get; set; }
            public string TextDelta { get; set; }
        }

        private class PartBatch
        {
            public string Type { get; set; }
            public List<PartUpdate> Updates { get; set; }
        }

        // Simplified SessionStreamScheduler for testing
        // THIS NEEDS TO BE IMPLEMENTED IN THE VS EXTENSION
        private class SessionStreamScheduler
        {
            private readonly Action<object> _onSend;
            private readonly Dictionary<string, PartUpdate> _deltas = new();
            private readonly Dictionary<string, PartUpdate> _fullParts = new();
            private string _focusedSession;
            private bool _disposed;

            public SessionStreamScheduler(Action<object> onSend)
            {
                _onSend = onSend;
            }

            public void Push(PartUpdate update)
            {
                if (_disposed) return;

                var key = $"{update.SessionID}:{update.MessageID}:{update.Part.Id}";

                if (update.Delta != null)
                {
                    _deltas[key] = update;
                }
                else
                {
                    _fullParts[key] = update;
                }
            }

            public void Focus(string sessionID)
            {
                _focusedSession = sessionID;
                if (sessionID != null)
                {
                    Flush(sessionID);
                }
            }

            public void Drop(string sessionID)
            {
                var deltaKeys = _deltas.Keys.Where(k => k.StartsWith(sessionID + ":")).ToList();
                foreach (var key in deltaKeys)
                {
                    _deltas.Remove(key);
                }

                var fullKeys = _fullParts.Keys.Where(k => k.StartsWith(sessionID + ":")).ToList();
                foreach (var key in fullKeys)
                {
                    _fullParts.Remove(key);
                }
            }

            public void Flush(string sessionID = null)
            {
                if (_disposed) return;

                if (sessionID != null)
                {
                    var keys = _deltas.Keys.Where(k => k.StartsWith(sessionID + ":")).ToList();
                    foreach (var key in keys)
                    {
                        if (_deltas.TryGetValue(key, out var delta))
                        {
                            _onSend(delta);
                            _deltas.Remove(key);
                        }
                    }

                    keys = _fullParts.Keys.Where(k => k.StartsWith(sessionID + ":")).ToList();
                    foreach (var key in keys)
                    {
                        if (_fullParts.TryGetValue(key, out var full))
                        {
                            _onSend(full);
                            _fullParts.Remove(key);
                        }
                    }
                }
                else
                {
                    var allDeltas = _deltas.Values.ToList();
                    foreach (var delta in allDeltas)
                    {
                        _onSend(delta);
                    }
                    _deltas.Clear();

                    var allFull = _fullParts.Values.ToList();
                    foreach (var full in allFull)
                    {
                        _onSend(full);
                    }
                    _fullParts.Clear();
                }
            }

            public void Dispose()
            {
                _disposed = true;
            }

            public string Focused => _focusedSession;
        }

        private static PartUpdate Update(string text, string delta = null, string sid = "sess-1", string partID = "p1")
        {
            return new PartUpdate(sid, "m1", text, delta, partID);
        }

        private static string PartText(PartUpdate msg) => msg.Part.Text;

        private static List<object> Items(List<object> sent)
        {
            return sent.Select(msg =>
            {
                if (msg is PartBatch batch)
                    return batch.Updates.Cast<object>().ToArray();
                return new[] { msg };
            }).SelectMany(x => x).ToList();
        }

        [Fact]
        public void Merges_repeated_text_deltas()
        {
            // Arrange
            var sent = new List<object>();
            var queue = new SessionStreamScheduler(msg => sent.Add(msg));

            // Act
            queue.Push(Update("a", "a"));
            queue.Push(Update("b", "b"));
            queue.Flush();

            // Assert
            sent.Count.Should().Be(1);
            PartText((PartUpdate)sent[0]).Should().Be("ab");
        }

        [Fact]
        public void Appends_deltas_onto_queued_full_parts()
        {
            // Arrange
            var sent = new List<object>();
            var queue = new SessionStreamScheduler(msg => sent.Add(msg));

            // Act
            queue.Push(Update("hello"));
            queue.Push(Update(" world", " world"));
            queue.Flush();

            // Assert
            PartText((PartUpdate)sent[0]).Should().Be("hello world");
        }

        [Fact]
        public void Keeps_the_latest_full_part()
        {
            // Arrange
            var sent = new List<object>();
            var queue = new SessionStreamScheduler(msg => sent.Add(msg));

            // Act
            queue.Push(Update("old"));
            queue.Push(Update("new"));
            queue.Flush();

            // Assert
            PartText((PartUpdate)sent[0]).Should().Be("new");
        }

        [Fact]
        public void Flushes_deltas_before_later_full_updates()
        {
            // Arrange
            var sent = new List<object>();
            var queue = new SessionStreamScheduler(msg => sent.Add(msg));

            // Act
            queue.Push(Update("a", "a"));
            queue.Push(Update("done"));
            queue.Flush();

            // Assert
            sent.Count.Should().Be(2);
            PartText((PartUpdate)sent[0]).Should().Be("a");
            PartText((PartUpdate)sent[1]).Should().Be("done");
        }

        [Fact]
        public void Batches_multiple_sessions_into_one_partsUpdated_message()
        {
            // Arrange
            var sent = new List<object>();
            var queue = new SessionStreamScheduler(msg => sent.Add(msg));

            // Act
            queue.Push(Update("a", "a", "sess-1"));
            queue.Push(Update("b", "b", "sess-2"));
            queue.Flush();

            // Assert
            sent.Count.Should().Be(1);
        }

        [Fact]
        public void Never_merges_across_sessions_messages_or_parts()
        {
            // Arrange
            var sent = new List<object>();
            var queue = new SessionStreamScheduler(msg => sent.Add(msg));

            // Act
            queue.Push(new PartUpdate("sess-1", "m1", "a", "a", "p1"));
            queue.Push(new PartUpdate("sess-2", "m1", "b", "b", "p1"));
            queue.Push(new PartUpdate("sess-1", "m2", "c", "c", "p1"));
            queue.Push(new PartUpdate("sess-1", "m1", "d", "d", "p2"));
            queue.Flush();

            // Assert
            sent.Count.Should().Be(4);
        }

        [Fact]
        public void Flushes_a_newly_focused_session_immediately()
        {
            // Arrange
            var sent = new List<object>();
            var queue = new SessionStreamScheduler(msg => sent.Add(msg));

            // Act
            queue.Push(Update("a", "a", "sess-2"));
            queue.Focus("sess-2");

            // Assert
            sent.Count.Should().Be(1);
            PartText((PartUpdate)sent[0]).Should().Be("a");
        }

        [Fact]
        public void Drop_discards_queued_updates_for_the_session()
        {
            // Arrange
            var sent = new List<object>();
            var queue = new SessionStreamScheduler(msg => sent.Add(msg));

            // Act
            queue.Focus("sess-1");
            queue.Push(Update("a", "a", "sess-2"));
            queue.Drop("sess-2");
            queue.Drop("sess-1");
            queue.Flush();

            // Assert
            sent.Count.Should().Be(0);
        }

        [Fact]
        public void Focus_undefined_clears_the_active_session()
        {
            // Arrange
            var sent = new List<object>();
            var queue = new SessionStreamScheduler(msg => sent.Add(msg));

            // Act
            queue.Focus("sess-1");
            queue.Focus(null);
            queue.Push(Update("a", "a", "sess-1"));
            queue.Flush();

            // Assert
            queue.Focused.Should().BeNull();
        }

        [Fact]
        public void Exposes_the_focused_session_id_via_the_getter()
        {
            // Arrange
            var queue = new SessionStreamScheduler(msg => { });

            // Act & Assert
            queue.Focused.Should().BeNull();

            queue.Focus("sess-1");
            queue.Focused.Should().Be("sess-1");

            queue.Focus("sess-2");
            queue.Focused.Should().Be("sess-2");

            queue.Focus(null);
            queue.Focused.Should().BeNull();
        }

        [Fact]
        public void Dispose_stops_further_emissions_from_queued_work()
        {
            // Arrange
            var sent = new List<object>();
            var queue = new SessionStreamScheduler(msg => sent.Add(msg));

            // Act
            queue.Focus("sess-1");
            queue.Push(Update("a", "a", "sess-1"));
            queue.Push(Update("b", "b", "sess-2"));
            queue.Dispose();
            queue.Flush();

            // Assert
            sent.Count.Should().Be(0);
        }

        [Fact]
        public void Flush_with_no_argument_drains_everything_across_sessions()
        {
            // Arrange
            var sent = new List<object>();
            var queue = new SessionStreamScheduler(msg => sent.Add(msg));

            // Act
            queue.Push(Update("a", "a", "sess-1"));
            queue.Push(Update("b", "b", "sess-2"));
            queue.Push(Update("c", "c", "sess-3"));
            queue.Flush();

            // Assert
            sent.Count.Should().Be(3);
        }

        [Fact]
        public void Flush_sid_on_an_empty_session_is_a_no_op()
        {
            // Arrange
            var sent = new List<object>();
            var queue = new SessionStreamScheduler(msg => sent.Add(msg));

            // Act
            queue.Flush("sess-does-not-exist");

            // Assert
            sent.Count.Should().Be(0);
        }
    }
}



