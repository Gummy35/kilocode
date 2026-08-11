using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Test-only CLI launcher that starts the Kilo CLI process.
    /// This shares the same logic as CliBackendManager but without Visual Studio dependencies.
    /// </summary>
    internal class CliLauncher : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private Process? _process;
        private string? _baseUrl;
        private string? _password;
        private bool _disposed;

        public string? BaseUrl => _baseUrl;
        public string? Password => _password;

        public CliLauncher(ITestOutputHelper output)
        {
            _output = output;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var cliPath = FindCliBinary();
            _password = GenerateRandomPassword();

            var startInfo = new ProcessStartInfo
            {
                FileName = cliPath,
                Arguments = "serve --port 0 --print-logs",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetTempPath()
            };

            // Set the same environment variables as CliBackendManager
            startInfo.EnvironmentVariables["KILO_SERVER_PASSWORD"] = _password;
            startInfo.EnvironmentVariables["KILO_CLIENT"] = "test";
            startInfo.EnvironmentVariables["KILO_PLATFORM"] = "test";
            startInfo.EnvironmentVariables["KILO_PARENT_PID"] = Process.GetCurrentProcess().Id.ToString();
            startInfo.EnvironmentVariables["MIMALLOC_PURGE_DELAY"] = "0";
            startInfo.EnvironmentVariables["NODE_USE_SYSTEM_CA"] = "1";

            _process = new Process { StartInfo = startInfo };
            _process.OutputDataReceived += OnOutputDataReceived;
            _process.ErrorDataReceived += OnErrorDataReceived;
            _process.EnableRaisingEvents = true;

            _output.WriteLine($"Starting CLI from: {cliPath}");
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            // Wait for port to be discovered (same logic as CliBackendManager.WaitForPortAsync)
            await WaitForPortAsync(cancellationToken);
        }

        private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data))
                return;
            _output.WriteLine($"CLI stdout: {e.Data}");
            
            // Parse port from output: "listening on http://127.0.0.1:PORT"
            var match = Regex.Match(e.Data, @"listening on http://127\.0\.0\.1:(\d+)");
            if (match.Success && !string.IsNullOrEmpty(_password))
            {
                var port = match.Groups[1].Value;
                _baseUrl = $"http://127.0.0.1:{port}";
                _output.WriteLine($"CLI backend started on {_baseUrl}");
            }
        }

        private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _output.WriteLine($"CLI stderr: {e.Data}");
            }
        }

        private async Task WaitForPortAsync(CancellationToken cancellationToken)
        {
            var timeout = TimeSpan.FromSeconds(30);
            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.Elapsed < timeout)
            {
                if (!string.IsNullOrEmpty(_baseUrl))
                    break;

                if (_process != null && _process.HasExited)
                    throw new TimeoutException("CLI process exited before reporting port");

                await Task.Delay(500, cancellationToken);
            }

            if (string.IsNullOrEmpty(_baseUrl))
                throw new TimeoutException("CLI failed to start and report port within timeout");
        }

        private string FindCliBinary()
        {
            // Try development build location - use fixed repo root path
            var repoRoot = @"C:\prog\kilocode\kilocode";
            var devCliPath = Path.Combine(repoRoot, "packages", "opencode", "dist", "@kilocode", "cli-windows-x64", "bin", "kilo.exe");

            _output.WriteLine($"Checking CLI path: {devCliPath}");

            if (File.Exists(devCliPath))
            {
                _output.WriteLine($"Found CLI at: {devCliPath}");
                return devCliPath;
            }

            // Try bundled location
            var assemblyDir = Path.GetDirectoryName(typeof(CliLauncher).Assembly.Location);
            if (assemblyDir != null)
            {
                var bundledCli = Path.Combine(assemblyDir, "kilo-cli", "bin", "kilo.exe");
                _output.WriteLine($"Checking bundled CLI path: {bundledCli}");
                if (File.Exists(bundledCli))
                {
                    _output.WriteLine($"Found bundled CLI at: {bundledCli}");
                    return bundledCli;
                }
            }

            throw new SkipTestException($"Kilo CLI not found. Build the CLI first at: {devCliPath}");
        }

        private static string GenerateRandomPassword()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create()!)
            {
                rng.GetBytes(bytes);
            }
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            if (_process != null)
            {
                try
                {
                    if (!_process.HasExited)
                    {
                        _output.WriteLine($"Terminating CLI process {_process.Id}");
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
        }
    }
}

/// <summary>
/// Exception to skip tests when CLI is not available
/// </summary>
public class SkipTestException : Exception
{
    public SkipTestException(string message) : base(message) { }
}
