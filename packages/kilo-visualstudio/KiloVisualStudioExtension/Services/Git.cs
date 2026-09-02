using KiloExtensionDTOs;
using KiloExtensionDTOs.ExtensionMessages;
using KiloExtensionDTOs.WebviewMessages;
using KiloVisualStudioExtension.ApiClient;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;

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

  public class GitOps
  {

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
        await CaptureGitChangesContextAsync(new Dictionary<string, object>
        {
          ["requestId"] = resolved.GetValueOrDefault("requestId") is string reqId ? reqId : "",
          ["contextDirectory"] = resolved.GetValueOrDefault("contextDirectory") is string ctxDir ? ctxDir : dir,
          ["gitChangesBase"] = resolved.GetValueOrDefault("gitChangesBase") is string baseStr ? baseStr : null,
          ["post"] = ctx.Post,
          ["error"] = ctx.Error
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
      if (_shared && !_shared.disposed) return _shared;
      _shared = new GitOps(/*{ log: () => undefined }*/);
      return _shared;
    }

    internal static  void DisposeGitChangesTarget() 
    {
      _shared?.dispose();
      _shared = null;
    }

    internal static async Task<IWebviewMessage> ResolveGitChangesTargetAsync(IWebviewMessage message, string Dir) 
    {
      if (!(message is RequestGitChangesContextMessage)) return message;
      var typedMessage = (RequestGitChangesContextMessage)message;
      if (!string.IsNullOrEmpty(typedMessage.ContextDirectory) || !string.IsNullOrEmpty(typedMessage.GitChangesBase) 
        return (IWebviewMessage)typedMessage;
      var target = await ResolveLocalDiffTarget(Ops(), () => null, Dir);
      if (target == null) 
      {
        typedMessage.ContextDirectory = Dir;
        return typedMessage;
      }
      typedMessage.ContextDirectory = target.Directory;
      typedMessage.GitChangesBase = target.BaseBranch;
      return typedMessage;
    }

    internal static async Task CaptureGitChangesContextAsync(Input input)
    {

  try {
        var output = await GetGitChangesContextAsync(input.Dir, input.Base);
        input.Post(
          new GitChangesContextResultMessage
          {
            RequestId = input.RequestId,
            Content = output.Content,
            Truncated = output.Truncated
          });
      } catch (Exception error) {
        System.Diagnostics.Debug.WriteLine($"[Kilo New] Failed to capture git changes context: {error.Message}");
        input.Post(
          new GitChangesContextErrorMessage
          {
            RequestId = input.RequestId,
            Error = input.Error(error) ?? "Failed to capture git changes"
          });
  }
}
  }
}
