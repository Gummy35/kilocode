// ============================================================================ 
// GitOps.cs - C# Port of TypeScript GitOps 
// Source: packages/kilo-vscode/src/agent-manager/GitOps.ts 
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
using System.IO;

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
  // interface GitOpsOptions { ... } 
  public class GitOpsOptions
  {
    // log: (...args: unknown[]) => void 
    public Action Log { get; set; } = null!;


    // runGit?: (args: string[], cwd: string) => Promise<string>
    public Func<string[], string, Task<string>>? RunGit { get; set; }

    // semaphore?: Semaphore
    public Semaphore? Semaphore { get; set; }
  }

  // export interface ApplyConflict { ... }
  public class ApplyConflict
  {
    // file?: string
    [JsonProperty("file")]
    public string? File { get; set; }

    // reason: string
    [JsonProperty("reason")]
    public string Reason { get; set; } = null!;
  }

  // interface ApplyCheckResult { ... }
  public class ApplyCheckResult
  {
    // ok: boolean
    [JsonProperty("ok")]
    public bool Ok { get; set; }

    // conflicts: ApplyConflict[]
    [JsonProperty("conflicts")]
    public List<ApplyConflict> Conflicts { get; set; } = new();

    // message: string
    [JsonProperty("message")]
    public string Message { get; set; } = null!;
  }

  // interface ApplyPatchResult { ... }
  public class ApplyPatchResult
  {
    // ok: boolean
    [JsonProperty("ok")]
    public bool Ok { get; set; }

    // conflicts: ApplyConflict[]
    [JsonProperty("conflicts")]
    public List<ApplyConflict> Conflicts { get; set; } = new();

    // message: string
    [JsonProperty("message")]
    public string Message { get; set; } = null!;
  }

  // interface ExecOptions { ... }
  public class ExecOptions
  {
    // env?: NodeJS.ProcessEnv
    public Dictionary<string, string>? Env { get; set; }

    // stdin?: string
    public string? Stdin { get; set; }
  }

  // export interface ExecResult { ... }
  public class ExecResult
  {
    // code: number
    [JsonProperty("code")]
    public int Code { get; set; }

    // stdout: string
    [JsonProperty("stdout")]
    public string Stdout { get; set; } = null!;

    // stderr: string
    [JsonProperty("stderr")]
    public string Stderr { get; set; } = null!;
  }

  // export interface ExecBufferResult { ... }
  public class ExecBufferResult
  {
    // code: number
    [JsonProperty("code")]
    public int Code { get; set; }

    // stdout: Buffer
    [JsonProperty("stdout")]
    public byte[] Stdout { get; set; } = Array.Empty<byte>();

    // stderr: string
    [JsonProperty("stderr")]
    public string Stderr { get; set; } = null!;
  }

  // type BranchListItem = { ... }
  public class BranchListItem
  {
    // name: string
    public string Name { get; set; } = null!;

    // isLocal: boolean
    public bool IsLocal { get; set; }

    // isRemote: boolean
    public bool IsRemote { get; set; }

    // isDefault: boolean
    public bool IsDefault { get; set; }

    // lastCommitDate?: string
    public string? LastCommitDate { get; set; }

    // isCheckedOut?: boolean
    public bool IsCheckedOut { get; set; }
  }

  // export class GitOps { ... }
  public class GitOps : IDisposable
  {
    // private readonly log: (...args: unknown[]) => void
    private readonly Action<string> _log;

    // private readonly runGit: (args: string[], cwd: string) => Promise<string>
    private readonly Func<string[], string, Task<string>> _runGit;

    // private readonly controller = new AbortController()
    private readonly CancellationTokenSource _controller = new();

    // private readonly semaphore: Semaphore | undefined
    private readonly Semaphore? _semaphore;

    // private readonly resolutionCache = new Map<string, { value: string; expires: number }>()
    private readonly ConcurrentDictionary<string, CacheEntry> _resolutionCache = new();

    // private static readonly CACHE_TTL_MS = 60000
    private static readonly long CacheTtlMs = 60000;

    // private static readonly MAX_CACHE_SIZE = 100
    private static readonly int MaxCacheSize = 100;

    // get disposed(): boolean { return this.controller.signal.aborted }
    public bool Disposed => _controller.IsCancellationRequested;

    // constructor(options: GitOpsOptions) { ... }
    public GitOps(GitOpsOptions options)
    {
      // this.log = options.log
      _log = options.Log;

      // this.semaphore = options.semaphore
      _semaphore = options.Semaphore;

      // this.runGit = options.runGit ?? ((args, cwd) => simpleGit(...).raw(args).then((out) => out.trim()))
      _runGit = options.RunGit ?? DefaultRunGit;
    }

    // (default runGit implementation using simpleGit)
    private Task<string> DefaultRunGit(string[] args, string cwd)
    {
      // simpleGit(cwd, { abort: this.controller.signal }).raw(args).then((out) => out.trim())
      var cts = CancellationTokenSource.CreateLinkedTokenSource(_controller.Token);
      return ExecBufferAsync(args, cwd, null, cts.Token).ContinueWith(t =>
          Encoding.UTF8.GetString(t.Result.Stdout).Trim(), TaskContinuationOptions.None);
    }

    // dispose(): void { ... }
    public void Dispose()
    {
      // if (!this.controller.signal.aborted) { this.controller.abort() }
      if (!_controller.IsCancellationRequested)
      {
        _controller.Cancel();
      }

      // this.resolutionCache.clear()
      _resolutionCache.Clear();

      _controller.Dispose();
    }

    // private getCached(key: string): string | undefined { ... }
    private string? GetCached(string key)
    {
      // const entry = this.resolutionCache.get(key)
      if (_resolutionCache.TryGetValue(key, out var entry) &&
          // entry && entry.expires > Date.now()
          entry.Expires > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
      {
        // return entry.value
        return entry.Value;
      }

      // return undefined
      return null;
    }

    // private setCached(key: string, value: string): void { ... }
    private void SetCached(string key, string value)
    {
      // if (this.resolutionCache.size >= GitOps.MAX_CACHE_SIZE) { ... }
      if (_resolutionCache.Count >= MaxCacheSize)
      {
        // let oldestKey: string | undefined
        string? oldestKey = null;

        // let oldestExpiry = Infinity
        long oldestExpiry = long.MaxValue;

        // for (const [k, v] of this.resolutionCache) { ... }
        foreach (var kvp in _resolutionCache)
        {
          // if (v.expires < oldestExpiry) { ... }
          if (kvp.Value.Expires < oldestExpiry)
          {
            // oldestExpiry = v.expires
            oldestExpiry = kvp.Value.Expires;

            // oldestKey = k
            oldestKey = kvp.Key;
          }
        }

        // if (oldestKey) this.resolutionCache.delete(oldestKey)
        if (oldestKey != null)
        {
          _resolutionCache.TryRemove(oldestKey, out _);
        }
      }

      // this.resolutionCache.set(key, { value, expires: Date.now() + GitOps.CACHE_TTL_MS })
      _resolutionCache[key] = new CacheEntry
      {
        Value = value,
        Expires = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + CacheTtlMs
      };
    }

    // private raw(args: string[], cwd: string): Promise<string> { ... }
    private Task<string> Raw(string[] args, string cwd)
    {
      // const signal = this.controller.signal
      var signal = _controller.Token;

      // if (signal.aborted) return Promise.reject(new Error("GitOps disposed"))
      if (signal.IsCancellationRequested) return Task.FromException<string>(new Exception("GitOps disposed"));

      // const invoke = () => new Promise<string>((resolve, reject) => { ... })
      var invoke = () =>
      {
        // new Promise<string>((resolve, reject) => { ... })
        var tcs = new TaskCompletionSource<string>();

        // const onAbort = () => reject(new Error("GitOps disposed"))
        Registration? onAbortReg = null;
        var onAbort = new Action(() =>
        {
          tcs.TrySetException(new Exception("GitOps disposed"));
          if (onAbortReg.HasValue) onAbortReg.Value.Dispose();
        });

        // signal.addEventListener("abort", onAbort, { once: true })
        onAbortReg = signal.Register(onAbort);

        // this.runGit(args, cwd).then((value) => { ... }, (err) => { ... })
        var runTask = _runGit(args, cwd);
        runTask.ContinueWith(t =>
        {
          // signal.removeEventListener("abort", onAbort)
          if (onAbortReg.HasValue) onAbortReg.Value.Dispose();

          // if (t.IsFaulted) { reject(err) } else { resolve(value) }
          if (t.IsFaulted)
          {
            tcs.TrySetException(t.Exception!.InnerException ?? t.Exception);
          }
          else
          {
            tcs.TrySetResult(t.Result);
          }
        }, TaskContinuationOptions.None);

        // return the promise
        return tcs.Task;
      };

      // return this.semaphore ? this.semaphore.run(invoke) : invoke()
      return _semaphore != null ? _semaphore.Run(invoke) : invoke();
    }

    // async currentBranch(cwd: string): Promise<string> { ... }
    public async Task<string> CurrentBranch(string cwd)
    {
      try
      {
        // return this.raw(["rev-parse", "--abbrev-ref", "HEAD"], cwd).catch(() => "")
        return await Raw(new[] { "rev-parse", "--abbrev-ref", "HEAD" }, cwd);
      }
      catch
      {
        return "";
      }
    }

    // async resolveRemote(cwd: string, branch?: string): Promise<string> { ... }
    public async Task<string> ResolveRemote(string cwd, string? branch = null)
    {
      // const cacheKey = `remote:${cwd}:${branch}`
      var cacheKey = $"remote:{cwd}:{branch}";

      // const cached = this.getCached(cacheKey)
      var cached = GetCached(cacheKey);

      // if (cached) return cached
      if (cached != null) return cached;

      // const upstream = await this.raw(["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"], cwd).catch(() => "")
      string upstream;
      try
      {
        upstream = await Raw(new[] { "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}" }, cwd);
      }
      catch
      {
        upstream = "";
      }

      // if (upstream.includes("/")) { ... }
      if (upstream.Contains("/"))
      {
        // const result = upstream.split("/")[0]
        var result = upstream.Split('/')[0];

        // this.setCached(cacheKey, result)
        SetCached(cacheKey, result);

        // return result
        return result;
      }

      // const name = branch || (await this.raw(["branch", "--show-current"], cwd).catch(() => ""))
      var name = branch ?? await CurrentBranch(cwd);

      // if (name) { ... }
      if (!string.IsNullOrEmpty(name))
      {
        // const configured = await this.raw(["config", `branch.${name}.remote`], cwd).catch(() => "")
        string configured;
        try
        {
          configured = await Raw(new[] { "config", $"branch.{name}.remote" }, cwd);
        }
        catch
        {
          configured = "";
        }

        // if (configured) { ... }
        if (!string.IsNullOrEmpty(configured))
        {
          // this.setCached(cacheKey, configured)
          SetCached(cacheKey, configured);

          // return configured
          return configured;
        }
      }

      // const result = "origin"
      var result = "origin";

      // this.setCached(cacheKey, result)
      SetCached(cacheKey, result);

      // return result
      return result;
    }

    // async resolveTrackingBranch(cwd: string, branch: string): Promise<string | undefined> { ... }
    public async Task<string?> ResolveTrackingBranch(string cwd, string branch)
    {
      // const cacheKey = `tracking:${cwd}:${branch}`
      var cacheKey = $"tracking:{cwd}:{branch}";

      // const cached = this.getCached(cacheKey)
      var cached = GetCached(cacheKey);

      // if (cached !== undefined) return cached === "" ? undefined : cached
      if (cached != null) return cached == "" ? null : cached;

      // const upstream = await this.raw(["rev-parse", "--abbrev-ref", "@{upstream}"], cwd).catch(() => "")
      string upstream;
      try
      {
        upstream = await Raw(new[] { "rev-parse", "--abbrev-ref", "@{upstream}" }, cwd);
      }
      catch
      {
        upstream = "";
      }

      // if (upstream) { ... }
      if (!string.IsNullOrEmpty(upstream))
      {
        // this.setCached(cacheKey, upstream)
        SetCached(cacheKey, upstream);

        // return upstream
        return upstream;
      }

      // const remote = await this.resolveRemote(cwd, branch)
      var remote = await ResolveRemote(cwd, branch);

      // const ref = `${remote}/${branch}`
      var @ref = $"{remote}/{branch}";

      // const resolved = await this.raw(["rev-parse", "--verify", ref], cwd).catch(() => "")
      string resolved;
      try
      {
        resolved = await Raw(new[] { "rev-parse", "--verify", @ref }, cwd);
      }
      catch
      {
        resolved = "";
      }

      // if (resolved) { ... }
      if (!string.IsNullOrEmpty(resolved))
      {
        // this.setCached(cacheKey, ref)
        SetCached(cacheKey, @ref);

        // return ref
        return @ref;
      }

      // this.setCached(cacheKey, "")
      SetCached(cacheKey, "");

      // return undefined
      return null;
    }

    // async resolveDefaultBranch(cwd: string, branch?: string): Promise<string | undefined> { ... }
    public async Task<string?> ResolveDefaultBranch(string cwd, string? branch = null)
    {
      // const remote = await this.resolveRemote(cwd, branch)
      var remote = await ResolveRemote(cwd, branch);

      // const cacheKey = `default-branch:${cwd}:${remote}`
      var cacheKey = $"default-branch:{cwd}:{remote}";

      // const cached = this.getCached(cacheKey)
      var cached = GetCached(cacheKey);

      // if (cached !== undefined) return cached === "" ? undefined : cached
      if (cached != null) return cached == "" ? null : cached;

      // const head = await this.raw(["symbolic-ref", "--short", `refs/remotes/${remote}/HEAD`], cwd).catch(() => "")
      string head;
      try
      {
        head = await Raw(new[] { "symbolic-ref", "--short", $"refs/remotes/{remote}/HEAD" }, cwd);
      }
      catch
      {
        head = "";
      }

      // const result = head || undefined
      var result = string.IsNullOrEmpty(head) ? null : head;

      // this.setCached(cacheKey, result ?? "")
      SetCached(cacheKey, result ?? "");

      // return result
      return result;
    }

    // async hasRemoteRef(cwd: string, ref: string): Promise<boolean> { ... }
    public async Task<bool> HasRemoteRef(string cwd, string @ref)
    {
      try
      {
        // return this.raw(["rev-parse", "--verify", "--quiet", `refs/remotes/${ref}`], cwd).then(() => true).catch(() => false)
        await Raw(new[] { "rev-parse", "--verify", "--quiet", $"refs/remotes/{@ref}" }, cwd);
        return true;
      }
      catch
      {
        return false;
      }
    }

    // async listBranches(cwd: string): Promise<{ branches: BranchListItem[]; defaultBranch: string }> { ... }
    public async Task<(List<BranchListItem> branches, string defaultBranch)> ListBranches(string cwd)
    {
      // const def = (await this.resolveDefaultBranch(cwd)) ?? ""
      var def = await ResolveDefaultBranch(cwd) ?? "";

      // const raw = await this.raw([...], cwd).catch((err) => { ... })
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
        // this.log("listBranches: for-each-ref failed", err instanceof Error ? err.message : String(err))
        _log($"listBranches: for-each-ref failed: {err.Message}");
        raw = "";
      }

      // const { locals, remotes, dates } = parseForEachRefOutput(raw)
      var (locals, remotes, dates) = ParseForEachRefOutput(raw);

      // return { branches: buildBranchList(locals, remotes, dates, def), defaultBranch: def }
      var branches = BuildBranchList(locals, remotes, dates, def);
      return (branches, def);
    }

    // async listWorktreePaths(cwd: string): Promise<Map<string, string>> { ... }
    public async Task<Dictionary<string, string>> ListWorktreePaths(string cwd)
    {
      // const raw = await this.raw(["worktree", "list", "--porcelain"], cwd)
      var raw = await Raw(new[] { "worktree", "list", "--porcelain" }, cwd);

      // const result = new Map<string, string>()
      var result = new Dictionary<string, string>();

      // for (const entry of parseWorktreeList(raw)) { ... }
      foreach (var entry in ParseWorktreeList(raw))
      {
        // if (entry.bare) continue
        if (entry.Bare) continue;

        // result.set(normalizePath(entry.path), entry.branch)
        result[NormalizePath(entry.Path)] = entry.Branch;
      }

      // return result
      return result;
    }

    // async workingTreeStats(cwd: string): Promise<{ files: number; additions: number; deletions: number }> { ... }
    public async Task<(int files, int additions, int deletions)> WorkingTreeStats(string cwd)
    {
      // const [numstat, untracked] = await Promise.all([ ... ])
      var numstatTask = Raw(new[] { "diff", "HEAD", "--numstat" }, cwd).ContinueWith(t =>
          t.IsFaulted ? "" : t.Result, TaskContinuationOptions.None);
      var untrackedTask = Raw(new[] { "ls-files", "--others", "--exclude-standard" }, cwd).ContinueWith(t =>
          t.IsFaulted ? "" : t.Result, TaskContinuationOptions.None);

      await Task.WhenAll(numstatTask, untrackedTask);
      var numstat = numstatTask.Result;
      var untracked = untrackedTask.Result;

      // const tracked = numstat ? numstat.split("\n").reduce(...) : { files: 0, additions: 0, deletions: 0 }
      var tracked = string.IsNullOrEmpty(numstat)
          ? (files: 0, additions: 0, deletions: 0)
          : numstat.Split('\n').Aggregate(
              (files: 0, additions: 0, deletions: 0),
              (acc, line) =>
              {
                // if (!line.trim()) return acc
                if (string.IsNullOrWhiteSpace(line)) return acc;

                // const parts = line.split("\t")
                var parts = line.Split('\t');

                // return { files: acc.files + 1, additions: ..., deletions: ... }
                return (
                        files: acc.files + 1,
                        additions: acc.additions + (parts[0] != "-" ? int.Parse(parts[0]) : 0),
                        deletions: acc.deletions + (parts[1] != "-" ? int.Parse(parts[1]) : 0)
                    );
              });

      // if (!untracked) return tracked
      if (string.IsNullOrEmpty(untracked)) return tracked;

      // const paths = untracked.split("\n").filter((line) => line.trim())
      var paths = untracked.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();

      // const counts = await Promise.all(paths.map(async (p) => { ... }))
      var counts = await Task.WhenAll(paths.Select(async p =>
      {
        try
        {
          // const full = nodePath.resolve(cwd, p)
          var full = Path.GetFullPath(Path.Combine(cwd, p));

          // const stat = await fs.stat(full)
          var stat = new FileInfo(full);

          // if (stat.size > 1_000_000) return 0
          if (stat.Length > 1_000_000) return 0;

          // const content = await fs.readFile(full, "utf-8")
          var content = await File.ReadAllTextAsync(full);

          // return content.split("\n").length
          return content.Split('\n').Length;
        }
        catch (Exception err)
        {
          // this.log(`Failed to read untracked file ${p}:`, err)
          _log($"Failed to read untracked file {p}: {err.Message}");
          return 0;
        }
      }));

      // return { files: tracked.files + paths.length, additions: ..., deletions: tracked.deletions }
      return (
          files: tracked.files + paths.Length,
          additions: tracked.additions + counts.Sum(),
          deletions: tracked.deletions
      );
    }

    // async aheadBehind(cwd: string, base: string): Promise<{ ahead: number; behind: number }> { ... }
    public async Task<(int ahead, int behind)> AheadBehind(string cwd, string @base)
    {
      // return this.parseLeftRight(cwd, base)
      return await ParseLeftRight(cwd, @base);
    }

    // private async parseLeftRight(cwd: string, ref: string): Promise<{ ahead: number; behind: number }> { ... }
    private async Task<(int ahead, int behind)> ParseLeftRight(string cwd, string @ref)
    {
      // const out = await this.raw(["rev-list", "--left-right", "--count", `${ref}...HEAD`], cwd).catch(() => "0\t0")
      string outStr;
      try
      {
        outStr = await Raw(new[] { "rev-list", "--left-right", "--count", $"{@ref}...HEAD" }, cwd);
      }
      catch
      {
        outStr = "0\t0";
      }

      // const [behind, ahead] = out.split(/\s+/).map((s) => parseInt(s, 10) || 0)
      var parts = outStr.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
      var behind = parts.Length > 0 ? int.Parse(parts[0]) : 0;
      var ahead = parts.Length > 1 ? int.Parse(parts[1]) : 0;

      // return { ahead, behind }
      return (ahead, behind);
    }

    // async buildWorktreePatch(sourcePath: string, baseBranch: string, selectedFiles?: string[]): Promise<string> { ... }
    public async Task<string> BuildWorktreePatch(string sourcePath, string baseBranch, string[]? selectedFiles = null)
    {
      // const tmp = await fs.mkdtemp(nodePath.join(os.tmpdir(), "kilo-apply-"))
      var tmp = Path.Combine(Path.GetTempPath(), $"kilo-apply-{Guid.NewGuid():N}");

      // await fs.mkdir(tmp) - implied by mkdtemp
      Directory.CreateDirectory(tmp);

      // const index = nodePath.join(tmp, "index")
      var index = Path.Combine(tmp, "index");

      // const env = { ...process.env, GIT_INDEX_FILE: index }
      var env = new Dictionary<string, string>(Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
          .ToDictionary(kvp => kvp.Key.ToString()!, kvp => kvp.Value!.ToString()!))
      {
        ["GIT_INDEX_FILE"] = index
      };

      // const files = (selectedFiles ?? []).map((file) => file.trim()).filter((file) => file.length > 0 && !nodePath.isAbsolute(file) && !file.split(/[\\/]/).includes(".."))
      var files = (selectedFiles ?? Array.Empty<string>())
          .Select(f => f.Trim())
          .Where(f => f.Length > 0 && !Path.IsPathRooted(f) && !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains(".."))
          .ToArray();

      // const pathspec = files.length > 0 ? files : ["."]
      var pathspec = files.Length > 0 ? files : new[] { "." };

      try
      {
        // const base = (await this.raw(["merge-base", "HEAD", baseBranch], sourcePath)).trim()
        string baseCommit;
        try
        {
          baseCommit = (await Raw(new[] { "merge-base", "HEAD", baseBranch }, sourcePath)).Trim();
        }
        catch
        {
          baseCommit = "";
        }

        // const baseTree = (await this.raw(["rev-parse", `${base}^{tree}`], sourcePath)).trim()
        string baseTree;
        try
        {
          baseTree = (await Raw(new[] { "rev-parse", $"{baseCommit}^{{tree}}" }, sourcePath)).Trim();
        }
        catch
        {
          baseTree = "";
        }

        // const read = await this.exec(["read-tree", "HEAD"], sourcePath, { env })
        var read = await ExecAsync(new[] { "read-tree", "HEAD" }, sourcePath, new ExecOptions { Env = env });

        // if (read.code !== 0) { throw new Error(read.stderr.trim() || "Failed to initialize temporary index") }
        if (read.Code != 0)
        {
          throw new Exception(read.Stderr.Trim() ?? "Failed to initialize temporary index");
        }

        // const add = await this.exec(["add", "-A", "--", ...pathspec], sourcePath, { env })
        var addArgs = new List<string> { "add", "-A", "--" };
        addArgs.AddRange(pathspec);
        var add = await ExecAsync(addArgs.ToArray(), sourcePath, new ExecOptions { Env = env });

        // if (add.code !== 0) { throw new Error(add.stderr.trim() || "Failed to stage worktree snapshot") }
        if (add.Code != 0)
        {
          throw new Exception(add.Stderr.Trim() ?? "Failed to stage worktree snapshot");
        }

        // const treeResult = await this.exec(["write-tree"], sourcePath, { env })
        var treeResult = await ExecAsync(new[] { "write-tree" }, sourcePath, new ExecOptions { Env = env });

        // if (treeResult.code !== 0) { throw new Error(treeResult.stderr.trim() || "Failed to snapshot worktree index") }
        if (treeResult.Code != 0)
        {
          throw new Exception(treeResult.Stderr.Trim() ?? "Failed to snapshot worktree index");
        }

        // const tree = treeResult.stdout.trim()
        var tree = treeResult.Stdout.Trim();

        // const diff = await this.exec(["diff", "--binary", "--full-index", "--find-renames", "--no-color", baseTree, tree], sourcePath)
        var diff = await ExecAsync(new[] { "diff", "--binary", "--full-index", "--find-renames", "--no-color", baseTree, tree }, sourcePath);

        // if (diff.code !== 0) { throw new Error(diff.stderr.trim() || "Failed to generate patch") }
        if (diff.Code != 0)
        {
          throw new Exception(diff.Stderr.Trim() ?? "Failed to generate patch");
        }

        // return diff.stdout
        return diff.Stdout;
      }
      finally
      {
        // await fs.rm(tmp, { recursive: true, force: true })
        if (Directory.Exists(tmp))
        {
          Directory.Delete(tmp, true);
        }
      }
    }

    // async revertFile(cwd: string, baseBranch: string, file: string, status?: "added" | "deleted" | "modified"): Promise<{ ok: boolean; message: string }> { ... }
    public async Task<(bool ok, string message)> RevertFile(string cwd, string baseBranch, string file, string? status = null)
    {
      // if (nodePath.isAbsolute(file) || file.split(/[\\/]/).includes("..")) { return { ok: false, message: "Invalid file path" } }
      if (Path.IsPathRooted(file) || file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains(".."))
      {
        return (false, "Invalid file path");
      }

      // const base = (await this.raw(["merge-base", "HEAD", baseBranch], cwd).catch(() => "")).trim()
      string baseCommit;
      try
      {
        baseCommit = (await Raw(new[] { "merge-base", "HEAD", baseBranch }, cwd)).Trim();
      }
      catch
      {
        baseCommit = "";
      }

      // if (!base) { return { ok: false, message: "Could not resolve merge-base" } }
      if (string.IsNullOrEmpty(baseCommit))
      {
        return (false, "Could not resolve merge-base");
      }

      // if (status === "added") { ... }
      if (status == "added")
      {
        // const full = nodePath.resolve(cwd, file)
        var full = Path.GetFullPath(Path.Combine(cwd, file));

        // const root = await fs.realpath(cwd)
        var root = await GetRealPathAsync(cwd);

        // const resolved = await fs.realpath(full).catch(() => full)
        var resolved = await GetRealPathAsync(full).ContinueWith(t => t.IsFaulted ? full : t.Result);

        // if (resolved !== root && !resolved.startsWith(root + nodePath.sep)) { return { ok: false, message: "File path outside worktree" } }
        if (resolved != root && !resolved.StartsWith(root + Path.DirectorySeparatorChar))
        {
          return (false, "File path outside worktree");
        }

        // await fs.rm(full, { force: true })
        if (File.Exists(full))
        {
          File.Delete(full);
        }

        // await this.raw(["rm", "--cached", "--force", "--ignore-unmatch", "--", file], cwd).catch(() => "")
        try
        {
          await Raw(new[] { "rm", "--cached", "--force", "--ignore-unmatch", "--", file }, cwd);
        }
        catch
        {
        }

        // return { ok: true, message: "Removed added file" }
        return (true, "Removed added file");
      }

      // const result = await this.exec(["checkout", base, "--", file], cwd)
      var execResult = await ExecAsync(new[] { "checkout", baseCommit, "--", file }, cwd);

      // if (result.code !== 0) { return { ok: false, message: result.stderr.trim() || "Failed to revert file" } }
      if (execResult.Code != 0)
      {
        return (false, execResult.Stderr.Trim() ?? "Failed to revert file");
      }

      // if (status === "modified") { await this.raw(["reset", "HEAD", "--", file], cwd).catch(() => "") }
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

      // return { ok: true, message: "Reverted file to base" }
      return (true, "Reverted file to base");
    }

    // async checkApplyPatch(targetPath: string, patch: string): Promise<ApplyCheckResult> { ... }
    public async Task<ApplyCheckResult> CheckApplyPatch(string targetPath, string patch)
    {
      // if (!patch.trim()) { return { ok: true, conflicts: [], message: "No changes to apply" } }
      if (string.IsNullOrWhiteSpace(patch))
      {
        return new ApplyCheckResult
        {
          Ok = true,
          Conflicts = new List<ApplyConflict>(),
          Message = "No changes to apply"
        };
      }

      // const result = await this.exec(["apply", "--3way", "--check", "--whitespace=nowarn", "-"], targetPath, { stdin: patch })
      var result = await ExecAsync(new[] { "apply", "--3way", "--check", "--whitespace=nowarn", "-" }, targetPath,
          new ExecOptions { Stdin = patch });

      // if (result.code === 0) { return { ok: true, conflicts: [], message: "Patch applies cleanly" } }
      if (result.Code == 0)
      {
        return new ApplyCheckResult
        {
          Ok = true,
          Conflicts = new List<ApplyConflict>(),
          Message = "Patch applies cleanly"
        };
      }

      // const output = [result.stderr, result.stdout].filter(Boolean).join("\n")
      var output = string.Join("\n", new[] { result.Stderr, result.Stdout }.Where(s => !string.IsNullOrEmpty(s)));

      // const message = output.trim() || "Patch does not apply cleanly"
      var message = string.IsNullOrWhiteSpace(output) ? "Patch does not apply cleanly" : output.Trim();

      // const conflicts = this.parseApplyConflicts(output)
      var conflicts = ParseApplyConflicts(output);

      // return { ok: false, conflicts, message }
      return new ApplyCheckResult
      {
        Ok = false,
        Conflicts = conflicts,
        Message = message
      };
    }

    // async applyPatch(targetPath: string, patch: string): Promise<ApplyPatchResult> { ... }
    public async Task<ApplyPatchResult> ApplyPatch(string targetPath, string patch)
    {
      // if (!patch.trim()) { return { ok: true, conflicts: [], message: "No changes to apply" } }
      if (string.IsNullOrWhiteSpace(patch))
      {
        return new ApplyPatchResult
        {
          Ok = true,
          Conflicts = new List<ApplyConflict>(),
          Message = "No changes to apply"
        };
      }

      // const result = await this.exec(["apply", "--3way", "--whitespace=nowarn", "-"], targetPath, { stdin: patch })
      var result = await ExecAsync(new[] { "apply", "--3way", "--whitespace=nowarn", "-" }, targetPath,
          new ExecOptions { Stdin = patch });

      // if (result.code === 0) { return { ok: true, conflicts: [], message: "Patch applied" } }
      if (result.Code == 0)
      {
        return new ApplyPatchResult
        {
          Ok = true,
          Conflicts = new List<ApplyConflict>(),
          Message = "Patch applied"
        };
      }

      // const output = [result.stderr, result.stdout].filter(Boolean).join("\n")
      var output = string.Join("\n", new[] { result.Stderr, result.Stdout }.Where(s => !string.IsNullOrEmpty(s)));

      // const message = output.trim() || "Failed to apply patch"
      var message = string.IsNullOrWhiteSpace(output) ? "Failed to apply patch" : output.Trim();

      // const conflicts = this.parseApplyConflicts(output)
      var conflicts = ParseApplyConflicts(output);

      // return { ok: false, conflicts, message }
      return new ApplyPatchResult
      {
        Ok = false,
        Conflicts = conflicts,
        Message = message
      };
    }

    // private parseApplyConflicts(output: string): ApplyConflict[] { ... }
    private List<ApplyConflict> ParseApplyConflicts(string output)
    {
      // const lines = output.split(/\r?\n/g).map((line) => line.trim()).filter(Boolean)
      var lines = output.Split('\r', '\n')
          .Select(l => l.Trim())
          .Where(l => !string.IsNullOrEmpty(l))
          .ToArray();

      // const seen = new Set<string>()
      var seen = new HashSet<string>();

      // const conflicts: ApplyConflict[] = []
      var conflicts = new List<ApplyConflict>();

      // for (const line of lines) { ... }
      foreach (var line in lines)
      {
        // const patchFailed = /^error:\s+patch failed:\s+(.+?):\d+$/i.exec(line)
        var patchFailed = Regex.Match(line, @"^error:\s+patch failed:\s+(.+?):\d+$", RegexOptions.IgnoreCase);

        // if (patchFailed) { ... }
        if (patchFailed.Success)
        {
          // const file = patchFailed[1]!
          var file = patchFailed.Groups[1].Value;

          // const reason = "patch failed"
          var reason = "patch failed";

          // const key = `${file}:${reason}`
          var key = $"{file}:{reason}";

          // if (seen.has(key)) continue
          if (seen.Contains(key)) continue;

          // seen.add(key)
          seen.Add(key);

          // conflicts.push({ file, reason })
          conflicts.Add(new ApplyConflict { File = file, Reason = reason });

          // continue
          continue;
        }

        // const fileReason = /^error:\s+(.+?):\s+(does not match index|patch does not apply|cannot read the current contents.*)$/i.exec(line)
        var fileReason = Regex.Match(line, @"^error:\s+(.+?):\s+(does not match index|patch does not apply|cannot read the current contents.*)$", RegexOptions.IgnoreCase);

        // if (fileReason) { ... }
        if (fileReason.Success)
        {
          // const file = fileReason[1]!
          var file = fileReason.Groups[1].Value;

          // const reason = fileReason[2]!
          var reason = fileReason.Groups[2].Value;

          // const key = `${file}:${reason}`
          var key = $"{file}:{reason}";

          // if (seen.has(key)) continue
          if (seen.Contains(key)) continue;

          // seen.add(key)
          seen.Add(key);

          // conflicts.push({ file, reason })
          conflicts.Add(new ApplyConflict { File = file, Reason = reason });

          // continue
          continue;
        }
      }

      // if (conflicts.length > 0) return conflicts
      if (conflicts.Count > 0) return conflicts;

      // const first = lines[0]
      if (lines.Length > 0) return new List<ApplyConflict> { new ApplyConflict { Reason = lines[0] } };

      // return [{ reason: "Patch does not apply cleanly" }]
      return new List<ApplyConflict> { new ApplyConflict { Reason = "Patch does not apply cleanly" } };
    }

    // execGit(args: string[], cwd: string, options?: { stdin?: string }): Promise<ExecResult> { ... }
    public Task<ExecResult> ExecGit(string[] args, string cwd, ExecOptions? options = null)
    {
      // return this.exec(args, cwd, options)
      return ExecAsync(args, cwd, options);
    }

    // execGitBuffer(args: string[], cwd: string): Promise<ExecBufferResult> { ... }
    public Task<ExecBufferResult> ExecGitBuffer(string[] args, string cwd)
    {
      // return this.execBuffer(args, cwd)
      return ExecBufferAsync(args, cwd, null);
    }

    // private async exec(args: string[], cwd: string, options?: ExecOptions): Promise<ExecResult> { ... }
    private async Task<ExecResult> ExecAsync(string[] args, string cwd, ExecOptions? options = null)
    {
      // const result = await this.execBuffer(args, cwd, options)
      var result = await ExecBufferAsync(args, cwd, options);

      // return { code: result.code, stdout: result.stdout.toString("utf8"), stderr: result.stderr }
      return new ExecResult
      {
        Code = result.Code,
        Stdout = Encoding.UTF8.GetString(result.Stdout),
        Stderr = result.Stderr
      };
    }

    // private execBuffer(args: string[], cwd: string, options?: ExecOptions): Promise<ExecBufferResult> { ... }
    private Task<ExecBufferResult> ExecBufferAsync(string[] args, string cwd, ExecOptions? options = null)
    {
      return ExecBufferAsync(args, cwd, options, CancellationToken.None);
    }

    // private execBuffer(args: string[], cwd: string, options?: ExecOptions): Promise<ExecBufferResult> { ... }
    private Task<ExecBufferResult> ExecBufferAsync(string[] args, string cwd, ExecOptions? options, CancellationToken token)
    {
      // if (this.controller.signal.aborted) { return Promise.resolve({ code: 1, stdout: Buffer.alloc(0), stderr: "GitOps disposed" }) }
      if (token.IsCancellationRequested || _controller.IsCancellationRequested)
      {
        return Task.FromResult(new ExecBufferResult
        {
          Code = 1,
          Stdout = Array.Empty<byte>(),
          Stderr = "GitOps disposed"
        });
      }

      // const invoke = () => new Promise<ExecBufferResult>((resolve) => { ... })
      var tcs = new TaskCompletionSource<ExecBufferResult>();

      // const child = spawn("git", args, { cwd, env: options?.env, signal: this.controller.signal, stdio: ["pipe", "pipe", "pipe"] })
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

      // if (options?.env) { ... }
      if (options?.Env != null)
      {
        foreach (var kvp in options.Env)
        {
          psi.Environment[kvp.Key] = kvp.Value;
        }
      }

      // const out: Buffer[] = []
      var outBuffers = new List<byte>();

      // const err: Buffer[] = []
      var errBuffers = new List<byte>();

      // child.stdout?.on("data", (chunk: Buffer) => out.push(chunk))
      proc.OutputDataReceived += (s, e) =>
      {
        if (e.Data != null)
        {
          outBuffers.AddRange(Encoding.UTF8.GetBytes(e.Data + "\n"));
        }
      };

      // child.stderr?.on("data", (chunk: Buffer) => err.push(chunk))
      proc.ErrorDataReceived += (s, e) =>
      {
        if (e.Data != null)
        {
          errBuffers.AddRange(Encoding.UTF8.GetBytes(e.Data + "\n"));
        }
      };

      // child.on("error", (error) => { resolve({ code: 1, stdout: Buffer.alloc(0), stderr: error.message }) })
      proc.EnableRaisingEvents = true;
      proc.Start();
      proc.BeginOutputReadLine();
      proc.BeginErrorReadLine();

      // if (options?.stdin !== undefined) { if (!child.stdin) { resolve({ ... }); return; } child.stdin.end(options.stdin) }
      if (options?.Stdin != null)
      {
        proc.StandardInput.Write(options.Stdin);
        proc.StandardInput.Close();
      }

      // child.on("close", (code) => { resolve({ code: code ?? 1, stdout: Buffer.concat(out), stderr: Buffer.concat(err).toString("utf8") }) })
      proc.WaitForExit();

      tcs.SetResult(new ExecBufferResult
      {
        Code = proc.ExitCode,
        Stdout = outBuffers.ToArray(),
        Stderr = Encoding.UTF8.GetString(errBuffers.ToArray()).TrimEnd('\n', '\r')
      });

      proc.Dispose();

      // return this.semaphore ? this.semaphore.run(invoke) : invoke()
      Func<Task<ExecBufferResult>> invoke = () => tcs.Task;
      return _semaphore != null ? _semaphore.Run(invoke) : invoke();
    }

    // (fs.realpath equivalent)
    private static async Task<string> GetRealPathAsync(string path)
    {
      // await fs.realpath(path)
      return Path.GetFullPath(path);
    }

    // (CacheEntry type for resolutionCache)
    private class CacheEntry
    {
      // value: string
      public string Value { get; set; } = null!;

      // expires: number
      public long Expires { get; set; }
    }

    // (WorktreeEntry type for parseWorktreeList)
    private class WorktreeEntry
    {
      // path: string
      public string Path { get; set; } = null!;

      // branch: string
      public string Branch { get; set; } = null!;

      // bare: boolean
      public bool Bare { get; set; }

      // detached: boolean
      public bool Detached { get; set; }
    }

    // (parseForEachRefOutput helper function)
    private static (HashSet<string> locals, HashSet<string> remotes, Dictionary<string, string> dates) ParseForEachRefOutput(string raw)
    {
      // const locals = new Set<string>()
      var locals = new HashSet<string>();

      // const remotes = new Set<string>()
      var remotes = new HashSet<string>();

      // const dates = new Map<string, string>()
      var dates = new Dictionary<string, string>();

      // for (const line of raw.split("\n")) { ... }
      foreach (var line in raw.Split('\n'))
      {
        // if (!line) continue
        if (string.IsNullOrEmpty(line)) continue;

        // const [ref, date] = line.split("\t")
        var parts = line.Split('\t');
        var @ref = parts[0];
        var date = parts.Length > 1 ? parts[1] : "";

        // if (ref.includes("HEAD")) continue
        if (@ref.Contains("HEAD")) continue;

        // if (ref.startsWith("refs/heads/")) { ... }
        if (@ref.StartsWith("refs/heads/"))
        {
          // const name = ref.slice(11)
          var name = @ref.Substring(11);

          // locals.add(name)
          locals.Add(name);

          // if (date && !dates.has(name)) dates.set(name, date)
          if (!string.IsNullOrEmpty(date) && !dates.ContainsKey(name))
          {
            dates[name] = date;
          }
        }
        // else if (ref.startsWith("refs/remotes/origin/")) { ... }
        else if (@ref.StartsWith("refs/remotes/origin/"))
        {
          // const name = ref.slice(20)
          var name = @ref.Substring(20);

          // remotes.add(name)
          remotes.Add(name);

          // if (date && !dates.has(name)) dates.set(name, date)
          if (!string.IsNullOrEmpty(date) && !dates.ContainsKey(name))
          {
            dates[name] = date;
          }
        }
      }

      // return { locals, remotes, dates }
      return (locals, remotes, dates);
    }

    // (buildBranchList helper function)
    private static List<BranchListItem> BuildBranchList(HashSet<string> locals, HashSet<string> remotes, Dictionary<string, string> dates, string defaultBranch)
    {
      // const all = new Set([...locals, ...remotes])
      var all = new HashSet<string>(locals);
      foreach (var r in remotes) all.Add(r);

      // const branches: BranchListItem[] = [...all].map((name) => ({ ... }))
      var branches = all.Select(name => new BranchListItem
      {
        Name = name,
        IsLocal = locals.Contains(name),
        IsRemote = remotes.Contains(name),
        IsDefault = name == defaultBranch,
        LastCommitDate = dates.ContainsKey(name) ? dates[name] : null
      }).ToList();

      // branches.sort((a, b) => { ... })
      branches.Sort((a, b) =>
      {
        // if (a.isDefault && !b.isDefault) return -1
        if (a.IsDefault && !b.IsDefault) return -1;

        // if (!a.isDefault && b.isDefault) return 1
        if (!a.IsDefault && b.IsDefault) return 1;

        // if (a.lastCommitDate && b.lastCommitDate) return b.lastCommitDate.localeCompare(a.lastCommitDate)
        if (a.LastCommitDate != null && b.LastCommitDate != null)
          return b.LastCommitDate.CompareTo(a.LastCommitDate);

        // return 0
        return 0;
      });

      // return branches
      return branches;
    }

    // (parseWorktreeList helper function)
    private static List<WorktreeEntry> ParseWorktreeList(string raw)
    {
      // const entries: WorktreeEntry[] = []
      var entries = new List<WorktreeEntry>();

      // for (const block of raw.split("\n\n")) { ... }
      foreach (var block in raw.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries))
      {
        // const lines = block.split("\n")
        var lines = block.Split('\n');

        // const wtPath = lines.find((l) => l.startsWith("worktree "))?.slice(9)
        var wtPath = lines.FirstOrDefault(l => l.StartsWith("worktree "))?.Substring(9);

        // if (!wtPath) continue
        if (wtPath == null) continue;

        // const branchLine = lines.find((l) => l.startsWith("branch "))
        var branchLine = lines.FirstOrDefault(l => l.StartsWith("branch "));

        // const bare = lines.some((l) => l === "bare")
        var bare = lines.Any(l => l == "bare");

        // const detached = lines.some((l) => l === "detached")
        var detached = lines.Any(l => l == "detached");

        // const branch = branchLine ? branchLine.slice(7).replace("refs/heads/", "") : detached ? "(detached)" : "unknown"
        var branch = branchLine != null
            ? branchLine.Substring(7).Replace("refs/heads/", "")
            : detached ? "(detached)" : "unknown";

        // entries.push({ path: wtPath, branch, bare, detached })
        entries.Add(new WorktreeEntry { Path = wtPath, Branch = branch, Bare = bare, Detached = detached });
      }

      // return entries
      return entries;
    }

    // (normalizePath helper function)
    private static string NormalizePath(string p)
    {
      // const normalized = p.replace(/\\/g, "/").replace(/\/+$/, "")
      var normalized = p.Replace('\\', '/').TrimEnd('/');

      // if (/^[A-Za-z]:/.test(normalized)) return normalized.toLowerCase()
      if (Regex.IsMatch(normalized, @"^[A-Za-z]:"))
      {
        return normalized.ToLower();
      }

      // return normalized
      return normalized;
    }

    // (CancellationTokenCallback helper class for abort handling)
    private class CancellationTokenCallback
    {
      private readonly Action _callback;
      public CancellationTokenCallback(Action callback) => _callback = callback;
      public void Invoke() => _callback();
    }

    // (Registration helper class for CancellationToken registration)
    private class Registration : IDisposable
    {
      private CancellationTokenRegistration _reg;
      public Registration(CancellationTokenRegistration reg) => _reg = reg;
      public void Dispose() => _reg.Dispose();
      public static implicit operator CancellationTokenRegistration(Registration r) => r._reg;
    }
  }

  // import type { Semaphore } from "./semaphore"
  public class Semaphore
  {
    // private running = 0
    private int _running = 0;

    // private readonly pending: (() => void)[] = []
    private readonly Queue<Action> _pending = new();

    // (no explicit lock in TypeScript, but implied by queue operations)
    private readonly object _lock = new();

    // constructor(private readonly limit: number) {}
    public Semaphore(int limit) => _limit = limit;

    // (limit stored as private readonly)
    private readonly int _limit;

    // async run<T>(fn: () => Promise<T>): Promise<T> { ... }
    public async Task<T> Run<T>(Func<Task<T>> fn)
    {
      // await this.acquire()
      await Acquire();

      try
      {
        // return await fn()
        return await fn();
      }
      finally
      {
        // this.release()
        Release();
      }
    }

    // private acquire(): Promise<void> { ... }
    private Task Acquire()
    {
      // (no explicit lock in TypeScript)
      lock (_lock)
      {
        // if (this.running < this.limit) { this.running++; return Promise.resolve() }
        if (_running < _limit)
        {
          _running++;
          return Task.CompletedTask;
        }
      }

      // return new Promise<void>((resolve) => { this.pending.push(() => { this.running++; resolve() }) })
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

    // private release(): void { ... }
    private void Release()
    {
      // (no explicit lock in TypeScript)
      lock (_lock)
      {
        // this.running--
        _running--;

        // const next = this.pending.shift()
        var next = _pending.Dequeue();

        // if (next) next()
        if (_pending.Count > 0)
        {
          next();
        }
      }
    }
  }
}
