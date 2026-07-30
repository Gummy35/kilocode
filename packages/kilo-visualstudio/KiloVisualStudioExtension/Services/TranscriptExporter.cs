using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace KiloVisualStudioExtension.Services
{
    /// <summary>
    /// Represents a message in a session transcript.
    /// </summary>
    public class TranscriptMessage
    {
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Result of a transcript export operation.
    /// </summary>
    public class ExportResult
    {
        public string Format { get; set; } = "";
        public string Content { get; set; } = "";
        public int MessageCount { get; set; }
    }

    /// <summary>
    /// Service for exporting session transcripts.
    /// </summary>
    public class TranscriptExporter
    {
        /// <summary>
        /// Exports messages as markdown format.
        /// </summary>
        public ExportResult ExportAsMarkdown(IEnumerable<TranscriptMessage> messages)
        {
            var messageList = messages.ToList();
            var content = string.Join("\n\n", messageList.Select(m => $"**{m.Role}**: {m.Content}"));
            
            return new ExportResult
            {
                Format = "markdown",
                Content = content,
                MessageCount = messageList.Count
            };
        }

        /// <summary>
        /// Exports messages as JSON format.
        /// </summary>
        public ExportResult ExportAsJson(IEnumerable<TranscriptMessage> messages)
        {
            var messageList = messages.ToList();
            var content = "[" + string.Join(",", messageList.Select(m => 
                $"{{\"role\":\"{m.Role}\",\"content\":\"{m.Content}\",\"timestamp\":{m.Timestamp.Ticks}}}"
            )) + "]";
            
            return new ExportResult
            {
                Format = "json",
                Content = content,
                MessageCount = messageList.Count
            };
        }
    }
}
