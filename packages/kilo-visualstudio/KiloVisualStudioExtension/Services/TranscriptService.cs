using KiloExtensionDTOs.ExtensionMessages;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.Services.Handlers.Ui;
using KiloVisualStudioExtension.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services
{
    public class TranscriptService : ServiceProviderServiceBase
    {
        public class TranscriptItem
        {
            public Message Info { get; set; } = null!;
            public List<ApiClient.Part> Parts { get; set; } = new();
        }

    private VSProvider Provider => _serviceProvider.GetService<VSProvider>()
        ?? throw new InvalidOperationException("VSProvider not registered in service provider");

    public TranscriptService(ServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    public async Task HandleExportSessionTranscriptAsync(string sessionID)
    {
        var client = Provider.GetNswagClient();
        if (client == null)
        {
            Provider.PostMessage(new ErrorMessage { Message = "Not connected to CLI backend" });
            return;
        }

        try
        {
            var saved = await ExportTranscriptAsync(client,
                new TranscriptInput
                {
                    SessionId = sessionID,
                    Dir = _serviceProvider.GetService<ProjectDirectoryProvider>().GetWorkspaceDirectory(sessionID),
                });
            if (saved)
                _serviceProvider.GetService<UiService>().ShowMessage("Session transcript exported as Markdown.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Kilo New] KiloProvider: Failed to export session transcript: {ex.Message}");
            Provider.PostMessage(new ErrorMessage { Message = ErrorHelper.GetErrorMessage(ex) });
        }
    }

    public async Task<bool> ExportTranscriptAsync(KiloApiClient client, TranscriptInput input)
    {
        var sessionTask = client.Session_getAsync(input.SessionId, input.Dir, "");
        var pageTask = FetchMessagePage(client, new MessagePageRequest
        {
            SessionId = input.SessionId,
            WorkspaceDir = input.Dir,
            Limit = 0
        });

        await Task.WhenAll(sessionTask, pageTask);
        var session = sessionTask.Result;
        var page = pageTask.Result;

        var text = FormatTranscript(session, page.Items);
        var defaultFileName = $"session-{session.Id.Substring(0, 8)}.md";
        var defaultPath = System.IO.Path.Combine(input.Dir, defaultFileName);
        var filePath = await _serviceProvider.GetService<UiService>().ShowSaveFileDialogAsync(
            defaultPath, new[] { "md", "markdown" }, "Markdown (*.md)|*.md|All files (*.*)|*.*");

        if (string.IsNullOrEmpty(filePath)) return false;

        await AsyncUtils.WriteAllTextAsync(filePath, text, Encoding.UTF8);
        return true;
    }
    public static string FormatTranscript(Session2 session, List<TranscriptItem> items)
    {
        var headLines = new List<string>
        {
            $"# {session.Title}",
            "",
            $"**Session ID:** {session.Id}",
            $"**Created:** {DateTimeOffset.FromUnixTimeMilliseconds(session.Time.Created).LocalDateTime:G}",
            $"**Updated:** {DateTimeOffset.FromUnixTimeMilliseconds(session.Time.Updated).LocalDateTime:G}",
            "",
            "---",
            "",
            ""
        };
        var head = string.Join("\n", headLines);
        var body = string.Join("---\n\n", items.Select(item => FormatMessage(item)));
        return $"{head}{body}{(items.Count > 0 ? "---\n\n" : "")}";
    }

    private static string FormatMessage(TranscriptItem item)
    {
        var head = item.Info is UserMessage ? "## User\n\n" : "## Assistant\n\n";
        return $"{head}{string.Join("", item.Parts.Select(part => FormatPart(part)))}";
    }

    private static string FormatPart(ApiClient.Part part)
    {
        if (part is TextPart textPart && !textPart.Synthetic) return $"{textPart.Text}\n\n";
        if (part is ToolPart toolPart) return $"**Tool: {toolPart.Tool}**\n\n";
        return "";
    }

    public class TranscriptInput
    {
        public string SessionId { get; set; } = "";
        public string Dir { get; set; } = "";
    }

    public class MessagePageResult
    {
        public List<TranscriptItem> Items { get; set; } = new();
    }

    private static Task<MessagePageResult> FetchMessagePage(KiloApiClient client, MessagePageRequest request)
    {
        return Task.FromResult(new MessagePageResult());
    }

    public class MessagePageRequest
    {
        public string SessionId { get; set; } = "";
        public string WorkspaceDir { get; set; } = "";
        public int Limit { get; set; }
    }
  }
}
