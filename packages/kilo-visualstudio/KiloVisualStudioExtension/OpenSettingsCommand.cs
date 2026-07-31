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
    /// </summary>
    internal sealed class OpenSettingsCommand
    {
        public const int CommandId = 0x0101;
        public static readonly Guid CommandSet = new Guid("8a8f8e8c-1234-5678-9abc-def012345679");

        private OpenSettingsCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        public static OpenSettingsCommand Instance { get; private set; }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new OpenSettingsCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            
            // Get the KiloToolWindow instance
            ToolWindowPane window = KiloProvider.Package.FindToolWindow(typeof(KiloToolWindow), 0, true);
            if (window?.Frame == null)
            {
                System.Diagnostics.Debug.WriteLine("[Kilo] OpenSettingsCommand: Cannot find KiloToolWindow");
                return;
            }
            
            // Show the tool window if it's not already visible
            IVsWindowFrame windowFrame = (IVsWindowFrame)window.Frame;
            windowFrame.Show();
            
            // Send message to webview to navigate to settings with "models" tab
            if (window is KiloToolWindow toolWindow && toolWindow.WebView != null)
            {
                var message = System.Text.Json.JsonSerializer.Serialize(new 
                { 
                    type = "navigate",
                    view = "settings",
                    tab = "models"
                });
                toolWindow.WebView.PostMessage(message);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[Kilo] OpenSettingsCommand: KiloToolWindow WebView not available");
            }
        }
    }
}
