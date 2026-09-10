// import type { KiloConnectionService } from "./cli-backend/connection-service"
using KiloExtensionDTOs;
using KiloExtensionDTOs.ExtensionMessages;
using KiloExtensionDTOs.WebviewMessages;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.Services;
using KiloVisualStudioExtension.Services.Git;
using KiloVisualStudioExtension.Services.Handlers.Settings;
using KiloVisualStudioExtension.Utils;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Threading.Tasks;
// import { routeAutocompleteMessage } from "./autocomplete/settings"
// (Assuming routeAutocompleteMessage is available from autocomplete settings module)
// import { handleSpeechToTextCancel, handleSpeechToTextStart, handleSpeechToTextStop } from "../speech-to-text/handler"
// (Assuming speech-to-text handlers are available)
// import { prewarmSpeechCapture } from "../speech-to-text/capture"
// (Assuming prewarmSpeechCapture is available)

namespace KiloVisualStudioExtension.WebviewMessageHandlers
{
  public static class InputTools
  {

    private static Dictionary<string, CancellationTokenSource> _aborts = new();
    private static HashSet<string> _cancelled = new();
    private static Dictionary<string, Task<bool>> _starts = new();
    private static HashSet<string> _stopping = new();

    // export async function routeInputToolMessage(message: Msg, ctx: Ctx): Promise<boolean> {
    public static async Task<bool> RouteWebviewMessageAsync(IWebviewMessage message, EarlyMessageRouter.Ctx ctx)

    {
      if (message is RequestAutocompleteSettingsMessage)
        await ctx.ServiceProvider.GetService<SettingsService>().SendAutocompleteSettingsAsync();

      //   if (message.type === "speechToTextPrewarm") {
      if (message is SpeechToTextPrewarmMessage)
      //     void prewarmSpeechCapture().catch((err: unknown) => console.warn("[Kilo New] Speech capture prewarm failed:", err))
      {
        _ = PrewarmSpeechCapture(ctx).ContinueWith(t =>
        {
          if (t.IsFaulted)
          {
            System.Diagnostics.Debug.WriteLine($"[Kilo New] Speech capture prewarm failed: {t.Exception?.GetBaseException().Message}");
          }
        }, TaskContinuationOptions.OnlyOnFaulted);
        //     return true
        return true;
        //   }
      }
      //
      //   if (message.type === "speechToTextStart") {
      if (message is SpeechToTextStartMessage speechToTextStartMessage)
      //     if (!message.requestId) return true
      {
        if (string.IsNullOrEmpty(speechToTextStartMessage.RequestId)) return true;
        //     handleSpeechToTextStart(
        await HandleSpeechToTextStartAsync(
            //       { requestId: message.requestId, model: message.model, language: message.language },
            new SpeechToTextStartOptions
            {
              RequestId = speechToTextStartMessage.RequestId,
              Model = speechToTextStartMessage.Model,
              Language = speechToTextStartMessage.Language
            },
            //       ctx.post,
            ctx
        //     )
        );
        //     return true
        return true;
        //   }
      }
      //
      //   if (message.type === "speechToTextStop") {
      if (message is SpeechToTextStopMessage speechToTextStopMessage)
      //     if (!message.requestId) return true
      {
        if (string.IsNullOrEmpty(speechToTextStopMessage.RequestId)) return true;
        //     handleSpeechToTextStop(ctx.connection, { requestId: message.requestId }, ctx.dir, ctx.post)
        await HandleSpeechToTextStopAsync(
            ctx.Connection,
            new SpeechToTextStopOptions { RequestId = speechToTextStopMessage.RequestId },
            ctx.Directory,
            ctx
        );
        //     return true
        return true;
        //   }
      }
      //
      //   if (message.type === "speechToTextCancel") {
      if (message is SpeechToTextCancelMessage speechToTextCancelMessage)
      //     if (!message.requestId) return true
      {
        if (string.IsNullOrEmpty(speechToTextCancelMessage.RequestId)) return true;
        //     handleSpeechToTextCancel({ requestId: message.requestId }, ctx.post)
        HandleSpeechToTextCancel(
            new SpeechToTextCancelOptions { RequestId = speechToTextCancelMessage.RequestId },
            ctx
        );
        //     return true
        return true;
        //   }
      }
      //
      //   return false
      return false;
      // }
    }
    //
    // Placeholder classes for speech-to-text options
    public class SpeechToTextStartOptions
    {
      public string RequestId { get; set; } = "";
      public string? Model { get; set; }
      public string? Language { get; set; }
    }
    //
    public class SpeechToTextStopOptions
    {
      public string RequestId { get; set; } = "";
    }
    //
    public class SpeechToTextCancelOptions
    {
      public string RequestId { get; set; } = "";
    }

    private static Task PrewarmSpeechCapture(EarlyMessageRouter.Ctx ctx)
    {
      return ctx.ServiceProvider.GetService<CaptureService>().PrewarmSpeechCaptureAsync();
    }
    //
    private static async Task HandleSpeechToTextStartAsync(SpeechToTextStartOptions options, EarlyMessageRouter.Ctx ctx)
    {
      ModelStateService.PostMessage post = ctx.Post;
      var task = ctx.ServiceProvider.GetService<CaptureService>().StartSpeechCaptureAsync(
        new CaptureService.Input
        {
          Language = options.Language,
          Model = options.Model,
          RequestId = options.RequestId
        });

      _starts[options.RequestId] = task;

      try
      {
        var started = await task;
        _starts.Remove(options.RequestId);
        if (!started) return;
        if (_stopping.Contains(options.RequestId) || _cancelled.Contains(options.RequestId)) return;
        post(new SpeechToTextStartedMessage { RequestId = options.RequestId });
      }
      catch (Exception err)
      {
        _starts.Remove(options.RequestId);
        _stopping.Remove(options.RequestId);
        if (_cancelled.Remove(options.RequestId)) return;
        post(new SpeechToTextErrorMessage { RequestId = options.RequestId, Error = ErrorHelper.GetErrorMessage(err) ?? "Speech recording failed" });
      }
    }
    //
    private static async Task HandleSpeechToTextStopAsync(KiloConnectionService connection, SpeechToTextStopOptions options, string dir, EarlyMessageRouter.Ctx ctx)
    {
      var ctrl = new CancellationTokenSource();
      var ready = _starts.TryGetValue(options.RequestId, out var task)
          ? AsyncUtils.SafeTaskAsync(task)
          : Task.FromResult(true);

      _aborts[options.RequestId] = ctrl;
      _stopping.Add(options.RequestId);

      _ = ready.ContinueWith(async t =>
      {
        try
        {
          var started = t.IsFaulted ? false : t.Result;
          if (!started) return;

          var audio = await ctx.ServiceProvider.GetService<CaptureService>().StopSpeechCaptureAsync(options.RequestId);
          var result = await ctx.ServiceProvider.GetService<SpeechTranscriptionService>().TranscribeSpeechAsync(connection, audio, dir, ctrl.Token);

          _aborts.Remove(options.RequestId);
          _stopping.Remove(options.RequestId);
          if (result == null) return;
          if (_cancelled.Remove(options.RequestId)) return;
          if (!result.Ok && result.Code == "cancelled") return;

          if (result.Ok)
          {
            ctx.Post(new SpeechToTextResultMessage
            {
              Text = result.Text,
              RequestId = options.RequestId
            });
            return;
          }
          ctx.Post(new SpeechToTextErrorMessage
          {
            Error = result.Error,
            Code = result.Code,
            RequestId = options.RequestId
          });
        }
        catch (Exception err)
        {
          _aborts.Remove(options.RequestId);
          _stopping.Remove(options.RequestId);
          if (_cancelled.Remove(options.RequestId)) return;
          ctx.Post(new SpeechToTextErrorMessage
          {
            Error = ErrorHelper.GetErrorMessage(err) ?? "Speech to text request failed",
            RequestId = options.RequestId
          });
        }
      }, TaskContinuationOptions.ExecuteSynchronously);
    }
    //
    private static void HandleSpeechToTextCancel(SpeechToTextCancelOptions options, EarlyMessageRouter.Ctx ctx)
    {
      // Check for active abort controller
      if (_aborts.TryGetValue(options.RequestId, out var ctrl))
      {
        _cancelled.Add(options.RequestId);
        _stopping.Remove(options.RequestId);
        ctrl.Cancel();
        _aborts.Remove(options.RequestId);
        ctx.Post(new SpeechToTextCancelledMessage { RequestId = options.RequestId });
        return;
      }

      // Check for pending start task
      if (_starts.TryGetValue(options.RequestId, out var ready))
      {
        _cancelled.Add(options.RequestId);
        _stopping.Remove(options.RequestId);

        _ = AsyncUtils.SafeTaskAsync(ready)
            .ContinueWith(async t =>
            {
              try
              {
                var started = t.IsFaulted ? false : t.Result;
                if (!started) return;

                await ctx.ServiceProvider.GetService<CaptureService>().CancelSpeechCaptureAsync(options.RequestId);
              }
              finally
              {
                _cancelled.Remove(options.RequestId);
                ctx.Post(new SpeechToTextCancelledMessage { RequestId = options.RequestId });
              }
            }, TaskContinuationOptions.ExecuteSynchronously)
            .ContinueWith(t =>
            {
              if (t.IsFaulted)
              {
                _cancelled.Remove(options.RequestId);
                ctx.Post(new SpeechToTextErrorMessage
                {
                  Error = ErrorHelper.GetErrorMessage(t.Exception?.GetBaseException() ?? t.Exception) ?? "Speech recording cancellation failed",
                  RequestId = options.RequestId
                });
              }
            }, TaskContinuationOptions.OnlyOnFaulted);

        return;
      }

      // No active or pending task, just cancel directly
      _ = ctx.ServiceProvider.GetService<CaptureService>().CancelSpeechCaptureAsync(options.RequestId)
          .ContinueWith(t =>
          {
            if (t.IsFaulted)
            {
              ctx.Post(new SpeechToTextErrorMessage
              {
                Error = ErrorHelper.GetErrorMessage(t.Exception?.GetBaseException() ?? t.Exception) ?? "Speech recording cancellation failed",
                RequestId = options.RequestId
              });
            }
            else
            {
              ctx.Post(new SpeechToTextCancelledMessage { RequestId = options.RequestId });
            }
          }, TaskContinuationOptions.ExecuteSynchronously);
    }
  }
}
