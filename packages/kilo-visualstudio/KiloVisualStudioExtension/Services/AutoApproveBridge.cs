using KiloExtensionDTOs;
using KiloExtensionDTOs.ExtensionMessages;
using KiloExtensionDTOs.WebviewMessages;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.Services.Git;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services
{

  internal interface IAutoApproveController
  {
    bool Active();
    Task<bool> ApproveAsync(EventPermissionAsked ev, string directory);
    Task<bool> ToggleAsync();
    IDisposable OnChange(Action<bool> listener);
  }

  internal class AutoApproveBridge: IDisposable
  {
    public System.Action? Dispose { get; set; } = null;
    public Func<IWebviewMessage, Task<IWebviewMessage>>? Handle { get; set; } = null;
    void IDisposable.Dispose() => Dispose?.Invoke();
   
    public static AutoApproveBridge CreateAutoApproveBridge(IAutoApproveController controller, Action<IWebviewMessage> post, Interceptor? next)
    {
      void Send(bool active = false)
      {
        if (active == false) active = controller.Active();
        post(new AutoApproveStateMessage { Active = active });
      }

      var sub = controller.OnChange(active => Send(active));
      return new AutoApproveBridge
      {
        Dispose = () => sub.Dispose(),
        Handle = async msg =>
        {
          if (msg is ToggleAutoApproveMessage)
          {
            await controller.ToggleAsync();
            return null;
          }
          if (msg is RequestAutoApproveStateMessage)
          {
            Send();
            return null;
          }
          if (msg is WebviewReadyRequest) Send();
          return next != null ? await next(msg) : msg;
        }
      };
    }
  }
}
