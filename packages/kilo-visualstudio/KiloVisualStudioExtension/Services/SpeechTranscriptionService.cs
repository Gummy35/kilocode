// import { PATH, PROMPT, SpeechToTextModel, getSpeechToTextModel, SpeechToTextResult, Req, errorMessage, parse, getErrorMessage } from "./shared"
using KiloVisualStudioExtension.ApiClient;
using System;
using System.Threading;
using System.Threading.Tasks;
// import type { KiloConnectionService } from "../services/cli-backend/connection-service"

namespace KiloVisualStudioExtension.Services
{
  public class SpeechTranscriptionService : ServiceProviderServiceBase
  {
    private const string PATH = "/kilo/audio/transcriptions";
    private const string PROMPT = "Transcribe exactly what is spoken. Do not paraphrase, summarize, infer intent, or rewrite for clarity. Preserve the speaker's original wording as closely as possible, including incomplete phrases and unusual wording when audible.";

    public SpeechTranscriptionService(ServiceProvider serviceProvider) : base(serviceProvider) { }

    // export async function transcribeSpeech(
    public async Task<SpeechToTextResult> TranscribeSpeechAsync(
        //   connection: KiloConnectionService,
        KiloConnectionService connection,
        //   input: Req,
        SpeechToTextRequest input,
        //   dir: string,
        string dir,
        //   signal?: AbortSignal,
        CancellationToken cancellationToken = default
    // ): Promise<SpeechToTextResult> {
    )
    // {
    {
      var client = connection.GetNswagClient();
      if (client == null || connection.State != ConnectionState.Connected)
      {
        return new SpeechToTextResult { Ok = false, Code = "not_connected", Error = "Not connected to Kilo backend" };
      }

      //   const model = getSpeechToTextModel(input.model)
      var model = SpeechToTextModels.GetModel(input.Model);
      //   const prompt = model.verbatim ? PROMPT : undefined
      var prompt = model.Verbatim.HasValue && model.Verbatim.Value ? PROMPT : null;
      //   if (dir) url.searchParams.set("directory", dir)
      //
      //   try {
      try
      //     const res = await fetch(url, {
      {
        var response = await client.Kilo_audio_transcriptionsAsync("", "", new KiloAudioTranscriptionsRequest
        {
          Model = model.Id,
          Language = input.Language,
          Prompt = prompt,
          Input_audio = new Input_audio
          {
            Data = input.Data,
            Format = input.Format
          }
        });

        //     const text = typeof body?.text === "string" ? body.text.trim() : ""
        var text = response?.Text is string t ? t.Trim() : "";
        //     if (!text) return { ok: false, error: "No speech was detected", code: "empty_transcript" }
        if (string.IsNullOrEmpty(text))
          return new SpeechToTextResult
          {
            Ok = false,
            Error = "No speech was detected",
            Code = "empty_transcript"
          };
        //
        //     return { ok: true, text }
        return new SpeechToTextResult { Ok = true, Text = text };
        //   } catch (err) {
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      //     if (signal?.aborted) return { ok: false, error: "Speech transcription cancelled", code: "cancelled" }
      {
        return new SpeechToTextResult
        {
          Ok = false,
          Error = "Speech transcription cancelled",
          Code = "cancelled"
        };
        //     const msg = getErrorMessage(err) || "Speech to text request failed"
      }
      //     return { ok: false, error: msg, code: msg === "Failed to fetch" ? "not_available" : undefined }
      catch (Exception err)
      {
        var msg = ErrorHelper.GetErrorMessage(err) ?? "Speech to text request failed";
        return new SpeechToTextResult
        {
          Ok = false,
          Error = msg,
          Code = msg == "Failed to fetch" ? "not_available" : null
        };
      }
      //   }
    }
    // }
  }
}
// Helper types (replace with actual SDK types)
public class SpeechToTextRequest
{
  public string Model { get; set; } = "";
  public string Data { get; set; } = "";
  public string Format { get; set; } = "";
  public string? Language { get; set; }
}

public class SpeechToTextResult
{
  public bool Ok { get; set; }
  public string? Text { get; set; }
  public string? Error { get; set; }
  public string? Code { get; set; }
}

public class SpeechToTextModel
{
  public string Id { get; set; } = "";
  public bool Verbatim { get; set; }
}
