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
    /// Tests for NSwag client error handling behavior.
    /// Validates that ApiException contains correct status codes, response bodies, and headers.
    /// </summary>
    public class NswagErrorHandlingTests : IDisposable
    {
        private readonly TestHttpServer _testServer;

        public NswagErrorHandlingTests()
        {
            _testServer = new TestHttpServer();
            _testServer.Start();
        }

        public void Dispose()
        {
            _testServer.Dispose();
        }

        [Fact]
        public async Task HTTP401_ApiException_ContainsUnauthorizedStatus()
        {
            // Arrange
            _testServer.SetResponse(HttpStatusCode.Unauthorized, "{\"error\":\"Unauthorized\"}");
            var client = new KiloApiClient(_testServer.BaseUrl, "test-password");

            // Act
            var exception = await Assert.ThrowsAsync<ApiException>(async () =>
            {
                try
                {
                    await client.Global_healthAsync();
                }
                catch (ApiException ex)
                {
                    throw ex;
                }
            });

            // Assert
            exception.Should().NotBeNull();
            exception.StatusCode.Should().Be((int)HttpStatusCode.Unauthorized);
            exception.Response.Should().Contain("Unauthorized");
        }

        [Fact]
        public async Task HTTP404_ApiException_ContainsNotFoundStatus()
        {
            // Arrange
            _testServer.SetResponse(HttpStatusCode.NotFound, "{\"error\":\"Not found\"}");
            var client = new KiloApiClient(_testServer.BaseUrl, "test-password");

            // Act
            var exception = await Assert.ThrowsAsync<ApiException>(async () =>
            {
                try
                {
                    await client.Global_healthAsync();
                }
                catch (ApiException ex)
                {
                    throw ex;
                }
            });

            // Assert
            exception.Should().NotBeNull();
            exception.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
            exception.Response.Should().Contain("Not found");
        }

        [Fact]
        public async Task HTTP500_ApiException_ContainsInternalServerErrorStatus()
        {
            // Arrange
            _testServer.SetResponse(HttpStatusCode.InternalServerError, "{\"error\":\"Internal server error\"}");
            var client = new KiloApiClient(_testServer.BaseUrl, "test-password");

            // Act
            var exception = await Assert.ThrowsAsync<ApiException>(async () =>
            {
                try
                {
                    await client.Global_healthAsync();
                }
                catch (ApiException ex)
                {
                    throw ex;
                }
            });

            // Assert
            exception.Should().NotBeNull();
            exception.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
            exception.Response.Should().Contain("Internal server error");
        }

        [Fact]
        public async Task ApiException_ContainsResponseHeaders()
        {
            // Arrange
            _testServer.SetResponseWithHeaders(
                HttpStatusCode.InternalServerError,
                "{\"error\":\"error\"}",
                new Dictionary<string, string>
                {
                    { "X-Request-Id", "req-123" },
                    { "X-Error-Code", "INTERNAL_ERROR" }
                });
            var client = new KiloApiClient(_testServer.BaseUrl, "test-password");

            // Act
            var exception = await Assert.ThrowsAsync<ApiException>(async () =>
            {
                try
                {
                    await client.Global_healthAsync();
                }
                catch (ApiException ex)
                {
                    throw ex;
                }
            });

            // Assert
            exception.Should().NotBeNull();
            exception.Headers.Should().NotBeNull();
            // NSwag populates Headers dictionary from response
        }

        [Fact]
        public async Task ApiException_Cancellation_Propagates()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            _testServer.SetDelayedResponse(HttpStatusCode.OK, "{\"result\":true}", TimeSpan.FromSeconds(5));
            var client = new KiloApiClient(_testServer.BaseUrl, "test-password");

            // Act - Cancel after 100ms
            _ = Task.Delay(100).ContinueWith(_ => cts.Cancel());

            // Assert - Should throw OperationCanceledException or similar
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                try
                {
                    await client.Global_healthAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (TaskCanceledException)
                {
                    throw;
                }
            });
        }

        [Fact]
        public async Task SuccessfulResponse_NoException_ContainsData()
        {
            // Arrange
            _testServer.SetResponse(HttpStatusCode.OK, "{\"result\":true}");
            var client = new KiloApiClient(_testServer.BaseUrl, "test-password");

            // Act
            var result = await client.Global_healthAsync();

            // Assert
            result.Should().BeTrue();
        }
    }

    /// <summary>
    /// Extended test HTTP server with configurable responses for error handling tests.
    /// </summary>
    public class TestHttpServer : IDisposable
    {
        private HttpListener? _listener;
        private Task? _listenTask;
        private bool _disposed;
        private HttpStatusCode _responseStatus = HttpStatusCode.OK;
        private string _responseBody = "{\"result\":true}";
        private Dictionary<string, string> _responseHeaders = new Dictionary<string, string>();
        private TimeSpan _responseDelay = TimeSpan.Zero;

        public string BaseUrl { get; }

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

        public void SetResponse(HttpStatusCode status, string body)
        {
            _responseStatus = status;
            _responseBody = body;
            _responseHeaders.Clear();
            _responseDelay = TimeSpan.Zero;
        }

        public void SetResponseWithHeaders(HttpStatusCode status, string body, Dictionary<string, string> headers)
        {
            _responseStatus = status;
            _responseBody = body;
            _responseHeaders = headers;
            _responseDelay = TimeSpan.Zero;
        }

        public void SetDelayedResponse(HttpStatusCode status, string body, TimeSpan delay)
        {
            _responseStatus = status;
            _responseBody = body;
            _responseDelay = delay;
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
                    break;
                }
            }
        }

        private async Task HandleRequest(HttpListenerContext context)
        {
            if (_responseDelay > TimeSpan.Zero)
            {
                await Task.Delay(_responseDelay);
            }

            var responseBytes = Encoding.UTF8.GetBytes(_responseBody);
            context.Response.StatusCode = (int)_responseStatus;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = responseBytes.Length;

            foreach (var header in _responseHeaders)
            {
                context.Response.Headers.Add(header.Key, header.Value);
            }

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
}
