using System;
using System.Collections.Generic;
using System.Linq;
using KiloVisualStudioExtension.Services;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for SessionQueue service
    /// </summary>
    public class SessionQueueTests
    {
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
    /// Tests for TabSwitcher service
    /// </summary>
    public class SessionTabSwitcherTests
    {
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
    /// Tests for TerminalManager service
    /// </summary>
    public class SessionTerminalManagerTests
    {
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

        [Fact]
        public void Tracks_multiple_terminals()
        {
            // Arrange
            var manager = new TerminalManager();

            // Act
            manager.CreateTerminal("s1");
            manager.CreateTerminal("s2");
            manager.CreateTerminal("s3");

            // Assert
            Assert.Equal(3, manager.TerminalCount);
        }

        [Fact]
        public void Ignores_dispose_of_nonexistent_terminal()
        {
            // Arrange
            var manager = new TerminalManager();
            manager.CreateTerminal("s1");

            // Act
            manager.DisposeTerminal("nonexistent");

            // Assert
            Assert.Equal(1, manager.TerminalCount);
        }
    }
}
