// /**
//  * Suggestion handlers — extracted from KiloProvider.
//  *
//  * Manages suggestion accept and dismiss flows plus recovery after SSE reconnects.
//  * No vscode dependency.
//  */
//
// import type { KiloClient, SuggestionRequest } from "@kilocode/sdk/v2/client"
using KiloExtensionDTOs;
using KiloExtensionDTOs.AgentManager;
using KiloExtensionDTOs.ExtensionMessages;
using KiloExtensionDTOs.WebviewMessages;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.Services.Git;
using KiloVisualStudioExtension.Utils;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static KiloVisualStudioExtension.WebviewMessageHandlers.SidebarWorktreeHandler;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ListView;

namespace KiloVisualStudioExtension.WebviewMessageHandlers
{
  public class SidebarWorktreeHandler
  {
    public delegate void Progress(ContinueInWorktreeStatusEnum status, string? detail = null, string? error = null);

    public class Ctx
    {
      public Action<IWebviewMessage?> Post { get; set; }
      public Func<Task> OpenAgentManager { get; set; }
      public Func<Task> OpenAdvancedWorktree { get; set; }
      public Func<string?, string?, Task> OpenChanges { get; set; }
      public string? CurrentSessionId { get; set; }
      public CreateWorktreeHandlerDelegate? CreateWorktree { get; set; }
      public ContinueInWorktreeHandlerDelegate? ContinueInWorktree
      { get; set; }
    }

    public static async Task Repo(Action<IWebviewMessage?> post)
    {
      string root = VSExtensionSettings.WorkspaceRoot;
      if (root == null) return;
      var git = new GitOps(new GitOpsOptions { Log = (s) => { } });
      var branch = await git.CurrentBranch(root);
      git.Dispose();
      if (string.IsNullOrEmpty(branch) || branch == "HEAD") return;
      post(new AgentManagerRepoInfoMessage { Branch = branch });
    }

    public static async Task<bool> HandleWorktreeMessage(IWebviewMessage message, Ctx ctx)
    {
      if (message is OpenAgentManagerRequest agentManagerRequest)
      {
        await ctx.OpenAgentManager();
        return true;
      }
      if (message is OpenAdvancedWorktreeRequest openAdvancedWorktreeRequest)
      {
        await ctx.OpenAdvancedWorktree();
        return true;
      }
      if (message is RequestRepoInfoMessage requestRepoInfoMessage)
      {
        await Repo(ctx.Post);
        return true;
      }
      if (message is CreateWorktreeRequest createWorktreeRequest)
      {
        await ctx.CreateWorktree(createWorktreeRequest.BaseBranch, createWorktreeRequest.BranchName);
        return true;
      }
      if (message is OpenChangesRequest openChangesRequest)
      {
        await ctx.OpenChanges(ctx.CurrentSessionId, openChangesRequest.TurnId);
        return true;
      }

      if (message is not ContinueInWorktreeRequest) return false;
      await HandleContinueInWorktreeAsync(
        ctx.CurrentSessionId,
        ctx.ContinueInWorktree,
        ctx.Post
      );
      return true;
    }



    public delegate Task CreateWorktreeHandlerDelegate(string? baseBranch, string? branchName);
    public delegate Task ContinueInWorktreeHandlerDelegate(string sessionId, Progress progress);




    public static async Task HandleContinueInWorktreeAsync(string sessionId, ContinueInWorktreeHandlerDelegate continueInWorktreeHandler, Action<ContinueInWorktreeProgressMessage> post)
    {
      if (sessionId != null && continueInWorktreeHandler != null)
      {
        try
        {
          await continueInWorktreeHandler(sessionId, (status, detail, error)
            => post(new ContinueInWorktreeProgressMessage { Detail = detail, Status = status, Error = error }));
        }
        catch (Exception err)
        {
          System.Diagnostics.Debug.WriteLine($"[Kilo New] continueInWorktree failed: {err.Message}");
          post(new ContinueInWorktreeProgressMessage
          {
            Status = ContinueInWorktreeStatusEnum.Error,
            Error = err.Message
          });
        }
        return;
      }

      if (sessionId == null) return;
      System.Diagnostics.Debug.WriteLine($"[Kilo New] continueInWorktree: no handler registered");
      post(new ContinueInWorktreeProgressMessage
      {
        Status = ContinueInWorktreeStatusEnum.Error,
        Error = "Continue in Worktree is not available"
      });
    }
  }
}
