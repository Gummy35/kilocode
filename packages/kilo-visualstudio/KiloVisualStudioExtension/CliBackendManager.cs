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
    /// Manages the Kilo CLI backend process lifecycle.
    /// Spawns 'kilo serve' command, monitors its output to extract the port,
    /// and provides health check capabilities.
    /// 
    /// This class handles:
    /// - Locating the CLI binary (bundled, development, or PATH)
    /// - Starting the process with appropriate environment variables
    /// - Parsing the port from stdout output
    /// - Waiting for the backend to be ready via health endpoint
    /// - Graceful process termination on disposal
    /// </summary>
    public class CliBackendManager : IDisposable
    {
        /// <summary>
        /// Singleton instance for easy access from other classes.
        /// </summary>
        private static CliBackendManager? _instance;
        
        /// <summary>
        /// The underlying CLI process.
        /// </summary>
        private Process? _process;
        
        /// <summary>
        /// The base URL where the backend is listening (e.g., "http://127.0.0.1:9999").
        /// </summary>
        private string? _baseUrl;
        
        /// <summary>
        /// Semaphore to ensure only one startup attempt at a time.
        /// </summary>
        private readonly SemaphoreSlim _initSemaphore = new(1, 1);
        
        /// <summary>
        /// Flag indicating whether the object has been disposed.
        /// </summary>
        private bool _isDisposed;

        /// <summary>
        /// Gets the base URL where the backend is listening.
        /// Returns null if the backend has not started yet.
        /// </summary>
        public string? BaseUrl => _baseUrl;

        /// <summary>
        /// Extracts the port number from the base URL.
        /// </summary>
        /// <returns>The port number, or null if not available.</returns>
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
        /// Set when StartAsync is called.
        /// </summary>
        public static CliBackendManager? Instance => _instance;

        /// <summary>
        /// Starts the CLI backend process asynchronously.
        /// Locates the CLI binary, starts the process, and waits for it to be ready.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the startup process.</param>
        /// <returns>A task representing the asynchronous startup operation.</returns>
        /// <exception cref="TimeoutException">Thrown if the backend fails to start within 30 seconds.</exception>
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
                    Arguments = "serve --port 0 --print-logs",
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

        /// <summary>
        /// Locates the Kilo CLI binary to execute.
        /// Searches in the following order:
        /// 1. Bundled CLI in the extension directory
        /// 2. Development CLI in packages/opencode/dist
        /// 3. CLI from system PATH (fallback)
        /// </summary>
        /// <returns>The path to the CLI binary or "kilo" for PATH lookup.</returns>
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

        /// <summary>
        /// Event handler for standard output data from the CLI process.
        /// Parses the output to extract the port number from the "listening on" message.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The data received event arguments.</param>
        private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data))
                return;
            System.Diagnostics.Debug.WriteLine($"Kilo Cli: {e.Data}");
            // Parse port from output: "listening on http://127.0.0.1:PORT"
            var match = Regex.Match(e.Data, @"listening on http://127\.0\.0\.1:(\d+)");
            if (match.Success)
            {
                var port = match.Groups[1].Value;
                _baseUrl = $"http://127.0.0.1:{port}";
                System.Diagnostics.Debug.WriteLine($"Kilo backend started on {_baseUrl}");
            }
        }

        /// <summary>
        /// Event handler for standard error data from the CLI process.
        /// Logs error messages for debugging.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The data received event arguments.</param>
        private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                System.Diagnostics.Debug.WriteLine($"Kilo backend error: {e.Data}");
            }
        }

        /// <summary>
        /// Waits for the backend port to become available and the health endpoint to respond.
        /// Polls the /global/health endpoint with a 30-second timeout.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the wait operation.</param>
        /// <returns>A task representing the asynchronous wait operation.</returns>
        /// <exception cref="TimeoutException">Thrown if the backend fails to start within 30 seconds.</exception>
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

        /// <summary>
        /// Disposes of the CLI backend manager and terminates the backend process.
        /// Kills the process if still running and cleans up resources.
        /// </summary>
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
