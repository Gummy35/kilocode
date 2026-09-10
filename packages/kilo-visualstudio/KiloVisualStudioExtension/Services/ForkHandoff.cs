using System.Collections.Generic;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services
{
    public interface IKiloClient
    {
        ISessionService Session { get; }
    }

    public interface ISessionService
    {
        Task PromptAsync(SessionPromptRequest request, PromptOptions options);
    }

    public class SessionPromptRequest
    {
        public string SessionID { get; set; }
        public string Directory { get; set; }
        public bool NoReply { get; set; }
        public List<Part> Parts { get; set; }
    }

    public class Part
    {
        public string Type { get; set; }
        public string Text { get; set; }
        public bool Synthetic { get; set; }
    }

    public class PromptOptions
    {
        public bool ThrowOnError { get; set; }
    }

    public static class ForkHandoff
    {
        public static string GetForkText(string directory)
        {
            var lines = new List<string>
            {
                "<system-reminder>",
                "This session was forked from an existing session in the current repository or worktree."
            };

            if (!string.IsNullOrEmpty(directory))
            {
                lines.Add($"Use this as the current working directory: {directory}");
                lines.Add("For this fork, this location supersedes any earlier repository or worktree location retained in the copied context.");
            }

            lines.Add("The prior conversation context was retained intentionally.");
            lines.Add("The user may continue the same task, explore an alternative approach, or provide new instructions.");
            lines.Add("Follow the user's next instruction as the direction for this fork, using retained context when relevant.");
            lines.Add("</system-reminder>");

            return string.Join("\n", lines);
        }

        public static async Task RecordForkHandoffAsync(IKiloClient client, string sessionId, string directory)
        {
            var payload = new SessionPromptRequest
            {
                SessionID = sessionId,
                Directory = directory,
                NoReply = true,
                Parts = new List<Part>
                {
                    new Part
                    {
                        Type = "text",
                        Text = GetForkText(directory),
                        Synthetic = true
                    }
                }
            };

            await client.Session.PromptAsync(payload, new PromptOptions { ThrowOnError = true });
        }
    }
}
