using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services
{
    /// <summary>
    /// Result of an MCP warmup operation.
    /// </summary>
    public class WarmupResult
    {
        public bool Success { get; set; }
    }

    /// <summary>
    /// Result of a session creation operation.
    /// </summary>
    public class CreateSessionResult
    {
        public string SessionId { get; set; } = "";
    }

    /// <summary>
    /// Parameters for creating a session.
    /// </summary>
    public class CreateSessionParams
    {
        public string SessionId { get; set; } = "";
        public string? Directory { get; set; }
    }

    /// <summary>
    /// Client interface for MCP warmup and session creation.
    /// </summary>
    public interface ISessionCreatorClient
    {
        Task<WarmupResult> WarmupMcpAsync(string sessionID);
        Task<CreateSessionResult> CreateSessionAsync(CreateSessionParams @params);
    }

    /// <summary>
    /// Service for creating sessions with MCP warmup.
    /// Ensures MCP is warmed up before session creation.
    /// </summary>
    public class SessionCreatorService
    {
        private readonly ISessionCreatorClient _client;

        public SessionCreatorService(ISessionCreatorClient client)
        {
            _client = client;
        }

        /// <summary>
        /// Creates a session after warming up MCP.
        /// MCP warmup MUST happen BEFORE session creation.
        /// </summary>
        public async Task<CreateSessionResult> CreateSession(string sessionId, string? directory)
        {
            // MCP warmup MUST happen BEFORE session creation
            await _client.WarmupMcpAsync(sessionId);
            return await _client.CreateSessionAsync(new CreateSessionParams { SessionId = sessionId, Directory = directory });
        }
    }
}
