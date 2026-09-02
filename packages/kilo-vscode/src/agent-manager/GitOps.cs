using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace KiloExtensionDTOs.AgentManager;

/// <summary>
/// Options for GitOps initialization.
/// </summary>
public class GitOpsOptions
{
    /// <summary>
    /// Logging action.
    /// </summary>
    public Action<object?> Log { get; set; } = _ => { };

    /// <summary>
    /// Semaphore for concurrency control.
    /// </summary>
    public ISemaphore? Semaphore { get; set; }

    /// <summary>
    /// Git command runner (optional, defaults to simple git execution).
    /// </summary>
    public Func<string[], string, Task<string>>? RunGit { get; set; }
}

/// <summary>
/// Interface for semaphore used to limit concurrent git operations.
/// </summary>
public interface ISemaphore
{
    Task<T> Run<T>(Func<Task<T>> action);
}

/// <summary>
/// Result of a git command execution.
/// </summary>
public class ExecResult
{
    public int Code { get; set; }
    public string Stdout { get; set; } = string.Empty;
    public string Stderr { get; set; } = string.Empty;
}

/// <summary>
/// Result of a git command execution with buffer output.
/// </summary>
public class ExecBufferResult
{
    public int Code { get; set; }
    public byte[] Stdout { get; set; } = Array.Empty<byte>();
    public string Stderr { get; set; } = string.Empty;
}

/// <summary>
/// Result of applying a patch check.
/// </summary>
public class ApplyCheckResult
{
    public bool Ok { get; set; }
    public List<ApplyConflict> Conflicts { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Result of applying a patch.
/// </summary>
public class ApplyPatchResult
{
    public bool Ok { get; set; }
    public List<ApplyConflict> Conflicts { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Represents a conflict when applying a patch.
/// </summary>
public class ApplyConflict
{
    public string? File { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Branch list item with metadata.
/// </summary>
public class BranchListItem
{
    public string Name { get; set; } = string.Empty;
    public DateTime LastCommitDate { get; set; }
    public bool IsRemote { get; set; }
    public bool IsDefault { get; set; }
}

/// <summary>
/// Git operations helper with caching and concurrency control.
/// </summary>
public class GitOps : IDisposable
{
    private readonly Action<object?> _log;
    private readonly Func<string[], string, Task<string>> _runGit;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly ISemaphore? _semaphore;
    private readonly ConcurrentDictionary<string, CacheEntry> _resolutionCache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(1);
    private static readonly int MaxCacheSize = 100;

    /// <summary>
    /// Gets a value indicating whether this instance has been disposed.
    /// </summary>
    public bool Disposed => _cancellationTokenSource.IsCancellationRequested;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitOps"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public GitOps(GitOpsOptions options)
    {
        _log = options.Log;
        _semaphore = options.Semaphore;
        _runGit = options.RunGit ?? DefaultRunGit;
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    public void Dispose()
    {
        if (!_cancellationTokenSource.IsCancellationRequested)
        {
            _cancellationTokenSource.Cancel();
        }
        _resolutionCache.Clear();
        _cancellationTokenSource.Dispose();
    }

    private string? GetCached(string key)
    {
        if (_resolutionCache.TryGetValue(key, out var entry) && entry.Expires > DateTimeOffset.UtcNow)
        {
            return entry.Value;
        }
        return null;
    }

    private void SetCached(string key, string value)
    {
        // Evict oldest entry if cache is full
        if (_resolutionCache.Count >= MaxCacheSize)
        {
            var oldest = _resolutionCache.OrderBy(x => x.Value.Expires).FirstOrDefault();
            if (oldest.Key != null)
            {
                _resolutionCache.TryRemove(oldest.Key, out _);
            }
        }

        _resolutionCache[key] = new CacheEntry
        {
            Value = value,
            Expires = DateTimeOffset.UtcNow + CacheTtl
        };
    }

    private async Task<string> Raw(string[] args, string cwd)
    {
        var cancellationToken = _cancellationTokenSource.Token;
        if (cancellationToken.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(GitOps), "GitOps disposed");
        }

        var invoke = async () =>
        {
            try
            {
                return await _runGit(args, cwd);
            }
            catch (OperationCanceledException)
            {
                throw new ObjectDisposedException(nameof(GitOps), "GitOps disposed");
            }
        };

        return _semaphore != null ? await _semaphore.Run(invoke) : await invoke();
    }

    private async Task<ExecResult> Exec(string[] args, string cwd, ExecOptions? options = null)
    {
        var result = await ExecBuffer(args, cwd, options);
        return new ExecResult
        {
            Code = result.Code,
            Stdout = System.Text.Encoding.UTF8.GetString(result.Stdout),
            Stderr = result.Stderr
        };
    }

    private async Task<ExecBufferResult> ExecBuffer(string[] args, string cwd, ExecOptions? options = null)
    {
        if (_cancellationTokenSource.IsCancellationRequested)
        {
            return new ExecBufferResult
            {
                Code = 1,
                Stdout = Array.Empty<byte>(),
                Stderr = "GitOps disposed"
            };
        }

        var invoke = async () =>
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = string.Join(" ", args.Select(a => $"\"{a}\"")),
                WorkingDirectory = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (options?.Env != null)
            {
                foreach (var kvp in options.Env)
                {
                    startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
                }
            }

            using var process = new Process { StartInfo = startInfo };

            if (options?.Stdin != null)
            {
                process.Start();
                await process.StandardInput.WriteAsync(options.Stdin);
                process.StandardInput.Close();
            }
            else
            {
                process.Start();
            }

            var stdout = new List<byte>();
            var stderr = new List<byte>();

            var stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(stdout);
            var stderrTask = process.StandardError.BaseStream.CopyToAsync(stderr);

            await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync());

            return new ExecBufferResult
            {
                Code = process.ExitCode,
                Stdout = stdout.ToArray(),
                Stderr = System.Text.Encoding.UTF8.GetString(stderr.ToArray())
            };
        };

        return _semaphore != null ? await _semaphore.Run(invoke) : await invoke();
    }

    private async Task<string> DefaultRunGit(string[] args, string cwd)
    {
        var result = await Exec(args, cwd);
        if (result.Code != 0)
        {
            throw new Exception($"Git command failed: {result.Stderr}");
        }
        return result.Stdout.Trim();
    }

    /// <summary>
    /// Return the name of the currently checked-out branch, or "HEAD" if detached.
    /// </summary>
    public async Task<string> CurrentBranch(string cwd)
    {
        try
        {
            return await Raw(new[] { "rev-parse", "--abbrev-ref", "HEAD" }, cwd);
        }
        catch
        {
            return "HEAD";
        }
    }

    /// <summary>
    /// Resolve the remote name for a branch.
    /// </summary>
    public async Task<string> ResolveRemote(string cwd, string? branch = null)
    {
        var cacheKey = $"remote:{cwd}:{branch}";
        var cached = GetCached(cacheKey);
        if (cached != null)
        {
            return cached;
        }

        string upstream;
        try
        {
            upstream = await Raw(new[] { "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}" }, cwd);
        }
        catch
        {
            upstream = string.Empty;
        }

        if (upstream.Contains("/"))
        {
            var result = upstream.Split('/')[0];
            SetCached(cacheKey, result);
            return result;
        }

        var name = branch ?? await CurrentBranch(cwd);
        if (!string.IsNullOrEmpty(name))
        {
            string configured;
            try
            {
                configured = await Raw(new[] { "config", $"branch.{name}.remote" }, cwd);
            }
            catch
            {
                configured = string.Empty;
            }

            if (!string.IsNullOrEmpty(configured))
            {
                SetCached(cacheKey, configured);
                return configured;
            }
        }

        var result = "origin";
        SetCached(cacheKey, result);
        return result;
    }

    /// <summary>
    /// Resolve the upstream tracking ref for branch, or null if none is set.
    /// </summary>
    public async Task<string?> ResolveTrackingBranch(string cwd, string branch)
    {
        var cacheKey = $"tracking:{cwd}:{branch}";
        var cached = GetCached(cacheKey);
        if (cached != null)
        {
            return cached == string.Empty ? null : cached;
        }

        string upstream;
        try
        {
            upstream = await Raw(new[] { "rev-parse", "--abbrev-ref", "@{upstream}" }, cwd);
        }
        catch
        {
            upstream = string.Empty;
        }

        if (!string.IsNullOrEmpty(upstream))
        {
            SetCached(cacheKey, upstream);
            return upstream;
        }

        var remote = await ResolveRemote(cwd, branch);
        var @ref = $"{remote}/{branch}";
        string resolved;
        try
        {
            resolved = await Raw(new[] { "rev-parse", "--verify", @ref }, cwd);
        }
        catch
        {
            resolved = string.Empty;
        }

        if (!string.IsNullOrEmpty(resolved))
        {
            SetCached(cacheKey, @ref);
            return @ref;
        }

        SetCached(cacheKey, string.Empty);
        return null;
    }

    /// <summary>
    /// Resolve the repo's default branch via &lt;remote&gt;/HEAD.
    /// </summary>
    public async Task<string?> ResolveDefaultBranch(string cwd, string? branch = null)
    {
        var remote = await ResolveRemote(cwd, branch);
        var cacheKey = $"default-branch:{cwd}:{remote}";
        var cached = GetCached(cacheKey);
        if (cached != null)
        {
            return cached == string.Empty ? null : cached;
        }

        string head;
        try
        {
            head = await Raw(new[] { "symbolic-ref", "--short", $"refs/remotes/{remote}/HEAD" }, cwd);
        }
        catch
        {
            head = string.Empty;
        }

        var result = string.IsNullOrEmpty(head) ? null : head;
        SetCached(cacheKey, result ?? string.Empty);
        return result;
    }

    /// <summary>
    /// Check if a remote ref exists.
    /// </summary>
    public async Task<bool> HasRemoteRef(string cwd, string @ref)
    {
        try
        {
            await Raw(new[] { "rev-parse", "--verify", "--quiet", $"refs/remotes/{@ref}" }, cwd);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// List local branches and origin/* remotes sorted by last commit date.
    /// </summary>
    public async Task<(List<BranchListItem> Branches, string DefaultBranch)> ListBranches(string cwd)
    {
        var def = await ResolveDefaultBranch(cwd) ?? string.Empty;

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
        catch (Exception ex)
        {
            _log($"listBranches: for-each-ref failed: {ex.Message}");
            raw = string.Empty;
        }

        var (locals, remotes, dates) = ParseForEachRefOutput(raw);
        var branches = BuildBranchList(locals, remotes, dates, def);
        return (branches, def);
    }

    /// <summary>
    /// Return the set of worktree paths for the repo, excluding bare entries.
    /// </summary>
    public async Task<Dictionary<string, string>> ListWorktreePaths(string cwd)
    {
        var raw = await Raw(new[] { "worktree", "list", "--porcelain" }, cwd);
        var result = new Dictionary<string, string>();

        foreach (var entry in ParseWorktreeList(raw))
        {
            if (entry.IsBare)
            {
                continue;
            }
            result[N