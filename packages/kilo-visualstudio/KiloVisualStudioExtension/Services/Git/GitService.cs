using KiloExtensionDTOs;
using KiloExtensionDTOs.ExtensionMessages;
using KiloExtensionDTOs.WebviewMessages;
using KiloVisualStudioExtension.ApiClient;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Xml.Linq;

namespace KiloVisualStudioExtension.Services.Git
{
  public delegate Task<IWebviewMessage> Interceptor(IWebviewMessage msg);



  public class Context
  {
    public Func<string?, string> WorkspaceDir { get; set; } = null!;
    public Action<object> Post { get; set; } = null!;
    public Func<object, string> Error { get; set; } = null!;
    public Interceptor? Before { get; set; }
  }

  public class Input
  {
    public string RequestId { get; set; }
    public string Dir { get; set; }
    public string Base { get; set; }
    public Action<object> Post { get; set; } = null;
    public Func<object, string> Error { get; set; } = null;
  }


  /// <summary>
  /// Result of resolving a local diff target.
  /// </summary>
  public class LocalDiffTarget
  {
    /// <summary>
    /// The directory path for the diff.
    /// </summary>
    public string Directory { get; set; } = string.Empty;

    /// <summary>
    /// The base branch to compare against.
    /// </summary>
    public string BaseBranch { get; set; } = string.Empty;
  }

  class State
  {
    public string Out { get; set; } = "";
    public string Err { get; set; } = "";
    public bool Done { get; set; }
    public bool Truncated { get; set; }
  }
  //
  // Helper class for context result
  public class ContextResult
  {
    public string Content { get; set; } = "";
    public bool Truncated { get; set; }
  }
  //
  // Helper extension for Join
  static class Extensions
  {
    public static string Join(this IEnumerable<string> parts, string separator)
    {
      return string.Join(separator, parts);
    }
  }


  //
  // type Result = {
  public class Result
  {
    //   out: string
    public string Out { get; set; } = "";
    //   err: string
    public string Err { get; set; } = "";
    //   code: number | null
    public int? Code { get; set; }
    //   signal: NodeJS.Signals | null
    public string? Signal { get; set; }
    //   truncated: boolean
    public bool Truncated { get; set; }
    //   error?: string
    public string? Error { get; set; }
    // }
  }

  /// <summary>
  /// Git operations interface for branch operations.
  /// </summary>
  public interface IGitOps
  {
    /// <summary>
    /// Gets the current branch name for a repository.
    /// </summary>
    Task<string?> CurrentBranch(string root);

    /// <summary>
    /// Resolves the tracking branch for a given branch.
    /// </summary>
    Task<string?> ResolveTrackingBranch(string root, string branch);

    /// <summary>
    /// Resolves the default branch for a repository.
    /// </summary>
    Task<string?> ResolveDefaultBranch(string root, string branch);
  }

  public class GitService : ServiceProviderServiceBase
  {
    private bool _disposed;

    private VSProvider Provider => _serviceProvider.GetService<VSProvider>()
        ?? throw new InvalidOperationException("VSProvider not registered in service provider");

    // const LIMIT = 400_000
    const int LIMIT = 400_000;
    // const SMALL = 80_000
    const int SMALL = 80_000;
    // const TIMEOUT = 15_000
    const int TIMEOUT = 15_000;

    public GitService(ServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    //
    // export async function getGitChangesContext(
    //   dir: string,
    //   base?: string,
    // ): Promise<{ content: string; truncated: boolean }>
    public async Task<ContextResult> GetGitChangesContextAsync(
        string dir,
        string? @base = null
    )
{
      //   const probe = await run(["rev-parse", "--is-inside-work-tree"], dir, SMALL)
      var probe = await Run(new[] { "rev-parse", "--is-inside-work-tree" }, dir, SMALL);
//   if (probe.error) return done(dir, `Unable to read git changes: ${probe.error}`)
    if (probe.Error != null) return Done(dir, $"Unable to read git changes: {probe.Error}");
//   if (probe.code !== 0 || probe.out.trim() !== "true") return done(dir, "Not a git repository.")
    if (probe.Code != 0 || probe.Out.Trim() != "true") return Done(dir, "Not a git repository.");
    //
    //   const head = await run(["rev-parse", "--verify", "HEAD"], dir, SMALL)
    var head = await Run(new[] { "rev-parse", "--verify", "HEAD" }, dir, SMALL);
//   if (head.error) return done(dir, `Unable to read git changes: ${head.error}`)
    if (head.Error != null) return Done(dir, $"Unable to read git changes: {head.Error}");
//   if (base && head.code === 0) return await against(dir, base)
    if (@base != null && head.Code == 0) return await Against(dir, @base);
//   return await local(dir, head.code === 0)
    return await Local(dir, head.Code == 0);
  // }
}
//
// async function local(dir: string, born: boolean): Promise<{ content: string; truncated: boolean }> {
async Task<ContextResult> Local(string dir, bool born)
    // {
    {
      //   const [status, diff, untracked] = await Promise.all([
      var tasks = await Task.WhenAll(new[] {
//     run(["status", "--short"], dir, SMALL),
        Run(new[] { "status", "--short" }, dir, SMALL),
//     changes(dir, born),
        Changes(dir, born),
//     run(["ls-files", "--others", "--exclude-standard", "-z"], dir, SMALL),
        Run(new[] { "ls-files", "--others", "--exclude-standard", "-z" }, dir, SMALL),
//   ])
    });
      //   const fail = status.error ?? diff.error ?? untracked.error
      var status = tasks[0];
      var diff = tasks[1];
      var untracked = tasks[2];
      var fail = status.Error ?? diff.Error ?? untracked.Error;
      //   if (fail) return done(dir, `Unable to read git changes: ${fail}`)
      if (fail != null) return Done(dir, $"Unable to read git changes: {fail}");
      //   if (status.code !== 0 && !status.truncated) return done(dir, `Unable to read git status:\n${output(status)}`.trim())
      if (status.Code != 0 && !status.Truncated) return Done(dir, $"Unable to read git status:\n{Output(status)}".Trim());
      //   if (diff.code !== 0 && !diff.truncated) return done(dir, `Unable to read git diff:\n${output(diff)}`.trim())
      if (diff.Code != 0 && !diff.Truncated) return Done(dir, $"Unable to read git diff:\n{Output(diff)}".Trim());
      //   if (untracked.code !== 0 && !untracked.truncated)
      if (untracked.Code != 0 && !untracked.Truncated)
        //     return done(dir, `Unable to read untracked files:\n${output(untracked)}`.trim())
        return Done(dir, $"Unable to read untracked files:\n{Output(untracked)}".Trim());
      //
      //   const extra = await untrackedDiff(dir, untracked.out)
      var extra = await UntrackedDiff(dir, untracked.Out);
      //   const body = [diff.out.trim(), extra.content.trim()].filter(Boolean).join("\n\n")
      var body = new[] { diff.Out.Trim(), extra.Content.Trim() }.Where(s => s != "").Join("\n\n");
      //   const changed = status.out.trim() || body.trim()
      var changed = status.Out.Trim() ?? body.Trim();
      //   if (!changed) return done(dir, "No changes in working directory.")
      if (changed == "") return Done(dir, "No changes in working directory.");
      //
      //   const truncated = status.truncated || diff.truncated || untracked.truncated || extra.truncated
      var truncated = status.Truncated || diff.Truncated || untracked.Truncated || extra.Truncated;
      //   const note = truncated ? "\n\nOutput truncated." : ""
      var note = truncated ? "\n\nOutput truncated." : "";
      //   return cap(
      return Cap(
          //     `Working directory: ${dir}\n\nStatus:\n${status.out.trim() || "(empty)"}\n\nDiff:\n${body || "(empty)"}${note}`,
          $"Working directory: {dir}\n\nStatus:\n{(status.Out.Trim() ?? "(empty)")}\n\nDiff:\n{(body ?? "(empty)")}{note}",
          //     truncated,
          truncated
    //   )
      );
      // }
    }
    //
    // async function against(dir: string, base: string): Promise<{ content: string; truncated: boolean }> {
    async Task<ContextResult> Against(string dir, string @base)
    // {
    {
      //   const ancestor = await run(["merge-base", "HEAD", base], dir, SMALL)
      var ancestor = await Run(new[] { "merge-base", "HEAD", @base }, dir, SMALL);
      //   if (ancestor.error) return done(dir, `Unable to resolve git base ${base}: ${ancestor.error}`)
      if (ancestor.Error != null) return Done(dir, $"Unable to resolve git base {@base}: {ancestor.Error}");
      //   if (ancestor.code !== 0) return done(dir, `Unable to resolve git base ${base}:\n${output(ancestor)}`.trim())
      if (ancestor.Code != 0) return Done(dir, $"Unable to resolve git base {@base}:\n{Output(ancestor)}".Trim());
      //
      //   const ref = ancestor.out.trim()
      var @ref = ancestor.Out.Trim();
      //   const [status, diff, untracked] = await Promise.all([
      var tasks = await Task.WhenAll(new[] {
//     run(["diff", "--name-status", "--no-renames", ref], dir, SMALL),
        Run(new[] { "diff", "--name-status", "--no-renames", @ref }, dir, SMALL),
//     run(["diff", ref], dir, LIMIT),
        Run(new[] { "diff", @ref }, dir, LIMIT),
//     run(["ls-files", "--others", "--exclude-standard", "-z"], dir, SMALL),
        Run(new[] { "ls-files", "--others", "--exclude-standard", "-z" }, dir, SMALL),
//   ])
    });
      //   const fail = status.error ?? diff.error ?? untracked.error
      var status = tasks[0];
      var diff = tasks[1];
      var untracked = tasks[2];
      var fail = status.Error ?? diff.Error ?? untracked.Error;
      //   if (fail) return done(dir, `Unable to read git changes: ${fail}`)
      if (fail != null) return Done(dir, $"Unable to read git changes: {fail}");
      //   if (status.code !== 0 && !status.truncated)
      if (status.Code != 0 && !status.Truncated)
        //     return done(dir, `Unable to read changed files:\n${output(status)}`.trim())
        return Done(dir, $"Unable to read changed files:\n{Output(status)}".Trim());
      //   if (diff.code !== 0 && !diff.truncated) return done(dir, `Unable to read git diff:\n${output(diff)}`.trim())
      if (diff.Code != 0 && !diff.Truncated) return Done(dir, $"Unable to read git diff:\n{Output(diff)}".Trim());
      //   if (untracked.code !== 0 && !untracked.truncated)
      if (untracked.Code != 0 && !untracked.Truncated)
        //     return done(dir, `Unable to read untracked files:\n${output(untracked)}`.trim())
        return Done(dir, $"Unable to read untracked files:\n{Output(untracked)}".Trim());
      //
      //   const extra = await untrackedDiff(dir, untracked.out)
      var extra = await UntrackedDiff(dir, untracked.Out);
      //   const files = [status.out.trim(), listed(untracked.out)].filter(Boolean).join("\n")
      var files = new[] { status.Out.Trim(), Listed(untracked.Out) }.Where(s => s != "").Join("\n");
      //   const body = [diff.out.trim(), extra.content.trim()].filter(Boolean).join("\n\n")
      var body = new[] { diff.Out.Trim(), extra.Content.Trim() }.Where(s => s != "").Join("\n\n");
      //   const changed = files.trim() || body.trim()
      var changed = files.Trim() ?? body.Trim();
      //   if (!changed) return done(dir, `Base: ${base}\n\nNo changes in worktree diff.`)
      if (changed == "") return Done(dir, $"Base: {@base}\n\nNo changes in worktree diff.");
      //
      //   const truncated = status.truncated || diff.truncated || untracked.truncated || extra.truncated
      var truncated = status.Truncated || diff.Truncated || untracked.Truncated || extra.Truncated;
      //   const note = truncated ? "\n\nOutput truncated." : ""
      var note = truncated ? "\n\nOutput truncated." : "";
      //   return cap(
      return Cap(
          //     `Working directory: ${dir}\nBase: ${base}\nMerge base: ${ref}\n\nFiles:\n${files || "(empty)"}\n\nDiff:\n${body || "(empty)"}${note}`,
          $"Working directory: {dir}\nBase: {@base}\nMerge base: {@ref}\n\nFiles:\n{(files ?? "(empty)")}\n\nDiff:\n{(body ?? "(empty)")}{note}",
          //     truncated,
          truncated
    //   )
      );
      // }
    }
    //
    // function listed(raw: string): string {
    string Listed(string raw)
    // {
    {
      //   return raw
      return raw
          //     .split("\0")
          .Split('\0')
          //     .filter(Boolean)
          .Where(s => s != "")
          //     .map((file) => `A\t${file}`)
          .Select(file => $"A\t{file}")
          //     .join("\n")
          .Join("\n");
      // }
    }
    //
    // async function changes(dir: string, born: boolean): Promise<Result> {
    async Task<Result> Changes(string dir, bool born)
    // {
    {
      //   if (born) return run(["diff", "HEAD"], dir, LIMIT)
      if (born) return await Run(new[] { "diff", "HEAD" }, dir, LIMIT);
      //
      //   const [cached, work] = await Promise.all([run(["diff", "--cached"], dir, LIMIT), run(["diff"], dir, LIMIT)])
      var tasks = await Task.WhenAll(new[] {
        Run(new[] { "diff", "--cached" }, dir, LIMIT),
        Run(new[] { "diff" }, dir, LIMIT)
    });
      //   return {
      return new Result
      //     out: [cached.out.trim(), work.out.trim()].filter(Boolean).join("\n\n"),
      {
        Out = new[] { tasks[0].Out.Trim(), tasks[1].Out.Trim() }.Where(s => s != "").Join("\n\n"),
        //     err: [cached.err.trim(), work.err.trim()].filter(Boolean).join("\n"),
        Err = new[] { tasks[0].Err.Trim(), tasks[1].Err.Trim() }.Where(s => s != "").Join("\n"),
        //     code: cached.code !== 0 ? cached.code : work.code,
        Code = tasks[0].Code != 0 ? tasks[0].Code : tasks[1].Code,
        //     signal: cached.signal ?? work.signal,
        Signal = tasks[0].Signal ?? tasks[1].Signal,
        //     truncated: cached.truncated || work.truncated,
        Truncated = tasks[0].Truncated || tasks[1].Truncated,
        //     error: cached.error ?? work.error,
        Error = tasks[0].Error ?? tasks[1].Error
        //   }
      };
      // }
    }

    private static Task<byte[]> ReadAllBytesAsync(string path)
    {
      return Task.Run(() => System.IO.File.ReadAllBytes(path));
    }

    //
    // async function untrackedDiff(dir: string, raw: string): Promise<{ content: string; truncated: boolean }> {
    async Task<ContextResult> UntrackedDiff(string dir, string raw)
    // {
    {
      //   const files = raw.split("\0").filter(Boolean)
      var files = raw.Split('\0').Where(s => s != "").ToArray();
      //   const parts: string[] = []
      var parts = new List<string>();
      //   let used = 0
      int used = 0;
      //   let truncated = false
      bool truncated = false;
      //
      //   for (const file of files) {
      foreach (var file in files)
      //     const full = path.join(dir, file)
      {
        var full = System.IO.Path.Combine(dir, file);
        //     const stat = await fs.stat(full).catch(() => undefined)
        FileInfo? stat = null;
        try { stat = new FileInfo(full); } catch { }
        //     if (!stat?.isFile()) continue
        if (stat == null || !stat.Exists) continue;
        //     if (stat.size > LIMIT) {
        if (stat.Length > LIMIT)
        {
          //       truncated = true
          truncated = true;
          //       parts.push(patch(file, `<${stat.size} byte file omitted>`))
          parts.Add(Patch(file, $"<{stat.Length} byte file omitted>"));
          //       continue
          continue;
          //     }
        }
        //
        //     const buf = await fs.readFile(full).catch(() => undefined)
        byte[]? buf = null;
        try { buf = await ReadAllBytesAsync(full); } catch { }
        //     const next = !buf
        string next;
        if (buf == null)
          //       ? patch(file, `<unreadable file: ${file}>`)
          next = Patch(file, $"<unreadable file: {file}>");
        //       : binary(buf)
        else if (Binary(buf))
          //         ? patch(file, `<binary file omitted: ${file}>`)
          next = Patch(file, $"<binary file omitted: {file}>");
        //         : patch(file, buf.toString("utf8"))
        else
          next = Patch(file, System.Text.Encoding.UTF8.GetString(buf));
        //     const size = Buffer.byteLength(next, "utf8") + (parts.length ? 2 : 0)
        var size = System.Text.Encoding.UTF8.GetByteCount(next) + (parts.Count > 0 ? 2 : 0);
        //     if (used + size > LIMIT) {
        if (used + size > LIMIT)
        {
          //       truncated = true
          truncated = true;
          //       break
          break;
          //     }
        }
        //     parts.push(next)
        parts.Add(next);
        //     used += size
        used += size;
        //   }
      }
      //
      //   return cap(parts.join("\n\n"), truncated)
      return Cap(string.Join("\n\n", parts), truncated);
      // }
    }
    //
    // function binary(buf: Buffer): boolean {
    bool Binary(byte[] buf)
    // {
    {
      //   const head = buf.subarray(0, Math.min(buf.length, 8192))
      var head = buf.Take(Math.Min(buf.Length, 8192));
      //   return head.includes(0)
      return head.Any(b => b == 0);
      // }
    }
    //
    // function patch(file: string, text: string) {
    string Patch(string file, string text)
    // {
    {
      //   const header = `diff --git a/${file} b/${file}\nnew file mode 100644\n--- /dev/null\n+++ b/${file}`
      var header = $"diff --git a/{file} b/{file}\nnew file mode 100644\n--- /dev/null\n+++ b/{file}";
      //   if (!text) return header
      if (text == "") return header;
      //   const lines = text.endsWith("\n") ? text.slice(0, -1).split("\n") : text.split("\n")
      var lines = text.EndsWith("\n") ? text.Substring(0, text.Length - 1).Split('\n') : text.Split('\n');
      //   const body = lines.map((line) => `+${line}`).join("\n")
      var body = string.Join("\n", lines.Select(line => $"+{line}"));
      //   return `${header}\n@@ -0,0 +1,${lines.length} @@\n${body}`
      return $"{header}\n@@ -0,0 +1,{lines.Length} @@\n{body}";
      // }
    }
    //
    // function cap(content: string, truncated = false) {
    ContextResult Cap(string content, bool truncated = false)
    // {
    {
      //   if (Buffer.byteLength(content, "utf8") <= LIMIT) return { content, truncated }
      if (System.Text.Encoding.UTF8.GetByteCount(content) <= LIMIT) return new ContextResult { Content = content, Truncated = truncated };
      //   const text = Buffer.from(content, "utf8").subarray(0, LIMIT).toString("utf8")
      var bytes = System.Text.Encoding.UTF8.GetBytes(content);
      var text = System.Text.Encoding.UTF8.GetString(bytes.Take(LIMIT).ToArray());
      //   return { content: text, truncated: true }
      return new ContextResult { Content = text, Truncated = true };
      // }
    }
    //
    // function done(dir: string, text: string) {
    ContextResult Done(string dir, string text)
    // {
    {
      //   return { content: `Working directory: ${dir}\n\n${text}`, truncated: false }
      return new ContextResult { Content = $"Working directory: {dir}\n\n{text}", Truncated = false };
      // }
    }
    //
    // function output(result: Result) {
    string Output(Result result)
    // {
    {
      //   return `${result.err.trim()}${result.err.trim() && result.out.trim() ? "\n" : ""}${result.out.trim()}`
      var err = result.Err.Trim();
      var @out = result.Out.Trim();
      return err + (err != "" && @out != "" ? "\n" : "") + @out;
      // }
    }
    //
    // function run(args: string[], cwd: string, limit: number): Promise<Result> {
    Task<Result> Run(string[] args, string cwd, int limit)
    // {
    {
      //   return new Promise((resolve) => {
      var tcs = new TaskCompletionSource<Result>();
      var state = new State();
      //     const state = { out: "", err: "", done: false, truncated: false }
      //     const child = spawn("git", args, { cwd })
      var child = new Process();
      child.StartInfo = new ProcessStartInfo
      {
        FileName = "git",
        Arguments = string.Join(" ", args.Select(a => $"\"{a}\"")),
        WorkingDirectory = cwd,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };
      //     const timer = setTimeout(() => {
      var timer = new System.Timers.Timer(TIMEOUT);
      timer.Elapsed += (s, e) =>
      {
        //       state.truncated = true
        state.Truncated = true;
        //       child.kill()
        child.Kill();
        //     }, TIMEOUT)
      };
      //
      //     const finish = (result: Pick<Result, "code" | "signal" | "error">) => {
      Action<Result> finish = (result) =>
      {
        //       if (state.done) return
        if (state.Done) return;
        //       state.done = true
        state.Done = true;
        //       clearTimeout(timer)
        timer.Stop();
        //       resolve({ out: state.out, err: state.err, truncated: state.truncated, ...result })
        tcs.SetResult(new Result
        {
          Out = state.Out,
          Err = state.Err,
          Truncated = state.Truncated,
          Code = result.Code,
          Signal = result.Signal,
          Error = result.Error
        });
        //     }
      };
      //
      //     const collect = (key: "out" | "err", chunk: Buffer) => {
      Action<string, byte[]> collect = (key, chunk) =>
      {
        //       if (state.truncated) return
        if (state.Truncated) return;
        //       const used = Buffer.byteLength(state[key], "utf8")
        var used = System.Text.Encoding.UTF8.GetByteCount(key == "out" ? state.Out : state.Err);
        //       const free = limit - used
        var free = limit - used;
        //       if (free <= 0) {
        if (free <= 0)
        {
          //         state.truncated = true
          state.Truncated = true;
          //         child.kill()
          child.Kill();
          //         return
          return;
          //       }
        }
        //       if (chunk.byteLength > free) {
        if (chunk.Length > free)
        {
          //         state[key] += chunk.subarray(0, free).toString("utf8")
          if (key == "out") state.Out += System.Text.Encoding.UTF8.GetString(chunk.Take(free).ToArray());
          else state.Err += System.Text.Encoding.UTF8.GetString(chunk.Take(free).ToArray());
          //         state.truncated = true
          state.Truncated = true;
          //         child.kill()
          child.Kill();
          //         return
          return;
          //       }
        }
        //       state[key] += chunk.toString("utf8")
        if (key == "out") state.Out += System.Text.Encoding.UTF8.GetString(chunk);
        else state.Err += System.Text.Encoding.UTF8.GetString(chunk);
        //     }
      };
      //
      //     child.stdout?.on("data", (chunk: Buffer) => collect("out", chunk))
      child.OutputDataReceived += (s, e) => { if (e.Data != null) collect("out", System.Text.Encoding.UTF8.GetBytes(e.Data)); };
      //     child.stderr?.on("data", (chunk: Buffer) => collect("err", chunk))
      child.ErrorDataReceived += (s, e) => { if (e.Data != null) collect("err", System.Text.Encoding.UTF8.GetBytes(e.Data)); };
      //     child.on("error", (err) => finish({ code: null, signal: null, error: err.message }))
      child.EnableRaisingEvents = true;
      child.Exited += (s, e) => finish(new Result { Code = null, Signal = null, Error = "Process error" });
      //     child.on("close", (code, signal) => finish({ code, signal }))
      child.Exited += (s, e) => finish(new Result { Code = child.ExitCode, Signal = null });
      //
      //     child.start();
      child.Start();
      //     child.BeginOutputReadLine();
      child.BeginOutputReadLine();
      //     child.BeginErrorReadLine();
      child.BeginErrorReadLine();
      //   })
      return tcs.Task;
      // }
    }
//
// Helper class for state tracking


    private static GitOps? _shared;
    public async Task<IWebviewMessage> InterceptMessageAsync(
        IWebviewMessage msg,
        Context ctx)
    {
      IWebviewMessage? next;
      try
      {
        next = ctx.Before != null ? await ctx.Before(msg) : msg;
      }
      catch (Exception e)
      {
        System.Diagnostics.Debug.WriteLine("[Kilo New] interceptor error: " + e);
        next = null;
      }

      if (next == null || !(next is RequestGitChangesContextMessage))
        return next;

      var request = (RequestGitChangesContextMessage)next;
      var sid = request.SessionID;
      var dir = ctx.WorkspaceDir(sid);
      var resolved = await ResolveGitChangesTargetAsync(next, dir);

      try
      {
        string reqId = "";
        string ctxDir = "";
        string baseStr = "";
        if (resolved is RequestGitChangesContextMessage typedResolved)
        {
          reqId = typedResolved.RequestId;
          ctxDir = typedResolved.ContextDirectory;
          baseStr = typedResolved.GitChangesBase;
        }

        await CaptureGitChangesContextAsync(new Input
        {
          RequestId = string.IsNullOrEmpty(reqId) ? "" : reqId,
          Dir = string.IsNullOrEmpty(ctxDir) ? dir : ctxDir, 
          Base = string.IsNullOrEmpty(baseStr) ? null : baseStr,
          Post = ctx.Post,
          Error = ctx.Error
        });
      }
      catch (Exception e)
      {
        System.Diagnostics.Debug.WriteLine("[Kilo New] git changes error: " + e);
      }

      return null;
    }

    internal GitOps Ops()
    {
      if (_shared != null && !_shared.Disposed) return _shared;
      _shared = new GitOps(new GitOpsOptions{Log = (s) => { } });
      return _shared;
    }

    internal void DisposeGitChangesTarget()
    {
      _shared?.Dispose();
      _shared = null;
    }

    internal async Task<IWebviewMessage> ResolveGitChangesTargetAsync(IWebviewMessage message, string Dir)
    {
      if (!(message is RequestGitChangesContextMessage)) return message;
      var typedMessage = (RequestGitChangesContextMessage)message;
      if (!string.IsNullOrEmpty(typedMessage.ContextDirectory) || !string.IsNullOrEmpty(typedMessage.GitChangesBase))
        return (IWebviewMessage)typedMessage;
      var target = await ResolveLocalDiffTarget(Ops(), (s) => { }, Dir);
      if (target == null)
      {
        typedMessage.ContextDirectory = Dir;
        return (IWebviewMessage)typedMessage;
      }
      typedMessage.ContextDirectory = target.Directory;
      typedMessage.GitChangesBase = target.BaseBranch;
      return (IWebviewMessage)typedMessage;
    }

    internal async Task CaptureGitChangesContextAsync(Input input)
    {

      try
      {
        var output = await GetGitChangesContextAsync(input.Dir, input.Base);
        input.Post(
          new GitChangesContextResultMessage
          {
            RequestId = input.RequestId,
            Content = output.Content,
            Truncated = output.Truncated
          });
      }
      catch (Exception error)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo New] Failed to capture git changes context: {error.Message}");
        input.Post(
          new GitChangesContextErrorMessage
          {
            RequestId = input.RequestId,
            Error = input.Error(error) ?? "Failed to capture git changes"
          });
      }
    }

    /// <summary>
    /// Resolves the local diff target by determining the current branch,
    /// tracking branch, and base branch for comparison.
    /// </summary>
    /// <param name="gitOps">Git operations interface.</param>
    /// <param name="log">Logging action.</param>
    /// <param name="root">Optional workspace root directory.</param>
    /// <returns>LocalDiffTarget if resolved, null otherwise.</returns>
    public async Task<LocalDiffTarget?> ResolveLocalDiffTarget(
        GitOps gitOps,
        Action<string> log,
        string? root = null)
    {
      if (string.IsNullOrEmpty(root))
      {
        log("Local diff: no workspace root");
        return null;
      }

      var branch = await gitOps.CurrentBranch(root);
      if (string.IsNullOrEmpty(branch) || branch == "HEAD")
      {
        log("Local diff: detached HEAD or no branch");
        return null;
      }

      var tracking = await gitOps.ResolveTrackingBranch(root, branch);
      var fallback = tracking != null
          ? null
          : await gitOps.ResolveDefaultBranch(root, branch);
      var raw = tracking ?? fallback ?? "HEAD";
      var baseBranch = await ResolveBase(gitOps, root, raw);

      log($"Local diff: branch={branch} tracking={tracking ?? "none"} default={fallback ?? "none"} base={baseBranch}");

      return new LocalDiffTarget
      {
        Directory = root,
        BaseBranch = baseBranch
      };
    }

    private readonly string[] BaseCandidates = ["main", "master", "dev", "develop"];

    /// <summary>
    /// Resolves the base branch for comparison.
    /// </summary>
    private async Task<string> ResolveBase(GitOps git, string root, string gitBase)
    {
      // If the caller gave an explicit base, honor it. Return it as-is so merge-base
      // fails loudly on a stale/misspelled ref instead of silently diffing against
      // an unrelated candidate branch.
      if (!string.IsNullOrEmpty(gitBase) && gitBase != "HEAD") return gitBase;
      foreach(var name in BaseCandidates) {
        var ok = await git.ExecGit(["rev-parse", "--verify", "--quiet", $"refs/heads/${name}"], root);
        if (ok.Code == 0) return name;
      }
      return "HEAD";
    }
  }
}
