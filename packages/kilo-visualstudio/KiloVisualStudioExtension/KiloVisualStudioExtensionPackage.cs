using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace KiloVisualStudioExtension
{
    public static class KiloProvider
    {
        public static AsyncPackage Package { get; set; }
    }

    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
  [ProvideAutoLoad(UIContextGuids.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
  [ProvideMenuResource("Menus.ctmenu", 1)]
  [ProvideToolWindow(typeof(KiloToolWindow), Style = VsDockStyle.Tabbed, Window = ToolWindowGuids.SolutionExplorer)]
  [Guid(KiloVisualStudioExtensionPackage.KiloCodePackageString)]
  [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    public sealed class KiloVisualStudioExtensionPackage : AsyncPackage
    {
        public const string KiloCodePackageString = "8a8f8e8c-1234-5678-9abc-def012345678";

        #region Package Members

        private CliBackendManager? _backendManager;

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited.
        /// </summary>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            KiloProvider.Package = this;

            System.Diagnostics.Debug.WriteLine("=== KiloVisualStudioExtensionPackage InitializeAsync started ===");

            // Register command to show tool window
            await ShowKiloWindowCommand.InitializeAsync(this);

            // Start CLI backend
            _backendManager = new CliBackendManager();
            await _backendManager.StartAsync(cancellationToken);

            System.Diagnostics.Debug.WriteLine("=== CLI backend started ===");

            System.Diagnostics.Debug.WriteLine("=== KiloVisualStudioExtensionPackage InitializeAsync completed ===");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _backendManager?.Dispose();
            }
            base.Dispose(disposing);
        }
        #endregion
    }
}
