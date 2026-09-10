// File: packages/kilo-visualstudio/tools/abort.cs
// Ported from: packages/kilo-vscode/src/kilo-provider/abort.ts

using KiloVisualStudioExtension.ApiClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension
{
  /// <summary>
  /// Manages session abortion tracking across directories.
  /// Tracks which sessions are active in which directories and provides
  /// methods to stop sessions when they become idle or when directories are disposed.
  /// </summary>
  public class SessionAbort
  {
    // Map of sessionID -> Set of directories where session is active
    private readonly Dictionary<string, HashSet<string>> _active = new();

    /// <summary>
    /// Observe a session status change for a directory.
    /// Adds directory to active tracking when session becomes busy,
    /// removes when session becomes idle.
    /// </summary>
    /// <param name="sessionId">The session identifier</param>
    /// <param name="status">The session status type ("idle" or other)</param>
    /// <param name="dir">The directory path to track</param>
    public void Observe(string sessionId, string status, string dir = null)
    {
      if (dir == null) return;

      if (!_active.TryGetValue(sessionId, out var dirs))
      {
        dirs = new HashSet<string>();
        _active[sessionId] = dirs;
      }

      if (status == "idle")
      {
        if (dirs == null) return;
        // Remove all entries that match the directory
        var toRemove = dirs.Where(entry => Utils.PathUtils.SameDirectory(entry, dir)).ToList();
        foreach (var entry in toRemove)
        {
          dirs.Remove(entry);
        }
        // Clean up empty sets
        if (dirs.Count == 0)
        {
          _active.Remove(sessionId);
        }
        return;
      }

      if (dirs == null)
      {
        dirs = new HashSet<string> { dir };
        _active[sessionId] = dirs;
        return;
      }

      // Add directory if not already present
      if (!dirs.Any(entry => Utils.PathUtils.SameDirectory(entry, dir)))
      {
        dirs.Add(dir);
      }
    }

    /// <summary>
    /// Preserve session tracking if not already tracked.
    /// Only calls Observe if session is not idle and not already tracked.
    /// </summary>
    /// <param name="sessionId">The session identifier</param>
    /// <param name="status">The session status type or null</param>
    /// <param name="dir">The directory path to track</param>
    public void Preserve(string sessionId, string status, string dir)
    {
      if (status == null || status == "idle" || _active.ContainsKey(sessionId)) return;
      Observe(sessionId, status, dir);
    }

    /// <summary>
    /// Stop a session across all tracked directories plus fallback.
    /// Returns true if session was known and successfully stopped.
    /// </summary>
    /// <param name="client">The Kilo client for API calls</param>
    /// <param name="sessionId">The session identifier</param>
    /// <param name="fallback">Fallback directory to include</param>
    /// <returns>True if session was known and all aborts succeeded, false otherwise</returns>
    public async Task<bool> StopAsync(KiloApiClient client, string sessionId, string fallback)
    {
      var known = _active.ContainsKey(sessionId);
      var dirs = _active.TryGetValue(sessionId, out var dirSet)
          ? dirSet.ToList()
          : new List<string>();

      // Add fallback if not already present
      if (!dirs.Any(dir => Utils.PathUtils.SameDirectory(dir, fallback)))
      {
        dirs.Add(fallback);
      }

      // Abort session in all directories
      var tasks = dirs.Select(dir => AbortSessionAsync(client, sessionId, dir)).ToArray();
      await Task.WhenAll(tasks);

      // Collect failures
      var failures = new List<(string dir, Exception error)>();
      for (var i = 0; i < tasks.Length; i++)
      {
        if (tasks[i].IsFaulted)
        {
          failures.Add((dirs[i], tasks[i].Exception?.InnerException ?? tasks[i].Exception));
        }
      }

      if (failures.Count > 0)
      {
        System.Console.Error.WriteLine($"[Kilo New] KiloProvider: Failed to abort session in one or more directories: {failures}");
        return false;
      }

      // Clean up known session tracking
      if (known)
      {
        _active.Remove(sessionId);
      }

      return known;
    }

    /// <summary>
    /// Dispose a directory from tracking.
    /// Removes the directory from all sessions and returns sessions that became idle.
    /// </summary>
    /// <param name="dir">The directory path to dispose</param>
    /// <returns>List of session IDs that became idle after disposal</returns>
    public List<string> Dispose(string dir)
    {
      var idle = new List<string>();

      // Iterate over a copy of keys to avoid modification during iteration
      var sessionIds = _active.Keys.ToList();

      foreach (var sessionId in sessionIds)
      {
        if (!_active.TryGetValue(sessionId, out var dirs)) continue;

        // Remove matching directories
        var toRemove = dirs.Where(entry => Utils.PathUtils.SameDirectory(entry, dir)).ToList();
        foreach (var entry in toRemove)
        {
          dirs.Remove(entry);
        }

        // If still has directories, continue
        if (dirs.Count > 0) continue;

        // Session is now idle, remove and track
        _active.Remove(sessionId);
        idle.Add(sessionId);
      }

      return idle;
    }

    /// <summary>
    /// Delete all tracking for a session.
    /// </summary>
    /// <param name="sessionId">The session identifier</param>
    public void Delete(string sessionId)
    {
      _active.Remove(sessionId);
    }

    /// <summary>
    /// Clear all session tracking.
    /// </summary>
    public void Clear()
    {
      _active.Clear();
    }

    /// <summary>
    /// Abort a session in a specific directory.
    /// </summary>
    /// <param name="client">The Kilo client for API calls</param>
    /// <param name="sessionId">The session identifier</param>
    /// <param name="dir">The directory path</param>
    private static async Task AbortSessionAsync(KiloApiClient client, string sessionId, string dir)
    {
      await client.Session_abortAsync(sessionId, dir, "");
    }
  }
}
