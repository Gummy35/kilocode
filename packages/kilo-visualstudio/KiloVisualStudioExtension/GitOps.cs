// ============================================================================ 
// GitOps.cs - C# Port of TypeScript GitOps 
// Source: packages/kilo-vscode/src/agent-manager/GitOps.ts 
// ============================================================================
using Newtonsoft.Json;
using System;
// import * as nodePath from "path" -> System.IO.Path using System.Collections; 
// import * as os from "os" -> System.Environment using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
// import * as fs from "fs/promises" -> System.IO.File / System.IO.Directory
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Collections.Concurrent;

#pragma warning disable CS8600
#pragma warning disable CS8601
#pragma warning disable CS8602 
#pragma warning disable CS8603 
#pragma warning disable CS8604 
#pragma warning disable CS8618 
#pragma warning disable CS8625

namespace KiloVisualStudioExtension
{
  // ======================================================================== 
  // TypeScript: interface GitOpsOptions { ... } 
  // C#: public class GitOpsOptions { ... } 
  // ========================================================================
  public class GitOpsOptions
  {
    // TypeScript: log: (...args: unknown[]) => void 
    // C#: Action Log
    public Action Log { get; set; } = null!;


    // TypeScript: runGit?: (args: string[], cwd: string) => Promise<string>
    // C#: Func<string[], string, Task<string>>? RunGit
    public Func<string[], string, Task<string>>? RunGit { get; set; }

    // TypeScript: semaphore?: Semaphore
    // C#: Semaphore? Semaphore
    public Semaphore? Semaphore { get; set; }
  }

  // ========================================================================
  // TypeScript: export interface ApplyConflict { ... }
  // C#: public class ApplyConflict { ... }
  // ========================================================================
  public class ApplyConflict
  {
    // TypeScript: file?: string
    // C#: string? File
    [JsonProperty("file")]
    public string? File { get; set; }

    // TypeScript: reason: string
    // C#: string Reason
    [JsonProperty("reason")]
    public string Reason { get; set; } = null!;
  }

  // ========================================================================
  // TypeScript: interface ApplyCheckResult { ... }
  // C#: public class ApplyCheckResult { ... }
  // ========================================================================
  public class ApplyCheckResult
  {
    // TypeScript: ok: boolean
    // C#: bool Ok
    [JsonProperty("ok")]
    public bool Ok { get; set; }

    // TypeScript: conflicts: ApplyConflict[]
    // C#: List<ApplyConflict> Conflicts
    [JsonProperty("conflicts")]
    public List<ApplyConflict> Conflicts { get; set; } = new();

    // TypeScript: message: string
    // C#: string Message
    [JsonProperty("message")]
    public string Message { get; set; } = null!;
  }

  // ========================================================================
  // TypeScript: interface ApplyPatchResult { ... }
  // C#: public class ApplyPatchResult { ... }
  // ========================================================================
  public class ApplyPatchResult
  {
    // TypeScript: ok: boolean
    // C#: bool Ok
    [JsonProperty("ok")]
    public bool Ok { get; set; }

    // TypeScript: conflicts: ApplyConflict[]
    // C#: List<ApplyConflict> Conflicts
    [JsonProperty("conflicts")]
    public List<ApplyConflict> Conflicts { get; set; } = new();

    // TypeScript: message: string
    // C#: string Message
    [JsonProperty("message")]
    public string Message { get; set; } = null!;
  }

  // ========================================================================
  // TypeScript: interface ExecOptions { ... }
  // C#: public class ExecOptions { ... }
  // ========================================================================
  public class ExecOptions
  {
    // TypeScript: env?: NodeJS.ProcessEnv
    // C#: Dictionary<string, string>? Env
    public Dictionary<string, string>? Env { get; set; }

    // TypeScript: stdin?: string
    // C#: string? Stdin
    public string? Stdin { get; set; }
  }

  // ========================================================================
  // TypeScript: export interface ExecResult { ... }
  // C#: public class ExecResult { ... }
  // ========================================================================
  public class ExecResult
  {
    // TypeScript: code: number
    // C#: int Code
    [JsonProperty("code")]
    public int Code { get; set; }

    // TypeScript: stdout: string
    // C#: string Stdout
    [JsonProperty("stdout")]
    public string Stdout { get; set; } = null!;

    // TypeScript: stderr: string
    // C#: string Stderr
    [JsonProperty("stderr")]
    public string Stderr { get; set; } = null!;
  }

  // ========================================================================
  // TypeScript: export interface ExecBufferResult { ... }
  // C#: public class ExecBufferResult { ... }
  // ========================================================================
  public class ExecBufferResult
  {
    // TypeScript: code: number
    // C#: int Code
    [JsonProperty("code")]
    public int Code { get; set; }

    // TypeScript: stdout: Buffer
    // C#: byte[] Stdout
    [JsonProperty("stdout")]
    public byte[] Stdout { get; set; } = Array.Empty<byte>();

    // TypeScript: stderr: string
    // C#: string Stderr
    [JsonProperty("stderr")]
    public string Stderr { get; set; } = null!;
  }

  // ========================================================================
  // TypeScript: type BranchListItem = { ... }
  // C#: public class BranchListItem { ... }
  // ========================================================================
  public class BranchListItem
  {
    // TypeScript: name: string
    // C#: string Name
    public string Name { get; set; } = null!;

    // TypeScript: isLocal: boolean
    // C#: bool IsLocal
    public bool IsLocal { get; set; }

    // TypeScript: isRemote: boolean
    // C#: bool IsRemote
    public bool IsRemote { get; set; }

    // TypeScript: isDefault: boolean
    // C#: bool IsDefault
    public bool IsDefault { get; set; }

    // TypeScript: lastCommitDate?: string
    // C#: string? LastCommitDate
    public string? LastCommitDate { get; set; }

    // TypeScript: isCheckedOut?: boolean
    // C#: bool IsCheckedOut
    public bool IsCheckedOut { get; set; }
  }

  // ========================================================================
  // TypeScript: export class GitOps { ... }
  // C#: public class GitOps : IDisposable { ... }
  // ========================================================================
  public class GitOps : IDisposable
  {
    // TypeScript: private readonly log: (...args: unknown[]) => void
    // C#: private readonly Action<string> _log
    private readonly Action<string> _log;

    // TypeScript: private readonly runGit: (args: string[], cwd: string) => Promise<string>
    // C#: private readonly Func<string[], string, Task<string>> _runGit
    private readonly Func<string[], string, Task<string>> _runGit;

    // TypeScript: private readonly controller = new AbortController()
    // C#: private readonly CancellationTokenSource _controller
    private readonly CancellationTokenSource _controller = new();

    // TypeScript: private readonly semaphore: Semaphore | undefined
    // C#: private readonly Semaphore? _semaphore
    private readonly Semaphore? _semaphore;

    // TypeScript: private readonly resolutionCache = new Map<string, { value: string; expires: number }>()
    // C#: private readonly ConcurrentDictionary<string, CacheEntry> _resolutionCache
    private readonly ConcurrentDictionary<string, CacheEntry> _resolutionCache = new();

    // TypeScript: private static readonly CACHE_TTL_MS = 60000
    // C#: private static readonly long CacheTtlMs
    private static readonly long CacheTtlMs = 60000;

    // TypeScript: private static readonly MAX_CACHE_SIZE = 100
    // C#: private static readonly int MaxCacheSize
    private static readonly int MaxCacheSize = 100;

    // ====================================================================
    // TypeScript: get disposed(): boolean { return this.controller.signal.aborted }
    // C#: public bool Disposed => _controller.IsCancellationRequested
    // ====================================================================
    public bool Disposed => _controller.IsCancellationRequested;

    // ====================================================================
    // TypeScript: constructor(options: GitOpsOptions) { ... }
    // C#: public GitOps(GitOpsOptions options) { ... }
    // ====================================================================
    public GitOps(GitOpsOptions options)
    {
      // TypeScript: this.log = options.log
      // C#: _log = options.Log
      _log = options.Log;

      // TypeScript: this.semaphore = options.semaphore
      // C#: _semaphore = options.Semaphore
      _semaphore = options.Semaphore;

      // TypeScript: this.runGit = options.runGit ?? ((args, cwd) => simpleGit(...).raw(args).then((out) => out.trim()))
      // C#: _runGit = options.RunGit ?? DefaultRunGit
      _runGit = options.RunGit ?? DefaultRunGit;
    }

    // ====================================================================
    // TypeScript: (default runGit implementation using simpleGit)
    // C#: private Task<string> DefaultRunGit(string[] args, string cwd)
    // ====================================================================
    private Task<string> DefaultRunGit(string[] args, string cwd)
    {
      // TypeScript: simpleGit(cwd, { abort: this.controller.signal }).raw(args).then((out) => out.trim())
      // C#: ExecBufferAsync with ContinueWith for trim
      var cts = CancellationTokenSource.CreateLinkedTokenSource(_controller.Token);
      return ExecBufferAsync(args, cwd, null, cts.Token).ContinueWith(t =>
          Encoding.UTF8.GetString(t.Result.Stdout).Trim(), TaskContinuationOptions.None);
    }

    // ====================================================================
    // TypeScript: dispose(): void { ... }
    // C#: public void Dispose() { ... }
    // ====================================================================
    public void Dispose()
    {
      // TypeScript: if (!this.controller.signal.aborted) { this.controller.abort() }
      // C#: if (!_controller.IsCancellationRequested) { _controller.Cancel() }
      if (!_controller.IsCancellationRequested)
      {
        _controller.Cancel();
      }

      // TypeScript: this.resolutionCache.clear()
      // C#: _resolutionCache.Clear()
      _resolutionCache.Clear();

      // C#: _controller.Dispose() - additional cleanup for CancellationTokenSource
      _controller.Dispose();
    }

    // ====================================================================
    // TypeScript: private getCached(key: string): string | undefined { ... }
    // C#: private string? GetCached(string key) { ... }
    // ====================================================================
    private string? GetCached(string key)
    {
      // TypeScript: const entry = this.resolutionCache.get(key)
      // C#: if (_resolutionCache.TryGetValue(key, out var entry)
      if (_resolutionCache.TryGetValue(key, out var entry) &&
          // TypeScript: entry && entry.expires > Date.now()
          // C#: entry.Expires > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
          entry.Expires > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
      {
        // TypeScript: return entry.value
        // C#: return entry.Value
        return entry.Value;
      }

      // TypeScript: return undefined
      // C#: return null
      return null;
    }

    // ====================================================================
    // TypeScript: private setCached(key: string, value: string): void { ... }
    // C#: private void SetCached(string key, string value) { ... }
    // ====================================================================
    private void SetCached(string key, string value)
    {
      // TypeScript: if (this.resolutionCache.size >= GitOps.MAX_CACHE_SIZE) { ... }
      // C#: if (_resolutionCache.Count >= MaxCacheSize) { ... }
      if (_resolutionCache.Count >= MaxCacheSize)
      {
        // TypeScript: let oldestKey: string | undefined
        // C#: string? oldestKey = null
        string? oldestKey = null;

        // TypeScript: let oldestExpiry = Infinity
        // C#: long oldestExpiry = long.MaxValue
        long oldestExpiry = long.MaxValue;

        // TypeScript: for (const [k, v] of this.resolutionCache) { ... }
        // C#: foreach (var kvp in _resolutionCache) { ... }
        foreach (var kvp in _resolutionCache)
        {
          // TypeScript: if (v.expires < oldestExpiry) { ... }
          // C#: if (kvp.Value.Expires < oldestExpiry) { ... }
          if (kvp.Value.Expires < oldestExpiry)
          {
            // TypeScript: oldestExpiry = v.expires
            // C#: oldestExpiry = kvp.Value.Expires
            oldestExpiry = kvp.Value.Expires;

            // TypeScript: oldestKey = k
            // C#: oldestKey = kvp.Key
            oldestKey = kvp.Key;
          }
        }

        // TypeScript: if (oldestKey) this.resolutionCache.delete(oldestKey)
        // C#: if (oldestKey != null) { _resolutionCache.TryRemove(oldestKey, out _) }
        if (oldestKey != null)
        {
          _resolutionCache.TryRemove(oldestKey, out _);
        }
      }

      // TypeScript: this.resolutionCache.set(key, { value, expires: Date.now() + GitOps.CACHE_TTL_MS })
      // C#: _resolutionCache[key] = new CacheEntry { Value = value, Expires = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + CacheTtlMs }
      _resolutionCache[key] = new CacheEntry
      {
        Value = value,
        Expires = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + CacheTtlMs
      };
    }

    // ====================================================================
    // TypeScript: private raw(args: string[], cwd: string): Promise<string> { ... }
    // C#: private Task<string> Raw(string[] args, string cwd) { ... }
    // ====================================================================
    private Task<string> Raw(string[] args, string cwd)
    {
      // TypeScript: const signal = this.controller.signal
      // C#: var signal = _controller.Token
      var signal = _controller.Token;

      // TypeScript: if (signal.aborted) return Promise.reject(new Error("GitOps disposed"))
      // C#: if (signal.IsCancellationRequested) return Task.FromException<string>(new Exception("GitOps disposed"))
      if (signal.IsCancellationRequested) return Task.FromException<string>(new Exception("GitOps disposed"));

      // TypeScript: const invoke = () => new Promise<string>((resolve, reject) => { ... })
      // C#: var invoke = () => { ... }
      var invoke = () =>
      {
        // TypeScript: new Promise<string>((resolve, reject) => { ... })
        // C#: var tcs = new TaskCompletionSource<string>()
        var tcs = new TaskCompletionSource<string>();

        // TypeScript: const onAbort = () => reject(new Error("GitOps disposed"))
        // C#: var onAbort = new Action(() => { tcs.TrySetException(new Exception("GitOps disposed")); ... })
        Registration? onAbortReg = null;
        var onAbort = new Action(() =>
        {
          tcs.TrySetException(new Exception("GitOps disposed"));
          if (onAbortReg.HasValue) onAbortReg.Value.Dispose();
        });

        // TypeScript: signal.addEventListener("abort", onAbort, { once: true })
        // C#: onAbortReg = signal.Register(onAbort)
        onAbortReg = signal.Register(onAbort);

        // TypeScript: this.runGit(args, cwd).then((value) => { ... }, (err) => { ... })
        // C#: var runTask = _runGit(args, cwd); runTask.ContinueWith(t => { ... })
        var runTask = _runGit(args, cwd);
        runTask.ContinueWith(t =>
        {
          // TypeScript: signal.removeEventListener("abort", onAbort)
          // C#: if (onAbortReg.HasValue) onAbortReg.Value.Dispose()
          if (onAbortReg.HasValue) onAbortReg.Value.Dispose();

          // TypeScript: if (t.IsFaulted) { reject(err) } else { resolve(value) }
          // C#: if (t.IsFaulted) { tcs.TrySetException(...) } else { tcs.TrySetResult(t.Result) }
          if (t.IsFaulted)
          {
            tcs.TrySetException(t.Exception!.InnerException ?? t.Exception);
          }
          else
          {
            tcs.TrySetResult(t.Result);
          }
        }, TaskContinuationOptions.None);

        // TypeScript: return the promise
        // C#: return tcs.Task
        return tcs.Task;
      };

      // TypeScript: return this.semaphore ? this.semaphore.run(invoke) : invoke()
      // C#: return _semaphore != null ? _semaphore.Run(invoke) : invoke()
      return _semaphore != null ? _semaphore.Run(invoke) : invoke();
    }

    // ====================================================================
    // TypeScript: async currentBranch(cwd: string): Promise<string> { ... }
    // C#: public async Task<string> CurrentBranch(string cwd) { ... }
    // ====================================================================
    public async Task<string> CurrentBranch(string cwd)
    {
      try
      {
        // TypeScript: return this.raw(["rev-parse", "--abbrev-ref", "HEAD"], cwd).catch(() => "")
        // C#: return await Raw(...).catch(() => "")
        return await Raw(new[] { "rev-parse", "--abbrev-ref", "HEAD" }, cwd);
      }
      catch
      {
        return "";
      }
    }

    // ====================================================================
    // TypeScript: async resolveRemote(cwd: string, branch?: string): Promise<string> { ... }
    // C#: public async Task<string> ResolveRemote(string cwd, string? branch = null) { ... }
    // ====================================================================
    public async Task<string> ResolveRemote(string cwd, string? branch = null)
    {
      // TypeScript: const cacheKey = `remote:${cwd}:${branch}`
      // C#: var cacheKey = $"remote:{cwd}:{branch}"
      var cacheKey = $"remote:{cwd}:{branch}";

      // TypeScript: const cached = this.getCached(cacheKey)
      // C#: var cached = GetCached(cacheKey)
      var cached = GetCached(cacheKey);

      // TypeScript: if (cached) return cached
      // C#: if (cached != null) return cached
      if (cached != null) return cached;

      // TypeScript: const upstream = await this.raw(["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"], cwd).catch(() => "")
      // C#: string upstream; try { ... } catch { upstream = ""; }
      string upstream;
      try
      {
        upstream = await Raw(new[] { "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}" }, cwd);
      }
      catch
      {
        upstream = "";
      }

      // TypeScript: if (upstream.includes("/")) { ... }
      // C#: if (upstream.Contains("/")) { ... }
      if (upstream.Contains("/"))
      {
        // TypeScript: const result = upstream.split("/")[0]
        // C#: var result = upstream.Split('/')[0]
        var result = upstream.Split('/')[0];

        // TypeScript: this.setCached(cacheKey, result)
        // C#: SetCached(cacheKey, result)
        SetCached(cacheKey, result);

        // TypeScript: return result
        // C#: return result
        return result;
      }

      // TypeScript: const name = branch || (await this.raw(["branch", "--show-current"], cwd).catch(() => ""))
      // C#: var name = branch ?? await CurrentBranch(cwd)
      var name = branch ?? await CurrentBranch(cwd);

      // TypeScript: if (name) { ... }
      // C#: if (!string.IsNullOrEmpty(name)) { ... }
      if (!string.IsNullOrEmpty(name))
      {
        // TypeScript: const configured = await this.raw(["config", `branch.${name}.remote`], cwd).catch(() => "")
        // C#: string configured; try { ... } catch { configured = ""; }
        string configured;
        try
        {
          configured = await Raw(new[] { "config", $"branch.{name}.remote" }, cwd);
        }
        catch
        {
          configured = "";
        }

        // TypeScript: if (configured) { ... }
        // C#: if (!string.IsNullOrEmpty(configured)) { ... }
        if (!string.IsNullOrEmpty(configured))
        {
          // TypeScript: this.setCached(cacheKey, configured)
          // C#: SetCached(cacheKey, configured)
          SetCached(cacheKey, configured);

          // TypeScript: return configured
          // C#: return configured
          return configured;
        }
      }

      // TypeScript: const result = "origin"
      // C#: var result = "origin"
      var result = "origin";

      // TypeScript: this.setCached(cacheKey, result)
      // C#: SetCached(cacheKey, result)
      SetCached(cacheKey, result);

      // TypeScript: return result
      // C#: return result
      return result;
    }

    // ====================================================================
    // TypeScript: async resolveTrackingBranch(cwd: string, branch: string): Promise<string | undefined> { ... }
    // C#: public async Task<string?> ResolveTrackingBranch(string cwd, string branch) { ... }
    // ====================================================================
    public async Task<string?> ResolveTrackingBranch(string cwd, string branch)
    {
      // TypeScript: const cacheKey = `tracking:${cwd}:${branch}`
      // C#: var cacheKey = $"tracking:{cwd}:{branch}"
      var cacheKey = $"tracking:{cwd}:{branch}";

      // TypeScript: const cached = this.getCached(cacheKey)
      // C#: var cached = GetCached(cacheKey)
      var cached = GetCached(cacheKey);

      // TypeScript: if (cached !== undefined) return cached === "" ? undefined : cached
      // C#: if (cached != null) return cached == "" ? null : cached
      if (cached != null) return cached == "" ? null : cached;

      // TypeScript: const upstream = await this.raw(["rev-parse", "--abbrev-ref", "@{upstream}"], cwd).catch(() => "")
      // C#: string upstream; try { ... } catch { upstream = ""; }
      string upstream;
      try
      {
        upstream = await Raw(new[] { "rev-parse", "--abbrev-ref", "@{upstream}" }, cwd);
      }
      catch
      {
        upstream = "";
      }

      // TypeScript: if (upstream) { ... }
      // C#: if (!string.IsNullOrEmpty(upstream)) { ... }
      if (!string.IsNullOrEmpty(upstream))
      {
        // TypeScript: this.setCached(cacheKey, upstream)
        // C#: SetCached(cacheKey, upstream)
        SetCached(cacheKey, upstream);

        // TypeScript: return upstream
        // C#: return upstream
        return upstream;
      }

      // TypeScript: const remote = await this.resolveRemote(cwd, branch)
      // C#: var remote = await ResolveRemote(cwd, branch)
      var remote = await ResolveRemote(cwd, branch);

      // TypeScript: const ref = `${remote}/${branch}`
      // C#: var @ref = $"{remote}/{branch}"
      var @ref = $"{remote}/{branch}";

      // TypeScript: const resolved = await this.raw(["rev-parse", "--verify", ref], cwd).catch(() => "")
      // C#: string resolved; try { ... } catch { resolved = ""; }
      string resolved;
      try
      {
        resolved = await Raw(new[] { "rev-parse", "--verify", @ref }, cwd);
      }
      catch
      {
        resolved = "";
      }

      // TypeScript: if (resolved) { ... }
      // C#: if (!string.IsNullOrEmpty(resolved)) { ... }
      if (!string.IsNullOrEmpty(resolved))
      {
        // TypeScript: this.setCached(cacheKey, ref)
        // C#: SetCached(cacheKey, @ref)
        SetCached(cacheKey, @ref);

        // TypeScript: return ref
        // C#: return @ref
        return @ref;
      }

      // TypeScript: this.setCached(cacheKey, "")
      // C#: SetCached(cacheKey, "")
      SetCached(cacheKey, "");

      // TypeScript: return undefined
      // C#: return null
      return null;
    }

    // ====================================================================
    // TypeScript: async resolveDefaultBranch(cwd: string, branch?: string): Promise<string | undefined> { ... }
    // C#: public async Task<string?> ResolveDefaultBranch(string cwd, string? branch = null) { ... }
    // ====================================================================
    public async Task<string?> ResolveDefaultBranch(string cwd, string? branch = null)
    {
      // TypeScript: const remote = await this.resolveRemote(cwd, branch)
      // C#: var remote = await ResolveRemote(cwd, branch)
      var remote = await ResolveRemote(cwd, branch);

      // TypeScript: const cacheKey = `default-branch:${cwd}:${remote}`
      // C#: var cacheKey = $"default-branch:{cwd}:{remote}"
      var cacheKey = $"default-branch:{cwd}:{remote}";

      // TypeScript: const cached = this.getCached(cacheKey)
      // C#: var cached = GetCached(cacheKey)
      var cached = GetCached(cacheKey);

      // TypeScript: if (cached !== undefined) return cached === "" ? undefined : cached
      // C#: if (cached != null) return cached == "" ? null : cached
      if (cached != null) return cached == "" ? null : cached;

      // TypeScript: const head = await this.raw(["symbolic-ref", "--short", `refs/remotes/${remote}/HEAD`], cwd).catch(() => "")
      // C#: string head; try { ... } catch { head = ""; }
      string head;
      try
      {
        head = await Raw(new[] { "symbolic-ref", "--short", $"refs/remotes/{remote}/HEAD" }, cwd);
      }
      catch
      {
        head = "";
      }

      // TypeScript: const result = head || undefined
      // C#: var result = string.IsNullOrEmpty(head) ? null : head
      var result = string.IsNullOrEmpty(head) ? null : head;

      // TypeScript: this.setCached(cacheKey, result ?? "")
      // C#: SetCached(cacheKey, result ?? "")
      SetCached(cacheKey, result ?? "");

      // TypeScript: return result
      // C#: return result
      return result;
    }

    // ====================================================================
    // TypeScript: async hasRemoteRef(cwd: string, ref: string): Promise<boolean> { ... }
    // C#: public async Task<bool> HasRemoteRef(string cwd, string @ref) { ... }
    // ====================================================================
    public async Task<bool> HasRemoteRef(string cwd, string @ref)
    {
      try
      {
        // TypeScript: return this.raw(["rev-parse", "--verify", "--quiet", `refs/remotes/${ref}`], cwd).then(() => true).catch(() => false)
        // C#: await Raw(...); return true;
        await Raw(new[] { "rev-parse", "--verify", "--quiet", $"refs/remotes/{@ref}" }, cwd);
        return true;
      }
      catch
      {
        return false;
      }
    }

    // ====================================================================
    // TypeScript: async listBranches(cwd: string): Promise<{ branches: BranchListItem[]; defaultBranch: string }> { ... }
    // C#: public async Task<(List<BranchListItem> branches, string defaultBranch)> ListBranches(string cwd) { ... }
    // ====================================================================
    public async Task<(List<BranchListItem> branches, string defaultBranch)> ListBranches(string cwd)
    {
      // TypeScript: const def = (await this.resolveDefaultBranch(cwd)) ?? ""
      // C#: var def = await ResolveDefaultBranch(cwd) ?? ""
      var def = await ResolveDefaultBranch(cwd) ?? "";

      // TypeScript: const raw = await this.raw([...], cwd).catch((err) => { ... })
      // C#: string raw; try { ... } catch (Exception err) { ... }
      string raw;
      try
      {
        raw = await Raw(new[]
        {
                "for-each-ref",
                "--sort=-committerdate",
                "--format=%(refname)\t%(committerdate:iso-strict)",
                "refs/heads/",
                "refs/remotes/origin/"
            }, cwd);
      }
      catch (Exception err)
      {
        // TypeScript: this.log("listBranches: for-each-ref failed", err instanceof Error ? err.message : String(err))
        // C#: _log($"listBranches: for-each-ref failed: {err.Message}")
        _log($"listBranches: for-each-ref failed: {err.Message}");
        raw = "";
      }

      // TypeScript: const { locals, remotes, dates } = parseForEachRefOutput(raw)
      // C#: var (locals, remotes, dates) = ParseForEachRefOutput(raw)
      var (locals, remotes, dates) = ParseForEachRefOutput(raw);

      // TypeScript: return { branches: buildBranchList(locals, remotes, dates, def), defaultBranch: def }
      // C#: var branches = BuildBranchList(...); return (branches, def)
      var branches = BuildBranchList(locals, remotes, dates, def);
      return (branches, def);
    }

    // ====================================================================
    // TypeScript: async listWorktreePaths(cwd: string): Promise<Map<string, string>> { ... }
    // C#: public async Task<Dictionary<string, string>> ListWorktreePaths(string cwd) { ... }
    // ====================================================================
    public async Task<Dictionary<string, string>> ListWorktreePaths(string cwd)
    {
      // TypeScript: const raw = await this.raw(["worktree", "list", "--porcelain"], cwd)
      // C#: var raw = await Raw(...)
      var raw = await Raw(new[] { "worktree", "list", "--porcelain" }, cwd);

      // TypeScript: const result = new Map<string, string>()
      // C#: var result = new Dictionary<string, string>()
      var result = new Dictionary<string, string>();

      // TypeScript: for (const entry of parseWorktreeList(raw)) { ... }
      // C#: foreach (var entry in ParseWorktreeList(raw)) { ... }
      foreach (var entry in ParseWorktreeList(raw))
      {
        // TypeScript: if (entry.bare) continue
        // C#: if (entry.Bare) continue
        if (entry.Bare) continue;

        // TypeScript: result.set(normalizePath(entry.path), entry.branch)
        // C#: result[NormalizePath(entry.Path)] = entry.Branch
        result[NormalizePath(entry.Path)] = entry.Branch;
      }

      // TypeScript: return result
      // C#: return result
      return result;
    }

    // ====================================================================
    // TypeScript: async workingTreeStats(cwd: string): Promise<{ files: number; additions: number; deletions: number }> { ... }
    // C#: public async Task<(int files, int additions, int deletions)> WorkingTreeStats(string cwd) { ... }
    // ====================================================================
    public async Task<(int files, int additions, int deletions)> WorkingTreeStats(string cwd)
    {
      // TypeScript: const [numstat, untracked] = await Promise.all([ ... ])
      // C#: var numstatTask = ...; var untrackedTask = ...; await Task.WhenAll(...)
      var numstatTask = Raw(new[] { "diff", "HEAD", "--numstat" }, cwd).ContinueWith(t =>
          t.IsFaulted ? "" : t.Result, TaskContinuationOptions.None);
      var untrackedTask = Raw(new[] { "ls-files", "--others", "--exclude-standard" }, cwd).ContinueWith(t =>
          t.IsFaulted ? "" : t.Result, TaskContinuationOptions.None);

      await Task.WhenAll(numstatTask, untrackedTask);
      var numstat = numstatTask.Result;
      var untracked = untrackedTask.Result;

      // TypeScript: const tracked = numstat ? numstat.split("\n").reduce(...) : { files: 0, additions: 0, deletions: 0 }
      // C#: var tracked = string.IsNullOrEmpty(numstat) ? ... : numstat.Split('\n').Aggregate(...)
      var tracked = string.IsNullOrEmpty(numstat)
          ? (files: 0, additions: 0, deletions: 0)
          : numstat.Split('\n').Aggregate(
              (files: 0, additions: 0, deletions: 0),
              (acc, line) =>
              {
                // TypeScript: if (!line.trim()) return acc
                // C#: if (string.IsNullOrWhiteSpace(line)) return acc
                if (string.IsNullOrWhiteSpace(line)) return acc;

                // TypeScript: const parts = line.split("\t")
                // C#: var parts = line.Split('\t')
                var parts = line.Split('\t');

                // TypeScript: return { files: acc.files + 1, additions: ..., deletions: ... }
                // C#: return (files: acc.files + 1, additions: ..., deletions: ...)
                return (
                        files: acc.files + 1,
                        additions: acc.additions + (parts[0] != "-" ? int.Parse(parts[0]) : 0),
                        deletions: acc.deletions + (parts[1] != "-" ? int.Parse(parts[1]) : 0)
                    );
              });

      // TypeScript: if (!untracked) return tracked
      // C#: if (string.IsNullOrEmpty(untracked)) return tracked
      if (string.IsNullOrEmpty(untracked)) return tracked;

      // TypeScript: const paths = untracked.split("\n").filter((line) => line.trim())
      // C#: var paths = untracked.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)).ToArray()
      var paths = untracked.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();

      // TypeScript: const counts = await Promise.all(paths.map(async (p) => { ... }))
      // C#: var counts = await Task.WhenAll(paths.Select(async p => { ... }))
      var counts = await Task.WhenAll(paths.Select(async p =>
      {
        try
        {
          // TypeScript: const full = nodePath.resolve(cwd, p)
          // C#: var full = Path.GetFullPath(Path.Combine(cwd, p))
          var full = Path.GetFullPath(Path.Combine(cwd, p));

          // TypeScript: const stat = await fs.stat(full)
          // C#: var stat = new FileInfo(full)
          var stat = new FileInfo(full);

          // TypeScript: if (stat.size > 1_000_000) return 0
          // C#: if (stat.Length > 1_000_000) return 0
          if (stat.Length > 1_000_000) return 0;

          // TypeScript: const content = await fs.readFile(full, "utf-8")
          // C#: var content = await File.ReadAllTextAsync(full)
          var content = await File.ReadAllTextAsync(full);

          // TypeScript: return content.split("\n").length
          // C#: return content.Split('\n').Length
          return content.Split('\n').Length;
        }
        catch (Exception err)
        {
          // TypeScript: this.log(`Failed to read untracked file ${p}:`, err)
          // C#: _log($"Failed to read untracked file {p}: {err.Message}")
          _log($"Failed to read untracked file {p}: {err.Message}");
          return 0;
        }
      }));

      // TypeScript: return { files: tracked.files + paths.length, additions: ..., deletions: tracked.deletions }
      // C#: return (files: ..., additions: ..., deletions: ...)
      return (
          files: tracked.files + paths.Length,
          additions: tracked.additions + counts.Sum(),
          deletions: tracked.deletions
      );
    }

    // ====================================================================
    // TypeScript: async aheadBehind(cwd: string, base: string): Promise<{ ahead: number; behind: number }> { ... }
    // C#: public async Task<(int ahead, int behind)> AheadBehind(string cwd, string @base) { ... }
    // ====================================================================
    public async Task<(int ahead, int behind)> AheadBehind(string cwd, string @base)
    {
      // TypeScript: return this.parseLeftRight(cwd, base)
      // C#: return await ParseLeftRight(cwd, @base)
      return await ParseLeftRight(cwd, @base);
    }

    // ====================================================================
    // TypeScript: private async parseLeftRight(cwd: string, ref: string): Promise<{ ahead: number; behind: number }> { ... }
    // C#: private async Task<(int ahead, int behind)> ParseLeftRight(string cwd, string @ref) { ... }
    // ====================================================================
    private async Task<(int ahead, int behind)> ParseLeftRight(string cwd, string @ref)
    {
      // TypeScript: const out = await this.raw(["rev-list", "--left-right", "--count", `${ref}...HEAD`], cwd).catch(() => "0\t0")
      // C#: string outStr; try { ... } catch { outStr = "0\t0"; }
      string outStr;
      try
      {
        outStr = await Raw(new[] { "rev-list", "--left-right", "--count", $"{@ref}...HEAD" }, cwd);
      }
      catch
      {
        outStr = "0\t0";
      }

      // TypeScript: const [behind, ahead] = out.split(/\s+/).map((s) => parseInt(s, 10) || 0)
      // C#: var parts = outStr.Split(...); var behind = ...; var ahead = ...
      var parts = outStr.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
      var behind = parts.Length > 0 ? int.Parse(parts[0]) : 0;
      var ahead = parts.Length > 1 ? int.Parse(parts[1]) : 0;

      // TypeScript: return { ahead, behind }
      // C#: return (ahead, behind)
      return (ahead, behind);
    }

    // ====================================================================
    // TypeScript: async buildWorktreePatch(sourcePath: string, baseBranch: string, selectedFiles?: string[]): Promise<string> { ... }
    // C#: public async Task<string> BuildWorktreePatch(string sourcePath, string baseBranch, string[]? selectedFiles = null) { ... }
    // ====================================================================
    public async Task<string> BuildWorktreePatch(string sourcePath, string baseBranch, string[]? selectedFiles = null)
    {
      // TypeScript: const tmp = await fs.mkdtemp(nodePath.join(os.tmpdir(), "kilo-apply-"))
      // C#: var tmp = Path.Combine(Path.GetTempPath(), $"kilo-apply-{Guid.NewGuid():N}")
      var tmp = Path.Combine(Path.GetTempPath(), $"kilo-apply-{Guid.NewGuid():N}");

      // TypeScript: await fs.mkdir(tmp) - implied by mkdtemp
      // C#: Directory.CreateDirectory(tmp)
      Directory.CreateDirectory(tmp);

      // TypeScript: const index = nodePath.join(tmp, "index")
      // C#: var index = Path.Combine(tmp, "index")
      var index = Path.Combine(tmp, "index");

      // TypeScript: const env = { ...process.env, GIT_INDEX_FILE: index }
      // C#: var env = new Dictionary<string, string>(Environment.GetEnvironmentVariables()...) { ["GIT_INDEX_FILE"] = index }
      var env = new Dictionary<string, string>(Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
          .ToDictionary(kvp => kvp.Key.ToString()!, kvp => kvp.Value!.ToString()!))
      {
        ["GIT_INDEX_FILE"] = index
      };

      // TypeScript: const files = (selectedFiles ?? []).map((file) => file.trim()).filter((file) => file.length > 0 && !nodePath.isAbsolute(file) && !file.split(/[\\/]/).includes(".."))
      // C#: var files = (selectedFiles ?? Array.Empty<string>()).Select(f => f.Trim()).Where(f => f.Length > 0 && !Path.IsPathRooted(f) && !f.Split(...).Contains("..")).ToArray()
      var files = (selectedFiles ?? Array.Empty<string>())
          .Select(f => f.Trim())
          .Where(f => f.Length > 0 && !Path.IsPathRooted(f) && !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains(".."))
          .ToArray();

      // TypeScript: const pathspec = files.length > 0 ? files : ["."]
      // C#: var pathspec = files.Length > 0 ? files : new[] { "." }
      var pathspec = files.Length > 0 ? files : new[] { "." };

      try
      {
        // TypeScript: const base = (await this.raw(["merge-base", "HEAD", baseBranch], sourcePath)).trim()
        // C#: string baseCommit; try { ... } catch { baseCommit = ""; }
        string baseCommit;
        try
        {
          baseCommit = (await Raw(new[] { "merge-base", "HEAD", baseBranch }, sourcePath)).Trim();
        }
        catch
        {
          baseCommit = "";
        }

        // TypeScript: const baseTree = (await this.raw(["rev-parse", `${base}^{tree}`], sourcePath)).trim()
        // C#: string baseTree; try { ... } catch { baseTree = ""; }
        string baseTree;
        try
        {
          baseTree = (await Raw(new[] { "rev-parse", $"{baseCommit}^{{tree}}" }, sourcePath)).Trim();
        }
        catch
        {
          baseTree = "";
        }

        // TypeScript: const read = await this.exec(["read-tree", "HEAD"], sourcePath, { env })
        // C#: var read = await ExecAsync(...)
        var read = await ExecAsync(new[] { "read-tree", "HEAD" }, sourcePath, new ExecOptions { Env = env });

        // TypeScript: if (read.code !== 0) { throw new Error(read.stderr.trim() || "Failed to initialize temporary index") }
        // C#: if (read.Code != 0) { throw new Exception(read.Stderr.Trim() ?? "...") }
        if (read.Code != 0)
        {
          throw new Exception(read.Stderr.Trim() ?? "Failed to initialize temporary index");
        }

        // TypeScript: const add = await this.exec(["add", "-A", "--", ...pathspec], sourcePath, { env })
        // C#: var addArgs = new List<string> { "add", "-A", "--" }; addArgs.AddRange(pathspec); var add = await ExecAsync(...)
        var addArgs = new List<string> { "add", "-A", "--" };
        addArgs.AddRange(pathspec);
        var add = await ExecAsync(addArgs.ToArray(), sourcePath, new ExecOptions { Env = env });

        // TypeScript: if (add.code !== 0) { throw new Error(add.stderr.trim() || "Failed to stage worktree snapshot") }
        // C#: if (add.Code != 0) { throw new Exception(add.Stderr.Trim() ?? "...") }
        if (add.Code != 0)
        {
          throw new Exception(add.Stderr.Trim() ?? "Failed to stage worktree snapshot");
        }

        // TypeScript: const treeResult = await this.exec(["write-tree"], sourcePath, { env })
        // C#: var treeResult = await ExecAsync(...)
        var treeResult = await ExecAsync(new[] { "write-tree" }, sourcePath, new ExecOptions { Env = env });

        // TypeScript: if (treeResult.code !== 0) { throw new Error(treeResult.stderr.trim() || "Failed to snapshot worktree index") }
        // C#: if (treeResult.Code != 0) { throw new Exception(treeResult.Stderr.Trim() ?? "...") }
        if (treeResult.Code != 0)
        {
          throw new Exception(treeResult.Stderr.Trim() ?? "Failed to snapshot worktree index");
        }

        // TypeScript: const tree = treeResult.stdout.trim()
        // C#: var tree = treeResult.Stdout.Trim()
        var tree = treeResult.Stdout.Trim();

        // TypeScript: const diff = await this.exec(["diff", "--binary", "--full-index", "--find-renames", "--no-color", baseTree, tree], sourcePath)
        // C#: var diff = await ExecAsync(...)
        var diff = await ExecAsync(new[] { "diff", "--binary", "--full-index", "--find-renames", "--no-color", baseTree, tree }, sourcePath);

        // TypeScript: if (diff.code !== 0) { throw new Error(diff.stderr.trim() || "Failed to generate patch") }
        // C#: if (diff.Code != 0) { throw new Exception(diff.Stderr.Trim() ?? "...") }
        if (diff.Code != 0)
        {
          throw new Exception(diff.Stderr.Trim() ?? "Failed to generate patch");
        }

        // TypeScript: return diff.stdout
        // C#: return diff.Stdout
        return diff.Stdout;
      }
      finally
      {
        // TypeScript: await fs.rm(tmp, { recursive: true, force: true })
        // C#: if (Directory.Exists(tmp)) { Directory.Delete(tmp, true); }
        if (Directory.Exists(tmp))
        {
          Directory.Delete(tmp, true);
        }
      }
    }

    // ====================================================================
    // TypeScript: async revertFile(cwd: string, baseBranch: string, file: string, status?: "added" | "deleted" | "modified"): Promise<{ ok: boolean; message: string }> { ... }
    // C#: public async Task<(bool ok, string message)> RevertFile(string cwd, string baseBranch, string file, string? status = null) { ... }
    // ====================================================================
    public async Task<(bool ok, string message)> RevertFile(string cwd, string baseBranch, string file, string? status = null)
    {
      // TypeScript: if (nodePath.isAbsolute(file) || file.split(/[\\/]/).includes("..")) { return { ok: false, message: "Invalid file path" } }
      // C#: if (Path.IsPathRooted(file) || file.Split(...).Contains("..")) { return (false, "Invalid file path"); }
      if (Path.IsPathRooted(file) || file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains(".."))
      {
        return (false, "Invalid file path");
      }

      // TypeScript: const base = (await this.raw(["merge-base", "HEAD", baseBranch], cwd).catch(() => "")).trim()
      // C#: string baseCommit; try { ... } catch { baseCommit = ""; }
      string baseCommit;
      try
      {
        baseCommit = (await Raw(new[] { "merge-base", "HEAD", baseBranch }, cwd)).Trim();
      }
      catch
      {
        baseCommit = "";
      }

      // TypeScript: if (!base) { return { ok: false, message: "Could not resolve merge-base" } }
      // C#: if (string.IsNullOrEmpty(baseCommit)) { return (false, "Could not resolve merge-base"); }
      if (string.IsNullOrEmpty(baseCommit))
      {
        return (false, "Could not resolve merge-base");
      }

      // TypeScript: if (status === "added") { ... }
      // C#: if (status == "added") { ... }
      if (status == "added")
      {
        // TypeScript: const full = nodePath.resolve(cwd, file)
        // C#: var full = Path.GetFullPath(Path.Combine(cwd, file))
        var full = Path.GetFullPath(Path.Combine(cwd, file));

        // TypeScript: const root = await fs.realpath(cwd)
        // C#: var root = await GetRealPathAsync(cwd)
        var root = await GetRealPathAsync(cwd);

        // TypeScript: const resolved = await fs.realpath(full).catch(() => full)
        // C#: var resolved = await GetRealPathAsync(full).ContinueWith(t => t.IsFaulted ? full : t.Result)
        var resolved = await GetRealPathAsync(full).ContinueWith(t => t.IsFaulted ? full : t.Result);

        // TypeScript: if (resolved !== root && !resolved.startsWith(root + nodePath.sep)) { return { ok: false, message: "File path outside worktree" } }
        // C#: if (resolved != root && !resolved.StartsWith(root + Path.DirectorySeparatorChar)) { return (false, "File path outside worktree"); }
        if (resolved != root && !resolved.StartsWith(root + Path.DirectorySeparatorChar))
        {
          return (false, "File path outside worktree");
        }

        // TypeScript: await fs.rm(full, { force: true })
        // C#: if (File.Exists(full)) { File.Delete(full); }
        if (File.Exists(full))
        {
          File.Delete(full);
        }

        // TypeScript: await this.raw(["rm", "--cached", "--force", "--ignore-unmatch", "--", file], cwd).catch(() => "")
        // C#: try { await Raw(...); } catch { }
        try
        {
          await Raw(new[] { "rm", "--cached", "--force", "--ignore-unmatch", "--", file }, cwd);
        }
        catch
        {
        }

        // TypeScript: return { ok: true, message: "Removed added file" }
        // C#: return (true, "Removed added file")
        return (true, "Removed added file");
      }

      // TypeScript: const result = await this.exec(["checkout", base, "--", file], cwd)
      // C#: var execResult = await ExecAsync(...)
      var execResult = await ExecAsync(new[] { "checkout", baseCommit, "--", file }, cwd);

      // TypeScript: if (result.code !== 0) { return { ok: false, message: result.stderr.trim() || "Failed to revert file" } }
      // C#: if (execResult.Code != 0) { return (false, execResult.Stderr.Trim() ?? "Failed to revert file"); }
      if (execResult.Code != 0)
      {
        return (false, execResult.Stderr.Trim() ?? "Failed to revert file");
      }

      // TypeScript: if (status === "modified") { await this.raw(["reset", "HEAD", "--", file], cwd).catch(() => "") }
      // C#: if (status == "modified") { try { await Raw(...); } catch { } }
      if (status == "modified")
      {
        try
        {
          await Raw(new[] { "reset", "HEAD", "--", file }, cwd);
        }
        catch
        {
        }
      }

      // TypeScript: return { ok: true, message: "Reverted file to base" }
      // C#: return (true, "Reverted file to base")
      return (true, "Reverted file to base");
    }

    // ====================================================================
    // TypeScript: async checkApplyPatch(targetPath: string, patch: string): Promise<ApplyCheckResult> { ... }
    // C#: public async Task<ApplyCheckResult> CheckApplyPatch(string targetPath, string patch) { ... }
    // ====================================================================
    public async Task<ApplyCheckResult> CheckApplyPatch(string targetPath, string patch)
    {
      // TypeScript: if (!patch.trim()) { return { ok: true, conflicts: [], message: "No changes to apply" } }
      // C#: if (string.IsNullOrWhiteSpace(patch)) { return new ApplyCheckResult { Ok = true, Conflicts = new List<ApplyConflict>(), Message = "No changes to apply" }; }
      if (string.IsNullOrWhiteSpace(patch))
      {
        return new ApplyCheckResult
        {
          Ok = true,
          Conflicts = new List<ApplyConflict>(),
          Message = "No changes to apply"
        };
      }

      // TypeScript: const result = await this.exec(["apply", "--3way", "--check", "--whitespace=nowarn", "-"], targetPath, { stdin: patch })
      // C#: var result = await ExecAsync(..., new ExecOptions { Stdin = patch })
      var result = await ExecAsync(new[] { "apply", "--3way", "--check", "--whitespace=nowarn", "-" }, targetPath,
          new ExecOptions { Stdin = patch });

      // TypeScript: if (result.code === 0) { return { ok: true, conflicts: [], message: "Patch applies cleanly" } }
      // C#: if (result.Code == 0) { return new ApplyCheckResult { Ok = true, Conflicts = new List<ApplyConflict>(), Message = "Patch applies cleanly" }; }
      if (result.Code == 0)
      {
        return new ApplyCheckResult
        {
          Ok = true,
          Conflicts = new List<ApplyConflict>(),
          Message = "Patch applies cleanly"
        };
      }

      // TypeScript: const output = [result.stderr, result.stdout].filter(Boolean).join("\n")
      // C#: var output = string.Join("\n", new[] { result.Stderr, result.Stdout }.Where(s => !string.IsNullOrEmpty(s)))
      var output = string.Join("\n", new[] { result.Stderr, result.Stdout }.Where(s => !string.IsNullOrEmpty(s)));

      // TypeScript: const message = output.trim() || "Patch does not apply cleanly"
      // C#: var message = string.IsNullOrWhiteSpace(output) ? "Patch does not apply cleanly" : output.Trim()
      var message = string.IsNullOrWhiteSpace(output) ? "Patch does not apply cleanly" : output.Trim();

      // TypeScript: const conflicts = this.parseApplyConflicts(output)
      // C#: var conflicts = ParseApplyConflicts(output)
      var conflicts = ParseApplyConflicts(output);

      // TypeScript: return { ok: false, conflicts, message }
      // C#: return new ApplyCheckResult { Ok = false, Conflicts = conflicts, Message = message }
      return new ApplyCheckResult
      {
        Ok = false,
        Conflicts = conflicts,
        Message = message
      };
    }

    // ====================================================================
    // TypeScript: async applyPatch(targetPath: string, patch: string): Promise<ApplyPatchResult> { ... }
    // C#: public async Task<ApplyPatchResult> ApplyPatch(string targetPath, string patch) { ... }
    // ====================================================================
    public async Task<ApplyPatchResult> ApplyPatch(string targetPath, string patch)
    {
      // TypeScript: if (!patch.trim()) { return { ok: true, conflicts: [], message: "No changes to apply" } }
      // C#: if (string.IsNullOrWhiteSpace(patch)) { return new ApplyPatchResult { Ok = true, Conflicts = new List<ApplyConflict>(), Message = "No changes to apply" }; }
      if (string.IsNullOrWhiteSpace(patch))
      {
        return new ApplyPatchResult
        {
          Ok = true,
          Conflicts = new List<ApplyConflict>(),
          Message = "No changes to apply"
        };
      }

      // TypeScript: const result = await this.exec(["apply", "--3way", "--whitespace=nowarn", "-"], targetPath, { stdin: patch })
      // C#: var result = await ExecAsync(..., new ExecOptions { Stdin = patch })
      var result = await ExecAsync(new[] { "apply", "--3way", "--whitespace=nowarn", "-" }, targetPath,
          new ExecOptions { Stdin = patch });

      // TypeScript: if (result.code === 0) { return { ok: true, conflicts: [], message: "Patch applied" } }
      // C#: if (result.Code == 0) { return new ApplyPatchResult { Ok = true, Conflicts = new List<ApplyConflict>(), Message = "Patch applied" }; }
      if (result.Code == 0)
      {
        return new ApplyPatchResult
        {
          Ok = true,
          Conflicts = new List<ApplyConflict>(),
          Message = "Patch applied"
        };
      }

      // TypeScript: const output = [result.stderr, result.stdout].filter(Boolean).join("\n")
      // C#: var output = string.Join("\n", new[] { result.Stderr, result.Stdout }.Where(s => !string.IsNullOrEmpty(s)))
      var output = string.Join("\n", new[] { result.Stderr, result.Stdout }.Where(s => !string.IsNullOrEmpty(s)));

      // TypeScript: const message = output.trim() || "Failed to apply patch"
      // C#: var message = string.IsNullOrWhiteSpace(output) ? "Failed to apply patch" : output.Trim()
      var message = string.IsNullOrWhiteSpace(output) ? "Failed to apply patch" : output.Trim();

      // TypeScript: const conflicts = this.parseApplyConflicts(output)
      // C#: var conflicts = ParseApplyConflicts(output)
      var conflicts = ParseApplyConflicts(output);

      // TypeScript: return { ok: false, conflicts, message }
      // C#: return new ApplyPatchResult { Ok = false, Conflicts = conflicts, Message = message }
      return new ApplyPatchResult
      {
        Ok = false,
        Conflicts = conflicts,
        Message = message
      };
    }

    // ====================================================================
    // TypeScript: private parseApplyConflicts(output: string): ApplyConflict[] { ... }
    // C#: private List<ApplyConflict> ParseApplyConflicts(string output) { ... }
    // ====================================================================
    private List<ApplyConflict> ParseApplyConflicts(string output)
    {
      // TypeScript: const lines = output.split(/\r?\n/g).map((line) => line.trim()).filter(Boolean)
      // C#: var lines = output.Split('\r', '\n').Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)).ToArray()
      var lines = output.Split('\r', '\n')
          .Select(l => l.Trim())
          .Where(l => !string.IsNullOrEmpty(l))
          .ToArray();

      // TypeScript: const seen = new Set<string>()
      // C#: var seen = new HashSet<string>()
      var seen = new HashSet<string>();

      // TypeScript: const conflicts: ApplyConflict[] = []
      // C#: var conflicts = new List<ApplyConflict>()
      var conflicts = new List<ApplyConflict>();

      // TypeScript: for (const line of lines) { ... }
      // C#: foreach (var line in lines) { ... }
      foreach (var line in lines)
      {
        // TypeScript: const patchFailed = /^error:\s+patch failed:\s+(.+?):\d+$/i.exec(line)
        // C#: var patchFailed = Regex.Match(line, @"^error:\s+patch failed:\s+(.+?):\d+$", RegexOptions.IgnoreCase)
        var patchFailed = Regex.Match(line, @"^error:\s+patch failed:\s+(.+?):\d+$", RegexOptions.IgnoreCase);

        // TypeScript: if (patchFailed) { ... }
        // C#: if (patchFailed.Success) { ... }
        if (patchFailed.Success)
        {
          // TypeScript: const file = patchFailed[1]!
          // C#: var file = patchFailed.Groups[1].Value
          var file = patchFailed.Groups[1].Value;

          // TypeScript: const reason = "patch failed"
          // C#: var reason = "patch failed"
          var reason = "patch failed";

          // TypeScript: const key = `${file}:${reason}`
          // C#: var key = $"{file}:{reason}"
          var key = $"{file}:{reason}";

          // TypeScript: if (seen.has(key)) continue
          // C#: if (seen.Contains(key)) continue
          if (seen.Contains(key)) continue;

          // TypeScript: seen.add(key)
          // C#: seen.Add(key)
          seen.Add(key);

          // TypeScript: conflicts.push({ file, reason })
          // C#: conflicts.Add(new ApplyConflict { File = file, Reason = reason })
          conflicts.Add(new ApplyConflict { File = file, Reason = reason });

          // TypeScript: continue
          // C#: continue
          continue;
        }

        // TypeScript: const fileReason = /^error:\s+(.+?):\s+(does not match index|patch does not apply|cannot read the current contents.*)$/i.exec(line)
        // C#: var fileReason = Regex.Match(line, @"^error:\s+(.+?):\s+(does not match index|patch does not apply|cannot read the current contents.*)$", RegexOptions.IgnoreCase)
        var fileReason = Regex.Match(line, @"^error:\s+(.+?):\s+(does not match index|patch does not apply|cannot read the current contents.*)$", RegexOptions.IgnoreCase);

        // TypeScript: if (fileReason) { ... }
        // C#: if (fileReason.Success) { ... }
        if (fileReason.Success)
        {
          // TypeScript: const file = fileReason[1]!
          // C#: var file = fileReason.Groups[1].Value
          var file = fileReason.Groups[1].Value;

          // TypeScript: const reason = fileReason[2]!
          // C#: var reason = fileReason.Groups[2].Value
          var reason = fileReason.Groups[2].Value;

          // TypeScript: const key = `${file}:${reason}`
          // C#: var key = $"{file}:{reason}"
          var key = $"{file}:{reason}";

          // TypeScript: if (seen.has(key)) continue
          // C#: if (seen.Contains(key)) continue
          if (seen.Contains(key)) continue;

          // TypeScript: seen.add(key)
          // C#: seen.Add(key)
          seen.Add(key);

          // TypeScript: conflicts.push({ file, reason })
          // C#: conflicts.Add(new ApplyConflict { File = file, Reason = reason })
          conflicts.Add(new ApplyConflict { File = file, Reason = reason });

          // TypeScript: continue
          // C#: continue
          continue;
        }
      }

      // TypeScript: if (conflicts.length > 0) return conflicts
      // C#: if (conflicts.Count > 0) return conflicts
      if (conflicts.Count > 0) return conflicts;

      // TypeScript: const first = lines[0]
      // C#: if (lines.Length > 0) return new List<ApplyConflict> { new ApplyConflict { Reason = lines[0] } }
      if (lines.Length > 0) return new List<ApplyConflict> { new ApplyConflict { Reason = lines[0] } };

      // TypeScript: return [{ reason: "Patch does not apply cleanly" }]
      // C#: return new List<ApplyConflict> { new ApplyConflict { Reason = "Patch does not apply cleanly" } }
      return new List<ApplyConflict> { new ApplyConflict { Reason = "Patch does not apply cleanly" } };
    }

    // ====================================================================
    // TypeScript: execGit(args: string[], cwd: string, options?: { stdin?: string }): Promise<ExecResult> { ... }
    // C#: public Task<ExecResult> ExecGit(string[] args, string cwd, ExecOptions? options = null) { ... }
    // ====================================================================
    public Task<ExecResult> ExecGit(string[] args, string cwd, ExecOptions? options = null)
    {
      // TypeScript: return this.exec(args, cwd, options)
      // C#: return ExecAsync(args, cwd, options)
      return ExecAsync(args, cwd, options);
    }

    // ====================================================================
    // TypeScript: execGitBuffer(args: string[], cwd: string): Promise<ExecBufferResult> { ... }
    // C#: public Task<ExecBufferResult> ExecGitBuffer(string[] args, string cwd) { ... }
    // ====================================================================
    public Task<ExecBufferResult> ExecGitBuffer(string[] args, string cwd)
    {
      // TypeScript: return this.execBuffer(args, cwd)
      // C#: return ExecBufferAsync(args, cwd, null)
      return ExecBufferAsync(args, cwd, null);
    }

    // ====================================================================
    // TypeScript: private async exec(args: string[], cwd: string, options?: ExecOptions): Promise<ExecResult> { ... }
    // C#: private async Task<ExecResult> ExecAsync(string[] args, string cwd, ExecOptions? options = null) { ... }
    // ====================================================================
    private async Task<ExecResult> ExecAsync(string[] args, string cwd, ExecOptions? options = null)
    {
      // TypeScript: const result = await this.execBuffer(args, cwd, options)
      // C#: var result = await ExecBufferAsync(args, cwd, options)
      var result = await ExecBufferAsync(args, cwd, options);

      // TypeScript: return { code: result.code, stdout: result.stdout.toString("utf8"), stderr: result.stderr }
      // C#: return new ExecResult { Code = result.Code, Stdout = Encoding.UTF8.GetString(result.Stdout), Stderr = result.Stderr }
      return new ExecResult
      {
        Code = result.Code,
        Stdout = Encoding.UTF8.GetString(result.Stdout),
        Stderr = result.Stderr
      };
    }

    // ====================================================================
    // TypeScript: private execBuffer(args: string[], cwd: string, options?: ExecOptions): Promise<ExecBufferResult> { ... }
    // C#: private Task<ExecBufferResult> ExecBufferAsync(string[] args, string cwd, ExecOptions? options = null) { ... }
    // ====================================================================
    private Task<ExecBufferResult> ExecBufferAsync(string[] args, string cwd, ExecOptions? options = null)
    {
      // C#: Call the overloaded method with CancellationToken.None
      return ExecBufferAsync(args, cwd, options, CancellationToken.None);
    }

    // ====================================================================
    // TypeScript: private execBuffer(args: string[], cwd: string, options?: ExecOptions): Promise<ExecBufferResult> { ... }
    // C#: private Task<ExecBufferResult> ExecBufferAsync(string[] args, string cwd, ExecOptions? options, CancellationToken token) { ... }
    // ====================================================================
    private Task<ExecBufferResult> ExecBufferAsync(string[] args, string cwd, ExecOptions? options, CancellationToken token)
    {
      // TypeScript: if (this.controller.signal.aborted) { return Promise.resolve({ code: 1, stdout: Buffer.alloc(0), stderr: "GitOps disposed" }) }
      // C#: if (token.IsCancellationRequested || _controller.IsCancellationRequested) { return Task.FromResult(new ExecBufferResult { ... }); }
      if (token.IsCancellationRequested || _controller.IsCancellationRequested)
      {
        return Task.FromResult(new ExecBufferResult
        {
          Code = 1,
          Stdout = Array.Empty<byte>(),
          Stderr = "GitOps disposed"
        });
      }

      // TypeScript: const invoke = () => new Promise<ExecBufferResult>((resolve) => { ... })
      // C#: var tcs = new TaskCompletionSource<ExecBufferResult>()
      var tcs = new TaskCompletionSource<ExecBufferResult>();

      // TypeScript: const child = spawn("git", args, { cwd, env: options?.env, signal: this.controller.signal, stdio: ["pipe", "pipe", "pipe"] })
      // C#: var psi = new ProcessStartInfo { FileName = "git", Arguments = ..., WorkingDirectory = cwd, ... }
      var psi = new ProcessStartInfo
      {
        FileName = "git",
        Arguments = string.Join(" ", args.Select(a => $"\"{a}\"")),
        WorkingDirectory = cwd,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };

      // TypeScript: if (options?.env) { ... }
      // C#: if (options?.Env != null) { foreach (var kvp in options.Env) { psi.Environment[kvp.Key] = kvp.Value; } }
      if (options?.Env != null)
      {
        foreach (var kvp in options.Env)
        {
          psi.Environment[kvp.Key] = kvp.Value;
        }
      }

      // TypeScript: const out: Buffer[] = []
      // C#: var outBuffers = new List<byte>()
      var outBuffers = new List<byte>();

      // TypeScript: const err: Buffer[] = []
      // C#: var errBuffers = new List<byte>()
      var errBuffers = new List<byte>();

      // TypeScript: child.stdout?.on("data", (chunk: Buffer) => out.push(chunk))
      // C#: proc.OutputDataReceived += (s, e) => { if (e.Data != null) { outBuffers.AddRange(Encoding.UTF8.GetBytes(e.Data + "\n")); } }
      proc.OutputDataReceived += (s, e) =>
      {
        if (e.Data != null)
        {
          outBuffers.AddRange(Encoding.UTF8.GetBytes(e.Data + "\n"));
        }
      };

      // TypeScript: child.stderr?.on("data", (chunk: Buffer) => err.push(chunk))
      // C#: proc.ErrorDataReceived += (s, e) => { if (e.Data != null) { errBuffers.AddRange(Encoding.UTF8.GetBytes(e.Data + "\n")); } }
      proc.ErrorDataReceived += (s, e) =>
      {
        if (e.Data != null)
        {
          errBuffers.AddRange(Encoding.UTF8.GetBytes(e.Data + "\n"));
        }
      };

      // TypeScript: child.on("error", (error) => { resolve({ code: 1, stdout: Buffer.alloc(0), stderr: error.message }) })
      // C#: proc.EnableRaisingEvents = true; proc.Start(); proc.BeginOutputReadLine(); proc.BeginErrorReadLine();
      proc.EnableRaisingEvents = true;
      proc.Start();
      proc.BeginOutputReadLine();
      proc.BeginErrorReadLine();

      // TypeScript: if (options?.stdin !== undefined) { if (!child.stdin) { resolve({ ... }); return; } child.stdin.end(options.stdin) }
      // C#: if (options?.Stdin != null) { proc.StandardInput.Write(options.Stdin); proc.StandardInput.Close(); }
      if (options?.Stdin != null)
      {
        proc.StandardInput.Write(options.Stdin);
        proc.StandardInput.Close();
      }

      // TypeScript: child.on("close", (code) => { resolve({ code: code ?? 1, stdout: Buffer.concat(out), stderr: Buffer.concat(err).toString("utf8") }) })
      // C#: proc.WaitForExit(); tcs.SetResult(new ExecBufferResult { ... });
      proc.WaitForExit();

      tcs.SetResult(new ExecBufferResult
      {
        Code = proc.ExitCode,
        Stdout = outBuffers.ToArray(),
        Stderr = Encoding.UTF8.GetString(errBuffers.ToArray()).TrimEnd('\n', '\r')
      });

      // C#: proc.Dispose()
      proc.Dispose();

      // TypeScript: return this.semaphore ? this.semaphore.run(invoke) : invoke()
      // C#: Func<Task<ExecBufferResult>> invoke = () => tcs.Task; return _semaphore != null ? _semaphore.Run(invoke) : invoke()
      Func<Task<ExecBufferResult>> invoke = () => tcs.Task;
      return _semaphore != null ? _semaphore.Run(invoke) : invoke();
    }

    // ====================================================================
    // TypeScript: (fs.realpath equivalent)
    // C#: private static async Task<string> GetRealPathAsync(string path)
    // ====================================================================
    private static async Task<string> GetRealPathAsync(string path)
    {
      // TypeScript: await fs.realpath(path)
      // C#: return Path.GetFullPath(path) - simplified for .NET Framework
      return Path.GetFullPath(path);
    }

    // ====================================================================
    // TypeScript: (CacheEntry type for resolutionCache)
    // C#: private class CacheEntry { ... }
    // ====================================================================
    private class CacheEntry
    {
      // TypeScript: value: string
      // C#: public string Value
      public string Value { get; set; } = null!;

      // TypeScript: expires: number
      // C#: public long Expires
      public long Expires { get; set; }
    }

    // ====================================================================
    // TypeScript: (WorktreeEntry type for parseWorktreeList)
    // C#: private class WorktreeEntry { ... }
    // ====================================================================
    private class WorktreeEntry
    {
      // TypeScript: path: string
      // C#: public string Path
      public string Path { get; set; } = null!;

      // TypeScript: branch: string
      // C#: public string Branch
      public string Branch { get; set; } = null!;

      // TypeScript: bare: boolean
      // C#: public bool Bare
      public bool Bare { get; set; }

      // TypeScript: detached: boolean
      // C#: public bool Detached
      public bool Detached { get; set; }
    }

    // ====================================================================
    // TypeScript: (parseForEachRefOutput helper function)
    // C#: private static (HashSet<string> locals, HashSet<string> remotes, Dictionary<string, string> dates) ParseForEachRefOutput(string raw)
    // ====================================================================
    private static (HashSet<string> locals, HashSet<string> remotes, Dictionary<string, string> dates) ParseForEachRefOutput(string raw)
    {
      // TypeScript: const locals = new Set<string>()
      // C#: var locals = new HashSet<string>()
      var locals = new HashSet<string>();

      // TypeScript: const remotes = new Set<string>()
      // C#: var remotes = new HashSet<string>()
      var remotes = new HashSet<string>();

      // TypeScript: const dates = new Map<string, string>()
      // C#: var dates = new Dictionary<string, string>()
      var dates = new Dictionary<string, string>();

      // TypeScript: for (const line of raw.split("\n")) { ... }
      // C#: foreach (var line in raw.Split('\n')) { ... }
      foreach (var line in raw.Split('\n'))
      {
        // TypeScript: if (!line) continue
        // C#: if (string.IsNullOrEmpty(line)) continue
        if (string.IsNullOrEmpty(line)) continue;

        // TypeScript: const [ref, date] = line.split("\t")
        // C#: var parts = line.Split('\t'); var @ref = parts[0]; var date = parts.Length > 1 ? parts[1] : ""
        var parts = line.Split('\t');
        var @ref = parts[0];
        var date = parts.Length > 1 ? parts[1] : "";

        // TypeScript: if (ref.includes("HEAD")) continue
        // C#: if (@ref.Contains("HEAD")) continue
        if (@ref.Contains("HEAD")) continue;

        // TypeScript: if (ref.startsWith("refs/heads/")) { ... }
        // C#: if (@ref.StartsWith("refs/heads/")) { ... }
        if (@ref.StartsWith("refs/heads/"))
        {
          // TypeScript: const name = ref.slice(11)
          // C#: var name = @ref.Substring(11)
          var name = @ref.Substring(11);

          // TypeScript: locals.add(name)
          // C#: locals.Add(name)
          locals.Add(name);

          // TypeScript: if (date && !dates.has(name)) dates.set(name, date)
          // C#: if (!string.IsNullOrEmpty(date) && !dates.ContainsKey(name)) { dates[name] = date; }
          if (!string.IsNullOrEmpty(date) && !dates.ContainsKey(name))
          {
            dates[name] = date;
          }
        }
        // TypeScript: else if (ref.startsWith("refs/remotes/origin/")) { ... }
        // C#: else if (@ref.StartsWith("refs/remotes/origin/")) { ... }
        else if (@ref.StartsWith("refs/remotes/origin/"))
        {
          // TypeScript: const name = ref.slice(20)
          // C#: var name = @ref.Substring(20)
          var name = @ref.Substring(20);

          // TypeScript: remotes.add(name)
          // C#: remotes.Add(name)
          remotes.Add(name);

          // TypeScript: if (date && !dates.has(name)) dates.set(name, date)
          // C#: if (!string.IsNullOrEmpty(date) && !dates.ContainsKey(name)) { dates[name] = date; }
          if (!string.IsNullOrEmpty(date) && !dates.ContainsKey(name))
          {
            dates[name] = date;
          }
        }
      }

      // TypeScript: return { locals, remotes, dates }
      // C#: return (locals, remotes, dates)
      return (locals, remotes, dates);
    }

    // ====================================================================
    // TypeScript: (buildBranchList helper function)
    // C#: private static List<BranchListItem> BuildBranchList(HashSet<string> locals, HashSet<string> remotes, Dictionary<string, string> dates, string defaultBranch)
    // ====================================================================
    private static List<BranchListItem> BuildBranchList(HashSet<string> locals, HashSet<string> remotes, Dictionary<string, string> dates, string defaultBranch)
    {
      // TypeScript: const all = new Set([...locals, ...remotes])
      // C#: var all = new HashSet<string>(locals); foreach (var r in remotes) all.Add(r)
      var all = new HashSet<string>(locals);
      foreach (var r in remotes) all.Add(r);

      // TypeScript: const branches: BranchListItem[] = [...all].map((name) => ({ ... }))
      // C#: var branches = all.Select(name => new BranchListItem { ... }).ToList()
      var branches = all.Select(name => new BranchListItem
      {
        Name = name,
        IsLocal = locals.Contains(name),
        IsRemote = remotes.Contains(name),
        IsDefault = name == defaultBranch,
        LastCommitDate = dates.ContainsKey(name) ? dates[name] : null
      }).ToList();

      // TypeScript: branches.sort((a, b) => { ... })
      // C#: branches.Sort((a, b) => { ... })
      branches.Sort((a, b) =>
      {
        // TypeScript: if (a.isDefault && !b.isDefault) return -1
        // C#: if (a.IsDefault && !b.IsDefault) return -1
        if (a.IsDefault && !b.IsDefault) return -1;

        // TypeScript: if (!a.isDefault && b.isDefault) return 1
        // C#: if (!a.IsDefault && b.IsDefault) return 1
        if (!a.IsDefault && b.IsDefault) return 1;

        // TypeScript: if (a.lastCommitDate && b.lastCommitDate) return b.lastCommitDate.localeCompare(a.lastCommitDate)
        // C#: if (a.LastCommitDate != null && b.LastCommitDate != null) return b.LastCommitDate.CompareTo(a.LastCommitDate)
        if (a.LastCommitDate != null && b.LastCommitDate != null)
          return b.LastCommitDate.CompareTo(a.LastCommitDate);

        // TypeScript: return 0
        // C#: return 0
        return 0;
      });

      // TypeScript: return branches
      // C#: return branches
      return branches;
    }

    // ====================================================================
    // TypeScript: (parseWorktreeList helper function)
    // C#: private static List<WorktreeEntry> ParseWorktreeList(string raw)
    // ====================================================================
    private static List<WorktreeEntry> ParseWorktreeList(string raw)
    {
      // TypeScript: const entries: WorktreeEntry[] = []
      // C#: var entries = new List<WorktreeEntry>()
      var entries = new List<WorktreeEntry>();

      // TypeScript: for (const block of raw.split("\n\n")) { ... }
      // C#: foreach (var block in raw.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)) { ... }
      foreach (var block in raw.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries))
      {
        // TypeScript: const lines = block.split("\n")
        // C#: var lines = block.Split('\n')
        var lines = block.Split('\n');

        // TypeScript: const wtPath = lines.find((l) => l.startsWith("worktree "))?.slice(9)
        // C#: var wtPath = lines.FirstOrDefault(l => l.StartsWith("worktree "))?.Substring(9)
        var wtPath = lines.FirstOrDefault(l => l.StartsWith("worktree "))?.Substring(9);

        // TypeScript: if (!wtPath) continue
        // C#: if (wtPath == null) continue
        if (wtPath == null) continue;

        // TypeScript: const branchLine = lines.find((l) => l.startsWith("branch "))
        // C#: var branchLine = lines.FirstOrDefault(l => l.StartsWith("branch "))
        var branchLine = lines.FirstOrDefault(l => l.StartsWith("branch "));

        // TypeScript: const bare = lines.some((l) => l === "bare")
        // C#: var bare = lines.Any(l => l == "bare")
        var bare = lines.Any(l => l == "bare");

        // TypeScript: const detached = lines.some((l) => l === "detached")
        // C#: var detached = lines.Any(l => l == "detached")
        var detached = lines.Any(l => l == "detached");

        // TypeScript: const branch = branchLine ? branchLine.slice(7).replace("refs/heads/", "") : detached ? "(detached)" : "unknown"
        // C#: var branch = branchLine != null ? branchLine.Substring(7).Replace("refs/heads/", "") : detached ? "(detached)" : "unknown"
        var branch = branchLine != null
            ? branchLine.Substring(7).Replace("refs/heads/", "")
            : detached ? "(detached)" : "unknown";

        // TypeScript: entries.push({ path: wtPath, branch, bare, detached })
        // C#: entries.Add(new WorktreeEntry { Path = wtPath, Branch = branch, Bare = bare, Detached = detached })
        entries.Add(new WorktreeEntry { Path = wtPath, Branch = branch, Bare = bare, Detached = detached });
      }

      // TypeScript: return entries
      // C#: return entries
      return entries;
    }

    // ====================================================================
    // TypeScript: (normalizePath helper function)
    // C#: private static string NormalizePath(string p)
    // ====================================================================
    private static string NormalizePath(string p)
    {
      // TypeScript: const normalized = p.replace(/\\/g, "/").replace(/\/+$/, "")
      // C#: var normalized = p.Replace('\\', '/').TrimEnd('/')
      var normalized = p.Replace('\\', '/').TrimEnd('/');

      // TypeScript: if (/^[A-Za-z]:/.test(normalized)) return normalized.toLowerCase()
      // C#: if (Regex.IsMatch(normalized, @"^[A-Za-z]:")) { return normalized.ToLower(); }
      if (Regex.IsMatch(normalized, @"^[A-Za-z]:"))
      {
        return normalized.ToLower();
      }

      // TypeScript: return normalized
      // C#: return normalized
      return normalized;
    }

    // ====================================================================
    // TypeScript: (CancellationTokenCallback helper class for abort handling)
    // C#: private class CancellationTokenCallback { ... }
    // ====================================================================
    private class CancellationTokenCallback
    {
      private readonly Action _callback;
      public CancellationTokenCallback(Action callback) => _callback = callback;
      public void Invoke() => _callback();
    }

    // ====================================================================
    // TypeScript: (Registration helper class for CancellationToken registration)
    // C#: private class Registration : IDisposable { ... }
    // ====================================================================
    private class Registration : IDisposable
    {
      private CancellationTokenRegistration _reg;
      public Registration(CancellationTokenRegistration reg) => _reg = reg;
      public void Dispose() => _reg.Dispose();
      public static implicit operator CancellationTokenRegistration(Registration r) => r._reg;
    }
  }

  // ========================================================================
  // TypeScript: import type { Semaphore } from "./semaphore"
  // C#: public class Semaphore (inline implementation)
  // ========================================================================
  public class Semaphore
  {
    // TypeScript: private running = 0
    // C#: private int _running = 0
    private int _running = 0;

    // TypeScript: private readonly pending: (() => void)[] = []
    // C#: private readonly Queue<Action> _pending = new()
    private readonly Queue<Action> _pending = new();

    // TypeScript: (no explicit lock in TypeScript, but implied by queue operations)
    // C#: private readonly object _lock = new()
    private readonly object _lock = new();

    // TypeScript: constructor(private readonly limit: number) {}
    // C#: public Semaphore(int limit) => _limit = limit
    public Semaphore(int limit) => _limit = limit;

    // TypeScript: (limit stored as private readonly)
    // C#: private readonly int _limit
    private readonly int _limit;

    // ====================================================================
    // TypeScript: async run<T>(fn: () => Promise<T>): Promise<T> { ... }
    // C#: public async Task<T> Run<T>(Func<Task<T>> fn) { ... }
    // ====================================================================
    public async Task<T> Run<T>(Func<Task<T>> fn)
    {
      // TypeScript: await this.acquire()
      // C#: await Acquire()
      await Acquire();

      try
      {
        // TypeScript: return await fn()
        // C#: return await fn()
        return await fn();
      }
      finally
      {
        // TypeScript: this.release()
        // C#: Release()
        Release();
      }
    }

    // ====================================================================
    // TypeScript: private acquire(): Promise<void> { ... }
    // C#: private Task Acquire() { ... }
    // ====================================================================
    private Task Acquire()
    {
      // TypeScript: (no explicit lock in TypeScript)
      // C#: lock (_lock) { ... }
      lock (_lock)
      {
        // TypeScript: if (this.running < this.limit) { this.running++; return Promise.resolve() }
        // C#: if (_running < _limit) { _running++; return Task.CompletedTask; }
        if (_running < _limit)
        {
          _running++;
          return Task.CompletedTask;
        }
      }

      // TypeScript: return new Promise<void>((resolve) => { this.pending.push(() => { this.running++; resolve() }) })
      // C#: var tcs = new TaskCompletionSource<object>(); lock (_lock) { _pending.Enqueue(() => { _running++; tcs.SetResult(null!); }); } return tcs.Task
      var tcs = new TaskCompletionSource<object>();
      lock (_lock)
      {
        _pending.Enqueue(() =>
        {
          _running++;
          tcs.SetResult(null!);
        });
      }
      return tcs.Task;
    }

    // ====================================================================
    // TypeScript: private release(): void { ... }
    // C#: private void Release() { ... }
    // ====================================================================
    private void Release()
    {
      // TypeScript: (no explicit lock in TypeScript)
      // C#: lock (_lock) { ... }
      lock (_lock)
      {
        // TypeScript: this.running--
        // C#: _running--
        _running--;

        // TypeScript: const next = this.pending.shift()
        // C#: var next = _pending.Dequeue()
        var next = _pending.Dequeue();

        // TypeScript: if (next) next()
        // C#: if (_pending.Count > 0) { next(); }
        if (_pending.Count > 0)
        {
          next();
        }
      }
    }
  }
}
