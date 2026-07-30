using System;

namespace KiloVisualStudioExtension.Services
{
    /// <summary>
    /// Initial message request from Agent Manager.
    /// </summary>
    public class SendInitialMessage
    {
        public string Type { get; set; } = "";
        public string SessionId { get; set; } = "";
        public string WorktreeId { get; set; } = "";
        public string? Text { get; set; }
        public string? ProviderId { get; set; }
        public string? ModelId { get; set; }
        public string? Agent { get; set; }
        public string? Variant { get; set; }
    }

    /// <summary>
    /// Send message payload for webview.
    /// </summary>
    public class SendMessage
    {
        public string Type { get; set; } = "sendMessage";
        public string? Text { get; set; }
        public string? SessionId { get; set; }
        public string? ProviderId { get; set; }
        public string? ModelId { get; set; }
        public string? Agent { get; set; }
        public string? Variant { get; set; }
        public object? Files { get; set; }
    }

    /// <summary>
    /// Session variant state.
    /// </summary>
    public class SessionVariant
    {
        public string SessionId { get; set; } = "";
        public string ProviderId { get; set; } = "";
        public string ModelId { get; set; } = "";
        public string Agent { get; set; } = "";
        public string Value { get; set; } = "";
    }

    /// <summary>
    /// Utility methods for Agent Manager initial message handling.
    /// </summary>
    public static class InitialMessageHandler
    {
        /// <summary>
        /// Creates initial sendMessage payload from SendInitialMessage.
        /// Returns null if no text is provided.
        /// </summary>
        public static SendMessage? CreateInitialMessage(SendInitialMessage msg)
        {
            if (string.IsNullOrEmpty(msg.Text))
                return null;

            return new SendMessage
            {
                Text = msg.Text,
                SessionId = msg.SessionId,
                ProviderId = msg.ProviderId,
                ModelId = msg.ModelId,
                Agent = msg.Agent,
                Variant = msg.Variant,
                Files = null
            };
        }

        /// <summary>
        /// Builds initial variant state for a session.
        /// Returns null if provider, model, or variant is missing.
        /// </summary>
        public static SessionVariant? CreateInitialVariant(SendInitialMessage msg, string agent)
        {
            if (string.IsNullOrEmpty(msg.ProviderId) || 
                string.IsNullOrEmpty(msg.ModelId) || 
                string.IsNullOrEmpty(msg.Variant))
                return null;

            return new SessionVariant
            {
                SessionId = msg.SessionId,
                ProviderId = msg.ProviderId,
                ModelId = msg.ModelId,
                Agent = agent,
                Value = msg.Variant
            };
        }
    }
}
