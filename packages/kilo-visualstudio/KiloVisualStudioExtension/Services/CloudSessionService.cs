using System;
using System.Threading;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services
{
    /// <summary>
    /// Cloud session data.
    /// </summary>
    public class CloudSessionData
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
    }

    /// <summary>
    /// Result of importing a cloud session.
    /// </summary>
    public class ImportResult
    {
        public bool Success { get; set; }
    }

    /// <summary>
    /// Message sent when cloud session import fails.
    /// </summary>
    public class CloudSessionImportFailed
    {
        public string Type { get; set; } = "cloudSessionImportFailed";
        public string CloudSessionId { get; set; } = "";
        public string Error { get; set; } = "";
    }

    /// <summary>
    /// Client interface for cloud session operations.
    /// </summary>
    public interface ICloudSessionClient
    {
        Task<CloudSessionData> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);
        Task<ImportResult> ImportSessionAsync(string sessionId, string directory, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Service for handling cloud session operations with timeout.
    /// </summary>
    public class CloudSessionService
    {
        private readonly ICloudSessionClient _client;

        public CloudSessionService(ICloudSessionClient client)
        {
            _client = client;
        }

        /// <summary>
        /// Handles request for cloud session data with timeout.
        /// </summary>
        public async Task HandleRequestCloudSessionData(ICloudSessionContext context, string sessionId, int timeoutMs = 5000)
        {
            using var cts = new CancellationTokenSource(timeoutMs);

            try
            {
                await _client.GetSessionAsync(sessionId, cts.Token);
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
        /// Handles import of cloud session with timeout.
        /// </summary>
        public async Task HandleImportAndSend(ICloudSessionContext context, string sessionId, string mode, int timeoutMs = 5000)
        {
            using var cts = new CancellationTokenSource(timeoutMs);

            try
            {
                await _client.ImportSessionAsync(sessionId, context.WorkspaceDirectory, cts.Token);
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
    }

    /// <summary>
    /// Context for cloud session operations.
    /// </summary>
    public interface ICloudSessionContext
    {
        string WorkspaceDirectory { get; }
        void PostMessage(object message);
    }
}
