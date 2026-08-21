using System;
using System.Threading;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services.Handlers.CloudSession
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
        /// Fetches cloud session data and handles timeout/errors gracefully.
        /// 
        /// VS Code workflow: Matches the pattern in kilo-provider/handlers/cloud-session.ts
        /// where cloud session data is fetched with timeout handling.
        /// 
        /// Workflow steps:
        /// 1. Create cancellation token with specified timeout
        /// 2. Call client.GetSessionAsync with cancellation token
        /// 3. On success, session data is returned
        /// 4. On timeout, post cloudSessionImportFailed with timeout error
        /// 5. On other exceptions, post cloudSessionImportFailed with error message
        /// 
        /// Messages sent to webview:
        /// - cloudSessionImportFailed: { cloudSessionId, error: "The operation timed out" | ex.Message }
        /// </summary>
        /// <param name="context">The context for cloud session operations containing workspace directory and message posting capability.</param>
        /// <param name="sessionId">The unique identifier of the cloud session to fetch.</param>
        /// <param name="timeoutMs">The timeout in milliseconds (default: 5000ms).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
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
        /// Imports a cloud session into the local workspace with timeout handling.
        /// 
        /// VS Code workflow: Matches the pattern in kilo-provider/handlers/cloud-session.ts
        /// where handleImportAndSend imports session with mode (replace/fork) and timeout.
        /// 
        /// Workflow steps:
        /// 1. Create cancellation token with specified timeout
        /// 2. Call client.ImportSessionAsync with workspace directory and cancellation token
        /// 3. On success, session is imported to workspace
        /// 4. On timeout, post cloudSessionImportFailed with timeout error
        /// 5. On other exceptions, post cloudSessionImportFailed with error message
        /// 
        /// Messages sent to webview:
        /// - cloudSessionImportFailed: { cloudSessionId, error: "The operation timed out" | ex.Message }
        /// </summary>
        /// <param name="context">The context for cloud session operations containing workspace directory and message posting capability.</param>
        /// <param name="sessionId">The unique identifier of the cloud session to import.</param>
        /// <param name="mode">The import mode (e.g., "replace", "fork").</param>
        /// <param name="timeoutMs">The timeout in milliseconds (default: 5000ms).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
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

