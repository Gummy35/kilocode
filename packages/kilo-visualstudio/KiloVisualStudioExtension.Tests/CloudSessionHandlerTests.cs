using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for cloud session handler
    /// Mirrors: cloud-session-handler.test.ts from VS Code
    /// </summary>
    public class CloudSessionHandlerTests
    {
        private class CloudSessionContext
        {
            public MockClient Client { get; set; } = new MockClient();
            public string? CurrentSession { get; set; }
            public HashSet<string> TrackedSessionIds { get; set; } = new HashSet<string>();
            public List<object> SentMessages { get; set; } = new List<object>();
            public string WorkspaceDirectory { get; set; } = "/repo";

            public void PostMessage(object message) => SentMessages.Add(message);
        }

        private class MockClient
        {
            public bool ShouldStall { get; set; }
            public bool ShouldFail { get; set; }
            public string? ErrorMessage { get; set; }

            public async Task<CloudSessionData> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
            {
                if (ShouldStall)
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }

                if (ShouldFail)
                {
                    throw new Exception(ErrorMessage ?? "Session not found");
                }

                return new CloudSessionData { Id = sessionId, Title = "Cloud Session" };
            }

            public async Task<ImportResult> ImportSessionAsync(string sessionId, string directory, CancellationToken cancellationToken = default)
            {
                if (ShouldStall)
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }

                if (ShouldFail)
                {
                    throw new Exception(ErrorMessage ?? "Import failed");
                }

                return new ImportResult { Success = true };
            }
        }

        private class CloudSessionData
        {
            public string Id { get; set; } = "";
            public string Title { get; set; } = "";
        }

        private class ImportResult
        {
            public bool Success { get; set; }
        }

        private class CloudSessionImportFailed
        {
            public string Type { get; set; } = "cloudSessionImportFailed";
            public string CloudSessionId { get; set; } = "";
            public string Error { get; set; } = "";
        }

        /// <summary>
        /// Handles request for cloud session data with timeout
        /// </summary>
        private async Task HandleRequestCloudSessionData(CloudSessionContext context, string sessionId, int timeoutMs = 5000)
        {
            using var cts = new CancellationTokenSource(timeoutMs);

            try
            {
                await context.Client.GetSessionAsync(sessionId, cts.Token);
            }
            catch (OperationCanceledException)
            {
                context.PostMessage(new CloudSessionImportFailed
                {
                    CloudSessionId = sessionId,
                    Error = "The operation timed out"
                });
            }
            catch (Exception ex)
            {
                context.PostMessage(new CloudSessionImportFailed
                {
                    CloudSessionId = sessionId,
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Handles import of cloud session with timeout
        /// </summary>
        private async Task HandleImportAndSend(CloudSessionContext context, string sessionId, string mode, int timeoutMs = 5000)
        {
            using var cts = new CancellationTokenSource(timeoutMs);

            try
            {
                await context.Client.ImportSessionAsync(sessionId, context.WorkspaceDirectory, cts.Token);
            }
            catch (OperationCanceledException)
            {
                context.PostMessage(new CloudSessionImportFailed
                {
                    CloudSessionId = sessionId,
                    Error = "The operation timed out"
                });
            }
            catch (Exception ex)
            {
                context.PostMessage(new CloudSessionImportFailed
                {
                    CloudSessionId = sessionId,
                    Error = ex.Message
                });
            }
        }

        [Fact]
        public async Task Reports_failure_when_preview_request_stalls()
        {
            // Arrange
            var context = new CloudSessionContext
            {
                Client = { ShouldStall = true }
            };

            // Act
            await HandleRequestCloudSessionData(context, "cloud-session", 50);

            // Assert
            Assert.Single(context.SentMessages);
            var failed = Assert.IsType<CloudSessionImportFailed>(context.SentMessages[0]);
            Assert.Equal("cloud-session", failed.CloudSessionId);
            Assert.Equal("The operation timed out", failed.Error);
        }

        [Fact]
        public async Task Reports_failure_when_import_request_stalls()
        {
            // Arrange
            var context = new CloudSessionContext
            {
                Client = { ShouldStall = true }
            };

            // Act
            await HandleImportAndSend(context, "cloud-session", "Continue", 50);

            // Assert
            Assert.Single(context.SentMessages);
            var failed = Assert.IsType<CloudSessionImportFailed>(context.SentMessages[0]);
            Assert.Equal("cloud-session", failed.CloudSessionId);
            Assert.Equal("The operation timed out", failed.Error);
        }

        [Fact]
        public async Task Successfully_imports_cloud_session()
        {
            // Arrange
            var context = new CloudSessionContext
            {
                Client = { ShouldStall = false, ShouldFail = false }
            };

            // Act
            await HandleImportAndSend(context, "cloud-session", "Continue", 5000);

            // Assert
            Assert.Empty(context.SentMessages); // No error message on success
        }

        [Fact]
        public async Task Reports_failure_when_import_fails()
        {
            // Arrange
            var context = new CloudSessionContext
            {
                Client = { ShouldStall = false, ShouldFail = true, ErrorMessage = "Session not found" }
            };

            // Act
            await HandleImportAndSend(context, "cloud-session", "Continue", 5000);

            // Assert
            Assert.Single(context.SentMessages);
            var failed = Assert.IsType<CloudSessionImportFailed>(context.SentMessages[0]);
            Assert.Equal("cloud-session", failed.CloudSessionId);
            Assert.Equal("Session not found", failed.Error);
        }
    }
}
