using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;
using System.Threading;
using System.Threading.Tasks;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using Task = System.Threading.Tasks.Task;

namespace KiloVisualStudioExtension
{
    /// <summary>
    /// Command handler for opening the Kilo Code settings panel.
    /// Registers a command that opens the SettingsToolWindow for configuring extension settings.
    /// </summary>
    internal sealed class OpenSettingsCommand
    {
        /// <summary>
        /// Command ID for the open settings command.
        /// </summary>
        public const int CommandId = 0x0101;
        
        /// <summary>
        /// Command set GUID for the open settings command.
        /// </summary>
        public static readonly Guid CommandSet = new Guid("8a8f8e8c-1234-5678-9abc-def012345679");

        /// <summary>
        /// Creates a new instance of the command handler and registers it with the command service.
        /// </summary>
        /// <param name="package">The AsyncPackage instance.</param>
        /// <param name="commandService">The menu command service for registration.</param>
        private OpenSettingsCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        /// <summary>
        /// Singleton instance of the command handler.
        /// </summary>
        public static OpenSettingsCommand Instance { get; private set; }

        /// <summary>
        /// Initializes the command handler. Must be called during package initialization.
        /// </summary>
        /// <param name="package">The AsyncPackage instance.</param>
        /// <returns>A task representing the asynchronous initialization operation.</returns>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new OpenSettingsCommand(package, commandService);
        }

        /// <summary>
        /// Executes the command by finding and showing the Settings tool window.
        /// Sets the pending tab to "models" and must be called on the UI thread.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event arguments.</param>
        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var window = (SettingsToolWindow)KiloProvider.Package.FindToolWindow(typeof(SettingsToolWindow), 0, true);
            if (window?.Frame == null)
            {
                throw new NotSupportedException("Cannot create tool window");
            }
            window.SetPendingTab("models");
            IVsWindowFrame windowFrame = (IVsWindowFrame)window.Frame;
            Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());
        }
    }
}
