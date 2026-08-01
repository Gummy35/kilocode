using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.AgentManager
{
    /// <summary>
    /// Result of a git worktree operation.
    /// </summary>
    public class GitResult
    {
        public bool Success { get; set; }
        public string Output { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public int ExitCode { get; set; }
    }

    /// <summary>
    /// Represents a git worktree.
    /// </summary>
    public class GitWorktree
    {
        public string Path { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public bool IsCurrent { get; set; }
        public bool IsLocked { get; set; }
    }

    /// <summary>
    /// Git service for managing worktrees and git operations.
    /// Wraps git CLI commands using System.Diagnostics.Process.
    /// Matches VS Code's GitOps pattern.
    /// </summary>
    public class GitService : IDisposable
    {
        private readonly string _workspaceRoot;
        private readonly object _lock = new object();
        private bool _disposed;

        public GitService(string workspaceRoot)
        {
            _workspaceRoot = workspaceRoot;
        }

        /// <summary>
        /// Execute a git command.
        /// </summary>
        private async Task<GitResult> ExecuteGitAsync(string arguments, CancellationToken cancellationToken = default)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = _workspaceRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            using var process = new Process { StartInfo = startInfo };
            
            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    lock (_lock)
                    {
                        outputBuilder.AppendLine(e.Data);
                    }
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    lock (_lock)
                    {
                        errorBuilder.AppendLine(e.Data);
                    }
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait for process to exit (with cancellation support)
            var tcs = new TaskCompletionSource<bool>();
            process.EnableRaisingEvents = true;
            process.Exited += (sender, e) => tcs.TrySetResult(true);
            
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                cts.Token.Register(() => tcs.TrySetCanceled());
                await tcs.Task;
            }

            return new GitResult
            {
                Success = process.ExitCode == 0,
                Output = outputBuilder.ToString().Trim(),
                Error = errorBuilder.ToString().Trim(),
                ExitCode = process.ExitCode
            };
        }

        /// <summary>
        /// List all worktrees.
        /// </summary>
        public async Task<List<GitWorktree>> ListWorktreesAsync(CancellationToken cancellationToken = default)
        {
            var result = await ExecuteGitAsync("worktree list --porcelain", cancellationToken);
            var worktrees = new List<GitWorktree>();
            GitWorktree? current = null;

            if (result.Success)
            {
                var lines = result.Output.Split('\n');
                foreach (var line in lines)
                {
                    if (line.StartsWith("worktree "))
                    {
                        if (current != null)
                        {
                            worktrees.Add(current);
                        }
                        current = new GitWorktree
                        {
                            Path = line.Substring("worktree ".Length).Trim()
                        };
                    }
                    else if (line.StartsWith("branch "))
                    {
                        if (current != null)
                        {
                            current.Branch = line.Substring("branch ".Length).Trim();
                        }
                    }
                    else if (line == "HEAD")
                    {
                        // Detached HEAD
                    }
                    else if (line.StartsWith("detached from "))
                    {
                        // Detached HEAD
                    }
                }
                if (current != null)
                {
                    worktrees.Add(current);
                }
            }

            return worktrees;
        }

        /// <summary>
        /// Create a new worktree.
        /// </summary>
        public async Task<GitResult> CreateWorktreeAsync(string path, string branch, bool createBranch = true, CancellationToken cancellationToken = default)
        {
            var arguments = createBranch 
                ? $"worktree add \"{path}\" -b {branch}"
                : $"worktree add \"{path}\" {branch}";
            
            return await ExecuteGitAsync(arguments, cancellationToken);
        }

        /// <summary>
        /// Remove a worktree.
        /// </summary>
        public async Task<GitResult> RemoveWorktreeAsync(string path, bool force = false, CancellationToken cancellationToken = default)
        {
            var arguments = force 
                ? $"worktree remove --force \"{path}\""
                : $"worktree remove \"{path}\"";
            
            return await ExecuteGitAsync(arguments, cancellationToken);
        }

        /// <summary>
        /// Switch to a worktree by changing to its directory.
        /// </summary>
        public async Task<GitResult> SwitchToWorktreeAsync(string path, CancellationToken cancellationToken = default)
        {
            // Change directory to the worktree
            var originalDir = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(path);
                return new GitResult { Success = true, Output = $"Switched to worktree: {path}" };
            }
            catch (Exception ex)
            {
                return new GitResult
                {
                    Success = false,
                    Error = ex.Message,
                    ExitCode = 1
                };
            }
            finally
            {
                Directory.SetCurrentDirectory(originalDir);
            }
        }

        /// <summary>
        /// Get the current branch.
        /// </summary>
        public async Task<string?> GetCurrentBranchAsync(CancellationToken cancellationToken = default)
        {
            var result = await ExecuteGitAsync("branch --show-current", cancellationToken);
            return result.Success ? result.Output : null;
        }

        /// <summary>
        /// Get all branches.
        /// </summary>
        public async Task<List<string>> GetBranchesAsync(CancellationToken cancellationToken = default)
        {
            var result = await ExecuteGitAsync("branch --format=%(refname:short)", cancellationToken);
            if (result.Success)
            {
                var lines = result.Output.Split('\n');
                var filtered = new List<string>();
                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        filtered.Add(line.Trim());
                }
                return filtered;
            }
            return new List<string>();
        }

        /// <summary>
        /// Get remote URL for the repository.
        /// </summary>
        public async Task<string?> GetRemoteUrlAsync(CancellationToken cancellationToken = default)
        {
            var result = await ExecuteGitAsync("remote get-url origin", cancellationToken);
            return result.Success ? result.Output : null;
        }

        /// <summary>
        /// Check if a directory is a git repository.
        /// </summary>
        public async Task<bool> IsGitRepositoryAsync(string path, CancellationToken cancellationToken = default)
        {
            var originalDir = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(path);
                var result = await ExecuteGitAsync("rev-parse --git-dir", cancellationToken);
                return result.Success;
            }
            catch
            {
                return false;
            }
            finally
            {
                Directory.SetCurrentDirectory(originalDir);
            }
        }

        /// <summary>
        /// Get the root of the git repository.
        /// </summary>
        public async Task<string?> GetRepoRootAsync(CancellationToken cancellationToken = default)
        {
            var result = await ExecuteGitAsync("rev-parse --show-toplevel", cancellationToken);
            return result.Success ? result.Output : null;
        }

        /// <summary>
        /// Check if a branch exists locally.
        /// </summary>
        public async Task<bool> BranchExistsAsync(string branch, CancellationToken cancellationToken = default)
        {
            var result = await ExecuteGitAsync($"rev-parse --verify {branch}", cancellationToken);
            return result.Success;
        }

        /// <summary>
        /// Checkout a branch.
        /// </summary>
        public async Task<GitResult> CheckoutAsync(string branch, CancellationToken cancellationToken = default)
        {
            return await ExecuteGitAsync($"checkout {branch}", cancellationToken);
        }

        /// <summary>
        /// Create and checkout a new branch.
        /// </summary>
        public async Task<GitResult> CreateAndCheckoutBranchAsync(string branch, string? startPoint = null, CancellationToken cancellationToken = default)
        {
            var arguments = startPoint != null
                ? $"checkout -b {branch} {startPoint}"
                : $"checkout -b {branch}";
            
            return await ExecuteGitAsync(arguments, cancellationToken);
        }

        /// <summary>
        /// Delete a branch.
        /// </summary>
        public async Task<GitResult> DeleteBranchAsync(string branch, bool force = false, CancellationToken cancellationToken = default)
        {
            var arguments = force
                ? $"branch -D {branch}"
                : $"branch -d {branch}";
            
            return await ExecuteGitAsync(arguments, cancellationToken);
        }

        /// <summary>
        /// Get commit hash for a reference.
        /// </summary>
        public async Task<string?> GetCommitHashAsync(string refName, CancellationToken cancellationToken = default)
        {
            var result = await ExecuteGitAsync($"rev-parse {refName}", cancellationToken);
            return result.Success ? result.Output : null;
        }

        /// <summary>
        /// Get the base branch for a feature branch (typically the branch it was created from).
        /// </summary>
        public async Task<string?> GetBaseBranchAsync(string branch, CancellationToken cancellationToken = default)
        {
            // Try to find the most common base branch
            var result = await ExecuteGitAsync($"merge-base --fork-point {branch}", cancellationToken);
            if (result.Success)
            {
                var commit = result.Output;
                // Get the branch that contains this commit
                var branchesResult = await ExecuteGitAsync($"branch --contains {commit}", cancellationToken);
                if (branchesResult.Success)
                {
                    var branches = branchesResult.Output.Split('\n');
                    foreach (var b in branches)
                    {
                        var clean = b.Trim().Replace("*", "").Trim();
                        if (clean != branch && clean != "HEAD")
                        {
                            return clean;
                        }
                    }
                }
            }
            return "main"; // Default fallback
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
