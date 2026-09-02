using KiloVisualStudioExtension.Services.Git;
using System;

namespace KiloVisualStudioExtension
{
  public class KiloProviderOptions
  {
    /// <summary>
    /// Context key updated from focus events reported by this provider's webview.
    /// </summary>
    public string? FocusContext { get; set; }

    /// <summary>
    /// Project directory for memory operations, or null when project scope is disabled.
    /// </summary>
    public string? ProjectDirectory { get; set; }

    /// <summary>
    /// Platform identifier.
    /// </summary>
    public string? Platform { get; set; }

    /// <summary>
    /// If "wait", wait for snapshot initialization.
    /// </summary>
    public string? SnapshotInitialization { get; set; }

    /// <summary>
    /// Whether to use slim edit metadata (default: true).
    /// </summary>
    public bool SlimEditMetadata { get; set; } = true;

    /// <summary>
    /// Callback to update tab title.
    /// </summary>
    public Action<string>? TabTitle { get; set; }

    /// <summary>
    /// Function that returns worktree directories.
    /// </summary>
    public Func<string[]>? WorktreeDirectories { get; set; }

    /// <summary>
    /// Composite hosts (Agent Manager) own viewed/presence registration themselves.
    /// </summary>
    public bool DisableViewedRegistration { get; set; }

    public Interceptor? OnBeforeMessage { get; set; } = null;
  }
}
