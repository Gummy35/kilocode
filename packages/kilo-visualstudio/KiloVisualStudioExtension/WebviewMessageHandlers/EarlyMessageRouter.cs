using KiloExtensionDTOs;
using KiloExtensionDTOs.WebviewMessages;
using KiloVisualStudioExtension.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KiloVisualStudioExtension.WebviewMessageHandlers
{
  internal class EarlyMessageRouter
  {
    internal class Ctx
    {
      internal SuggestionContext Question { get; }
      internal IKiloClient Client { get; }
      internal KiloConnectionService Connection { get; }
      internal string Directory { get; }
      internal Action<IWebviewMessage> Post { get; }
      internal Func<string, Task> ExportTranscript {  get; }
      internal Action<List<string>> OpenSessions { get; }    
    }

    public static async Task<bool> Route(IWebviewMessage message, Ctx ctx)
    {
      await SuggestionWebviewMessage.Route(ctx.Question, message);
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
      return await InputToolMessage.Route(message, ctx);
    }
  }
}
