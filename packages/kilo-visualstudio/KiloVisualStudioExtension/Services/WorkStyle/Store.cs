using KiloExtensionDTOs;
using System;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services.WorkStyle
{
  public class Store
  {
    public Func<Task<WorkStyleConfig>> ReadAsync { get; set; }
    public Func<string, Task<SettingsSnapshot>> InspectAsync { get; set; }
    public Func<string, object?, Task> WriteAsync { get; set; }
    public Func<WorkStyleConfig, Task> PatchAsync { get; set; }
  }

}
