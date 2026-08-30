using KiloExtensionDTOs.Sessions;
using KiloExtensionDTOs.WebviewMessages;
using KiloVisualStudioExtension.ApiClient;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Interop;
using Message = KiloExtensionDTOs.Sessions.Message;

namespace KiloVisualStudioExtension
{
  internal class VisibleTaskStreams
  {
    private readonly Dictionary<string, long> refs = new();
    private bool _active = true;

    internal Action<string, bool> Set; 

  public VisibleTaskStreams(Action<string, bool> set) 
  {
      Set = set;
  }

  internal void Clear()
    {
      foreach(var key in refs.Keys)
      {
        Set(key, false);
      }
      refs.Clear();
    }

    internal void Delete(string id)
    {
      Set(id, false);
      refs.Remove(id);
    }

    internal void SetActive(bool active)
    {
      if (_active == active) return;
      _active = active;
      foreach (var id in refs.Keys)
        Set(id, active);
    }

    internal IDisposable BindPanel(KiloWebViewControl panel, System.Action focus)
    {
      SetActive(panel.IsFocused);
      panel.GotFocus += (object sender, System.Windows.RoutedEventArgs e) =>
      {
        SetActive(true);
        focus.Invoke();
      };
      panel.LostFocus += (object sender, System.Windows.RoutedEventArgs e) =>
      {
        SetActive(false);
        focus.Invoke();
      };
    }

    internal bool Handle(StreamSessionVisibleMessage message)
    {
      //const msg = parse(message)
      //  if (!msg) return false
      if (message == null) return false;
      // if (typeof msg.sessionID !== "string" || typeof msg.visible !== "boolean") return true
      // Message is strong typed, and even in ts code, the message type is checked so returning true here is nonsense.
      if (string.IsNullOrEmpty(message.SessionID)) return true;
      var count = refs.ContainsKey(message.SessionID) ? refs[message.SessionID] : 0;
      var next = message.Visible ? count + 1 : Math.Max(0, count - 1);
      if (next == 0)
        refs.Remove(message.SessionID);
      else
        refs[message.SessionID] = next;
      Set(message.SessionID, _active && (next > 0));
      return true;
    }
  }
}
