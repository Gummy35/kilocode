using KiloVisualStudioExtension;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using Task = System.Threading.Tasks.Task;

namespace KiloVisualStudio
{
    /// <summary>
    /// Kilo Code tool window containing the WebView2 control.
    /// </summary>
    [Guid(PackageGuids.KiloToolWindowString)]
    public class KiloToolWindow : ToolWindowPane
    {
        private KiloWebViewControl? _webView;

        public KiloToolWindow() : base(null)
        {
            Caption = "Kilo Code";
            BitmapResourceID = 301;
            BitmapIndex = 1;
        }

        /// <summary>
        /// Initialize the tool window with the package reference.
        /// </summary>
        public static async Task InitializeAsync(KiloVisualStudioExtensionPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var window = package.FindToolWindow(typeof(KiloToolWindow), 0, true);
            if (window == null)
            {
                throw new NotSupportedException("Cannot find Kilo tool window.");
            }

            // Create WebView2 control on main thread
            var webView = new KiloWebViewControl();
            window.Content = webView;

            // Initialize WebView2 (will need to be called after window is shown)
            // Note: WebView2 initialization requires async, but we can't override OnToolWindowCreatedAsync
            // The control will initialize when first accessed
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _webView?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
