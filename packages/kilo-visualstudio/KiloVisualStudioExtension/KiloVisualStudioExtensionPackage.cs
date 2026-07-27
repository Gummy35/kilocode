using KiloVisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension
{
  /// <summary>
  /// This is the class that implements the package exposed by this assembly.
  /// </summary>
  /// <remarks>
  /// <para>
  /// The minimum requirement for a class to be considered a valid package for Visual Studio
  /// is to implement the IVsPackage interface and register itself with the shell.
  /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
  /// to do it: it derives from the Package class that provides the implementation of the
  /// IVsPackage interface and uses the registration attributes defined in the framework to
  /// register itself and its components with the shell. These attributes tell the pkgdef creation
  /// utility what data to put into .pkgdef file.
  /// </para>
  /// <para>
  /// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
  /// </para>
  /// </remarks>
  [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
  [Guid(KiloVisualStudioExtensionPackage.PackageGuidString)]
  [ProvideMenuResource("Menus.ctmenu", 1)]
  [ProvideToolWindow(
        typeof(KiloToolWindow),
        Style = VsDockStyle.Tabbed,
        Window = ToolWindowGuids.SolutionExplorer,
        MultiInstances = false,
        Transient = false)]
  [ProvideToolWindowVisibility(typeof(KiloToolWindow), PackageGuids.KiloCodeContextString)]
  public sealed class KiloVisualStudioExtensionPackage : AsyncPackage
  {
    /// <summary>
    /// KiloVisualStudioExtensionPackage GUID string.
    /// </summary>
    public const string PackageGuidString = "4f80f246-f2cc-406c-aad9-15b082a022c5";

    #region Package Members

    private CliBackendManager? _backendManager;

    /// <summary>
    /// Initialization of the package; this method is called right after the package is sited, so this is the place
    /// where you can put all the initialization code that rely on services provided by VisualStudio.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to monitor for initialization cancellation, which can occur when VS is shutting down.</param>
    /// <param name="progress">A provider for progress updates.</param>
    /// <returns>A task representing the async work of package initialization, or an already completed task if there is none. Do not return null from this method.</returns>
    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
      // When initialized asynchronously, the current thread may be a background thread at this point.
      // Do any initialization that requires the UI thread after switching to the UI thread.
      await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

      // Initialize commands
      await InitializeCommandsAsync();

      // Start CLI backend
      _backendManager = new CliBackendManager();
      await _backendManager.StartAsync(cancellationToken);

      // Initialize tool window
      await KiloToolWindow.InitializeAsync(this);
    }

    private async System.Threading.Tasks.Task InitializeCommandsAsync()
    {
      await JoinableTaskFactory.SwitchToMainThreadAsync();

      // Register command handlers here when implemented
      // Example: OleMenuCommandService commandService = (OleMenuCommandService)GetService(typeof(IMenuCommandService));
      // if (commandService != null) { ... }
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
