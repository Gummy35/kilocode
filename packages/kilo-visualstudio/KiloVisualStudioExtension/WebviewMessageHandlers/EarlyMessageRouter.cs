using KiloExtensionDTOs;
using KiloExtensionDTOs.WebviewMessages;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KiloVisualStudioExtension.WebviewMessageHandlers
{
  public class EarlyMessageRouter
  {
    public class Ctx
    {
      public SuggestionHandler.ISuggestionContext Question { get; set; }
      public KiloApiClient Client { get; set; }
      public KiloConnectionService Connection { get; set; }
      public string Directory { get; set; }
      public ModelState.PostMessage Post { get; set; }
      public Func<string, Task> ExportTranscript { get; set; }
      public Action<List<string>> OpenSessions { get; set; }
      public ServiceProvider ServiceProvider { get; set; }
    }

    public static async Task<bool> RouteWebviewMessage(IWebviewMessage message, Ctx ctx)
    {
      await SuggestionHandler.RouteWebviewMessage(ctx.Question, message);
      if (await ModelState.HandleMessage(message, ctx.Client, ctx.Post)) return true;
      if (message is ExportSessionTranscriptRequest exportMessage)
      {
        if (!string.IsNullOrEmpty(exportMessage.SessionID))
          await (ctx.ExportTranscript(exportMessage.SessionID));
        return true;
      }
      if (message is SidebarOpenSessionsMessage sidebarOpenSessionsMessage)
      {
        var ids = sidebarOpenSessionsMessage.SessionIDs.Where(s => !string.IsNullOrEmpty(s)).ToList();
        ctx.OpenSessions(ids);
        return true;
      }
      return await InputTools.RouteWebviewMessage(message, ctx);
    }
  }
}
