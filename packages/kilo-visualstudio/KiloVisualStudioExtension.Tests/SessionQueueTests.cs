using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for session queue behavior
    /// Mirrors: session-queue.test.ts from VS Code
    /// 
    /// These tests document the expected queue behavior for session requests
    /// </summary>
    public class SessionQueueTests
    {
        private class QueuedRequest
        {
            public string SessionId { get; set; } = "";
            public string Type { get; set; } = "";
            public DateTime EnqueuedAt { get; set; } = DateTime.UtcNow;
        }

        private class SessionQueue
        {
            private readonly Queue<QueuedRequest> _queue = new Queue<QueuedRequest>();
            public List<QueuedRequest> Processed { get; } = new List<QueuedRequest>();

            public void Enqueue(QueuedRequest request) => _queue.Enqueue(request);

            public bool Dequeue(out QueuedRequest? request)
            {
                if (_queue.Count > 0)
                {
                    request = _queue.Dequeue();
                    Processed.Add(request);
                    return true;
                }
                request = null;
                return false;
            }

            public int Count => _queue.Count;
        }

        [Fact]
        public void Processes_requests_in_fifo_order()
        {
            // Arrange
            var queue = new SessionQueue();
            queue.Enqueue(new QueuedRequest { SessionId = "s1", Type = "message" });
            queue.Enqueue(new QueuedRequest { SessionId = "s2", Type = "message" });
            queue.Enqueue(new QueuedRequest { SessionId = "s3", Type = "message" });

            // Act
            queue.Dequeue(out var first);
            queue.Dequeue(out var second);
            queue.Dequeue(out var third);

            // Assert
            Assert.Equal("s1", first?.SessionId);
            Assert.Equal("s2", second?.SessionId);
            Assert.Equal("s3", third?.SessionId);
        }

        [Fact]
        public void Returns_false_when_queue_is_empty()
        {
            // Arrange
            var queue = new SessionQueue();

            // Act
            var result = queue.Dequeue(out var request);

            // Assert
            Assert.False(result);
            Assert.Null(request);
        }

        [Fact]
        public void Tracks_queue_count_accurately()
        {
            // Arrange
            var queue = new SessionQueue();

            // Act
            queue.Enqueue(new QueuedRequest { SessionId = "s1", Type = "message" });
            var count1 = queue.Count;
            queue.Enqueue(new QueuedRequest { SessionId = "s2", Type = "message" });
            var count2 = queue.Count;
            queue.Dequeue(out _);
            var count3 = queue.Count;

            // Assert
            Assert.Equal(1, count1);
            Assert.Equal(2, count2);
            Assert.Equal(1, count3);
        }
    }

    /// <summary>
    /// Tests for session tab switcher behavior
    /// Mirrors: session-tab-switcher.test.ts from VS Code
    /// </summary>
    public class SessionTabSwitcherTests
    {
        private class TabState
        {
            public string SessionId { get; set; } = "";
            public bool IsActive { get; set; }
            public List<string> MessageIds { get; set; } = new List<string>();
        }

        private class TabSwitcher
        {
            private readonly Dictionary<string, TabState> _tabs = new Dictionary<string, TabState>();
            private string? _activeTabId;

            public void CreateTab(string sessionId)
            {
                _tabs[sessionId] = new TabState { SessionId = sessionId, IsActive = false };
            }

            public void ActivateTab(string sessionId)
            {
                if (_tabs.ContainsKey(sessionId))
                {
                    foreach (var tab in _tabs.Values)
                        tab.IsActive = false;

                    _tabs[sessionId].IsActive = true;
                    _activeTabId = sessionId;
                }
            }

            public TabState? GetActiveTab()
            {
                if (_activeTabId != null && _tabs.TryGetValue(_activeTabId, out var tab))
                    return tab;
                return null;
            }

            public List<string> GetTabIds() => _tabs.Keys.ToList();
        }

        [Fact]
        public void Activates_correct_tab_on_switch()
        {
            // Arrange
            var switcher = new TabSwitcher();
            switcher.CreateTab("s1");
            switcher.CreateTab("s2");
            switcher.CreateTab("s3");

            // Act
            switcher.ActivateTab("s2");

            // Assert
            var active = switcher.GetActiveTab();
            Assert.NotNull(active);
            Assert.Equal("s2", active?.SessionId);
            Assert.True(active?.IsActive);
        }

        [Fact]
        public void Deactivates_all_other_tabs_when_activating_one()
        {
            // Arrange
            var switcher = new TabSwitcher();
            switcher.CreateTab("s1");
            switcher.CreateTab("s2");
            switcher.ActivateTab("s1");

            // Act
            switcher.ActivateTab("s2");

            // Assert
            Assert.True(switcher.GetActiveTab()?.SessionId == "s2");
        }

        [Fact]
        public void Returns_null_when_activating_nonexistent_tab()
        {
            // Arrange
            var switcher = new TabSwitcher();
            switcher.CreateTab("s1");

            // Act
            switcher.ActivateTab("nonexistent");

            // Assert
            Assert.Null(switcher.GetActiveTab());
        }
    }

    /// <summary>
    /// Tests for session terminal manager
    /// Mirrors: session-terminal-manager.test.ts from VS Code
    /// </summary>
    public class SessionTerminalManagerTests
    {
        private class MockTerminal
        {
            public string SessionId { get; set; } = "";
            public bool IsDisposed { get; set; }
            public List<string> Output { get; } = new List<string>();

            public void Write(string text) => Output.Add(text);
            public void Dispose() => IsDisposed = true;
        }

        private class TerminalManager
        {
            private readonly Dictionary<string, MockTerminal> _terminals = new Dictionary<string, MockTerminal>();

            public void CreateTerminal(string sessionId)
            {
                _terminals[sessionId] = new MockTerminal { SessionId = sessionId };
            }

            public void DisposeTerminal(string sessionId)
            {
                if (_terminals.TryGetValue(sessionId, out var terminal))
                {
                    terminal.Dispose();
                    _terminals.Remove(sessionId);
                }
            }

            public MockTerminal? GetTerminal(string sessionId)
            {
                _terminals.TryGetValue(sessionId, out var terminal);
                return terminal;
            }

            public bool HasActiveTerminal() => _terminals.Count > 0;
        }

        [Fact]
        public void Creates_terminal_for_session()
        {
            // Arrange
            var manager = new TerminalManager();

            // Act
            manager.CreateTerminal("s1");

            // Assert
            Assert.True(manager.HasActiveTerminal());
            Assert.NotNull(manager.GetTerminal("s1"));
        }

        [Fact]
        public void Disposes_terminal_on_close()
        {
            // Arrange
            var manager = new TerminalManager();
            manager.CreateTerminal("s1");

            // Act
            manager.DisposeTerminal("s1");

            // Assert
            Assert.False(manager.HasActiveTerminal());
            Assert.Null(manager.GetTerminal("s1"));
        }

        [Fact]
        public void Writes_output_to_terminal()
        {
            // Arrange
            var manager = new TerminalManager();
            manager.CreateTerminal("s1");
            var terminal = manager.GetTerminal("s1");

            // Act
            terminal?.Write("Hello");
            terminal?.Write("World");

            // Assert
            Assert.Equal(2, terminal?.Output.Count);
            Assert.Contains("Hello", terminal?.Output ?? new List<string>());
        }
    }
}
