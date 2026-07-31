using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension
{
    [Guid("8a8f8e8c-1234-5678-9abc-def012345680")]
    public class KiloToolWindow : ToolWindowPane
    {
        private KiloWebViewControl? _webView;
        private KiloConnectionService? _connectionService;
        private VSProvider? _vsProvider;

        public KiloWebViewControl? WebView => _webView;

        public KiloToolWindow() : base(null)
        {
            Caption = "Kilo Code";
            _webView = new KiloWebViewControl();
            Content = _webView;
            
            _ = InitializeWebViewAsync();
        }

        private async Task InitializeWebViewAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            await _webView!.InitializeAsync();
            
            var backendManager = CliBackendManager.Instance;
            if (backendManager != null)
            {
                _connectionService = new KiloConnectionService(backendManager);
                _webView.SetConnectionService(_connectionService);
                _vsProvider = new VSProvider(_webView, _connectionService);
                await _connectionService.ConnectAsync();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _connectionService?.Dispose();
                _webView?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
