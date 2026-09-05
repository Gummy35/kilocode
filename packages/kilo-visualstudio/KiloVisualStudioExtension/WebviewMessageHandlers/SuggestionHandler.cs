// /**
//  * Suggestion handlers — extracted from KiloProvider.
//  *
//  * Manages suggestion accept and dismiss flows plus recovery after SSE reconnects.
//  * No vscode dependency.
//  */
//
// import type { KiloClient, SuggestionRequest } from "@kilocode/sdk/v2/client"
using KiloExtensionDTOs;
using KiloExtensionDTOs.ExtensionMessages;
using KiloExtensionDTOs.WebviewMessages;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.Utils;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VSLangProj80;
// import { recoveryDirs } from "./permission-handler"

namespace KiloVisualStudioExtension.WebviewMessageHandlers
{
  public class SuggestionHandler {
    // export type RecoverableSuggestion = SuggestionRequest
    //    public using RecoverableSuggestion = global::KiloSdk.V2.Client.SuggestionRequest;
    //
    // export interface SuggestionContext {
    public interface ISuggestionContext
    // {
    {
      //   readonly client: KiloClient | null
      KiloApiClient? Client { get; }
      //   readonly currentSessionId: string | undefined
      string? CurrentSessionId { get; }
      //   readonly trackedSessionIds: Set<string>
      HashSet<string> TrackedSessionIds { get; }
      //   readonly sessionDirectories: ReadonlyMap<string, string>
      IDictionary<string, string> SessionDirectories { get; }
      //   postMessage(msg: unknown): void
      Action<IWebviewMessage?> PostMessage { get; }
      //   getWorkspaceDirectory(sessionId?: string): string
      Func<string?, string> GetWorkspaceDirectory { get; }
    }

    internal class SuggestionContextImplementation : ISuggestionContext
    {
      public KiloApiClient? Client { get; set; }
      public string? CurrentSessionId { get; set; }
      public HashSet<string> TrackedSessionIds { get; set; } = new();
      public IDictionary<string, string> SessionDirectories { get; set; } = new Dictionary<string, string>();
      public Action<IWebviewMessage?> PostMessage { get; set; } = _ => { };
      public Func<string?, string> GetWorkspaceDirectory { get; set; } = _ => "";
    }

    //
    // export function recoverableSuggestions(items: RecoverableSuggestion[], tracked: Set<string>, seen: Set<string>) {
    public static IEnumerable<SuggestionRequest> RecoverableSuggestions(
        IEnumerable<SuggestionRequest> items,
        HashSet<string> tracked,
        HashSet<string> seen)
    // {
    {
      //   return items.filter((item) => {
      foreach (var item in items)
      //     if (seen.has(item.id)) return false
      {
        if (seen.Contains(item.Id)) continue;
        //     seen.add(item.id)
        seen.Add(item.Id);
        //     return tracked.has(item.sessionID)
        if (tracked.Contains(item.SessionID))
          yield return item;
        //   })
      }
      // }
    }
    //
    // /**
    //  * Route suggestion-related webview messages.
    //  * Extracted from the main message handler to stay within the complexity limit.
    //  */
    // export async function routeSuggestionWebviewMessage(
    public static async Task RouteWebviewMessage(
        //   ctx: SuggestionContext,
        ISuggestionContext ctx,
        //   message: { type: string; requestID?: string; sessionID?: string; index?: number },
        IWebviewMessage message
// ): Promise<void> {
    )
    // {
    {
      if (message is SuggestionAcceptRequest suggestionAccept)
      {
        await HandleSuggestionAccept(ctx, suggestionAccept.RequestID, (int)suggestionAccept.Index, suggestionAccept.SessionID);
        return;
      }
      if (message is SuggestionDismissRequest suggestionDismiss)
      {
        await HandleSuggestionDismiss(ctx, suggestionDismiss.RequestID, suggestionDismiss.SessionID);
      }
    }
    //
    // export async function handleSuggestionAccept(
    public static async Task HandleSuggestionAccept(
        //   ctx: SuggestionContext,
        ISuggestionContext ctx,
        //   requestID: string,
        string requestId,
        //   index: number,
        int index,
        //   sessionID?: string,
        string? sessionId = null
// ): Promise<void> {
    )
    // {
    {
      //   if (!ctx.client) {
      if (ctx.Client == null)
      //     ctx.postMessage({ type: "suggestionError", requestID })
      {
        ctx.PostMessage(new SuggestionErrorMessage {RequestID = requestId });
        //     return
        return;
        //   }
      }
      //
      //   try {
      try
      //     await ctx.client.suggestion.accept(
      {
        //       { requestID, index, directory: ctx.getWorkspaceDirectory(sessionID ?? ctx.currentSessionId) },
        await ctx.Client.Suggestion_acceptAsync(
          requestId, 
          ctx.GetWorkspaceDirectory(sessionId ?? ctx.CurrentSessionId), 
          "",
          new Body64 { Index = index }
          );
        //   } catch (error) {
      }
      catch (Exception error)
      //     console.error("[Kilo New] KiloProvider: Failed to accept suggestion:", error)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo New] KiloProvider: Failed to accept suggestion: {error.Message}");
        //     ctx.postMessage({ type: "suggestionError", requestID })
        ctx.PostMessage(new SuggestionErrorMessage { RequestID = requestId });
        //   }
      }
      // }
    }
    //
    // export async function handleSuggestionDismiss(
    public static async Task HandleSuggestionDismiss(
        //   ctx: SuggestionContext,
        ISuggestionContext ctx,
        //   requestID: string,
        string requestId,
        //   sessionID?: string,
        string? sessionId = null
// ): Promise<void> {
    )
    // {
    {
      //   if (!ctx.client) {
      if (ctx.Client == null)
      //     ctx.postMessage({ type: "suggestionError", requestID })
      {
        ctx.PostMessage(new SuggestionErrorMessage {RequestID = requestId });
        //     return
        return;
        //   }
      }
      //
      //   try {
      try
      //     await ctx.client.suggestion.dismiss(
      {
        await ctx.Client.Suggestion_dismissAsync(requestId, ctx.GetWorkspaceDirectory(sessionId ?? ctx.CurrentSessionId), "");
        //   } catch (error) {
      }
      catch (Exception error)
      //     console.error("[Kilo New] KiloProvider: Failed to dismiss suggestion:", error)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo New] KiloProvider: Failed to dismiss suggestion: {error.Message}");
        //     ctx.postMessage({ type: "suggestionError", requestID })
        ctx.PostMessage(new SuggestionErrorMessage { RequestID = requestId });
        //   }
      }
      // }
    }
    //
    // export async function fetchAndSendPendingSuggestions(ctx: SuggestionContext): Promise<void> {
    public async Task FetchAndSendPendingSuggestions(ISuggestionContext ctx)
    // {
    {
      //   if (!ctx.client) return
      if (ctx.Client == null) return;
      //   try {
      try
      //     const dirs = recoveryDirs(ctx.getWorkspaceDirectory(), ctx.sessionDirectories)
      {
        var dirs =  RecoveryDirs(ctx.GetWorkspaceDirectory(null), ctx.SessionDirectories);
        //
        //     const seen = new Set<string>()
        var seen = new HashSet<string>();
        //     for (const dir of dirs) {
        foreach (var dir in dirs)
        //       const { data } = await ctx.client.suggestion.list({ directory: dir })
        {
          var response = await ctx.Client.Suggestion_listAsync(dir, "");
          //       if (!data) continue
          if (response == null) continue;
          //       for (const suggestion of recoverableSuggestions(data, ctx.trackedSessionIds, seen)) {
          foreach (var suggestion in RecoverableSuggestions(response, ctx.TrackedSessionIds, seen))
          //         ctx.postMessage({
          //           type: "suggestionRequest",
          //           suggestion,
          //         })
          {
            ctx.PostMessage(new SuggestionRequestMessage { Suggestion = EntityConverter.Convert(suggestion) });
            //       }
          }
          //     }
        }
        //   } catch (error) {
      }
      catch (Exception error)
      //     console.error("[Kilo New] KiloProvider: Failed to fetch pending suggestions:", error)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo New] KiloProvider: Failed to fetch pending suggestions: {error.Message}");
        //   }
      }
      // }
    }
   
    //
    // Helper method for recoveryDirs (placeholder - actual implementation from permission-handler)
    private static IEnumerable<string> RecoveryDirs(string workspaceDir, IDictionary<string, string> sessionDirectories)
    {
      // Placeholder implementation - replace with actual logic from permission-handler.ts
      yield return workspaceDir;
      foreach (var dir in sessionDirectories.Values)
      {
        yield return dir;
      }
    }
  }
}
