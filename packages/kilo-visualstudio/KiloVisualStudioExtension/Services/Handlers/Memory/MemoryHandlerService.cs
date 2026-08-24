// import * as vscode from "vscode"
// import { isMemoryOperation, type MemoryOperation } from "@kilocode/kilo-memory/commands"
// import { MemorySchema } from "@kilocode/kilo-memory/schema"
// import type { KiloClient, Session } from "@kilocode/sdk/v2/client"
// import { retry } from "../services/cli-backend/retry"
// import { getErrorMessage } from "../kilo-provider-utils"
using KiloExtensionDTOs.Memory;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.Utils;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Threading.Tasks;
using MemoryConfigureResponse = KiloVisualStudioExtension.ApiClient.Response46;
using MemoryCorrectBody = KiloVisualStudioExtension.ApiClient.Body69;
using MemoryCorrectResponse = KiloVisualStudioExtension.ApiClient.Response49;
using MemoryDisableResponse = KiloVisualStudioExtension.ApiClient.Response45;
using MemoryEnableResponse = KiloVisualStudioExtension.ApiClient.Response44;
using MemoryForgetBody = KiloVisualStudioExtension.ApiClient.Body70;
using MemoryForgetResponse = KiloVisualStudioExtension.ApiClient.Response50;
using MemoryPurgeBody = KiloVisualStudioExtension.ApiClient.Body71;
using MemoryPurgeResponse = KiloVisualStudioExtension.ApiClient.Response51;
using MemoryRebuildResponse = KiloVisualStudioExtension.ApiClient.Response47;
using MemoryRememberBody = KiloVisualStudioExtension.ApiClient.Body68;
using MemoryRememberResponse = KiloVisualStudioExtension.ApiClient.Response48;
using MemoryShowResponse = KiloVisualStudioExtension.ApiClient.Response43;
using MemoryStatusResponse = KiloVisualStudioExtension.ApiClient.Response42;

namespace KiloVisualStudioExtension.Services.Handlers.Memory
{
  // export type KiloProviderMemoryMessage = {
  public class KiloProviderMemoryMessage
  {
    //   operation: MemoryOperation
    public string Operation { get; set; } = "";
    //   sessionID?: string
    public string? SessionID { get; set; }
    //   mode?: "status" | "on" | "off"
    public string? Mode { get; set; }
    //   confirm?: boolean
    public bool? Confirm { get; set; }
    //   text?: string
    public string? Text { get; set; }
    //   query?: string
    public string? Query { get; set; }
    //   key?: string
    public string? Key { get; set; }
    //   file?: MemorySourceFile
    public string? File { get; set; }
    //   section?: string
    public string? Section { get; set; }
  }

  // export type KiloProviderMemoryInput = {
  public interface IKiloProviderMemoryInput
  {
    //   client(): KiloClient | undefined
    KiloApiClient? Client();
    //   session(): Session | undefined
    ApiClient.Session? Session();
    //   /** Project directory for memory operations, or undefined when project scope is disabled. */
    //   dir(sessionID?: string): string | undefined
    string? Dir(string? sessionID = null);
    //   post(message: unknown): void
    void Post(object message);
  }

  // Helper functions - placed in a static class
  internal static class MemoryHelpers
  {
    // type MemorySourceFile = MemorySchema.Source
    // type MemoryApi = KiloClient["memory"]

    // function file(value: unknown): MemorySourceFile | undefined {
    //   return MemorySchema.source(value)
    // }
    internal static string? File(object? value)
    {
      // TODO: Implement MemorySchema.source equivalent
      return value as string;
    }

    // function operation(value: unknown): MemoryOperation | undefined {
    //   return isMemoryOperation(value) ? value : undefined
    // }
    internal static string? Operation(object? value)
    {
      // TODO: Implement isMemoryOperation equivalent
      return value as string;
    }

    // function mode(value: unknown) {
    //   if (value === "status" || value === "on" || value === "off") return value
    //   return undefined
    // }
    internal static string? Mode(object? value)
    {
      if (value is string s && (s == "status" || s == "on" || s == "off"))
        return s;
      return null;
    }

    // function count(text: string) {
    //   return text.split("\n").filter((line) => line.trim().startsWith("- ")).length
    // }
    internal static int Count(string text)
    {
      return text.Split('\n').Where(line => line.Trim().StartsWith("- ")).Count();
    }

    // function stored(text: string) {
    //   return text
    //     .split("\n")
    //     .filter((line) => line.trim())
    //     .map((line) => {
    //       const marker = line.indexOf(":: ")
    //       return marker === -1 ? line : line.slice(marker + 3)
    //     })
    // }
    internal static string[] Stored(string text)
    {
      return text
          .Split('\n')
          .Where(line => line.Trim().Length > 0)
          .Select(line =>
          {
            var marker = line.IndexOf(":: ");
            return marker == -1 ? line : line.Substring(marker + 3);
          })
          .ToArray();
    }

    // function request(input: Record<string, unknown>): { value: KiloProviderMemoryMessage } | { error: string } {
    //   const op = operation(input.operation)
    //   if (!op) return { error: "Unknown memory operation" }
    //   const source = file(input.file)
    //   if (input.file !== undefined && !source) return { error: "Invalid memory source file" }
    //   return {
    //     value: {
    //       operation: op,
    //       sessionID: typeof input.sessionID === "string" ? input.sessionID : undefined,
    //       mode: mode(input.mode),
    //       confirm: input.confirm === true,
    //       text: typeof input.text === "string" ? input.text : undefined,
    //       query: typeof input.query === "string" ? input.query : undefined,
    //       key: typeof input.key === "string" ? input.key : undefined,
    //       file: source,
    //       section: typeof input.section === "string" ? input.section : undefined,
    //     },
    //   }
    // }
    internal static (KiloProviderMemoryMessage? value, string? error) Request(IDictionary<string, object> input)
    {
      var op = Operation(input.TryGetValue("operation", out var opObj) ? opObj : null);
      if (op == null) return (null, "Unknown memory operation");

      var source = File(input.TryGetValue("file", out var fileObj) ? fileObj : null);
      if (input.ContainsKey("file") && source == null) return (null, "Invalid memory source file");

      var message = new KiloProviderMemoryMessage
      {
        Operation = op,
        SessionID = input.TryGetValue("sessionID", out var sidObj) && sidObj is string sid ? sid : null,
        Mode = Mode(input.TryGetValue("mode", out var modeObj) ? modeObj : null),
        Confirm = input.TryGetValue("confirm", out var confObj) && confObj is bool conf ? conf : (bool?)null,
        Text = input.TryGetValue("text", out var textObj) && textObj is string text ? text : null,
        Query = input.TryGetValue("query", out var queryObj) && queryObj is string query ? query : null,
        Key = input.TryGetValue("key", out var keyObj) && keyObj is string key ? key : null,
        File = source,
        Section = input.TryGetValue("section", out var sectionObj) && sectionObj is string section ? section : null,
      };
      return (message, null);
    }
  }

  // export class KiloProviderMemory {
  public class MemoryHandlerService : ServiceProviderServiceBase
  {
    private bool _disposed;

    private VSProvider Provider => _serviceProvider.GetService<VSProvider>()
        ?? throw new InvalidOperationException("VSProvider not registered in service provider");

    // const CACHE_LIMIT = 8
    internal const int CACHE_LIMIT = 8;
    // const STORED_LIMIT = 16
    internal const int STORED_LIMIT = 16;
    // const NO_PROJECT = "No active project for memory. Open a file in the target folder to manage its memory."
    internal const string NO_PROJECT = "No active project for memory. Open a file in the target folder to manage its memory.";




    //   private readonly cached = new Map<string, unknown>()
    private readonly Dictionary<string, object> _cached = new Dictionary<string, object>();
    //   private tail = Promise.resolve()
    private Task _tail = Task.CompletedTask;

    //   constructor(private readonly input: KiloProviderMemoryInput) {}

    public MemoryHandlerService(ServiceProvider serviceProvider, IKiloProviderMemoryInput input = null): base(serviceProvider)
    {  
      _input = input ?? new MemoryInput(serviceProvider.GetService<VSProvider>());
    }

    private readonly IKiloProviderMemoryInput _input;

    //   private cache(dir: string, msg: unknown) {
    //     this.cached.delete(dir)
    //     this.cached.set(dir, msg)
    //     while (this.cached.size > CACHE_LIMIT) {
    //       const key = this.cached.keys().next().value
    //       if (typeof key !== "string") return
    //       this.cached.delete(key)
    //     }
    //   }
    private void Cache(string dir, object msg)
    {
      _cached.Remove(dir);
      _cached[dir] = msg;
      while (_cached.Count > CACHE_LIMIT)
      {
        var key = _cached.Keys.FirstOrDefault();
        if (key == null) return;
        _cached.Remove(key);
      }
    }

    //   private serial<T>(fn: () => Promise<T>) {
    //     // this.tail is always reassigned below to a never-rejecting promise, so it
    //     // never settles rejected — a single fulfillment handler is sufficient.
    //     const next = this.tail.then(fn)
    //     this.tail = next.then(
    //       () => undefined,
    //       () => undefined,
    //     )
    //     return next
    //   }
    private async Task<T> Serial<T>(Func<Task<T>> fn)
    {
      // this.tail is always reassigned below to a never-rejecting promise, so it
      // never settles rejected — a single fulfillment handler is sufficient.
      var next = _tail.ContinueWith(async _ => await fn());
      _tail = next.ContinueWith(async _ =>
      {
        try { await next; }
        catch { }
      });
      return await next.Unwrap();
    }

    private async Task Serial(Func<Task> fn)
    {
      var next = _tail.ContinueWith(async _ => await fn());
      _tail = next.ContinueWith(async _ =>
      {
        try { await next; }
        catch { }
      });
      await next.Unwrap();
    }


    //   async handle(message: Record<string, unknown>): Promise<boolean> {
    //     if (message.type === "requestMemory") {
    //       this.fetch(typeof message.sessionID === "string" ? message.sessionID : undefined).catch((err: unknown) =>
    //         console.error("[Kilo New] fetchAndSendMemory failed:", err),
    //       )
    //       return true
    //     }
    //     if (message.type === "memoryShow") {
    //       await this.show(
    //         typeof message.sessionID === "string" ? message.sessionID : undefined,
    //         message.mode === "status" ? "status" : "show",
    //       )
    //       return true
    //     }
    //     if (message.type === "memoryOperation") {
    //       const parsed = request(message)
    //       if ("error" in parsed) {
    //         this.input.post({
    //           type: "memoryOperationResult",
    //           operation: typeof message.operation === "string" ? message.operation : "unknown",
    //           sessionID: typeof message.sessionID === "string" ? message.sessionID : undefined,
    //           ok: false,
    //           error: parsed.error,
    //         })
    //         return true
    //       }
    //       await this.run(parsed.value)
    //       return true
    //     }
    //     return false
    //   }
    public async Task<bool> Handle(IDictionary<string, object> message)
    {
      if (message.TryGetValue("type", out var typeObj) && typeObj is string type)
      {
        if (type == "requestMemory")
        {
          var sessionID = message.TryGetValue("sessionID", out var sidObj) && sidObj is string sid ? sid : null;
          _ = Fetch(sessionID).ContinueWith(t =>
          {
            if (t.IsFaulted)
              System.Diagnostics.Debug.WriteLine($"[Kilo New] fetchAndSendMemory failed: {t.Exception?.GetBaseException().Message}");
          });
          return true;
        }
        if (type == "memoryShow")
        {
          var sessionID = message.TryGetValue("sessionID", out var sidObj) && sidObj is string sid ? sid : null;
          var mode = message.TryGetValue("mode", out var modeObj) && modeObj is string m && m == "status" ? "status" : "show";
          await Show(sessionID, mode);
          return true;
        }
        if (type == "memoryOperation")
        {
          var parsed = MemoryHelpers.Request(message);
          if (parsed.error != null)
          {
            _input.Post(new
            {
              type = "memoryOperationResult",
              operation = message.TryGetValue("operation", out var opObj) && opObj is string op ? op : "unknown",
              sessionID = message.TryGetValue("sessionID", out var sidObj) && sidObj is string sid ? sid : null,
              ok = false,
              error = parsed.error,
            });
            return true;
          }
          await Run(parsed.value!);
          return true;
        }
      }
      return false;
    }

    //   fetch(sessionID?: string): Promise<void> {
    //     return this.serial(() => this.load(sessionID))
    //   }
    public Task Fetch(string? sessionID = null)
    {
      return Serial(() => Load(sessionID));
    }

    //   /** Resolves once the serialized operation queue has drained. */
    //   idle(): Promise<void> {
    //     return this.tail
    //   }
    public Task Idle()
    {
      return _tail;
    }

    //   private async load(sessionID?: string): Promise<void> {
    //     try {
    //       const directory = this.input.dir(sessionID ?? this.input.session()?.id)
    //       const client = this.input.client()
    //       if (!client) {
    //         const cached = directory ? this.cached.get(directory) : undefined
    //         if (cached && typeof cached === "object" && !Array.isArray(cached)) this.input.post({ ...cached, sessionID })
    //         else this.input.post({ type: "memoryLoaded", sessionID, error: "Not connected to CLI backend" })
    //         return
    //       }
    //
    //       const api = memory(client)
    //       if (!api) {
    //         this.input.post({ type: "memoryLoaded", sessionID, error: "Memory unavailable in CLI backend" })
    //         return
    //       }
    //
    //       if (!directory) {
    //         this.input.post({ type: "memoryLoaded", sessionID, error: NO_PROJECT })
    //         return
    //       }
    //
    //       const { data: status } = await retry(() => api.status({ directory }, { throwOnError: true }))
    //       const msg = {
    //         type: "memoryLoaded",
    //         sessionID,
    //         status,
    //       }
    //       this.cache(directory, msg)
    //       this.input.post(msg)
    //     } catch (err) {
    //       console.error("[Kilo New] KiloProvider: Failed to fetch memory:", err)
    //       this.input.post({
    //         type: "memoryLoaded",
    //         sessionID,
    //         error: getErrorMessage(err) || "Failed to load memory",
    //       })
    //     }
    //   }
    private async Task Load(string? sessionID = null)
    {
      try
      {
        var directory = _input.Dir(sessionID ?? _input.Session()?.Id);
        var client = _input.Client();
        if (client == null)
        {
          object? cached = directory != null ? (_cached.TryGetValue(directory, out var c) ? c : null) : null;
          if (cached != null && cached is System.Collections.IDictionary cd && !(cached is object[]))
            _input.Post(new { cd, sessionID });
          else
            _input.Post(new MemoryLoadedMessage { SessionID = sessionID, Error = "Not connected to CLI backend" });
          return;
        }

        if (directory == null)
        {
          _input.Post(new MemoryLoadedMessage { SessionID = sessionID, Error = NO_PROJECT });
          return;
        }

        var statusResult = await Retry(() => client.Memory_statusAsync(directory, ""));
        var msg = new MemoryLoadedMessage
        {
          SessionID = sessionID,
          Status = statusResult,
        };
        Cache(directory, msg);
        _input.Post(msg);
      }
      catch (Exception err)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo New] KiloProvider: Failed to fetch memory: {err.Message}");
        _input.Post(new MemoryLoadedMessage
        {
          SessionID = sessionID,
          Error = "Failed to load memory",
        });
      }
    }

    //   show(sessionID?: string, mode: "status" | "show" = "show"): Promise<void> {
    //     return this.serial(() => this.doShow(sessionID, mode))
    //   }
    public Task Show(string? sessionID = null, string mode = "show")
    {
      return Serial(() => DoShow(sessionID, mode));
    }

    //   private async doShow(sessionID: string | undefined, mode: "status" | "show"): Promise<void> {
    //     const client = this.input.client()
    //     if (!client) {
    //       this.input.post({
    //         type: "memoryLoaded",
    //         sessionID,
    //         error: "Not connected to CLI backend",
    //       })
    //       return
    //     }
    //
    //     const api = memory(client)
    //
    //     if (!api) {
    //       this.input.post({
    //         type: "memoryLoaded",
    //         sessionID,
    //         error: "Memory unavailable in CLI backend",
    //       })
    //       return
    //     }
    //
    //     try {
    //       const directory = this.input.dir(sessionID ?? this.input.session()?.id)
    //       if (!directory) {
    //         this.input.post({ type: "memoryLoaded", sessionID, error: NO_PROJECT })
    //         return
    //       }
    //       const [{ data: show }, { data: status }] = await Promise.all([
    //         retry(() => api.show({ directory }, { throwOnError: true })),
    //         retry(() => api.status({ directory }, { throwOnError: true })),
    //       ])
    //       const msg = {
    //         type: "memoryLoaded",
    //         sessionID,
    //         status,
    //       }
    //       this.cache(directory, msg)
    //       this.input.post(msg)
    //       const items = stored(show.items)
    //       if (mode === "show" && items.length === 0) {
    //         void vscode.window.showInformationMessage(
    //           "This project doesn't have any memory yet. It will start showing after you use Kilo.",
    //         )
    //         return
    //       }
    //       const entries: vscode.QuickPickItem[] = [
    //         {
    //           label: `${status.state.enabled ? "Enabled" : "Disabled"} · ${status.state.scope}`,
    //           description: status.state.autoConsolidate ? "Auto-save on" : "Auto-save off",
    //         },
    //         { label: "Storage", detail: status.root },
    //         {
    //           label: "Sources",
    //           description: `project.md ${count(show.sources.project)} · environment.md ${count(show.sources.environment)} · corrections.md ${count(show.sources.corrections)}`,
    //         },
    //         {
    //           label: "Index",
    //           description: `${status.index.estimatedTokens.toLocaleString()} estimated tokens`,
    //         },
    //       ]
    //       if (mode === "show") {
    //         const shown = items.slice(0, STORED_LIMIT)
    //         entries.push(
    //           {
    //             label: "Stored memory",
    //             description:
    //               shown.length < items.length ? `${shown.length} of ${items.length} shown` : `${shown.length} shown`,
    //           },
    //           ...shown.map((label) => ({ label })),
    //         )
    //       }
    //       void vscode.window.showQuickPick(entries, {
    //         title: mode === "show" ? "Memory" : "Memory status",
    //         placeHolder: mode === "show" ? "Stored project memory" : "Project memory status",
    //         matchOnDescription: true,
    //         matchOnDetail: true,
    //       })
    //     } catch (err) {
    //       console.error("[Kilo New] KiloProvider: Failed to show memory:", err)
    //       this.input.post({
    //         type: "memoryLoaded",
    //         sessionID,
    //         error: getErrorMessage(err) || "Failed to show memory",
    //       })
    //     }
    //   }
    private async Task DoShow(string? sessionID, string mode)
    {
      var client = _input.Client();
      if (client == null)
      {
        _input.Post(new MemoryLoadedMessage
        {
          SessionID = sessionID,
          Error = "Not connected to CLI backend",
        });
        return;
      }

      try
      {
        var directory = _input.Dir(sessionID ?? _input.Session()?.Id);
        if (directory == null)
        {
          _input.Post(new MemoryLoadedMessage { SessionID = sessionID, Error = NO_PROJECT });
          return;
        }
        var showResult = await Retry(() => client.Memory_showAsync(directory, ""));
        var statusResult = await Retry(() => client.Memory_statusAsync(directory, ""));

        var msg = new MemoryLoadedMessage
        {
          SessionID = sessionID,
          Status = statusResult,
        };
        Cache(directory, msg);
        _input.Post(msg);

        var items = MemoryHelpers.Stored(showResult.Items);
        if (mode == "show" && items.Length == 0)
        {
          await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
          var model = new InfoBarModel(
              new[] {
                new InfoBarTextSpan("This project doesn't have any memory yet. It will start showing after you use Kilo."),
              },
              KnownMonikers.StatusInformation,
              true);
          KiloToolWindow.Instance.AddInfoBar(model);

          return;
        }

        var statusLabel = statusResult.State.Enabled ? "Enabled" : "Disabled";

        var entries = new List<QuickPickEntry>([
             new QuickPickEntry {
               Label = $"{statusLabel} · {statusResult.State.Scope}",
               Description = statusResult.State.AutoConsolidate ? "Auto-save on" : "Auto-save off",
             },
             new QuickPickEntry
             { Label = "Storage", Detail = statusResult.Root },
             new QuickPickEntry
             {
                Label= "Sources",
               Description = $"project.md { showResult.Sources.Project.Length } · environment.md ${ showResult.Sources.Environment.Length } · corrections.md ${ showResult.Sources.Corrections.Length }",
             },
             new QuickPickEntry
             {
              Label= "Index",
               Description= $"{ statusResult.Index.EstimatedTokens } estimated tokens",
             },
           ]);
        if (mode == "show")
        {
          var shown = items.Take(STORED_LIMIT).ToArray();
          entries.Add(new QuickPickEntry
          {
            Label = "Stored memory",
            Description = shown.Length < items.Length
              ? $"{shown.Length} of {items.Length} shown"
              : $"{shown.Length} shown"
          });
          foreach (var s in shown)
            entries.Add(new QuickPickEntry { Label = s });

          await QuickPickHelper.ShowQuickPickAsync(
            entries,
            new QuickPickOptions
            {
              Title = mode == "show" ? "Memory" : "Memory status",
              PlaceHolder = mode == "show" ? "Stored project memory" : "Project memory status",
              MatchOnDescription = true,
              MatchOnDetail = true
            }, null);
        }
      }
      catch (Exception err)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo New] KiloProvider: Failed to show memory: {err.Message}");
        _input.Post(new MemoryLoadedMessage
        {
          SessionID = sessionID,
          Error = "Failed to show memory",
        });
      }
    }

    //   run(message: KiloProviderMemoryMessage): Promise<boolean> {
    //     return this.serial(() => this.execute(message))
    //   }
    public Task<bool> Run(KiloProviderMemoryMessage message)
    {
      return Serial(() => Execute(message));
    }

    //   /**
    //    * Serialized status read + enable/disable, so two rapid toggles can't both
    //    * read the same pre-toggle state and apply the same operation twice.
    //    * Returns the applied operation, or undefined when it failed (the failure is
    //    * already posted to the webview by execute()).
    //    */
    //   toggle(sessionID?: string): Promise<MemoryOperation | undefined> {
    //     return this.serial(async () => {
    //       const client = this.input.client()
    //       if (!client) throw new Error("Not connected to CLI backend")
    //       const api = memory(client)
    //       if (!api) throw new Error("Memory unavailable in CLI backend")
    //       const directory = this.input.dir(sessionID ?? this.input.session()?.id)
    //       if (!directory) throw new Error(NO_PROJECT)
    //       const { data: status } = await retry(() => api.status({ directory }, { throwOnError: true }))
    //       const operation = status.state.enabled ? "disable" : "enable"
    //       return (await this.execute({ operation, sessionID })) ? operation : undefined
    //     })
    //   }
    public async Task<string?> Toggle(string? sessionID = null)
    {
      return await Serial(async () =>
      {
        var client = _input.Client();
        if (client == null) throw new Exception("Not connected to CLI backend");
        var directory = _input.Dir(sessionID ?? _input.Session()?.Id);
        if (directory == null) throw new Exception(NO_PROJECT);
        var statusResult = await Retry(() => client.Memory_statusAsync(directory, ""));
        var operation = statusResult.State.Enabled ? "disable" : "enable";
        return await Execute(new KiloProviderMemoryMessage { Operation = operation, SessionID = sessionID }) ? operation : null;
      });
    }

    //   private async execute(message: KiloProviderMemoryMessage): Promise<boolean> {
    //     const client = this.input.client()
    //     if (!client) {
    //       this.input.post({
    //         type: "memoryOperationResult",
    //         operation: message.operation,
    //         sessionID: message.sessionID,
    //         ok: false,
    //         error: "Not connected to CLI backend",
    //       })
    //       return false
    //     }
    //
    //     const api = memory(client)
    //     if (!api) {
    //       this.input.post({
    //         type: "memoryOperationResult",
    //         operation: message.operation,
    //         sessionID: message.sessionID,
    //         ok: false,
    //         error: "Memory unavailable in CLI backend",
    //       })
    //       return false
    //     }
    //
    //     try {
    //       const directory = this.input.dir(message.sessionID ?? this.input.session()?.id)
    //       if (!directory) {
    //         this.input.post({
    //           type: "memoryOperationResult",
    //           operation: message.operation,
    //           sessionID: message.sessionID,
    //           ok: false,
    //           error: NO_PROJECT,
    //         })
    //         return false
    //       }
    //       const data = await this.action(api, directory, message)
    //       const refreshed =
    //         message.operation === "status"
    //           ? { data }
    //           : await retry(() => api.status({ directory }, { throwOnError: true })).catch((err: unknown) => {
    //               console.warn("[Kilo New] Memory changed but refresh failed:", err)
    //               return undefined
    //             })
    //       const status = refreshed?.data
    //       const result = {
    //         type: "memoryOperationResult",
    //         operation: message.operation,
    //         sessionID: message.sessionID,
    //         ok: true,
    //         ...(status ? { status } : {}),
    //         result: data,
    //       }
    //       this.input.post(result)
    //       if (status) {
    //         const loaded = {
    //           type: "memoryLoaded",
    //           sessionID: message.sessionID,
    //           status,
    //         }
    //         this.cache(directory, loaded)
    //         this.input.post(loaded)
    //       } else {
    //         // Mutation succeeded but the refresh failed: drop the now-stale cached
    //         // entry so a later offline read doesn't report pre-mutation state.
    //         this.cached.delete(directory)
    //       }
    //       return true
    //     } catch (err) {
    //       console.error("[Kilo New] KiloProvider: Failed memory operation:", err)
    //       this.input.post({
    //         type: "memoryOperationResult",
    //         operation: message.operation,
    //         sessionID: message.sessionID,
    //         ok: false,
    //         error: getErrorMessage(err) || "Memory operation failed",
    //       })
    //       return false
    //     }
    //   }
    private async Task<bool> Execute(KiloProviderMemoryMessage message)
    {
      var client = _input.Client();
      if (client == null)
      {
        _input.Post(new MemoryOperationResultMessage
        {
          Operation = message.Operation.ToEnum(MemoryResultOperation.Auto),
          SessionID = message.SessionID,
          Ok = false,
          Error = "Not connected to CLI backend",
        });
        return false;
      }

      try
      {
        var directory = _input.Dir(message.SessionID ?? _input.Session()?.Id);
        if (directory == null)
        {
          _input.Post(new MemoryOperationResultMessage
          {
            Operation = message.Operation.ToEnum(MemoryResultOperation.Auto),
            SessionID = message.SessionID,
            Ok = false,
            Error = NO_PROJECT,
          });
          return false;
        }
        var data = (MemoryStatusResponse) await Action(client, directory, message);
        var refreshed = (message.Operation == "status")
            ? data
            : await Retry(() => client.Memory_statusAsync(directory, "")).ContinueWith(t =>
            {
              if (t.IsFaulted)
              {
                System.Diagnostics.Debug.WriteLine($"[Kilo New] Memory changed but refresh failed: {t.Exception?.GetBaseException().Message}");
                return null;
              }
              return t.Result;
            });
        var status = refreshed;
        var result = new MemoryOperationResultMessage
        {
          Operation = message.Operation.ToEnum(MemoryResultOperation.Status),
          SessionID = message.SessionID,
          Ok = true,
          Status = status,
          Result = data,
        };
        _input.Post(result);
        if (status != null)
        {
          var loaded = new MemoryLoadedMessage
          {
            SessionID = message.SessionID,
            Status = status,
          };
          Cache(directory, loaded);
          _input.Post(loaded);
        }
        else
        {
          // Mutation succeeded but the refresh failed: drop the now-stale cached
          // entry so a later offline read doesn't report pre-mutation state.
          _cached.Remove(directory);
        }
        return true;
      }
      catch (Exception err)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo New] KiloProvider: Failed memory operation: {err.Message}");
        _input.Post(new MemoryOperationResultMessage
        {
          Operation = message.Operation.ToEnum(MemoryResultOperation.Auto),
          SessionID = message.SessionID,
          Ok = false,
          Error = "Memory operation failed",
        });
        return false;
      }
    }

    //   private async action(api: MemoryApi, directory: string, message: KiloProviderMemoryMessage) {
    //     const op = message.operation
    //     if (op === "enable") return (await api.enable({ directory }, { throwOnError: true })).data
    //     if (op === "status") return (await api.status({ directory }, { throwOnError: true })).data
    //     if (op === "inspect") return this.inspect(api, directory)
    //     if (op === "disable") return (await api.disable({ directory }, { throwOnError: true })).data
    //     if (op === "rebuild") return (await api.rebuild({ directory }, { throwOnError: true })).data
    //     if (op === "purge") return this.purge(api, directory, message)
    //     if (op === "auto") return this.auto(api, directory, message)
    //     if (op === "remember") return this.remember(api, directory, message)
    //     if (op === "correct") return this.correct(api, directory, message)
    //     return this.forget(api, directory, message)
    //   }
    private async Task<IMemoryResponseBase> Action(KiloApiClient client, string directory, KiloProviderMemoryMessage message)
    {
      var op = message.Operation;
      if (op == "enable") return (await Retry(() => client.Memory_enableAsync(directory, "")).ConfigureAwait(false));
      if (op == "status") return (await Retry(() => client.Memory_statusAsync(directory, "")).ConfigureAwait(false));
      if (op == "inspect") return await Inspect(client, directory);
      if (op == "disable") return (await Retry(() => client.Memory_disableAsync(directory, "")).ConfigureAwait(false));
      if (op == "rebuild") return (await Retry(() => client.Memory_rebuildAsync(directory, "")).ConfigureAwait(false));
      if (op == "purge") return await Purge(client, directory, message);
      if (op == "auto") return await Auto(client, directory, message);
      if (op == "remember") return await Remember(client, directory, message);
      if (op == "correct") return await Correct(client, directory, message);
      return await Forget(client, directory, message);
    }

    //   private async remember(api: MemoryApi, directory: string, message: KiloProviderMemoryMessage) {
    //     const text = message.text?.trim()
    //     if (!text) throw new Error("Memory text is required")
    //     return (
    //       await api.remember(
    //         {
    //           directory,
    //           text,
    //           key: message.key,
    //           file: message.file,
    //           section: message.section,
    //           sessionID: message.sessionID,
    //         },
    //         { throwOnError: true },
    //       )
    //     ).data
    //   }
    private async Task<IMemoryResponseBase> Remember(KiloApiClient client, string directory, KiloProviderMemoryMessage message)
    {
      var text = message.Text?.Trim();
      if (string.IsNullOrEmpty(text)) throw new Exception("Memory text is required");
      // TODO : missing workspace
      return (await Retry(() => client.Memory_rememberAsync(
         directory, 
         "", 
         new MemoryRememberBody
         {
           Text = text,
           File = message.File.ToEnum(Body68File.Project_md),
           Section = message.Section,
           SessionID = message.SessionID,
           Key = message.Key
         }
         )).ConfigureAwait(false));
    }

    //   private async correct(api: MemoryApi, directory: string, message: KiloProviderMemoryMessage) {
    //     const text = message.text?.trim()
    //     if (!text) throw new Error("Correction text is required")
    //     return (
    //       await api.correct(
    //         {
    //           directory,
    //           text,
    //           key: message.key,
    //           sessionID: message.sessionID,
    //         },
    //         { throwOnError: true },
    //       )
    //     ).data
    //   }
    private async Task<IMemoryResponseBase> Correct(KiloApiClient client, string directory, KiloProviderMemoryMessage message)
    {
      var text = message.Text?.Trim();
      if (string.IsNullOrEmpty(text)) throw new Exception("Correction text is required");
      return (await Retry(() => client.Memory_correctAsync(
        directory, 
        "",
        new MemoryCorrectBody
        {
          Text = text,
          Key = message.Key,
          SessionID = message.SessionID
        })).ConfigureAwait(false));
    }

    //   private async forget(api: MemoryApi, directory: string, message: KiloProviderMemoryMessage) {
    //     const query = message.query?.trim()
    //     if (!query) throw new Error("Forget query is required")
    //     return (await api.forget({ directory, query, sessionID: message.sessionID }, { throwOnError: true })).data
    //   }
    private async Task<IMemoryResponseBase> Forget(KiloApiClient client, string directory, KiloProviderMemoryMessage message)
    {
      var query = message.Query?.Trim();
      if (string.IsNullOrEmpty(query)) throw new Exception("Forget query is required");
      return (await Retry(() => client.Memory_forgetAsync(
        directory, 
        "",
        
        new MemoryForgetBody
        {
          Query = query,
          SessionID = message.SessionID
        }
        )).ConfigureAwait(false));
    }

    //   private async inspect(api: MemoryApi, directory: string) {
    //     const { data: status } = await retry(() => api.status({ directory }, { throwOnError: true }))
    //     if (!status.state.enabled) throw new Error("Memory is disabled. Run /memory on first.")
    //     await vscode.commands.executeCommand("revealFileInOS", vscode.Uri.file(status.root))
    //     return status
    //   }
    private async Task<IMemoryResponseBase> Inspect(KiloApiClient client, string directory)
    {
      var statusResult = await Retry(() => client.Memory_statusAsync(directory, ""));
      if (!statusResult.State.Enabled) throw new Exception("Memory is disabled. Run /memory on first.");
      // TODO: Reveal file in Visual Studio
      System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{statusResult.Root}\"");
      return statusResult;
    }

    //   private async purge(api: MemoryApi, directory: string, message: KiloProviderMemoryMessage) {
    //     if (message.confirm !== true) throw new Error("Memory purge requires confirmation")
    //     return (await api.purge({ directory, confirm: true }, { throwOnError: true })).data
    //   }
    private async Task<IMemoryResponseBase> Purge(KiloApiClient client, string directory, KiloProviderMemoryMessage message)
    {
      if (message.Confirm != true) throw new Exception("Memory purge requires confirmation");
      return (await Retry(() => client.Memory_purgeAsync(directory, "", new MemoryPurgeBody { Confirm = true })).ConfigureAwait(false));
    }

    //   private async auto(api: MemoryApi, directory: string, message: KiloProviderMemoryMessage) {
    //     if (message.mode === "status") return (await retry(() => api.status({ directory }, { throwOnError: true }))).data
    //     if (message.mode === "on" || message.mode === "off") {
    //       return (await api.configure({ directory, autoConsolidate: message.mode === "on" }, { throwOnError: true })).data
    //     }
    //     throw new Error("Auto-save mode is required")
    //   }
    private async Task<IMemoryResponseBase> Auto(KiloApiClient client, string directory, KiloProviderMemoryMessage message)
    {
      if (message.Mode == "status") return (await Retry(() => client.Memory_statusAsync(directory, "")).ConfigureAwait(false));
      if (message.Mode == "on" || message.Mode == "off")
      {
        return (await Retry(() => client.Memory_configureAsync(directory, "", new Body67 { AutoConsolidate = message.Mode == "on" })).ConfigureAwait(false));
      }
      throw new Exception("Auto-save mode is required");
    }

    // Helper retry method
    private async Task<T> Retry<T>(Func<Task<T>> func)
    {
      int maxRetries = 3;
      for (int i = 0; i < maxRetries; i++)
      {
        try
        {
          return await func();
        }
        catch when (i < maxRetries - 1)
        {
          await Task.Delay(100 * (i + 1));
        }
      }
      return await func();
    }

    public void Dispose()
    {
      if (_disposed) return;
      _disposed = true;
    }
  }
}

