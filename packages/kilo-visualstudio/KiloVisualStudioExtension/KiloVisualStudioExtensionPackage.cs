using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace KiloVisualStudioExtension
{
    /// <summary>
    /// Panel view types for settings editor
    /// </summary>
    public enum PanelView { Settings, Profile, Indexing }
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
    [ProvideToolWindow(typeof(SettingsToolWindow), Style = VsDockStyle.MDI, Window = ToolWindowGuids.SolutionExplorer)]
    [Guid(KiloVisualStudioExtensionPackage.KiloCodePackageString)]
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    public sealed class KiloVisualStudioExtensionPackage : AsyncPackage
    {
        public const string KiloCodePackageString = "8a8f8e8c-1234-5678-9abc-def012345678";

        #region Package Members

        private static CliBackendManager? _backendManager;
        private static KiloConnectionService? _connectionService;
       // private SettingsEditorProvider? _settingsEditorProvider;

        /// <summary>
        /// Get the shared connection service instance.
        /// Backend starts lazily when first provider connects.
        /// </summary>
        public static KiloConnectionService GetConnectionService()
        {
            if (_connectionService == null)
            {
                throw new InvalidOperationException("KiloConnectionService not initialized. Call InitializeConnectionService first.");
            }
            return _connectionService;
        }

        /// <summary>
        /// Initialize the shared connection service and backend manager.
        /// Backend starts lazily on first ConnectAsync() call.
        /// </summary>
        public static async Task InitializeConnectionServiceAsync(CancellationToken cancellationToken)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            
            if (_connectionService != null)
            {
                System.Diagnostics.Debug.WriteLine("[Kilo] Package: connection service already initialized");
                return;
            }

            System.Diagnostics.Debug.WriteLine("[Kilo] Package: initializing connection service");
            
            _backendManager = new CliBackendManager();
            _connectionService = new KiloConnectionService(_backendManager);
            KiloConnectionService.SetInstance(_connectionService);
            
            System.Diagnostics.Debug.WriteLine("[Kilo] Package: connection service initialized (backend starts on first connect)");
        }

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited.
        /// Backend starts lazily when first provider connects.
        /// </summary>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            KiloProvider.Package = this;

            System.Diagnostics.Debug.WriteLine("=== KiloVisualStudioExtensionPackage InitializeAsync started ===");

            // Register command to show tool window
            await ShowKiloWindowCommand.InitializeAsync(this);

            // Register command to open settings
            await OpenSettingsCommand.InitializeAsync(this);

            // Initialize connection service (backend starts lazily on first connect)
            await InitializeConnectionServiceAsync(cancellationToken);

            System.Diagnostics.Debug.WriteLine("=== KiloVisualStudioExtensionPackage InitializeAsync completed ===");
        }

        //public SettingsEditorProvider? GetSettingsEditorProvider()
        //{
        //    return _settingsEditorProvider;
        //}

        public static SettingsToolWindow? FindSettingsToolWindow(AsyncPackage package, PanelView view)
        {
            // Helper to find a settings tool window by view type
            // This is used by the SettingsEditorProvider to manage panels
            return null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _connectionService?.Dispose();
                _backendManager?.Dispose();
                _connectionService = null;
                _backendManager = null;
            }
            base.Dispose(disposing);
        }
        #endregion
    }
}
