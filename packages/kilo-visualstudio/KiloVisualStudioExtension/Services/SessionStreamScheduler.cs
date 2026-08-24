using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services
{
    /// <summary>
    /// Session-aware streaming scheduler that sits between SSE events and the webview postMessage path.
    /// Coalesces repeated partUpdated events for the same (sessionID, messageID, partID) tuple,
    /// prioritizes the focused session, and throttles background sessions so multi-agent streaming
    /// doesn't saturate the renderer main thread.
    /// 
    /// This matches the VS Code implementation in kilo-provider/session-stream-scheduler.ts.
    /// 
    /// Scheduler tuning rationale (matching TypeScript defaults):
    /// - ActiveMs (16ms): One 60Hz animation frame. The focused session should feel indistinguishable
    ///   from immediate streaming, so we coalesce within a single paint window but no longer.
    /// - VisibleMs (50ms): Flush cadence for visible inline child sessions.
    /// - BackgroundBaseMs (150ms): Background (non-focused) sessions don't need frame-perfect updates.
    ///   150ms keeps tab-status signals feeling alive while collapsing per-token deltas.
    /// - BackgroundStepMs (20ms): Per-extra-background-session backoff beyond the first 2.
    /// - BackgroundMaxMs (400ms): Ceiling for the adaptive backoff.
    /// </summary>
    public class SessionStreamScheduler : IServiceProviderService, IDisposable
    {
        /// <summary>
        /// Options for configuring the scheduler behavior.
        /// </summary>
        public class Options
        {
            /// <summary>
            /// Flush cadence for the focused/active session. Defaults to 16ms.
            /// </summary>
            public int ActiveMs { get; set; } = 16;

            /// <summary>
            /// Base background cadence. Defaults to 150ms.
            /// </summary>
            public int BackgroundBaseMs { get; set; } = 150;

            /// <summary>
            /// Additional ms per background session above the first 2. Defaults to 20ms.
            /// </summary>
            public int BackgroundStepMs { get; set; } = 20;

            /// <summary>
            /// Hard cap for the background cadence. Defaults to 400ms.
            /// </summary>
            public int BackgroundMaxMs { get; set; } = 400;

            /// <summary>
            /// Flush cadence for visible inline child sessions. Defaults to 50ms.
            /// </summary>
            public int VisibleMs { get; set; } = 50;
        }

        /// <summary>
        /// Statistics for monitoring scheduler performance.
        /// </summary>
        public class Stats
        {
            public int Received { get; set; }
            public int Emitted { get; set; }
            public int Batches { get; set; }
            public int Active { get; set; }
            public int Visible { get; set; }
            public int Background { get; set; }
        }

        private string? _active;
        private Timer? _activeTimer;
        private Timer? _visibleTimer;
        private Timer? _backgroundTimer;
        private long _backgroundFirstQueuedAt = 0;
        private long _visibleFirstQueuedAt = 0;
        private readonly Dictionary<string, Dictionary<string, PartUpdate>> _queues = new();
        private readonly HashSet<string> _visible = new();
        private readonly int _activeMs;
        private readonly int _visibleMs;
        private readonly int _bgBase;
        private readonly int _bgStep;
        private readonly int _bgMax;
        private readonly Stats _counters = new();
        private readonly Action<string, string, PartUpdate> _send;
        private readonly object _lock = new();
        private bool _disposed;

        /// <summary>
        /// Creates a new SessionStreamScheduler instance.
        /// </summary>
        /// <param name="send">Action to send messages to the webview. Receives (sessionID, messageID, partUpdate).</param>
        /// <param name="options">Optional configuration options.</param>
        public SessionStreamScheduler(Action<string, string, PartUpdate> send, Options? options = null)
        {
            _send = send;
            _activeMs = options?.ActiveMs ?? 16;
            _visibleMs = options?.VisibleMs ?? 50;
            _bgBase = options?.BackgroundBaseMs ?? 150;
            _bgStep = options?.BackgroundStepMs ?? 20;
            _bgMax = options?.BackgroundMaxMs ?? 400;
        }

        /// <summary>
        /// Currently focused (active-lane) session ID, if any.
        /// </summary>
        public string? Focused => _active;

        /// <summary>
        /// Sets the focus to a specific session.
        /// </summary>
        /// <param name="sessionID">The session ID to focus, or null to clear focus.</param>
        public void Focus(string? sessionID)
        {
            lock (_lock)
            {
                if (_active == sessionID) return;
                var prev = _active;
                if (_activeTimer != null)
                {
                    _activeTimer.Dispose();
                    _activeTimer = null;
                }
                _active = sessionID;
                if (prev != null && HasQueue(prev)) Schedule(prev);
                if (sessionID != null) Flush(sessionID);
            }
        }

        /// <summary>
        /// Sets the visibility state for a session.
        /// </summary>
        /// <param name="sessionID">The session ID.</param>
        /// <param name="visible">True if the session is visible, false otherwise.</param>
        public void SetVisible(string sessionID, bool visible)
        {
            lock (_lock)
            {
                var changed = visible ? !_visible.Contains(sessionID) : _visible.Contains(sessionID);
                if (!changed) return;
                if (visible) _visible.Add(sessionID);
                else _visible.Remove(sessionID);
                if (HasQueue(sessionID)) Schedule(sessionID);
                if (_visibleTimer != null && !HasVisible())
                {
                    _visibleTimer.Dispose();
                    _visibleTimer = null;
                }
                if (_backgroundTimer != null && !HasBackground())
                {
                    _backgroundTimer.Dispose();
                    _backgroundTimer = null;
                }
            }
        }

        /// <summary>
        /// Pushes a part update to the scheduler.
        /// </summary>
        /// <param name="sessionID">The session ID.</param>
        /// <param name="messageID">The message ID.</param>
        /// <param name="update">The part update.</param>
        public void Push(string sessionID, string messageID, PartUpdate update)
        {
            lock (_lock)
            {
                _counters.Received++;
                var key = $"{sessionID}:{messageID}";
                
                if (!_queues.TryGetValue(sessionID, out var queue))
                {
                    queue = new Dictionary<string, PartUpdate>();
                    _queues[sessionID] = queue;
                }

                if (queue.TryGetValue(key, out var prev))
                {
                    queue[key] = MergePartUpdate(prev, update);
                }
                else
                {
                    queue[key] = update;
                }
                Schedule(sessionID);
            }
        }

        /// <summary>
        /// Flushes all pending updates for a session.
        /// </summary>
        /// <param name="sessionID">The session ID, or null to flush all sessions.</param>
        public void Flush(string? sessionID)
        {
            lock (_lock)
            {
                if (sessionID == null)
                {
                    ClearTimers();
                    EmitAll();
                    return;
                }

                if (_active == sessionID && _activeTimer != null)
                {
                    _activeTimer.Dispose();
                    _activeTimer = null;
                }

                Emit(sessionID);

                if (_visibleTimer != null && !HasVisible())
                {
                    _visibleTimer.Dispose();
                    _visibleTimer = null;
                }

                if (_backgroundTimer != null && !HasBackground())
                {
                    _backgroundTimer.Dispose();
                    _backgroundTimer = null;
                }
            }
        }

        /// <summary>
        /// Discards any queued updates for a session without emitting them.
        /// Called when an authoritative snapshot supersedes buffered deltas.
        /// </summary>
        /// <param name="sessionID">The session ID to drop.</param>
        public void Drop(string sessionID)
        {
            lock (_lock)
            {
                _queues.Remove(sessionID);
                if (_visibleTimer != null && !HasVisible())
                {
                    _visibleTimer.Dispose();
                    _visibleTimer = null;
                }
                if (_backgroundTimer != null && !HasBackground())
                {
                    _backgroundTimer.Dispose();
                    _backgroundTimer = null;
                }
            }
        }

        /// <summary>
        /// Gets scheduler statistics.
        /// </summary>
        public Stats GetStats()
        {
            lock (_lock)
            {
                return new Stats
                {
                    Received = _counters.Received,
                    Emitted = _counters.Emitted,
                    Batches = _counters.Batches,
                    Active = _counters.Active,
                    Visible = _counters.Visible,
                    Background = _counters.Background
                };
            }
        }

        /// <summary>
        /// Disposes of all resources used by the scheduler.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (_lock)
            {
                ClearTimers();
                _queues.Clear();
                _visible.Clear();
            }
        }

        private bool HasQueue(string sessionID)
        {
            return _queues.TryGetValue(sessionID, out var queue) && queue.Count > 0;
        }

        private void Schedule(string sessionID)
        {
            if (!HasQueue(sessionID)) return;

            if (_active == sessionID)
            {
                if (_activeTimer != null) return;
                _activeTimer = new Timer(_ => FlushActive(), null, _activeMs, Timeout.Infinite);
                return;
            }

            if (_visible.Contains(sessionID))
            {
                ScheduleVisible();
                return;
            }

            ScheduleBackground();
        }

        private void ScheduleVisible()
        {
            if (!HasVisible()) return;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (_visibleTimer == null) _visibleFirstQueuedAt = now;
            var elapsed = now - _visibleFirstQueuedAt;
            var remaining = Math.Max(0, _visibleMs - elapsed);
            if (_visibleTimer != null) _visibleTimer.Dispose();
            _visibleTimer = new Timer(_ => FlushVisible(), null, remaining, Timeout.Infinite);
        }

        private void ScheduleBackground()
        {
            var count = BackgroundCount();
            if (count == 0) return;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (_backgroundTimer == null) _backgroundFirstQueuedAt = now;
            var elapsed = now - _backgroundFirstQueuedAt;
            var extra = Math.Max(0, count - 2) * _bgStep;
            var target = Math.Min(_bgMax, _bgBase + extra);
            var remaining = Math.Max(0, Math.Min(target - elapsed, _bgMax - elapsed));
            if (_backgroundTimer != null) _backgroundTimer.Dispose();
            _backgroundTimer = new Timer(_ => FlushBackground(), null, remaining, Timeout.Infinite);
        }

        private void FlushActive()
        {
            lock (_lock)
            {
                _activeTimer = null;
                if (_active != null) Emit(_active);
            }
        }

        private void FlushBackground()
        {
            lock (_lock)
            {
                _backgroundTimer = null;
                _backgroundFirstQueuedAt = 0;
                EmitBackground();
            }
        }

        private void FlushVisible()
        {
            lock (_lock)
            {
                _visibleTimer = null;
                _visibleFirstQueuedAt = 0;
                EmitVisible();
            }
        }

        private void Emit(string sessionID)
        {
            if (_queues.TryGetValue(sessionID, out var queue))
            {
                foreach (var kvp in queue)
                {
                    EmitOne(sessionID, kvp.Key, kvp.Value);
                }
                _queues.Remove(sessionID);
            }
        }

        private void EmitAll()
        {
            foreach (var kvp in _queues)
            {
                foreach (var item in kvp.Value)
                {
                    EmitOne(kvp.Key, item.Key, item.Value);
                }
            }
            _queues.Clear();
        }

        private void EmitBackground()
        {
            var toEmit = new List<(string, string, PartUpdate)>();
            foreach (var kvp in _queues)
            {
                if (kvp.Key == _active) continue;
                if (_visible.Contains(kvp.Key)) continue;
                foreach (var item in kvp.Value)
                {
                    toEmit.Add((kvp.Key, item.Key, item.Value));
                }
            }
            foreach (var (sid, key, update) in toEmit)
            {
                EmitOne(sid, key, update);
            }
            foreach (var sid in toEmit.Select(t => t.Item1).Distinct())
            {
                _queues.Remove(sid);
            }
        }

        private void EmitVisible()
        {
            var toEmit = new List<(string, string, PartUpdate)>();
            foreach (var kvp in _queues)
            {
                if (kvp.Key == _active) continue;
                if (!_visible.Contains(kvp.Key)) continue;
                foreach (var item in kvp.Value)
                {
                    toEmit.Add((kvp.Key, item.Key, item.Value));
                }
            }
            foreach (var (sid, key, update) in toEmit)
            {
                EmitOne(sid, key, update);
            }
            foreach (var sid in toEmit.Select(t => t.Item1).Distinct())
            {
                _queues.Remove(sid);
            }
        }

        private void EmitOne(string sessionID, string key, PartUpdate update)
        {
            _counters.Emitted++;
            _counters.Batches++;
            CountLane(sessionID);
            _send(sessionID, key, update);
        }

        private int VisibleCount()
        {
            var n = 0;
            foreach (var kvp in _queues)
            {
                if (kvp.Key != _active && _visible.Contains(kvp.Key) && kvp.Value.Count > 0) n++;
            }
            return n;
        }

        private int BackgroundCount()
        {
            var n = 0;
            foreach (var kvp in _queues)
            {
                if (kvp.Key != _active && !_visible.Contains(kvp.Key) && kvp.Value.Count > 0) n++;
            }
            return n;
        }

        private bool HasVisible() => VisibleCount() > 0;
        private bool HasBackground() => BackgroundCount() > 0;

        private void CountLane(string sessionID)
        {
            if (sessionID == _active) _counters.Active++;
            else if (_visible.Contains(sessionID)) _counters.Visible++;
            else _counters.Background++;
        }

        private void ClearTimers()
        {
            if (_activeTimer != null)
            {
                _activeTimer.Dispose();
                _activeTimer = null;
            }
            if (_visibleTimer != null)
            {
                _visibleTimer.Dispose();
                _visibleTimer = null;
            }
            if (_backgroundTimer != null)
            {
                _backgroundTimer.Dispose();
                _backgroundTimer = null;
            }
        }

        private PartUpdate MergePartUpdate(PartUpdate prev, PartUpdate msg)
        {
            if (msg.TextDelta == null) return msg.Delta != null ? prev : msg;
            if (prev.TextDelta == null) return new PartUpdate { Part = AppendPart(prev.Part, msg.TextDelta), TextDelta = msg.TextDelta };
            return new PartUpdate
            {
                Part = AppendPart(prev.Part, msg.TextDelta),
                TextDelta = prev.TextDelta + msg.TextDelta
            };
        }

        private object AppendPart(object? part, string text)
        {
            if (part == null || part is not JsonElement element) return part;
            if (!element.TryGetProperty("type", out var typeProp)) return part;
            var type = typeProp.GetString();
            if (type != "text" && type != "reasoning") return part;
            if (!element.TryGetProperty("text", out var textProp)) return part;
            var currentText = textProp.GetString() ?? "";
            var props = new Dictionary<string, object>();
            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Name == "text") props[prop.Name] = currentText + text;
                else props[prop.Name] = prop.Value.Clone();
            }
            return JsonSerializer.SerializeToElement(props, typeof(Dictionary<string, object>));
        }
    }

    /// <summary>
    /// Represents a part update message.
    /// </summary>
    public class PartUpdate
    {
        /// <summary>
        /// The part data.
        /// </summary>
        public object? Part { get; set; }

        /// <summary>
        /// The text delta for incremental updates.
        /// </summary>
        public string? TextDelta { get; set; }

        /// <summary>
        /// Delta metadata.
        /// </summary>
        public object? Delta { get; set; }
    }
}
