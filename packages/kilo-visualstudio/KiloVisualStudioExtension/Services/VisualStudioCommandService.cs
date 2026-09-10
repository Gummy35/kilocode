using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace KiloVisualStudioExtension.Services
{
  public sealed class VisualStudioCommandService:ServiceProviderServiceBase
  {
    private readonly DTE _dte;

    public VisualStudioCommandService(ServiceProvider serviceProvider):base(serviceProvider)
    {
      _dte = serviceProvider.GetDTE();
    }

    public async Task ExecuteCommandAsync(
        string commandName,
        string? arguments = null)
    {
      await ThreadHelper.JoinableTaskFactory
          .SwitchToMainThreadAsync();
      _dte.ExecuteCommand(
          commandName,
          arguments ?? string.Empty);
    }
  }
}
