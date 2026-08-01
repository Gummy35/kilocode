using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;

namespace KiloVisualStudioExtension
{
    /// <summary>
    /// KiloClaw panel provider - chat panel in the editor area.
    /// Matches VS Code's KiloClawProvider pattern.
    /// 
    /// Note: Full implementation requires KiloChatClient and EventServiceClient
    /// which are specific to the KiloClaw feature. This is a stub that provides
    /// the panel infrastructure.
    /// </summary>
    public class KiloClawProvider : IDisposable
    {
        public const string ViewType = "kilo-code.new.KiloClawPanel";
        
        private KiloToolWindow? _panel;
        private readonly KiloConnectionService _connectionService;
        private readonly Uri _extensionUri;
        private bool _disposed;

        /// <summary>
        /// Constructor.
        /// </summary>
        public KiloClawProvider(Uri extensionUri, KiloConnectionService connectionService)
        {
            _extensionUri = extensionUri;
            _connectionService = connectionService;
        }

        /// <summary>
        /// Open the KiloClaw panel.
        /// </summary>
        public void OpenPanel()
        {
            if (_panel != null)
            {
                // Panel already open - show it
                ShowPanel(_panel);
                return;
            }

            // Create new panel as a tool window
            _panel = new KiloToolWindow();
            _panel.Caption = "KiloClaw";
            
            ShowPanel(_panel);
        }

        /// <summary>
        /// Restore a panel after Visual Studio restart.
        /// </summary>
        public void RestorePanel(KiloToolWindow panel)
        {
            _panel = panel;
        }

        private void ShowPanel(KiloToolWindow toolWindow)
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                
                var frame = toolWindow.Frame as Microsoft.VisualStudio.Shell.Interop.IVsWindowFrame;
                frame?.Show();
            });
        }

        /// <summary>
        /// Dispose resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            _panel?.Dispose();
            _panel = null;
        }
    }
}
