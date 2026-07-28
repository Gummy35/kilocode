using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;

namespace KiloVisualStudioExtension
{
    /// <summary>
    /// Manages the Kilo CLI backend process.
    /// Spawns 'kilo serve' and tracks its port.
    /// </summary>
    public class CliBackendManager : IDisposable
    {
        private static CliBackendManager? _instance;
        private Process? _process;
        private string? _baseUrl;
        private readonly SemaphoreSlim _initSemaphore = new(1, 1);
        private bool _isDisposed;

        public string? BaseUrl => _baseUrl;

        public int? GetPort()
        {
            if (string.IsNullOrEmpty(_baseUrl))
                return null;
            try
            {
                var uri = new Uri(_baseUrl);
                return uri.Port;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Static instance for easy access from other classes.
        /// </summary>
        public static CliBackendManager? Instance => _instance;

        /// <summary>
        /// Start the CLI backend process.
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _initSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (_process != null)
                    return;

                _instance = this;

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                // Determine CLI binary path
                var cliPath = GetCliBinaryPath();
                if (!File.Exists(cliPath))
                {
                    System.Diagnostics.Debug.WriteLine($"Kilo CLI not found at {cliPath}. Extension UI will show a message.");
                    _baseUrl = "http://127.0.0.1:9999";
                    return;
                }

                // Start the process
                var startInfo = new ProcessStartInfo
                {
                    FileName = cliPath,
                    Arguments = "serve --port 0",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Environment.CurrentDirectory
                };

                // Set environment variables
                startInfo.EnvironmentVariables["KILO_CLIENT"] = "visualstudio";
                startInfo.EnvironmentVariables["KILO_PLATFORM"] = "visualstudio";
                startInfo.EnvironmentVariables["KILO_APP_NAME"] = "kilo-code";

                _process = new Process { StartInfo = startInfo };
                
                _process.OutputDataReceived += OnOutputDataReceived;
                _process.ErrorDataReceived += OnErrorDataReceived;
                _process.EnableRaisingEvents = true;

                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();

                // Wait for port to be available
                await WaitForPortAsync(cancellationToken);
            }
            finally
            {
                _initSemaphore.Release();
            }
        }

        private string GetCliBinaryPath()
        {
            // Try multiple locations:
            // 1. Bundled in extension directory
            var extensionDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location);
            
            if (extensionDir != null)
            {
                var bundledCli = Path.Combine(extensionDir, "kilo-cli", "bin", "kilo.exe");
                if (File.Exists(bundledCli))
                    return bundledCli;
            }

            // 2. From packages/opencode/dist (development)
            var repoRoot = Path.GetFullPath(Path.Combine(extensionDir ?? ".", "..", "..", ".."));
            repoRoot = Path.Combine("c:\\", "prog", "kilocode", "kilocode", "packages");
            var devCli = Path.Combine(repoRoot, "opencode", "dist", "@kilocode", "cli-windows-x64", "bin", "kilo.exe");
            if (File.Exists(devCli))
                return devCli;

            // 3. From PATH (fallback)
            return "kilo";
        }

        private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data))
                return;

            // Parse port from output: "listening on http://127.0.0.1:PORT"
            var match = Regex.Match(e.Data, @"listening on http://127\.0\.0\.1:(\d+)");
            if (match.Success)
            {
                var port = match.Groups[1].Value;
                _baseUrl = $"http://127.0.0.1:{port}";
                System.Diagnostics.Debug.WriteLine($"Kilo backend started on {_baseUrl}");
            }
        }

        private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                System.Diagnostics.Debug.WriteLine($"Kilo backend error: {e.Data}");
            }
        }

        private async Task WaitForPortAsync(CancellationToken cancellationToken)
        {
            var timeout = TimeSpan.FromSeconds(30);
            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.Elapsed < timeout)
            {
                if (!string.IsNullOrEmpty(_baseUrl))
                {
                    // Verify the server is responding
                    try
                    {
                        using var client = new HttpClient();
                        client.Timeout = TimeSpan.FromSeconds(2);
                        var response = await client.GetAsync($"{_baseUrl}/global/health", cancellationToken);
                        if (response.IsSuccessStatusCode)
                            return;
                    }
                    catch
                    {
                        // Server not ready yet, continue waiting
                    }
                }

                await Task.Delay(500, cancellationToken);
            }

            throw new TimeoutException("Kilo backend failed to start within timeout period.");
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            if (_process != null)
            {
                try
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill();
                        _process.WaitForExit(5000);
                    }
                }
                catch
                {
                    // Process may already be exited
                }

                _process.OutputDataReceived -= OnOutputDataReceived;
                _process.ErrorDataReceived -= OnErrorDataReceived;
                _process.Dispose();
                _process = null;
            }

            _initSemaphore.Dispose();
        }
    }
}
