using KiloExtensionDTOs;
using KiloExtensionDTOs.ExtensionMessages;
using KiloExtensionDTOs.WebviewMessages;
using KiloVisualStudioExtension;
using KiloVisualStudioExtension.ApiClient;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Xml.Linq;

namespace KiloProvider
{
  public delegate Task<IWebviewMessage> Interceptor(IWebviewMessage msg);



  public class Context
  {
    public Func<string?, string> WorkspaceDir { get; set; } = null!;
    public Action<object> Post { get; set; } = null!;
    public Func<object, string> Error { get; set; } = null!;
    public Interceptor? Before { get; set; }
  }

  public class Input
  {
    public string RequestId { get; set; }
    public string Dir { get; set; }
    public string Base { get; set; }
    public Action<object> Post { get; set; } = null;
    public Func<object, string> Error { get; set; } = null;
  }


  /// <summary>
  /// Result of resolving a local diff target.
  /// </summary>
  public class LocalDiffTarget
  {
    /// <summary>
    /// The directory path for the diff.
    /// </summary>
    public string Directory { get; set; } = string.Empty;

    /// <summary>
    /// The base branch to compare against.
    /// </summary>
    public string BaseBranch { get; set; } = string.Empty;
  }



  /// <summary>
  /// Git operations interface for branch operations.
  /// </summary>
  public interface IGitOps
  {
    /// <summary>
    /// Gets the current branch name for a repository.
    /// </summary>
    Task<string?> CurrentBranch(string root);

    /// <summary>
    /// Resolves the tracking branch for a given branch.
    /// </summary>
    Task<string?> ResolveTrackingBranch(string root, string branch);

    /// <summary>
    /// Resolves the default branch for a repository.
    /// </summary>
    Task<string?> ResolveDefaultBranch(string root, string branch);
  }

  public static class GitChangesRequest
  {
    private static GitOps? _shared;
    public static async Task<IWebviewMessage> InterceptMessageAsync(
        IWebviewMessage msg,
        Context ctx)
    {
      IWebviewMessage? next;
      try
      {
        next = ctx.Before != null ? await ctx.Before(msg) : msg;
      }
      catch (Exception e)
      {
        System.Diagnostics.Debug.WriteLine("[Kilo New] interceptor error: " + e);
        next = null;
      }

      if (next == null || !(next is RequestGitChangesContextMessage))
        return next;

      var request = (RequestGitChangesContextMessage)next;
      var sid = request.SessionID;
      var dir = ctx.WorkspaceDir(sid);
      var resolved = await ResolveGitChangesTargetAsync(next, dir);

      try
      {
        string reqId = "";
        string ctxDir = "";
        string baseStr = "";
        if (resolved is RequestGitChangesContextMessage typedResolved)
        {
          reqId = typedResolved.RequestId;
          ctxDir = typedResolved.ContextDirectory;
          baseStr = typedResolved.GitChangesBase;
        }

        await CaptureGitChangesContextAsync(new Input
        {
          RequestId = string.IsNullOrEmpty(reqId) ? "" : reqId,
          Dir = string.IsNullOrEmpty(ctxDir) ? dir : ctxDir, 
          Base = string.IsNullOrEmpty(baseStr) ? null : baseStr,
          Post = ctx.Post,
          Error = ctx.Error
        });
      }
      catch (Exception e)
      {
        System.Diagnostics.Debug.WriteLine("[Kilo New] git changes error: " + e);
      }

      return null;
    }

    internal static GitOps Ops()
    {
      if (_shared != null && !_shared.Disposed) return _shared;
      _shared = new GitOps(new GitOpsOptions{Log = (s) => { } });
      return _shared;
    }

    internal static void DisposeGitChangesTarget()
    {
      _shared?.Dispose();
      _shared = null;
    }

    internal static async Task<IWebviewMessage> ResolveGitChangesTargetAsync(IWebviewMessage message, string Dir)
    {
      if (!(message is RequestGitChangesContextMessage)) return message;
      var typedMessage = (RequestGitChangesContextMessage)message;
      if (!string.IsNullOrEmpty(typedMessage.ContextDirectory) || !string.IsNullOrEmpty(typedMessage.GitChangesBase))
        return (IWebviewMessage)typedMessage;
      var target = await ResolveLocalDiffTarget(Ops(), (s) => { }, Dir);
      if (target == null)
      {
        typedMessage.ContextDirectory = Dir;
        return (IWebviewMessage)typedMessage;
      }
      typedMessage.ContextDirectory = target.Directory;
      typedMessage.GitChangesBase = target.BaseBranch;
      return (IWebviewMessage)typedMessage;
    }

    internal static async Task CaptureGitChangesContextAsync(Input input)
    {

      try
      {
        var output = await GetGitChangesContextAsync(input.Dir, input.Base);
        input.Post(
          new GitChangesContextResultMessage
          {
            RequestId = input.RequestId,
            Content = output.Content,
            Truncated = output.Truncated
          });
      }
      catch (Exception error)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo New] Failed to capture git changes context: {error.Message}");
        input.Post(
          new GitChangesContextErrorMessage
          {
            RequestId = input.RequestId,
            Error = input.Error(error) ?? "Failed to capture git changes"
          });
      }
    }

    /// <summary>
    /// Resolves the local diff target by determining the current branch,
    /// tracking branch, and base branch for comparison.
    /// </summary>
    /// <param name="gitOps">Git operations interface.</param>
    /// <param name="log">Logging action.</param>
    /// <param name="root">Optional workspace root directory.</param>
    /// <returns>LocalDiffTarget if resolved, null otherwise.</returns>
    public static async Task<LocalDiffTarget?> ResolveLocalDiffTarget(
        GitOps gitOps,
        Action<string> log,
        string? root = null)
    {
      if (string.IsNullOrEmpty(root))
      {
        log("Local diff: no workspace root");
        return null;
      }

      var branch = await gitOps.CurrentBranch(root);
      if (string.IsNullOrEmpty(branch) || branch == "HEAD")
      {
        log("Local diff: detached HEAD or no branch");
        return null;
      }

      var tracking = await gitOps.ResolveTrackingBranch(root, branch);
      var fallback = tracking != null
          ? null
          : await gitOps.ResolveDefaultBranch(root, branch);
      var raw = tracking ?? fallback ?? "HEAD";
      var baseBranch = await ResolveBase(gitOps, root, raw);

      log($"Local diff: branch={branch} tracking={tracking ?? "none"} default={fallback ?? "none"} base={baseBranch}");

      return new LocalDiffTarget
      {
        Directory = root,
        BaseBranch = baseBranch
      };
    }

    private static readonly string[] BaseCandidates = ["main", "master", "dev", "develop"];

    /// <summary>
    /// Resolves the base branch for comparison.
    /// </summary>
    private static async Task<string> ResolveBase(GitOps git, string root, string gitBase)
    {
      // If the caller gave an explicit base, honor it. Return it as-is so merge-base
      // fails loudly on a stale/misspelled ref instead of silently diffing against
      // an unrelated candidate branch.
      if (!string.IsNullOrEmpty(gitBase) && gitBase != "HEAD") return gitBase;
      foreach(var name in BaseCandidates) {
        var ok = await git.ExecGit(["rev-parse", "--verify", "--quiet", $"refs/heads/${name}"], root);
        if (ok.Code == 0) return name;
      }
      return "HEAD";
    }
  }
}
