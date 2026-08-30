using EnvDTE;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.Services;
using KiloVisualStudioExtension.Services.Handlers.Session;
using KiloVisualStudioExtension.Utils;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Microsoft.VisualStudio.Shell.ThreadedWaitDialogHelper;

namespace KiloVisualStudioExtension.Services.Handlers.Followup
{
  /// <summary>
  /// Handles followup session logic - tracks pending followups and matches them to new sessions.
  /// Matches the VS Code followup-session.ts pattern.
  /// </summary>
  public class FollowupService : ServiceProviderServiceBase, IDisposable
  {
    private bool _disposed;

    // Pending followup state - moved from SSEHelper
    private Followup? _pendingFollowup;
    private readonly List<Action<ApiClient.Session, string>> _followupListeners = new();

    private VSProvider Provider => _serviceProvider.GetService<VSProvider>()
        ?? throw new InvalidOperationException("VSProvider not registered in service provider");

    /// <summary>
    /// Creates a new FollowupHandlerService instance.
    /// </summary>
    public FollowupService(ServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    /// <summary>
    /// Records a pending followup.
    /// </summary>
    public void RecordFollowup(string dir, long time)
    {
      _pendingFollowup = new Followup { Dir = dir, Time = time };
    }

    /// <summary>
    /// Gets the pending followup.
    /// </summary>
    public Followup? GetPendingFollowup()
    {
      return _pendingFollowup;
    }

    /// <summary>
    /// Clears the pending followup.
    /// </summary>
    public void ClearPendingFollowup()
    {
      _pendingFollowup = null;
    }

    /// <summary>
    /// Checks if a session matches the pending followup.
    /// </summary>
    public bool MatchesPendingFollowup(string sessionDir, long currentTime, string? parentID = null)
    {
      if (parentID != null) return false;
      if (_pendingFollowup == null) return false;
      if (currentTime - _pendingFollowup.Time > 30_000) return false; // 30 second TTL
      return NormalizePath(_pendingFollowup.Dir) == NormalizePath(sessionDir);
    }

    public bool MatchesPendingFollowup(ApiClient.Session session)
    {
      return MatchesPendingFollowup(session.Directory, DateTimeOffset.Now.ToUnixTimeMilliseconds(), session.ParentID);
    }

    public bool AdoptPendingFollowup(ApiClient.Session session)
    {
      var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
      var match = MatchesPendingFollowup(session);
      if (!match)
      {
        if (
          _pendingFollowup != null &&
          !MatchesPendingFollowup(_pendingFollowup.Dir, now))
        {
          ClearPendingFollowup();
        }
        return false;
      }

      ClearPendingFollowup();
      _serviceProvider.GetService<ProjectDirectoryProvider>().TrackDirectory(session.Id, session.Directory);
      foreach (var callback in _followupListeners)
        callback(session, session.Directory);
      return true;
    }

    /// <summary>
    /// Normalizes a path for comparison.
    /// </summary>
    private static string NormalizePath(string path)
    {
      return path?.ToLowerInvariant().Replace("\\", "/") ?? "";
    }

    public void Dispose()
    {
      if (_disposed) return;
      _disposed = true;
    }
  }

  /// <summary>
  /// Represents a pending followup session.
  /// </summary>
  public class Followup
  {
    public string Dir { get; set; } = "";
    public long Time { get; set; }
  }
}
