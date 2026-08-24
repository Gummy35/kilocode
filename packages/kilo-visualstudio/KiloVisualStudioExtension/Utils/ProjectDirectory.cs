using EnvDTE;
using KiloVisualStudioExtension.Services;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;

namespace KiloVisualStudioExtension.Utils
{
  /// <summary>
  /// Resolves the workspace directory based on session ID and session directories map.
  /// </summary>
  public static class WorkspaceDirectoryResolver
  {
    public static string ResolveWorkspaceDirectory(string? sessionID, IDictionary<string, string> sessionDirectories, string workspaceDirectory)
    {
      if (string.IsNullOrEmpty(sessionID))
        return workspaceDirectory;

      if (sessionDirectories.TryGetValue(sessionID, out var dir))
        return dir;

      return workspaceDirectory;
    }
  }

  /// <summary>
  /// Resolves the context directory for operations.
  /// </summary>
  public static class ContextDirectoryResolver
  {
    public static string ResolveContextDirectory(
        string? currentSessionID,
        string? contextSessionID,
        IDictionary<string, string> sessionDirectories,
        string workspaceDirectory,
        bool forceWorkspaceRoot = false)
    {
      if (forceWorkspaceRoot)
        return workspaceDirectory;

      return WorkspaceDirectoryResolver.ResolveWorkspaceDirectory(
          currentSessionID ?? contextSessionID,
          sessionDirectories,
          workspaceDirectory);
    }
  }

  /// <summary>
  /// Resolves the directory for a new session.
  /// </summary>
  public static class NewSessionDirectoryResolver
  {
    public static string ResolveNewSessionDirectory(
        string? sessionID,
        string? currentSessionID,
        string? contextSessionID,
        string? agentManagerContext,
        string? contextDirectory,
        IDictionary<string, string> sessionDirectories,
        string workspaceDirectory)
    {
      if (!string.IsNullOrEmpty(sessionID))
      {
        return WorkspaceDirectoryResolver.ResolveWorkspaceDirectory(
            sessionID,
            sessionDirectories,
            workspaceDirectory);
      }

      if (!string.IsNullOrEmpty(contextDirectory))
        return contextDirectory;

      return ContextDirectoryResolver.ResolveContextDirectory(
          currentSessionID,
          contextSessionID,
          sessionDirectories,
          workspaceDirectory,
          forceWorkspaceRoot: agentManagerContext == "local");
    }
  }

  /// <summary>
  /// Resolves the effective project directory.
  /// </summary>
  public static class ProjectDirectoryResolver
  {
    public static string? ResolveProjectDirectory(string? @override, Func<string?> fallback)
    {
      if (@override != null)
        return @override;

      return fallback();
    }
  }

  /// <summary>
  /// Tracks session directories and provides directory resolution.
  /// Mirrors trackDirectory and getProjectDirectory from KiloProvider.ts
  /// </summary>
  public class ProjectDirectoryProvider: IServiceProviderService
  {
    private readonly string? _projectDirectoryOverride;
    private readonly Func<string?, string?> _getWorkspaceDirectory;
    private readonly Func<string?>? _getSolutionDirectory;
    private readonly IDictionary<string, string> _sessionDirectories;
    private readonly Func<string> _getRootDirectory;

    public ProjectDirectoryProvider(
        string? projectDirectoryOverride,
        Func<string?, string?> getWorkspaceDirectory,
        Func<string?>? getSolutionDirectory = null,
        IDictionary<string, string>? sessionDirectories = null,
        Func<string>? getRootDirectory = null)
    {
      _projectDirectoryOverride = projectDirectoryOverride;
      _getWorkspaceDirectory = getWorkspaceDirectory;
      _getSolutionDirectory = getSolutionDirectory;
      _sessionDirectories = sessionDirectories ?? new Dictionary<string, string>();
      _getRootDirectory = getRootDirectory ?? (() => Directory.GetCurrentDirectory());
    }

    /// <summary>
    /// Gets the project directory for a given session.
    /// </summary>
    public string? GetProjectDirectory(string? sessionId = null)
    {
      return ProjectDirectoryResolver.ResolveProjectDirectory(
          _projectDirectoryOverride,
          () => GetWorkspaceDirectory(sessionId));
    }

    /// <summary>
    /// Gets the workspace directory for a given session.
    /// </summary>
    public string? GetWorkspaceDirectory(string? sessionId = null)
    {
      if (string.IsNullOrEmpty(sessionId))
        return _getWorkspaceDirectory(null);

      if (_sessionDirectories.TryGetValue(sessionId, out var dir))
        return dir;

      return _getWorkspaceDirectory(sessionId);
    }

    /// <summary>
    /// Gets the solution root directory.
    /// </summary>
    public string? GetSolutionDirectory()
    {
      return _getSolutionDirectory?.Invoke();
    }

    /// <summary>
    /// Gets the root directory (workspace root).
    /// </summary>
    public string GetRootDirectory()
    {
      return _getRootDirectory();
    }

    /// <summary>
    /// Tracks a directory for a session.
    /// Removes the session entry if the directory matches the root directory.
    /// </summary>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="dir">The directory to track.</param>
    public void TrackDirectory(string sessionId, string dir)
    {
      var resolvedDir = Path.GetFullPath(dir);
      var resolvedRoot = Path.GetFullPath(_getRootDirectory());

      if (string.Equals(resolvedDir, resolvedRoot, StringComparison.OrdinalIgnoreCase))
      {
        _sessionDirectories.Remove(sessionId);
      }
      else
      {
        _sessionDirectories[sessionId] = dir;
      }
    }

    /// <summary>
    /// Clears the directory override for a session.
    /// </summary>
    public void ClearSessionDirectory(string sessionId)
    {
      _sessionDirectories.Remove(sessionId);
    }

    /// <summary>
    /// Sets a directory override for a session.
    /// </summary>
    public void SetSessionDirectory(string sessionId, string directory)
    {
      _sessionDirectories[sessionId] = directory;
    }

    /// <summary>
    /// Gets all tracked session directories.
    /// </summary>
    public IDictionary<string, string> GetSessionDirectories()
    {
      return new Dictionary<string, string>(_sessionDirectories);
    }
  }

  /// <summary>
  /// Visual Studio-specific implementation using DTE.
  /// </summary>
  public class VisualStudioDirectoryProvider: IServiceProviderService
  {
    private readonly DTE _dte;

    public VisualStudioDirectoryProvider(DTE dte)
    {
      _dte = dte;
    }

    public string GetActiveProjectDirectory()
    {
      try
      {
        var activeDoc = _dte.ActiveDocument;
        if (activeDoc != null && !string.IsNullOrEmpty(activeDoc.FullName))
        {
          var docPath = Path.GetDirectoryName(activeDoc.FullName);
          if (!string.IsNullOrEmpty(docPath))
          {
            foreach (Project project in _dte.Solution.Projects)
            {
              var projectPath = GetProjectPath(project);
              if (projectPath != null && docPath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
              {
                return projectPath;
              }
            }

            return docPath;
          }
        }
      }
      catch
      {
        // Ignore errors and fall through
      }

      var solutionDir = GetSolutionDirectory();
      if (!string.IsNullOrEmpty(solutionDir))
      {
        return solutionDir;
      }

      return Directory.GetCurrentDirectory();
    }

    public string? GetSolutionDirectory()
    {
      try
      {
        if (_dte.Solution != null && !string.IsNullOrEmpty(_dte.Solution.FullName))
        {
          return Path.GetDirectoryName(_dte.Solution.FullName);
        }
      }
      catch
      {
        // Ignore errors
      }

      return null;
    }

    private string? GetProjectPath(Project project)
    {
      try
      {
        if (!string.IsNullOrEmpty(project.FullName))
        {
          return Path.GetDirectoryName(project.FullName);
        }
      }
      catch
      {
        // Ignore errors
      }

      return null;
    }

    /// <summary>
    /// Creates a ProjectDirectoryProvider configured for Visual Studio with trackDirectory support.
    /// </summary>
    public ProjectDirectoryProvider CreateProvider(
        string? projectDirectoryOverride = null,
        IDictionary<string, string>? sessionDirectories = null)
    {
      var sessionDirs = sessionDirectories ?? new Dictionary<string, string>();
      var self = this;

      return new ProjectDirectoryProvider(
          projectDirectoryOverride,
          sessionId =>
          {
            if (!string.IsNullOrEmpty(sessionId) && sessionDirs.TryGetValue(sessionId, out var sessionDir))
            {
              return sessionDir;
            }

            return self.GetActiveProjectDirectory();
          },
          self.GetSolutionDirectory,
          sessionDirs,
          () => self.GetActiveProjectDirectory());
    }
  }
}
