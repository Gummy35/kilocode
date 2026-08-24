using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KiloVisualStudioExtension.Services;

namespace KiloVisualStudioExtension.Services.Handlers.Followup
{
    /// <summary>
    /// Handles followup session logic - tracks pending followups and matches them to new sessions.
    /// Matches the VS Code followup-session.ts pattern.
    /// </summary>
    public class FollowupHandlerService : ServiceProviderServiceBase, IDisposable
    {
        private bool _disposed;

        // Pending followup state - moved from SSEHelper
        private Followup? _pendingFollowup;

        private VSProvider Provider => _serviceProvider.GetService<VSProvider>() 
            ?? throw new InvalidOperationException("VSProvider not registered in service provider");

        /// <summary>
        /// Creates a new FollowupHandlerService instance.
        /// </summary>
        public FollowupHandlerService(ServiceProvider serviceProvider): base(serviceProvider)
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
        public bool MatchesPendingFollowup(string sessionDir, long currentTime, string? parentID)
        {
            if (parentID != null) return false;
            if (_pendingFollowup == null) return false;
            if (currentTime - _pendingFollowup.Time > 30_000) return false; // 30 second TTL
            return NormalizePath(_pendingFollowup.Dir) == NormalizePath(sessionDir);
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
