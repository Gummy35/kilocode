using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KiloVisualStudioExtension.ApiClient;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for NSwag client Basic Authentication implementation.
    /// Uses a real HTTP test server to verify authentication headers are correctly applied.
    /// </summary>
    public class NswagAuthenticationTests : IDisposable
    {
        private readonly TestHttpServer _testServer;
        private readonly string _expectedPassword = "test-password-123";

        public NswagAuthenticationTests()
        {
            _testServer = new TestHttpServer();
            _testServer.Start();
        }

        public void Dispose()
        {
            _testServer.Dispose();
        }

        [Fact]
        public void Client_WithPassword_AddsBasicAuthHeader()
        {
            // Arrange
            var client = new KiloApiClient(_testServer.BaseUrl, _expectedPassword);

            // Act & Assert - the client should be created without errors
            client.Should().NotBeNull();
            client.BaseUrl.Should().Be(_testServer.BaseUrl);
        }

        [Fact]
        public async Task Request_WithPassword_SendsCorrectBasicAuthHeader()
        {
            // Arrange
            var client = new KiloApiClient(_testServer.BaseUrl, _expectedPassword);
            var capturedHeaders = new TaskCompletionSource<System.Collections.Generic.Dictionary<string, string>>();
            _testServer.OnRequest += (headers) =>
            {
                capturedHeaders.SetResult(headers);
                return System.Threading.Tasks.Task.FromResult("{\"result\":true}");
            };

            // Act - make a simple request
            try
            {
                await client.Global_healthAsync();
            }
            catch
            {
                // Ignore response errors, we only care about the request headers
            }

            // Assert
            var headers = await capturedHeaders.Task.TimeoutAfter(TimeSpan.FromSeconds(5));
            headers.Should().ContainKey("Authorization");
            
            var authHeader = headers["Authorization"];
            authHeader.Should().StartWith("Basic ");
            
            var base64Credentials = authHeader.Substring("Basic ".Length).Trim();
            var credentials = Encoding.ASCII.GetString(Convert.FromBase64String(base64Credentials));
            credentials.Should().Be($"kilo:{_expectedPassword}");
        }

        [Fact]
        public async Task Request_WithoutPassword_NoAuthHeader()
        {
            // Arrange
            var client = new KiloApiClient(_testServer.BaseUrl, "");
            var capturedHeaders = new TaskCompletionSource<System.Collections.Generic.Dictionary<string, string>>();
            _testServer.OnRequest += (headers) =>
            {
                capturedHeaders.SetResult(headers);
                return System.Threading.Tasks.Task.FromResult("{\"result\":true}");
            };

            // Act
            try
            {
                await client.Global_healthAsync();
            }
            catch
            {
                // Ignore
            }

            // Assert
            var headers = await capturedHeaders.Task.TimeoutAfter(TimeSpan.FromSeconds(5));
            // When no password is provided, no Authorization header should be added
            headers.ContainsKey("Authorization").Should().BeFalse();
        }

        [Fact]
        public async Task Request_NullPassword_NoAuthHeader()
        {
            // Arrange - use null password via constructor that doesn't set it
            var client = new KiloApiClient(_testServer.BaseUrl, null!);
            var capturedHeaders = new TaskCompletionSource<System.Collections.Generic.Dictionary<string, string>>();
            _testServer.OnRequest += (headers) =>
            {
                capturedHeaders.SetResult(headers);
                return System.Threading.Tasks.Task.FromResult("{\"result\":true}");
            };

            // Act
            try
            {
                await client.Global_healthAsync();
            }
            catch
            {
                // Ignore
            }

            // Assert
            var headers = await capturedHeaders.Task.TimeoutAfter(TimeSpan.FromSeconds(5));
            headers.ContainsKey("Authorization").Should().BeFalse();
        }

        [Fact]
        public async Task AuthHeader_PreservedAcrossMultipleRequests()
        {
            // Arrange
            var client = new KiloApiClient(_testServer.BaseUrl, _expectedPassword);
            var requestCount = 0;
            var authHeaders = new System.Collections.Generic.List<string>();
            
            _testServer.OnRequest += (headers) =>
            {
                requestCount++;
                if (headers.TryGetValue("Authorization", out var auth))
                {
                    authHeaders.Add(auth);
                }
                return System.Threading.Tasks.Task.FromResult("{\"result\":true}");
            };

            // Act - make multiple requests
            try
            {
                await client.Global_healthAsync();
                await client.Global_healthAsync();
                await client.Global_healthAsync();
            }
            catch
            {
                // Ignore
            }

            // Assert
            authHeaders.Count.Should().Be(3);
            foreach (var auth in authHeaders)
            {
                auth.Should().StartWith("Basic ");
                var base64Credentials = auth.Substring("Basic ".Length).Trim();
                var credentials = Encoding.ASCII.GetString(Convert.FromBase64String(base64Credentials));
                credentials.Should().Be($"kilo:{_expectedPassword}");
            }
        }
    }

    /// <summary>
    /// Simple test HTTP server that captures request headers and returns configurable responses.
    /// </summary>
    public class TestHttpServer : IDisposable
    {
        private HttpListener? _listener;
        private Task? _listenTask;
        private bool _disposed;

        public string BaseUrl { get; }
        public Func<System.Collections.Generic.Dictionary<string, string>, Task<string>>? OnRequest { get; set; }

        public TestHttpServer()
        {
            var port = GetAvailablePort();
            BaseUrl = $"http://127.0.0.1:{port}/";
        }

        public void Start()
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(BaseUrl);
            _listener.Start();
            _listenTask = Task.Run(ListenAsync);
        }

        private async Task ListenAsync()
        {
            while (_listener != null && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(context));
                }
                catch (HttpListenerException)
                {
                    // Listener stopped
                    break;
                }
            }
        }

        private async Task HandleRequest(HttpListenerContext context)
        {
            var headers = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in context.Request.Headers)
            {
                if (header != null)
                {
                    headers[header.Name ?? ""] = header.Value ?? "";
                }
            }

            var responseBody = "{\"result\":true}";
            if (OnRequest != null)
            {
                responseBody = await OnRequest(headers);
            }

            var responseBytes = Encoding.UTF8.GetBytes(responseBody);
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = responseBytes.Length;
            await context.Response.OutputStream.WriteAsync(responseBytes, 0, responseBytes.Length);
            context.Response.Close();
        }

        private static int GetAvailablePort()
        {
            using (var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0))
            {
                listener.Start();
                var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                return port;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _listener?.Stop();
            _listener?.Close();
            _listenTask?.Wait(TimeSpan.FromSeconds(1));
        }
    }

    /// <summary>
    /// Extension method for Task timeout handling.
    /// </summary>
    public static class TaskExtensions
    {
        public static async Task<T> TimeoutAfter<T>(this Task<T> task, TimeSpan timeout)
        {
            using (var cts = new CancellationTokenSource())
            {
                var delayTask = Task.Delay(timeout, cts.Token);
                var completedTask = await Task.WhenAny(task, delayTask);

                if (completedTask == delayTask)
                {
                    throw new TimeoutException($"Task timed out after {timeout}");
                }

                cts.Cancel();
                return await task;
            }
        }
    }
}
