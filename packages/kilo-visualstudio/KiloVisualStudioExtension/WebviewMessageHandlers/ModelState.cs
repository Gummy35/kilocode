// /**
//  * Per-mode model selection persistence via the CLI's model.json.
//  *
//  * Reads/writes ~/.local/state/kilo/model.json (same file the CLI TUI uses)
//  * so per-mode model choices are shared between CLI and extension.
//  */
//
// import * as fs from "fs"
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
// import * as path from "path"
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.Utils;
using KiloExtensionDTOs;
using KiloExtensionDTOs.WebviewMessages;
using KiloExtensionDTOs.ExtensionMessages;
// import type { KiloClient } from "@kilocode/sdk/v2/client"
// (KiloClient assumed to be available from generated SDK)
// import { validateModelSelections } from "../provider-actions"

namespace KiloVisualStudioExtension.WebviewMessageHandlers
{
  public static class ModelState
  {
    // type PostMessage = (msg: unknown) => void
    public delegate void PostMessage(IWebviewMessage? msg);
    //
    // let cached: string | undefined
    private static string? _cached;
    // let queue: Promise<void> = Promise.resolve()
    private static Task _queue = Task.CompletedTask;
    //
    // async function resolve(client: KiloClient | null): Promise<string | undefined> {
    private static async Task<string?> Resolve(KiloApiClient? client)
    // {
    {
      //   if (cached) return cached
      if (_cached != null) return _cached;
      //   try {
      try
      //     const resp = await client?.path.get()
      {
        var resp = await client.Path_getAsync("", "");
        //     if (!resp?.data?.state) return undefined
        if (resp?.State == null) return null;
        //     cached = path.join(resp.data.state, "model.json")
        _cached = System.IO.Path.Combine(resp.State, "model.json");
        //     return cached
        return _cached;
        //   } catch {
      }
      catch
      //     return undefined
      {
        return null;
        //   }
      }
      // }
    }
    //
    // async function read(client: KiloClient | null): Promise<Record<string, unknown>> {
    private static async Task<Dictionary<string, object?>> Read(KiloApiClient? client)
    // {
    {
      //   const p = await resolve(client)
      var p = await Resolve(client);
      //   if (!p) return {}
      if (p == null) return new Dictionary<string, object?>();
      //   try {
      try
      //     const raw = await fs.promises.readFile(p, "utf-8")
      {
        var raw = await AsyncUtils.ReadAllTextAsync(p);
        //     const parsed = JSON.parse(raw)
        var parsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(raw);
        //     return typeof parsed === "object" && parsed !== null && !Array.isArray(parsed)
        return parsed != null
            //       ? (parsed as Record<string, unknown>)
            ? parsed
            //       : {}
            : new Dictionary<string, object?>();
        //   } catch {
      }
      catch
      //     return {}
      {
        return new Dictionary<string, object?>();
        //   }
      }
      // }
    }
    //
    // function write(client: KiloClient | null, key: string, value: unknown): Promise<void> {
    private static Task Write(KiloApiClient? client, string key, object? value)
    // {
    {
      //   const op = queue.then(async () => {
      var op = _queue.ContinueWith(async _ =>
      //     const p = await resolve(client)
      {
        var p = await Resolve(client);
        //     if (!p) return
        if (p == null) return;
        //     const existing = await read(client)
        var existing = await Read(client);
        //     existing[key] = value
        existing[key] = value;
        //     await fs.promises.writeFile(p, JSON.stringify(existing, null, 2))
        await AsyncUtils.WriteAllTextAsync(p, JsonSerializer.Serialize(existing, new JsonSerializerOptions { WriteIndented = true }));
        //   })
      });
      //   queue = op.catch(() => {})
      _queue = op.ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);
      //   return op
      return op;
      // }
    }
    //
    // /**
    //  * Handle a model-state webview message. Returns true if handled.
    //  */
    // export async function handleMessage(
    public static async Task<bool> HandleMessage(
        //   type: string,
        //   message: Record<string, unknown>,
        IWebviewMessage message,
        //   client: KiloClient | null,
        KiloApiClient? client,
        //   post: PostMessage,
        PostMessage post
    // ): Promise<boolean> {
    )
    // {
    {
      //   if (type === "persistModelSelection") {
      if (message is PersistModelSelectionRequest modelSelectionRequest)
      //     const data = await read(client)
      {
        var data = await Read(client);
        //     const model = validateModelSelections(data.model)
        var model = ValidateModelSelections(GetModelFromData(data));
        //     model[message.agent as string] = {
        var agent = modelSelectionRequest.Agent;
        var providerId = modelSelectionRequest.ProviderID;
        var modelId = modelSelectionRequest.ModelID;
        if (agent != null)
        {
          model[agent] = new Dictionary<string, object?>
//       providerID: message.providerID as string,
                        {
//       modelID: message.modelID as string,
                            { "providerID", providerId },
//     }
                            { "modelID", modelId }
//     await write(client, "model", model)
                        };
          await Write(client, "model", model);
          //     return true
          return true;
          //   }
        }
      }
      //   if (type === "clearModelSelection") {
      if (message is ClearModelSelectionRequest clearModelSelection)
      //     const data = await read(client)
      {
        var data = await Read(client);
        //     const model = validateModelSelections(data.model)
        var model = ValidateModelSelections(GetModelFromData(data));
        //     delete model[message.agent as string]
        var agent = clearModelSelection.Agent;
        if (agent != null && model.ContainsKey(agent))
        {
          model.Remove(agent);
          //     await write(client, "model", model)
        }
        await Write(client, "model", model);
        //     return true
        return true;
        //   }
      }
      //   if (type === "requestModelSelections") {
      if (message is RequestModelSelectionsMessage requestModelSelections)
      //     const data = await read(client)
      {
        var data = await Read(client);
        //     const selections = validateModelSelections(data.model)
        var selections = ValidateModelSelections(GetModelFromData(data));
        //     post({ type: "modelSelectionsLoaded", selections })
        post(new ModelSelectionsLoadedMessage { Selections = selections });
        //     return true
        return true;
        //   }
      }
      //   return false
      return false;
      // }
    }
    //
    // export async function reset(client: KiloClient | null, post: PostMessage): Promise<void> {
    public static async Task Reset(KiloApiClient? client, PostMessage post)
    // {
    {
      //   await write(client, "model", {})
      await Write(client, "model", new Dictionary<string, object?>());
      //   post({ type: "modelSelectionsLoaded", selections: {} })
      post(new ModelSelectionsLoadedMessage
      {
        Selections = new Dictionary<string, object?>()
      });
      // }
    }
    //
    // Helper to extract model from data
    private static Dictionary<string, object?> GetModelFromData(Dictionary<string, object?> data)
    {
      if (data.TryGetValue("model", out var modelObj) && modelObj is Dictionary<string, object?> model)
      {
        return model;
      }
      return new Dictionary<string, object?>();
    }
    //
    // Helper to validate model selections (placeholder - actual implementation from provider-actions)
    private static Dictionary<string, object?> ValidateModelSelections(object? model)
    {
      if (model is Dictionary<string, object?> dict)
      {
        return dict;
      }
      return new Dictionary<string, object?>();
    }
  }
}
