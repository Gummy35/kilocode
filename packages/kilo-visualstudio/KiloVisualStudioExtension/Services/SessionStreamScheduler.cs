using KiloExtensionDTOs;
using KiloExtensionDTOs.KiloProviderUtils;
using KiloVisualStudioExtension.ApiClient;
using Microsoft.VisualStudio.RpcContracts.Commands;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.VisualStudio.Shell.ThreadedWaitDialogHelper;
using static System.Windows.Forms.AxHost;

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
    // Scheduler tuning — rationale:
    //
    // These defaults balance perceived streaming smoothness against renderer pressure.
    // The scheduler sits between SSE (dozens to hundreds of deltas per second per session)
    // and the webview message loop, which applies updates through Solid `batch()` and
    // triggers DOM / style / layout work on the renderer main thread.
    //
    // - DEFAULT_ACTIVE_MS = 16
    //   One 60Hz animation frame. The focused session should feel indistinguishable
    //   from immediate streaming, so we coalesce within a single paint window but no
    //   longer. Raising this to 32ms visibly stutters live text; lowering below ~8ms
    //   stops coalescing meaningfully because SSE delta arrival is already ~10-20ms
    //   apart at typical model rates.
    //
    // - DEFAULT_BG_BASE_MS = 150
    //   Background (non-focused) sessions don't need frame-perfect updates — the user
    //   can't see their content. 150ms keeps tab-status signals (spinner motion,
    //   token counts via related events) feeling alive while collapsing most
    //   per-token deltas into a single batched emission. Under 100ms the coalescing
    //   win shrinks; over ~250ms users start perceiving lag when switching tabs
    //   mid-stream (though `focus()` also immediately flushes, so this is mostly a
    //   concern for users watching tab-level indicators).
    //
    // - DEFAULT_BG_STEP_MS = 20
    //   Per-extra-background-session backoff beyond the first 2. Each additional
    //   streaming agent adds ~20ms to the background interval so total background
    //   message throughput stays roughly flat as the agent count grows. Without this,
    //   10 concurrent agents would put the same ~7 msg/sec pressure per-session on
    //   the renderer as 1 agent does (~70 msg/sec total background).
    //
    // - DEFAULT_BG_MAX_MS = 400
    //   Ceiling for the adaptive backoff. Even with 20+ agents streaming we never
    //   stall background emissions longer than 400ms, which keeps tab indicators
    //   recognizably "live" and keeps the `drop()`-on-delete path timely. Above
    //   ~500ms the UI starts feeling disconnected; below 300ms the many-agent
    //   backoff stops providing meaningful throttling.
    private const int DEFAULT_ACTIVE_MS = 16;
    private const int DEFAULT_VISIBLE_MS = 50;
    private const int DEFAULT_BG_BASE_MS = 150;
    private const int DEFAULT_BG_STEP_MS = 20;
    private const int DEFAULT_BG_MAX_MS = 400;
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
    private readonly Action<IWebviewMessage> _send;
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// Creates a new SessionStreamScheduler instance.
    /// </summary>
    /// <param name="send">Action to send messages to the webview. Receives PartUpdate|PartsBatch.</param>
    /// <param name="options">Optional configuration options.</param>
    public SessionStreamScheduler(Action<IWebviewMessage> send, Options? options = null)
    {
      _send = send;
      _activeMs = options?.ActiveMs ?? DEFAULT_ACTIVE_MS;
      _visibleMs = options?.VisibleMs ?? DEFAULT_VISIBLE_MS;
      _bgBase = options?.BackgroundBaseMs ?? DEFAULT_BG_BASE_MS;
      _bgStep = options?.BackgroundStepMs ?? DEFAULT_BG_STEP_MS;
      _bgMax = options?.BackgroundMaxMs ?? DEFAULT_BG_MAX_MS;
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
    /// <param name="update">The part update.</param>
    public void Push(PartUpdate msg)
    {
      lock (_lock)
      {
        _counters.Received++;

        var key = PartUpdateKey(msg);

        var queue = EnsureQueue(msg.SessionID);
        // A full-part replacement after buffered deltas would lose information; flush first.
        if (queue.TryGetValue(key, out var prev) && prev.Delta != null && msg.Delta == null)
        {
          Flush(msg.SessionID);
          EnsureQueue(msg.SessionID)[key] = msg;
        }
        else
        {
          queue[key] = MergePartUpdate(prev, msg);
        }

        Schedule(msg.SessionID);
      }
    }

    public Dictionary<string, PartUpdate> EnsureQueue(string sid)
    {
      var existing = _queues.ContainsKey(sid) ? _queues[sid] : null;
      if (existing != null) return existing;
      var queue = new Dictionary<string, PartUpdate>();
      _queues[sid] = queue;
      return queue;
    }

    public string PartUpdateKey(PartUpdate msg)
    {
      if (msg.Part != null && msg.Part is JObject partObj)
      {
        var id = partObj["id"]?.Value<string>() ?? null;
        var mid = partObj["messageID"]?.Value<string>() ?? null;
        if (id == null || mid == null) return null;
        return $"{msg.SessionID}:{mid}:{id}";
      }
      return null;
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
          Emit(TakeAll());
          return;
        }

        if (_active == sessionID && _activeTimer != null)
        {
          _activeTimer.Dispose();
          _activeTimer = null;
        }

        Emit(Take(sessionID));

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

    private List<PartUpdate> TakeAll()
    {
      var updates = _queues.Values
          .SelectMany(queue => queue.Values)
          .ToList();

      _queues.Clear();
      return updates;
    }

    private List<PartUpdate> Take(string sessionId)
    {
      var queue = _queues.ContainsKey(sessionId) ? _queues[sessionId] : null;
      if (queue == null) return new List<PartUpdate>();
      _queues.Remove(sessionId);
      return queue.Values.ToList();
    }

    /// <summary>
    ///    Discard any queued updates for a session without emitting them.
    ///    Called when an authoritative snapshot supersedes buffered deltas
    ///    (messagesLoaded fetch) or when a session is deleted.Does NOT alter focus
    ///    state — callers that also want to clear focus should call `focus(undefined)`
    ///    themselves.A pending active - lane timer is left to fire harmlessly
    ///    (`take()` returns `[]` for the emptied queue).
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
        _activeTimer = new Timer(_ => FlushActive(), null, _activeMs, System.Threading.Timeout.Infinite);
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
      _visibleTimer = new Timer(_ => FlushVisible(), null, (int)remaining, System.Threading.Timeout.Infinite);
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
      _backgroundTimer = new Timer(_ => FlushBackground(), null, (int)remaining, System.Threading.Timeout.Infinite);
    }

    private void FlushActive()
    {
      lock (_lock)
      {
        _activeTimer = null;
        if (_active != null) Emit(Take(_active));
      }
    }

    private void FlushBackground()
    {
      lock (_lock)
      {
        _backgroundTimer = null;
        _backgroundFirstQueuedAt = 0;
        Emit(TakeBackground());
      }
    }

    private void FlushVisible()
    {
      lock (_lock)
      {
        _visibleTimer = null;
        _visibleFirstQueuedAt = 0;
        Emit(TakeVisible());
      }
    }

    private List<PartUpdate> TakeBackground()
    {
      var updates = new List<PartUpdate>();
      var removes = new List<string>();
      foreach(var kv in _queues)
      {
        if (kv.Key == _active) continue;
        if (_visible.Contains(kv.Key)) continue;
        updates.AddRange(kv.Value.Values);
        removes.Add(kv.Key);
      }
      foreach (var key in removes) _queues.Remove(key);

      return updates;
    }

    private List<PartUpdate> TakeVisible()
    {
      var updates = new List<PartUpdate>();
      var removes = new List<string>();
      foreach (var kv in _queues)
      {
        if (kv.Key == _active) continue;
        if (!_visible.Contains(kv.Key)) continue;
        updates.AddRange(kv.Value.Values);
        removes.Add(kv.Key);
      }
      foreach (var key in removes) _queues.Remove(key);

      return updates;
    }


    private void Emit(List<PartUpdate> updates)
    {
      if (updates.Count == 0) return;
      if (updates.Count == 1) {
        EmitOne(updates[0]);
        return;
      }
      _counters.Emitted += updates.Count;
      _counters.Batches++;
      CountLane(updates[0].SessionID);
      _send(new PartBatch { Updates = updates });
    }
    private void EmitOne(PartUpdate update)
    {
      _counters.Emitted++;
      _counters.Batches++;
      CountLane(update.SessionID);
      _send(update);
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

    private PartUpdate MergePartUpdate(PartUpdate? prev, PartUpdate msg)
    {
      if (prev == null) return null;
      var text = msg.Delta as JObject;
      if (text == null) return msg.Delta != null ? prev : msg;
      if (prev.Delta == null)
        return new PartUpdate
        {
          Delta = prev.Delta,
          MessageID = prev.MessageID,
          SessionID = prev.SessionID,
          Part = AppendPart(prev.Part, text["textDelta"]?.Value<string>())
        };

      var prevDelta = msg.Delta as JObject;
      return new PartUpdate
      {
        MessageID = prev.MessageID,
        SessionID = prev.SessionID,
        Part = AppendPart(prev.Part, text["textDelta"]?.Value<string>()),
        Delta = new PartTextDelta { TextDelta = prevDelta["textDelta"]?.Value<string>() + text["textDelta"]?.Value<string>() }
      };
    }

    private object AppendPart(object? part, string text)
    {
      if (part == null || part is not JObject element) return part;
      var parttype = element.ContainsKey("type") ? element["type"].Value<string>() : null;
      var parttext = element.ContainsKey("text") ? element["text"].Value<string>() : null;
      if (parttype == null) return part;
      if ((parttype != "text" && parttype != "reasoning") || parttext == null) return part;

      var result = element.DeepClone();
      result["text"] = result["text"] + text;
      return result;
    }
  }

  ///// <summary>
  ///// Represents a part update message.
  ///// </summary>
  //public class PartUpdate
  //{
  //    /// <summary>
  //    /// The part data.
  //    /// </summary>
  //    public object? Part { get; set; }

  //    /// <summary>
  //    /// The text delta for incremental updates.
  //    /// </summary>
  //    public string? TextDelta { get; set; }

  //    /// <summary>
  //    /// Delta metadata.
  //    /// </summary>
  //    public object? Delta { get; set; }
  //}
}
