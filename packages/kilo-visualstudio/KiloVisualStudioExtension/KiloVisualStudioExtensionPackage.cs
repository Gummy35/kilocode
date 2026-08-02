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
    /// Panel view types for the settings editor tool window.
    /// Each view corresponds to a different settings category.
    /// </summary>
    public enum PanelView 
    { 
        /// <summary>
        /// General settings view.
        /// </summary>
        Settings, 
        
        /// <summary>
        /// User profile settings view.
        /// </summary>
        Profile, 
        
        /// <summary>
        /// Code indexing configuration view.
        /// </summary>
        Indexing 
    }

    /// <summary>
    /// Static class providing access to the main extension package instance.
    /// Used by other classes to access Visual Studio services.
    /// </summary>
    public static class KiloProvider
    {
        /// <summary>
        /// The main AsyncPackage instance for the extension.
        /// Set during package initialization.
        /// </summary>
        public static AsyncPackage Package { get; set; }
    }

    /// <summary>
    /// Main package class for the Kilo Code Visual Studio extension.
    /// This is the entry point that implements the package exposed by this assembly.
    /// 
    /// Responsibilities:
    /// - Registers tool windows (KiloToolWindow, SettingsToolWindow)
    /// - Initializes commands (ShowKiloWindow, OpenSettings, ToolbarCommands)
    /// - Creates and manages the shared KiloConnectionService singleton
    /// - Manages the CLI backend lifecycle via CliBackendManager
    /// 
    /// The package uses lazy backend startup - the CLI backend only starts when
    /// the first provider calls ConnectAsync(), reducing startup time.
    /// </summary>
    [ProvideAutoLoad(UIContextGuids.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(KiloToolWindow), Style = VsDockStyle.Tabbed, Window = ToolWindowGuids.SolutionExplorer)]
    [ProvideToolWindow(typeof(SettingsToolWindow), Style = VsDockStyle.MDI, Window = ToolWindowGuids.SolutionExplorer)]
    [Guid(KiloVisualStudioExtensionPackage.KiloCodePackageString)]
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    public sealed class KiloVisualStudioExtensionPackage : AsyncPackage
    {
        /// <summary>
        /// Unique GUID for the Kilo Code package.
        /// </summary>
        public const string KiloCodePackageString = "8a8f8e8c-1234-5678-9abc-def012345678";

        #region Package Members

        /// <summary>
        /// Manages the Kilo CLI backend process lifecycle.
        /// Static field for access from other classes.
        /// </summary>
        private static CliBackendManager? _backendManager;
        
        /// <summary>
        /// Shared connection service for managing CLI backend connection.
        /// Static field for access from other classes.
        /// </summary>
        private static KiloConnectionService? _connectionService;

        /// <summary>
        /// Get the shared connection service instance.
        /// The backend starts lazily when the first provider calls ConnectAsync().
        /// </summary>
        /// <returns>The singleton KiloConnectionService instance.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the service has not been initialized yet.</exception>
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
        /// This method should be called during package initialization.
        /// The backend starts lazily on the first ConnectAsync() call to minimize startup time.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel initialization.</param>
        /// <returns>A task representing the asynchronous initialization operation.</returns>
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
        /// Initialization of the package; called right after the package is sited.
        /// This method registers all commands and initializes the connection service.
        /// The CLI backend starts lazily when the first provider connects.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the initialization process.</param>
        /// <param name="progress">Progress reporter for initialization status.</param>
        /// <returns>A task representing the asynchronous initialization operation.</returns>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            KiloProvider.Package = this;

            System.Diagnostics.Debug.WriteLine("=== KiloVisualStudioExtensionPackage InitializeAsync started ===");

            // Register command to show tool window
            await ShowKiloWindowCommand.InitializeAsync(this);

            // Register command to open settings
            await OpenSettingsCommand.InitializeAsync(this);

            // Register toolbar commands
            await KiloToolbarCommands.InitializeAsync(this);

            // Initialize connection service (backend starts lazily on first connect)
            await InitializeConnectionServiceAsync(cancellationToken);

            System.Diagnostics.Debug.WriteLine("=== KiloVisualStudioExtensionPackage InitializeAsync completed ===");
        }

        /// <summary>
        /// Finds a settings tool window by view type.
        /// Currently returns null - implementation pending.
        /// </summary>
        /// <param name="package">The AsyncPackage instance.</param>
        /// <param name="view">The desired panel view type.</param>
        /// <returns>The SettingsToolWindow instance, or null if not found.</returns>
        public static SettingsToolWindow? FindSettingsToolWindow(AsyncPackage package, PanelView view)
        {
            // Helper to find a settings tool window by view type
            // This is used by the SettingsEditorProvider to manage panels
            return null;
        }

        /// <summary>
        /// Disposes of package resources.
        /// Cleans up the connection service and backend manager.
        /// </summary>
        /// <param name="disposing">True if called from Dispose, false from finalizer.</param>
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
