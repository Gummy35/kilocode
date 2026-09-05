// import { existsSync } from "fs"
using KiloVisualStudioExtension.Utils;
using Microsoft.VisualStudio.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
// import { readFile, stat, unlink } from "fs/promises"
// import * as os from "os"
// import * as path from "path"
// import type { ChildProcess } from "child_process"
// import { exec, spawn } from "../util/process"

namespace KiloVisualStudioExtension.Services
{
  public class CaptureService : ServiceProviderServiceBase
  {
    // type Input = {
    public class Input
    //   requestId: string
    {
      public string RequestId { get; set; } = "";
      //   model: string
      public string Model { get; set; } = "";
      //   language?: string
      public string? Language { get; set; }
      // }
    }
    //
    // type Recording = Input & {
    public class Recording : Input
    //   file: string
    {
      public string File { get; set; } = "";
      //   proc: ChildProcess
      public Process Proc { get; set; } = null!;
      //   stderr: string[]
      public List<string> Stderr { get; set; } = new();
      //   stopped: boolean
      public bool Stopped { get; set; }
      public bool InputStreamClosed { get; set; }
      //   exit?: {
      public ExitInfo? Exit { get; set; }
      //     code: number | null
      //     signal: string | null
    }
    //   }
    // }
    public class ExitInfo
    {
      public int? Code { get; set; }
      public string? Signal { get; set; }
    }
    //
    // type Audio = {
    public class Audio
    //   data: string
    {
      public string Data { get; set; } = "";
      //   format: "wav"
      public string Format { get; set; } = "wav";
      //   model: string
      public string Model { get; set; } = "";
      //   language?: string
      public string? Language { get; set; }
      // }
    }
    //
    // type Args = {
    public class Args
    //   pipe?: string[]
    {
      public string[]? Pipe { get; set; }
      //   input: string[]
      public string[] Input { get; set; } = Array.Empty<string>();
      // }
    }
    //
    // let active: Recording | undefined
    private static Recording? _active;
    // let starting: string | undefined
    private static string? _starting;
    // let ffmpeg: Promise<string> | undefined
    private static Task<string>? _ffmpeg;
    //

    public CaptureService(ServiceProvider provider) : base(provider)
    {

    }

    // export async function prewarmSpeechCapture(): Promise<void> {
    public async Task PrewarmSpeechCapture()
    // {
    {
      //   await resolveFFmpeg()
      await ResolveFFmpeg();
      // }
    }
    //
    // export async function startSpeechCapture(input: Input): Promise<boolean> {
    public async Task<bool> StartSpeechCapture(Input input)
    // {
    {
      //   if (active || starting) throw new Error("Speech recording is already in progress")
      if (_active != null || _starting != null)
        throw new InvalidOperationException("Speech recording is already in progress");
      //
      //   starting = input.requestId
      _starting = input.RequestId;
      //   try {
      try
      //     const bin = await resolveFFmpeg()
      {
        var bin = await ResolveFFmpeg();
        //     const file = path.join(os.tmpdir(), `kilo-stt-${process.pid}-${Date.now()}.wav`)
        var file = Path.Combine(Path.GetTempPath(), $"kilo-stt-{Process.GetCurrentProcess().Id}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.wav");
        //     const state = await startWithArgs(bin, file, input, await inputArgSets(bin))
        var state = await StartWithArgs(bin, file, input, await GetInputArgSets(bin));
        //     return !state.stopped
        return !state.Stopped;
        //   } finally {
      }
      finally
      //     if (starting === input.requestId) starting = undefined
      {
        if (_starting == input.RequestId) _starting = null;
        //   }
      }
      // }
    }
    //
    // export async function stopSpeechCapture(requestId: string): Promise<Audio> {
    public async Task<Audio> StopSpeechCapture(string requestId)
    // {
    {
      //   const state = requireActive(requestId)
      var state = RequireActive(requestId);
      //   state.stopped = true
      state.Stopped = true;
      //   active = undefined
      _active = null;
      //
      //   await stopProcess(state)
      await StopProcess(state);
      //
      //   const size = await stat(state.file)
      //     .then((info) => info.size)
      long size = 0;
      try
      {
        var info = new FileInfo(state.File);
        if (info.Exists) size = info.Length;
        //     .catch((err: unknown) => {
      }
      catch (Exception err)
      //       console.warn("[Kilo New] Failed to stat speech recording", err)
      {
        Console.WriteLine($"[Kilo New] Failed to stat speech recording: {err.Message}");
        //       return 0
      }
      //     })
      //
      //   if (size < 44) {
      if (size < 44)
      //     await removeFile(state.file)
      {
        await RemoveFile(state.File);
        //     throw new Error(summary(state, "No audio was recorded"))
        throw new Exception(Summary(state, "No audio was recorded"));
        //   }
      }
      //
      //   const file = await readFile(state.file)
      var file = await AsyncUtils.ReadAllBytesAsync(state.File);
      //   await removeFile(state.file)
      await RemoveFile(state.File);
      //   return { data: file.toString("base64"), format: "wav", model: state.model, language: state.language }
      return new Audio
      {
        Data = Convert.ToBase64String(file),
        Format = "wav",
        Model = state.Model,
        Language = state.Language
      };
      // }
    }
    //
    // export async function cancelSpeechCapture(requestId: string): Promise<void> {
    public async Task CancelSpeechCapture(string requestId)
    // {
    {
      //   const state = active
      var state = _active;
      //   if (!state || state.requestId !== requestId) return
      if (state == null || state.RequestId != requestId) return;
      //   state.stopped = true
      state.Stopped = true;
      //   active = undefined
      _active = null;
      //   await stopProcess(state)
      await StopProcess(state);
      //   await removeFile(state.file)
      await RemoveFile(state.File);
      // }
    }
    //
    // async function waitForStart(state: Recording): Promise<void> {
    private async Task WaitForStart(Recording state)
    // {
    {
      //   await new Promise<void>((resolve, reject) => {
      var tcs = new TaskCompletionSource<object?>();
      var timer = new System.Timers.Timer(5000);
      //     const done = () => {
      void Done()
      //       state.proc.off("error", onError)
      {
        state.Proc.ErrorDataReceived -= OnData;
        //       state.proc.off("exit", onExit)
        state.Proc.Exited -= OnExit;
        //       state.proc.stderr?.off("data", onData)
        timer.Stop();
        //       clearTimeout(timer)
        //       resolve()
        tcs.TrySetResult(null);
        //     }
      }
      //     const onError = (err: Error) => {
      void OnError(string error = "Could not start microphone recording")
      //       state.proc.off("exit", onExit)
      {
        state.Proc.Exited -= OnExit;
        //       state.proc.stderr?.off("data", onData)
        timer.Stop();
        //       clearTimeout(timer)
        //       reject(err)
        tcs.TrySetException(new Exception(Summary(state, error)));
        //     }
      }
      //     const onExit = () => {
      void OnExit(object? sender, EventArgs e)
      //       if (state.stopped) {
      {
        if (state.Stopped)
        //         done()
        {
          Done();
          //         return
          return;
          //       }
        }
        //       onError(new Error(summary(state, "Could not start microphone recording")))
        OnError("Could not start microphone recording");
        //     }
      }
      //     const onData = (data: Buffer) => {
      void OnData(object? sender, DataReceivedEventArgs e)
      //       if (/Output #0|Press \[q\]|size=\s*\d+/i.test(data.toString())) done()
      {
        if (e.Data != null && Regex.IsMatch(e.Data, @"Output #0|Press \[q\]|size=\s*\d+", RegexOptions.IgnoreCase))
          Done();
        //     }
      }
      //     const timer = setTimeout(() => {
      timer.Elapsed += (s, ea) =>
      //       onError(new Error(summary(state, "Timed out starting microphone recording")))
      {
        timer.Stop();
        OnError("Timed out starting microphone recording");
        //     }, 5000)
      };
      timer.Start();
      //
      //     state.proc.stderr?.on("data", onData)
      state.Proc.ErrorDataReceived += OnData;
      //     state.proc.once("error", onError)
      state.Proc.EnableRaisingEvents = true;
      //     state.proc.once("exit", onExit)
      state.Proc.Exited += OnExit;
      //   }).catch(async (err: unknown) => {
      state.Proc.Start();
      state.Proc.BeginErrorReadLine();
      //     if (active === state) active = undefined
      try
      //     await stopProcess(state)
      {
        await tcs.Task;
        //     await removeFile(state.file)
        //     throw err
      }
      //   })
      catch
      {
        if (_active == state) _active = null;
        await StopProcess(state);
        await RemoveFile(state.File);
        throw;
      }
      // }
    }
    //
    // async function startWithArgs(bin: string, file: string, input: Input, args: Args[]): Promise<Recording> {
    private async Task<Recording> StartWithArgs(string bin, string file, Input input, List<Args> args)
    // {
    {
      //   const [first, ...rest] = args
      var first = args.FirstOrDefault();
      var rest = args.Skip(1).ToList();
      //   if (!first) throw new Error(`Unsupported platform for speech input: ${process.platform}`)
      if (first == null)
        throw new Exception($"Unsupported platform for speech input: {Environment.OSVersion.Platform}");
      //
      //   const proc = first.pipe
      Process proc;
      //            if (first.Pipe != null)
      ////     ? pipeProcess(first.pipe, bin, file)
      //            {
      //                proc = PipeProcess(first.Pipe, bin, file);
      ////     : spawn(bin, ["-y", ...first.input, "-acodec", "pcm_s16le", "-ar", "16000", "-ac", "1", "-f", "wav", file], {
      //            }
      ////         stdio: ["pipe", "ignore", "pipe"],
      //            else
      ////       })
      //            {
      var psi = new ProcessStartInfo
      //   const state: Recording = { ...input, file, proc, stderr: [], stopped: false }
      {
        FileName = bin,
        Arguments = $"-y {string.Join(" ", first.Input)} -acodec pcm_s16le -ar 16000 -ac 1 -f wav \"{file}\"",
        RedirectStandardInput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };
      proc = Process.Start(psi)!;
      //   active = state
      //}

      var state = new Recording
      {
        RequestId = input.RequestId,
        Model = input.Model,
        Language = input.Language,
        File = file,
        Proc = proc,
        Stderr = new List<string>(),
        Stopped = false,
        InputStreamClosed = false
      };
      _active = state;
      //
      //   proc.stderr?.on("data", (data: Buffer) => {
      proc.ErrorDataReceived += (s, e) =>
      //     if (state.stderr.length < 20) state.stderr.push(data.toString())
      {
        if (e.Data != null && state.Stderr.Count < 20)
          state.Stderr.Add(e.Data);
        //   })
      };
      proc.BeginErrorReadLine();
      //   proc.on("exit", (code, signal) => {
      proc.Exited += (s, e) =>
      //     state.exit = { code, signal }
      {
        state.Exit = new ExitInfo { Code = proc.ExitCode, Signal = proc.HasExited ? "exited" : null };
        //     if (active === state && !state.stopped) active = undefined
        if (_active == state && !state.Stopped) _active = null;
        //   })
      };
      //   proc.on("error", (err) => {
      proc.EnableRaisingEvents = true;
      //     state.stderr.push(err.message)
      //     if (active === state && !state.stopped) active = undefined
      //   })
      //
      //   try {
      try
      //     await waitForStart(state)
      {
        await WaitForStart(state);
        //     return state
        return state;
        //   } catch (err) {
      }
      catch
      //     if (state.stopped) return state
      {
        if (state.Stopped) return state;
        //     if (rest.length === 0) throw err
        if (rest.Count == 0) throw;
        //     return startWithArgs(bin, file, input, rest)
        return await StartWithArgs(bin, file, input, rest);
        //   }
      }
      // }
    }
    //
    //// function pipeProcess(pipe: string[], bin: string, file: string): ChildProcess {
    //        private static Process PipeProcess(string[] pipe, string bin, string file)
    //// {
    //        {
    ////   const source = spawn("pw-record", pipe, { stdio: ["ignore", "pipe", "pipe"] })
    //            var psi1 = new ProcessStartInfo
    //            {
    //                FileName = "pw-record",
    //                Arguments = string.Join(" ", pipe),
    //                RedirectStandardInput = true,
    //                RedirectStandardOutput = true,
    //                RedirectStandardError = true,
    //                UseShellExecute = false,
    //                CreateNoWindow = true
    //            };
    //            var source = Process.Start(psi1)!;
    ////   const proc = spawn(
    ////     bin,
    ////     [
    ////       "-y",
    ////       "-f",
    ////       "s16le",
    ////       "-ar",
    ////       "16000",
    ////       "-ac",
    ////       "1",
    ////       "-i",
    ////       "pipe:0",
    ////       "-acodec",
    ////       "pcm_s16le",
    ////       "-ar",
    ////       "16000",
    ////       "-ac",
    ////       "1",
    ////       "-f",
    ////       "wav",
    ////       file,
    ////     ],
    ////     {
    ////       stdio: ["pipe", "ignore", "pipe"],
    ////     },
    ////   )
    //            var psi2 = new ProcessStartInfo
    //            {
    //                FileName = bin,
    //                Arguments = $"-y -f s16le -ar 16000 -ac 1 -i pipe:0 -acodec pcm_s16le -ar 16000 -ac 1 -f wav \"{file}\"",
    //                RedirectStandardInput = true,
    //                RedirectStandardOutput = true,
    //                RedirectStandardError = true,
    //                UseShellExecute = false,
    //                CreateNoWindow = true
    //            };
    //            var proc = Process.Start(psi2)!;
    ////
    ////   if (source.stdout && proc.stdin) source.stdout.pipe(proc.stdin)
    //            if (source.StandardOutput != null && proc.StandardInput != null)
    //            {
    //                source.StandardOutput.BaseStream.CopyTo(proc.StandardInput.BaseStream);
    //            }
    ////   source.on("error", (err) => proc.emit("error", err))
    //            source.EnableRaisingEvents = true;
    //            source.Exited += (s, e) => { if (!proc.HasExited) proc.Kill(); };
    ////   source.stderr?.on("data", (data: Buffer) => proc.stderr?.emit("data", data))
    //            source.ErrorDataReceived += (s, e) => { if (e.Data != null && proc.StandardError != null) proc.StandardError.Write(e.Data); };
    //            source.BeginErrorReadLine();
    ////   source.once("exit", () => {
    //            source.Exited += (s, e) =>
    ////     if (proc.stdin?.writable) proc.stdin.end()
    //            {
    //                if (proc.StandardInput != null && !proc.StandardInput.BaseStream.IsClosed)
    //                    proc.StandardInput.Close();
    ////   })
    //            };
    ////   proc.once("exit", () => {
    //            proc.Exited += (s, e) =>
    ////     if (!source.killed) source.kill("SIGTERM")
    //            {
    //                if (!source.HasExited)
    //                    source.Kill();
    ////   })
    //            };
    ////
    ////   return proc
    //            proc.EnableRaisingEvents = true;
    //            return proc;
    //// }
    //        }
    //
    // async function stopProcess(state: Recording): Promise<void> {
    private async Task StopProcess(Recording state)
    // {
    {
      //   if (state.proc.exitCode !== null || state.proc.signalCode) return
      if (state.Proc.HasExited) return;
      //
      //   await new Promise<void>((resolve) => {
      var tcs = new TaskCompletionSource<object?>();
      var timer = new System.Timers.Timer(2000);
      //     const timer = setTimeout(() => {
      timer.Elapsed += (s, e) =>
      //       if (!state.proc.killed) state.proc.kill("SIGKILL")
      {
        timer.Stop();
        if (!state.Proc.HasExited)
          state.Proc.Kill();
        //       resolve()
        tcs.TrySetResult(false);
        //     }, 2000)
      };
      timer.Start();
      //
      //     state.proc.once("exit", () => {
      state.Proc.Exited += (s, e) =>
      //       clearTimeout(timer)
      //       resolve()
      {
        timer.Stop();
        tcs.TrySetResult(false);
        //     })
      };
      state.Proc.EnableRaisingEvents = true;
      //
      //     if (state.proc.stdin?.writable) {
      if (state.Proc.StandardInput != null && !state.InputStreamClosed)
      //       state.proc.stdin.write("q")
      {
        state.Proc.StandardInput.Write("q");
        //       state.proc.stdin.end()
        state.Proc.StandardInput.Close();
        state.InputStreamClosed = true;
        //       return
        tcs.TrySetResult(true);
        //     }
      }
      //
      //     state.proc.kill("SIGTERM")
      else
      {
        state.Proc.Kill();
        //   })
      }
      // }
    }
    //
    // function requireActive(requestId: string): Recording {
    private Recording RequireActive(string requestId)
    // {
    {
      //   if (!active || active.requestId !== requestId) throw new Error("No active speech recording")
      if (_active == null || _active.RequestId != requestId)
        throw new InvalidOperationException("No active speech recording");
      //   return active
      return _active;
      // }
    }
    //
    // async function resolveFFmpeg(): Promise<string> {
    private async Task<string> ResolveFFmpeg()
    // {
    {
      //   const cached = ffmpeg
      var cached = _ffmpeg;
      //   if (cached) {
      if (cached != null)
      //     const bin = await cached
      {
        var bin = await cached;
        //     if (!path.isAbsolute(bin) || existsSync(bin)) return bin
        if (!Path.IsPathRooted(bin) || File.Exists(bin)) return bin;
        //   }
      }
      //
      //   const task = findFFmpeg()
      var task = FindFFmpeg();
      //   ffmpeg = task
      _ffmpeg = task;
      //   try {
      try
      //     return await task
      {
        return await task;
        //   } catch (err) {
      }
      catch
      //     if (ffmpeg === task) ffmpeg = undefined
      {
        if (_ffmpeg == task) _ffmpeg = null;
        //     throw err
        throw;
        //   }
      }
      // }
    }
    //
    // async function findFFmpeg(): Promise<string> {
    private async Task<string> FindFFmpeg()
    // {
    {
      //   const paths = [
      var paths = new List<string?>
            {
//     process.env.KILO_FFMPEG_PATH,
                Environment.GetEnvironmentVariable("KILO_FFMPEG_PATH"),
//     process.env.FFMPEG_PATH,
                Environment.GetEnvironmentVariable("FFMPEG_PATH"),
//     bundledPath(),
                BundledPath(),
//     ...platformPaths(),
            };
      paths.AddRange(PlatformPaths());
      //     "ffmpeg",
      paths.Add("ffmpeg");
      //   ].filter(Boolean)
      paths = paths.Where(p => !string.IsNullOrEmpty(p)).ToList();
      //
      //   for (const bin of paths) {
      foreach (var bin in paths)
      //     if (!bin) continue
      {
        if (string.IsNullOrEmpty(bin)) continue;
        //     if (path.isAbsolute(bin) && !existsSync(bin)) continue
        if (Path.IsPathRooted(bin) && !File.Exists(bin)) continue;
        //     try {
        try
        //       await exec(bin, ["-version"], { timeout: 3000 })
        {
          await RunCommand(bin, new[] { "-version" }, 3000);
          //       return bin
          return bin;
          //     } catch (err) {
        }
        //       console.warn(`[Kilo New] FFmpeg candidate failed: ${bin}`, err)
        catch (Exception err)
        //     }
        {
          Console.WriteLine($"[Kilo New] FFmpeg candidate failed: {bin} - {err.Message}");
        }
      }
      //   }
      //
      //   throw new Error("Speech input needs the bundled FFmpeg helper, but it was not found. Rebuild or reinstall Kilo Code.")
      throw new Exception("Speech input needs the bundled FFmpeg helper, but it was not found. Rebuild or reinstall Kilo Code.");
      // }
    }
    //
    // function bundledPath(): string {
    private string BundledPath()
    // {
    {
      //   return path.join(__dirname, "..", "bin", process.platform === "win32" ? "ffmpeg.exe" : "ffmpeg")
      return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "ffmpeg.exe");
      // }
    }
    //
    // function platformPaths(): string[] {
    private static List<string> PlatformPaths()
    // {
    {
      ////   if (process.platform === "darwin")
      //            if (Environment.OSVersion.Platform == PlatformID.MacOS)
      ////     return ["/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg", "/opt/local/bin/ffmpeg"]
      //                return new List<string> { "/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg", "/opt/local/bin/ffmpeg" };
      //   if (process.platform === "win32") {
      //            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
      ////     return [
      //            {
      return new List<string>
//       "C:\\ffmpeg\\bin\\ffmpeg.exe",
                {
                    "C:\\ffmpeg\\bin\\ffmpeg.exe",
//       process.env.ProgramFiles ? path.join(process.env.ProgramFiles, "ffmpeg", "bin", "ffmpeg.exe") : undefined,
                    Environment.GetEnvironmentVariable("ProgramFiles") != null
                        ? Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles")!, "ffmpeg", "bin", "ffmpeg.exe")
                        : null,
//       process.env["ProgramFiles(x86)"]
//         ? path.join(process.env["ProgramFiles(x86)"], "ffmpeg", "bin", "ffmpeg.exe")
//         : undefined,
                    Environment.GetEnvironmentVariable("ProgramFiles(x86)") != null
                        ? Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles(x86)")!, "ffmpeg", "bin", "ffmpeg.exe")
                        : null,
//       process.env.USERPROFILE
//         ? path.join(process.env.USERPROFILE, "scoop", "apps", "ffmpeg", "current", "bin", "ffmpeg.exe")
//         : undefined,
                    Environment.GetEnvironmentVariable("USERPROFILE") != null
                        ? Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE")!, "scoop", "apps", "ffmpeg", "current", "bin", "ffmpeg.exe")
                        : null,
//       "C:\\ProgramData\\chocolatey\\bin\\ffmpeg.exe",
                    "C:\\ProgramData\\chocolatey\\bin\\ffmpeg.exe",
//     ].filter((item): item is string => !!item)
                }.Where(p => p != null).Cast<string>().ToList();
      //   }
      //            }
      ////   return ["/usr/bin/ffmpeg", "/usr/local/bin/ffmpeg", "/snap/bin/ffmpeg", "/home/linuxbrew/.linuxbrew/bin/ffmpeg"]
      //            return new List<string> { "/usr/bin/ffmpeg", "/usr/local/bin/ffmpeg", "/snap/bin/ffmpeg", "/home/linuxbrew/.linuxbrew/bin/ffmpeg" };
      // }
    }
    //
    // async function inputArgSets(bin: string): Promise<Args[]> {
    private async Task<List<Args>> GetInputArgSets(string bin)
    // {
    {
      ////   if (process.platform === "darwin") return [{ input: ["-f", "avfoundation", "-i", ":default"] }]
      //            if (Environment.OSVersion.Platform == PlatformID.MacOS)
      //                return new List<Args> { new Args { Input = new[] { "-f", "avfoundation", "-i", ":default" } } };
      ////   if (process.platform === "linux") {
      //            if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX)
      ////     const device = process.env.KILO_FFMPEG_AUDIO_DEVICE
      //            {
      //                var device = Environment.GetEnvironmentVariable("KILO_FFMPEG_AUDIO_DEVICE");
      ////     if (device)
      //                if (!string.IsNullOrEmpty(device))
      ////       return [
      //                    return new List<Args>
      ////         { pipe: ["--target", device, "--format", "s16", "--rate", "16000", "--channels", "1", "-"], input: [] },
      //                    {
      //                        new Args { Pipe = new[] { "--target", device, "--format", "s16", "--rate", "16000", "--channels", "1", "-" }, Input = Array.Empty<string>() },
      ////         { input: ["-f", "pulse", "-i", device] },
      //                        new Args { Input = new[] { "-f", "pulse", "-i", device } },
      ////         { input: ["-f", "alsa", "-i", device] },
      //                        new Args { Input = new[] { "-f", "alsa", "-i", device } },
      ////       ]
      //                    };
      ////     return [
      //                return new List<Args>
      ////       { pipe: ["--format", "s16", "--rate", "16000", "--channels", "1", "-"], input: [] },
      //                {
      //                    new Args { Pipe = new[] { "--format", "s16", "--rate", "16000", "--channels", "1", "-" }, Input = Array.Empty<string>() },
      ////       { input: ["-f", "pulse", "-i", "default"] },
      //                    new Args { Input = new[] { "-f", "pulse", "-i", "default" } },
      ////       { input: ["-f", "alsa", "-i", "default"] },
      //                    new Args { Input = new[] { "-f", "alsa", "-i", "default" } },
      ////     ]
      //                };
      ////   }
      //            }
      //   if (process.platform === "win32") return await windowsInputArgSets(bin)
      //            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
      return await WindowsInputArgSets(bin);
      ////   return []
      //            return new List<Args>();
      // }
    }
    //
    // async function windowsInputArgSets(bin: string): Promise<Args[]> {
    private static async Task<List<Args>> WindowsInputArgSets(string bin)
    // {
    {
      //   const configured = process.env.KILO_FFMPEG_AUDIO_DEVICE
      var configured = Environment.GetEnvironmentVariable("KILO_FFMPEG_AUDIO_DEVICE");
      //   if (configured) return [{ input: ["-f", "dshow", "-i", `audio=${configured}`] }]
      if (!string.IsNullOrEmpty(configured))
        return new List<Args> { new Args { Input = new[] { "-f", "dshow", "-i", $"audio={configured}" } } };
      //
      //   const devices = await listDshowAudioDevices(bin)
      var devices = await ListDshowAudioDevices(bin);
      //   if (devices.length === 0) throw new Error("No Windows audio input devices found for speech input")
      if (devices.Count == 0)
        throw new Exception("No Windows audio input devices found for speech input");
      //   return devices.map((device) => ({ input: ["-f", "dshow", "-i", `audio=${device}`] }))
      return devices.Select(device => new Args { Input = new[] { "-f", "dshow", "-i", $"audio={device}" } }).ToList();
      // }
    }
    //
    // async function listDshowAudioDevices(bin: string): Promise<string[]> {
    private static async Task<List<string>> ListDshowAudioDevices(string bin)
    // {
    {
      //   const raw = await exec(bin, ["-list_devices", "true", "-f", "dshow", "-i", "dummy"], { timeout: 5000 })
      string raw;
      try
      //     .then((result) => `${result.stdout}\n${result.stderr}`)
      {
        var result = await RunCommandWithOutput(bin, new[] { "-list_devices", "true", "-f", "dshow", "-i", "dummy" }, 5000);
        raw = $"{result.Output}\n{result.Error}";
        //     .catch((err: unknown) => processOutput(err))
      }
      catch (Exception err)
      {
        raw = ProcessOutput(err);
      }
      //   return parseDshowAudioDevices(raw)
      return ParseDshowAudioDevices(raw);
      // }
    }
    //
    // export function parseDshowAudioDevices(raw: string): string[] {
    public static List<string> ParseDshowAudioDevices(string raw)
    // {
    {
      //   const devices = new Set<string>()
      var devices = new HashSet<string>();
      //   const state = { audio: false }
      var audio = false;
      //   const legacy = /"([^"]+)"\s+\(audio\)/
      var legacy = new Regex(@"""([^""]+)""\s+\(audio\)");
      //   const quoted = /"([^"]+)"/
      var quoted = new Regex(@"""([^""]+)""");
      //   const section = (line: string) => /DirectShow audio devices/i.test(line)
      var section = new Regex(@"DirectShow audio devices", RegexOptions.IgnoreCase);
      //   const other = (line: string) => /DirectShow (video|external) devices/i.test(line)
      var other = new Regex(@"DirectShow (video|external) devices", RegexOptions.IgnoreCase);
      //   const alt = (line: string) => /\]\s+Alternative name\s+"/i.test(line)
      var alt = new Regex(@"\]\s+Alternative name\s+""", RegexOptions.IgnoreCase);
      //   for (const line of raw.split(/\r?\n/)) {
      foreach (var line in raw.Split('\n', '\r'))
      //     const match = legacy.exec(line)
      {
        var match = legacy.Match(line);
        //     if (match) devices.add(match[1]!)
        if (match.Success)
          devices.Add(match.Groups[1].Value);
        //
        //     if (section(line)) {
        if (section.IsMatch(line))
        //       state.audio = true
        {
          audio = true;
          //       continue
          continue;
          //     }
        }
        //     if (other(line)) {
        if (other.IsMatch(line))
        //       if (!state.audio) continue
        {
          if (!audio) continue;
          //       state.audio = false
          audio = false;
          //       break
          break;
          //     }
        }
        //     if (!state.audio || alt(line)) continue
        if (!audio || alt.IsMatch(line)) continue;
        //     const found = quoted.exec(line)
        var found = quoted.Match(line);
        //     if (found) devices.add(found[1]!)
        if (found.Success)
          devices.Add(found.Groups[1].Value);
        //   }
      }
      //   return [...devices]
      return devices.ToList();
      // }
    }
    //
    // function processOutput(err: unknown): string {
    private static string ProcessOutput(Exception err)
    // {
    {
      //   if (!err || typeof err !== "object") return ""
      var dyn = err as dynamic;
      //   const result = err as { stdout?: unknown; stderr?: unknown }
      var stdout = dyn?.stdout?.ToString() ?? "";
      //   return `${typeof result.stdout === "string" ? result.stdout : ""}\n${typeof result.stderr === "string" ? result.stderr : ""}"
      var stderr = dyn?.stderr?.ToString() ?? "";
      return $"{stdout}\n{stderr}";
      // }
    }
    //
    // function summary(state: Recording, fallback: string): string {
    private static string Summary(Recording state, string fallback)
    // {
    {
      //   const stderr = cleanOutput(state.stderr.join("\n"))
      var stderr = CleanOutput(string.Join("\n", state.Stderr));
      //   if (!stderr) return fallback
      if (string.IsNullOrEmpty(stderr)) return fallback;
      //   return `${fallback}: ${stderr.slice(-800)}`
      return $"{fallback}: {stderr.Substring(Math.Max(0, stderr.Length - 800))}";
      // }
    }
    //
    // export function cleanOutput(raw: string): string {
    public static string CleanOutput(string raw)
    // {
    {
      //   const lines = raw
      var lines = raw
          //     .split(/\r?\n/)
          .Split('\n', '\r')
          //     .map((line) => line.trim())
          .Select(l => l.Trim())
          //     .filter(Boolean)
          .Where(l => !string.IsNullOrEmpty(l))
          //     .filter((line) => !/^(ffmpeg version|built with|configuration:|lib[a-z]+\s+\d)/i.test(line))
          .Where(l => !Regex.IsMatch(l, @"^(ffmpeg version|built with|configuration:|lib[a-z]+\s+\d)", RegexOptions.IgnoreCase))
          .ToList();
      //   return lines.join("\n").trim()
      return string.Join("\n", lines).Trim();
      // }
    }
    //
    // async function removeFile(file: string): Promise<void> {
    private static async Task RemoveFile(string file)
    // {
    {
      //   await unlink(file).catch((err: unknown) => {
      try
      //     console.warn("[Kilo New] Failed to remove speech recording", err)
      {
        if (File.Exists(file))
          File.Delete(file);
        //   })
      }
      catch (Exception err)
      {
        Console.WriteLine($"[Kilo New] Failed to remove speech recording: {err.Message}");
      }
      // }
    }
    //
    private static async Task RunCommand(string exe, string[] args, int timeout)
    {
      var psi = new ProcessStartInfo
      {
        FileName = exe,
        Arguments = string.Join(" ", args),
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };

      using var proc = Process.Start(psi)!;
      await Task.WhenAny(proc.WaitForExitAsync(), Task.Delay(timeout));

      if (!proc.HasExited)
        proc.Kill();

      if (!proc.HasExited)
        throw new TimeoutException("Command timed out");
    }
    //
    private static async Task<(string Output, string Error)> RunCommandWithOutput(string exe, string[] args, int timeout)
    {
      var psi = new ProcessStartInfo
      {
        FileName = exe,
        Arguments = string.Join(" ", args),
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };

      using var proc = Process.Start(psi)!;
      var outputTask = proc.StandardOutput.ReadToEndAsync();
      var errorTask = proc.StandardError.ReadToEndAsync();

      await Task.WhenAny(proc.WaitForExitAsync(), Task.Delay(timeout));

      if (!proc.HasExited)
        proc.Kill();

      var output = await outputTask;
      var error = await errorTask;

      return (output, error);
    }
  }
}
