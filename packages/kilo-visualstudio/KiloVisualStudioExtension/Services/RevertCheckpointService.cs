using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services
{
    /// <summary>
    /// Information about a session revert.
    /// </summary>
    public class RevertInfo
    {
        public string MessageID { get; set; } = "";
    }

    /// <summary>
    /// Session state including revert information.
    /// </summary>
    public class Session
    {
        public string Id { get; set; } = "";
        public RevertInfo? Revert { get; set; }
    }

    /// <summary>
    /// Result of a revert operation.
    /// </summary>
    public class RevertResult
    {
        public string SessionId { get; set; } = "";
        public string MessageId { get; set; } = "";
    }

    /// <summary>
    /// Service for handling session revert operations.
    /// </summary>
    public class RevertCheckpointService
    {
        private readonly ISessionRevertClient _client;
        private Session? _currentSession;

        public RevertCheckpointService(ISessionRevertClient client)
        {
            _client = client;
        }

        public Session? CurrentSession => _currentSession;

        /// <summary>
        /// Reverts a session to a specific message.
        /// </summary>
        public async Task<RevertResult> RevertSessionAsync(string sessionID, string messageID)
        {
            var result = await _client.RevertSessionAsync(sessionID, messageID);
            
            _currentSession = new Session 
            { 
                Id = result.SessionId, 
                Revert = new RevertInfo { MessageID = result.MessageId } 
            };

            return result;
        }

        /// <summary>
        /// Unreverts a session, clearing the revert state.
        /// </summary>
        public void UnrevertSession(string sessionID)
        {
            _currentSession = new Session { Id = sessionID, Revert = null };
        }
    }

    /// <summary>
    /// Client interface for session revert operations.
    /// </summary>
    public interface ISessionRevertClient
    {
        Task<RevertResult> RevertSessionAsync(string sessionID, string messageID);
    }
}
