// /**
//  * Per-mode model selection persistence via the CLI's model.json.
//  *
//  * Reads/writes ~/.local/state/kilo/model.json (same file the CLI TUI uses)
//  * so per-mode model choices are shared between CLI and extension.
//  */
//
// import * as fs from "fs"
using KiloExtensionDTOs;
using KiloExtensionDTOs.ExtensionMessages;
using KiloExtensionDTOs.Providers;
using KiloExtensionDTOs.WebviewMessages;
// import * as path from "path"
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
// import type { KiloClient } from "@kilocode/sdk/v2/client"
// (KiloClient assumed to be available from generated SDK)
// import { validateModelSelections } from "../provider-actions"

namespace KiloVisualStudioExtension.WebviewMessageHandlers
{
  public class ModelSelection
  {
    [JsonProperty("providerID")]
    public string ProviderId { get; set; } = "";

    [JsonProperty("modelID")]
    public string ModelId { get; set; } = "";

    [JsonProperty("variant", NullValueHandling = NullValueHandling.Ignore)]
    public string? Variant { get; set; }
  }

  public class ModelStateService:ServiceProviderServiceBase
  {
    // type PostMessage = (msg: unknown) => void
    public delegate void PostMessage(IWebviewMessage? msg);
    //
    // let cached: string | undefined
    private string? _cachedPath;
    private readonly string _filePath;
    private readonly JsonSerializerSettings _jsonSettings;
    private readonly SemaphoreSlim _lock = new(1, 1);

    // let queue: Promise<void> = Promise.resolve()
    private static Task _queue = Task.CompletedTask;
    private bool _disposed;

    public ModelStateService(ServiceProvider provider) : base(provider)
    {
      _jsonSettings = new JsonSerializerSettings
      {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        MissingMemberHandling = MissingMemberHandling.Ignore
      };
    }

    //
    // async function resolve(client: KiloClient | null): Promise<string | undefined> {
    private async Task<string?> ResolvePathAsync(KiloApiClient? client)
    // {
    {
      //   if (cached) return cached
      if (_cachedPath != null) return _cachedPath;
      //   try {
      try
      //     const resp = await client?.path.get()
      {
        var resp = await client.Path_getAsync("", "");
        //     if (!resp?.data?.state) return undefined
        if (resp?.State == null) return null;
        //     cached = path.join(resp.data.state, "model.json")
        _cachedPath = System.IO.Path.Combine(resp.State, "model.json");
        //     return cached
        return _cachedPath;
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
    private async Task<Dictionary<string, ModelSelection>> ReadAsync(KiloApiClient? client)
    // {
    {
      await _lock.WaitAsync();
      try
      {
        var p = await ResolvePathAsync(client);
        if (p == null || !System.IO.File.Exists(p)) return new Dictionary<string, ModelSelection>();
        try
        {
          var raw = await AsyncUtils.ReadAllTextAsync(p);
          var parsed = JObject.Parse(raw);
          var result = new Dictionary<string, ModelSelection>();

          if (parsed.TryGetValue("model", out var modelToken) && modelToken is JObject modelObj)
          {
            foreach (var prop in modelObj.Properties())
            {
              var selection = prop.Value.ToObject<ModelSelection>();
              if (selection != null)
              {
                result[prop.Name] = selection;
              }
            }
          }
          return result;
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"[Kilo] ModelStorage load error: {ex.Message}");
          return new Dictionary<string, ModelSelection>();
        }
      }
      finally
      {
        _lock.Release();
      }
    }
    //
    // function write(client: KiloClient | null, key: string, value: unknown): Promise<void> {
    private Task WriteAsync(KiloApiClient? client, Dictionary<string,ModelSelection> models)
    {
      var op = _queue.ContinueWith(async _ =>
      {
      await _lock.WaitAsync();
      try
      {
          var p = await ResolvePathAsync(client);
          if (p == null) return;

          JObject? existing;

          if (System.IO.File.Exists(p))
          {
            var raw = await AsyncUtils.ReadAllTextAsync(p);
            existing = JObject.Parse(raw);
          }
          else
          {
            existing = new JObject();
          }

          if (existing == null)
          {
            existing = new JObject();
          }

          existing["model"] = JToken.FromObject(models);

          await AsyncUtils.WriteAllTextAsync(p, existing.ToString(Formatting.Indented));
        }
        finally
        {
          _lock.Release();
        }
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
    public async Task<bool> HandleMessageAsync(
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
        var models = await ReadAsync(client);
        //     model[message.agent as string] = {
        var agent = modelSelectionRequest.Agent;
        var providerId = modelSelectionRequest.ProviderID;
        var modelId = modelSelectionRequest.ModelID;
        if (agent != null)
        {
          models[agent] = new ModelSelection
          {
            ModelId = modelId,
            ProviderId = providerId
          };
          await WriteAsync(client, models);
          //     return true
          return true;
          //   }
        }
      }
      //   if (type === "clearModelSelection") {
      if (message is ClearModelSelectionRequest clearModelSelection)
      //     const data = await read(client)
      {
        //     const model = validateModelSelections(data.model)
        var models = await ReadAsync(client);
        //     delete model[message.agent as string]
        var agent = clearModelSelection.Agent;
        if (agent != null && models.ContainsKey(agent))
        {
          models.Remove(agent);
          //     await write(client, "model", model)
        }
        await WriteAsync(client, models);
        //     return true
        return true;
        //   }
      }
      //   if (type === "requestModelSelections") {
      if (message is RequestModelSelectionsMessage requestModelSelections)
      //     const data = await read(client)
      { 
        
        var selections = await ReadAsync(client);
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
    public async Task ResetAsync(KiloApiClient? client, PostMessage post)
    // {
    {
      //   await write(client, "model", {})
      await WriteAsync(client, new Dictionary<string, ModelSelection>());
      //   post({ type: "modelSelectionsLoaded", selections: {} })
      post(new ModelSelectionsLoadedMessage
      {
        Selections = new Dictionary<string, ModelSelection>()
      });
      // }
    }
  
  }
}
