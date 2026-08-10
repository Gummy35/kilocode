using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KiloVisualStudioExtension.Services;
using KiloVisualStudioExtension.Services.Handlers.CloudSession;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for CloudSessionService
    /// </summary>
    public class CloudSessionHandlerTests
    {
        private class MockCloudSessionClient : ICloudSessionClient
        {
            public bool ShouldStall { get; set; }
            public bool ShouldFail { get; set; }
            public string? ErrorMessage { get; set; }

            public Task<CloudSessionData> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
            {
                if (ShouldStall)
                {
                    return Task.FromException<CloudSessionData>(new OperationCanceledException());
                }

                if (ShouldFail)
                {
                    return Task.FromException<CloudSessionData>(new Exception(ErrorMessage ?? "Session not found"));
                }

                return Task.FromResult(new CloudSessionData { Id = sessionId, Title = "Cloud Session" });
            }

            public Task<ImportResult> ImportSessionAsync(string sessionId, string directory, CancellationToken cancellationToken = default)
            {
                if (ShouldStall)
                {
                    return Task.FromException<ImportResult>(new OperationCanceledException());
                }

                if (ShouldFail)
                {
                    return Task.FromException<ImportResult>(new Exception(ErrorMessage ?? "Import failed"));
                }

                return Task.FromResult(new ImportResult { Success = true });
            }
        }

        private class MockCloudSessionContext : ICloudSessionContext
        {
            public string WorkspaceDirectory { get; set; } = "/repo";
            public List<object> SentMessages { get; } = new List<object>();

            public void PostMessage(object message) => SentMessages.Add(message);
        }

        [Fact]
        public async Task Reports_failure_when_preview_request_stalls()
        {
            // Arrange
            var client = new MockCloudSessionClient { ShouldStall = true };
            var service = new CloudSessionService(client);
            var context = new MockCloudSessionContext();

            // Act
            await service.HandleRequestCloudSessionData(context, "cloud-session", 50);

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
            var client = new MockCloudSessionClient { ShouldStall = true };
            var service = new CloudSessionService(client);
            var context = new MockCloudSessionContext();

            // Act
            await service.HandleImportAndSend(context, "cloud-session", "Continue", 50);

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
            var client = new MockCloudSessionClient { ShouldStall = false, ShouldFail = false };
            var service = new CloudSessionService(client);
            var context = new MockCloudSessionContext();

            // Act
            await service.HandleImportAndSend(context, "cloud-session", "Continue", 5000);

            // Assert
            Assert.Empty(context.SentMessages);
        }

        [Fact]
        public async Task Reports_failure_when_import_fails()
        {
            // Arrange
            var client = new MockCloudSessionClient { ShouldStall = false, ShouldFail = true, ErrorMessage = "Session not found" };
            var service = new CloudSessionService(client);
            var context = new MockCloudSessionContext();

            // Act
            await service.HandleImportAndSend(context, "cloud-session", "Continue", 5000);

            // Assert
            Assert.Single(context.SentMessages);
            var failed = Assert.IsType<CloudSessionImportFailed>(context.SentMessages[0]);
            Assert.Equal("cloud-session", failed.CloudSessionId);
            Assert.Equal("Session not found", failed.Error);
        }
    }
}
