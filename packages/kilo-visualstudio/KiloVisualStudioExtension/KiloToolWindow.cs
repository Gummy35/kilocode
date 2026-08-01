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
            
            // Use ProviderFactory to create provider with shared connection service
            _webView.SetConnectionService(KiloVisualStudioExtensionPackage.GetConnectionService());
            _vsProvider = ProviderFactory.CreateSidebarProvider(_webView);
            
            // Connect to backend (starts lazily if not already running)
            await KiloVisualStudioExtensionPackage.GetConnectionService().ConnectAsync();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _vsProvider?.Dispose();
                _webView?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
