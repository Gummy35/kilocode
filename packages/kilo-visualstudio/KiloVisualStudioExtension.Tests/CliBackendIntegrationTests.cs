using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Integration tests that start the real Kilo CLI process using CliLauncher.
    /// CliLauncher shares the same logic as CliBackendManager but without Visual Studio dependencies.
    /// These tests verify the complete authentication flow from CLI startup to health check.
    /// </summary>
    public class CliBackendIntegrationTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private CliLauncher? _cliLauncher;

        public CliBackendIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public void Dispose()
        {
            // Clean up the CLI process - IMPORTANT: always shutdown the CLI
            if (_cliLauncher != null)
            {
                _output.WriteLine($"Shutting down CLI process (port: {GetPort()})");
                _cliLauncher.Dispose();
                _cliLauncher = null;
            }
        }

        private int? GetPort()
        {
            if (string.IsNullOrEmpty(_cliLauncher?.BaseUrl))
                return null;
            try
            {
                var uri = new Uri(_cliLauncher.BaseUrl);
                return uri.Port;
            }
            catch
            {
                return null;
            }
        }

        private async Task StartCliAsync(CancellationToken cancellationToken)
        {
            _cliLauncher = new CliLauncher(_output);
            
            _output.WriteLine("Starting CLI via CliLauncher...");
            
            try
            {
                await _cliLauncher.StartAsync(cancellationToken);
            }
            catch (TimeoutException ex)
            {
                _output.WriteLine($"CLI startup failed: {ex.Message}");
                _cliLauncher.Dispose();
                _cliLauncher = null;
                throw;
            }

            if (string.IsNullOrEmpty(_cliLauncher.BaseUrl))
            {
                _cliLauncher.Dispose();
                _cliLauncher = null;
                throw new SkipTestException("CLI started but did not report a valid base URL");
            }

            _output.WriteLine($"CLI started at {_cliLauncher.BaseUrl} with password set");
        }

        [Fact]
        public async Task Cli_HealthCheckWithoutAuth_Returns401()
        {
            // Arrange
            var cancellationToken = CancellationToken.None;
            await StartCliAsync(cancellationToken);
            
            _cliLauncher.Should().NotBeNull("CLI should have started");
            _cliLauncher!.BaseUrl.Should().NotBeNullOrEmpty("Base URL should be set");
            _cliLauncher.Password.Should().NotBeNullOrEmpty("Password should be set");

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            // Act - health check WITHOUT authentication
            var response = await client.GetAsync($"{_cliLauncher.BaseUrl}/global/health");

            // Assert
            response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized,
                "health endpoint should require authentication");
        }

        [Fact]
        public async Task Cli_HealthCheckWithCorrectAuth_Succeeds()
        {
            // Arrange
            var cancellationToken = CancellationToken.None;
            await StartCliAsync(cancellationToken);
            
            _cliLauncher.Should().NotBeNull("CLI should have started");
            _cliLauncher!.BaseUrl.Should().NotBeNullOrEmpty("Base URL should be set");
            _cliLauncher.Password.Should().NotBeNullOrEmpty("Password should be set");

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            
            // Add Basic Authentication using the password from CliLauncher
            // This matches the format: "kilo:<password>"
            var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"kilo:{_cliLauncher.Password}"));
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);

            // Act
            var response = await client.GetAsync($"{_cliLauncher.BaseUrl}/global/health");

            // Assert
            response.IsSuccessStatusCode.Should().BeTrue(
                "health check with correct password should succeed");
        }

        [Fact]
        public async Task Cli_HealthCheckWithWrongAuth_Fails()
        {
            // Arrange
            var cancellationToken = CancellationToken.None;
            await StartCliAsync(cancellationToken);
            
            _cliLauncher.Should().NotBeNull("CLI should have started");
            _cliLauncher!.BaseUrl.Should().NotBeNullOrEmpty("Base URL should be set");
            _cliLauncher.Password.Should().NotBeNullOrEmpty("Password should be set");

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            
            // Use WRONG password (not the one from CliLauncher)
            var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes("kilo:wrong-password"));
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);

            // Act
            var response = await client.GetAsync($"{_cliLauncher.BaseUrl}/global/health");

            // Assert
            response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized,
                "health check with wrong password should fail");
        }

        [Fact]
        public async Task Cli_WaitForPortPattern_WithAuth_Works()
        {
            // This test verifies the exact WaitForPortAsync pattern WITH the fix
            
            // Arrange
            var cancellationToken = CancellationToken.None;
            await StartCliAsync(cancellationToken);
            
            _cliLauncher.Should().NotBeNull("CLI should have started");
            _cliLauncher!.BaseUrl.Should().NotBeNullOrEmpty("Base URL should be set");
            _cliLauncher.Password.Should().NotBeNullOrEmpty("Password should be set");

            // Simulate the FIXED WaitForPortAsync pattern (with authentication)
            var timeout = TimeSpan.FromSeconds(10);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            bool healthCheckSucceeded = false;

            while (stopwatch.Elapsed < timeout)
            {
                try
                {
                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(2);
                    
                    // THE FIX: Add authentication using password from CliLauncher
                    var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"kilo:{_cliLauncher.Password}"));
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);
                    
                    var response = await client.GetAsync($"{_cliLauncher.BaseUrl}/global/health", cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        healthCheckSucceeded = true;
                        break;
                    }
                }
                catch
                {
                    // Server not ready yet
                }

                await Task.Delay(500, cancellationToken);
            }

            // Assert
            healthCheckSucceeded.Should().BeTrue(
                "WaitForPortAsync pattern WITH authentication should succeed");
        }

        [Fact]
        public async Task Cli_WaitForPortPattern_WithoutAuth_Fails()
        {
            // This test verifies that WITHOUT the fix, the health check fails
            
            // Arrange
            var cancellationToken = CancellationToken.None;
            await StartCliAsync(cancellationToken);
            
            _cliLauncher.Should().NotBeNull("CLI should have started");
            _cliLauncher!.BaseUrl.Should().NotBeNullOrEmpty("Base URL should be set");
            _cliLauncher.Password.Should().NotBeNullOrEmpty("Password should be set");

            // Simulate the OLD WaitForPortAsync pattern WITHOUT authentication (the bug)
            var timeout = TimeSpan.FromSeconds(5);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            bool healthCheckSucceeded = false;
            int attemptCount = 0;

            while (stopwatch.Elapsed < timeout)
            {
                try
                {
                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(1);
                    
                    // NO AUTHENTICATION - this is the bug that was fixed
                    var response = await client.GetAsync($"{_cliLauncher.BaseUrl}/global/health", cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        healthCheckSucceeded = true;
                        break;
                    }
                    attemptCount++;
                }
                catch
                {
                    // Server not ready yet
                }

                await Task.Delay(500, cancellationToken);
            }

            // Assert
            healthCheckSucceeded.Should().BeFalse(
                "WaitForPortAsync pattern WITHOUT authentication should fail with 401");
            attemptCount.Should().BeGreaterThan(0,
                "should have made multiple attempts before timeout");
        }
    }
}
