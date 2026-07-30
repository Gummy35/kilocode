using System;
using System.Collections.Generic;

namespace KiloVisualStudioExtension.Services
{
    /// <summary>
    /// Represents a draft for a session.
    /// </summary>
    public class Draft
    {
        public string SessionId { get; set; } = "";
        public string Content { get; set; } = "";
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Store for managing session drafts.
    /// </summary>
    public class DraftStore
    {
        private readonly Dictionary<string, Draft> _drafts = new Dictionary<string, Draft>();

        /// <summary>
        /// Saves or updates a draft for a session.
        /// </summary>
        public void SaveDraft(string sessionId, string content)
        {
            _drafts[sessionId] = new Draft { SessionId = sessionId, Content = content };
        }

        /// <summary>
        /// Retrieves a draft for a session.
        /// </summary>
        public Draft? GetDraft(string sessionId)
        {
            _drafts.TryGetValue(sessionId, out var draft);
            return draft;
        }

        /// <summary>
        /// Deletes a draft for a session.
        /// </summary>
        public void DeleteDraft(string sessionId)
        {
            _drafts.Remove(sessionId);
        }

        /// <summary>
        /// Checks if a draft exists for a session.
        /// </summary>
        public bool HasDraft(string sessionId)
        {
            return _drafts.ContainsKey(sessionId);
        }
    }
}
