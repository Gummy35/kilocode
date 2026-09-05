// export interface SpeechToTextModelDef {
using System.Collections.Generic;

namespace KiloVisualStudioExtension.Services
{
  public class SpeechToTextModelDef
  //   readonly id: string
  {
    public string Id { get; }
    //   readonly label: string
    public string Label { get; }
    //   readonly provider: string
    public string Provider { get; }
    //   readonly verbatim?: boolean
    public bool? Verbatim { get; }
    // }
    public SpeechToTextModelDef(string id, string label, string provider, bool? verbatim)
    {
      Id = id;
      Label = label;
      Provider = provider;
      Verbatim = verbatim;
    }
  }
  //
  // const models: SpeechToTextModelDef[] = [
  public static class SpeechToTextModels
  {
    private static readonly SpeechToTextModelDef[] Models = new[]
    //   {
    //     id: "openai/whisper-large-v3-turbo",
    //     label: "Whisper Large V3 Turbo",
    //     provider: "OpenAI-compatible",
    //   },
    {
            new SpeechToTextModelDef("openai/whisper-large-v3-turbo", "Whisper Large V3 Turbo", "OpenAI-compatible", null),
//   {
//     id: "openai/gpt-4o-mini-transcribe",
//     label: "GPT-4o Mini Transcribe",
//     provider: "OpenAI",
//     verbatim: true,
//   },
            new SpeechToTextModelDef("openai/gpt-4o-mini-transcribe", "GPT-4o Mini Transcribe", "OpenAI", true),
//   {
//     id: "openai/gpt-4o-transcribe",
//     label: "GPT-4o Transcribe",
//     provider: "OpenAI",
//     verbatim: true,
//   },
            new SpeechToTextModelDef("openai/gpt-4o-transcribe", "GPT-4o Transcribe", "OpenAI", true),
//   {
//     id: "openai/whisper-1",
//     label: "Whisper 1",
//     provider: "OpenAI",
//   },
            new SpeechToTextModelDef("openai/whisper-1", "Whisper 1", "OpenAI", null),
//   {
//     id: "openai/whisper-large-v3",
//     label: "Whisper Large V3",
//     provider: "OpenAI-compatible",
//   },
            new SpeechToTextModelDef("openai/whisper-large-v3", "Whisper Large V3", "OpenAI-compatible", null),
//   {
//     id: "google/chirp-3",
//     label: "Chirp 3",
//     provider: "Google",
//   },
            new SpeechToTextModelDef("google/chirp-3", "Chirp 3", "Google", null),
//   {
//     id: "nvidia/parakeet-tdt-0.6b-v3",
//     label: "Parakeet TDT 0.6B v3",
//     provider: "NVIDIA",
//   },
// ]
            new SpeechToTextModelDef("nvidia/parakeet-tdt-0.6b-v3", "Parakeet TDT 0.6B v3", "NVIDIA", null),
        };
    //
    // export const SPEECH_TO_TEXT_MODELS: readonly SpeechToTextModelDef[] = models
    public static IReadOnlyList<SpeechToTextModelDef> All => Models;
    // export const DEFAULT_SPEECH_TO_TEXT_MODEL: SpeechToTextModelDef = models[0]!
    public static SpeechToTextModelDef Default => Models[0];
    //
    // export function getSpeechToTextModel(id: string | undefined): SpeechToTextModelDef {
    public static SpeechToTextModelDef GetModel(string? id)
    //   for (const model of models) {
    {
      foreach (var model in Models)
      //     if (model.id === id) return model
      {
        if (model.Id == id) return model;
        //   }
      }
      //   return DEFAULT_SPEECH_TO_TEXT_MODEL
      return Default;
      // }
    }
    //
  }
}
