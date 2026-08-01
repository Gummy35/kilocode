using System;
using Microsoft.VisualStudio.Shell;

namespace KiloVisualStudioExtension
{
    /// <summary>
    /// Factory for creating provider instances with shared connection service.
    /// Matches VS Code's provider creation pattern where all providers receive
    /// the same KiloConnectionService instance.
    /// </summary>
    public static class ProviderFactory
    {
        /// <summary>
        /// Create VSProvider for sidebar tool window.
        /// </summary>
        public static VSProvider CreateSidebarProvider(KiloWebViewControl webView)
        {
            var connectionService = KiloVisualStudioExtensionPackage.GetConnectionService();
            var provider = new VSProvider(webView, connectionService);
            return provider;
        }

        /// <summary>
        /// Create VSProvider for tab panel (Open in Tab).
        /// </summary>
        public static VSProvider CreateTabPanelProvider(KiloWebViewControl webView)
        {
            var connectionService = KiloVisualStudioExtensionPackage.GetConnectionService();
            var provider = new VSProvider(webView, connectionService);
            return provider;
        }

        /// <summary>
        /// Create SettingsEditorProvider for settings panels.
        /// </summary>
        public static SettingsEditorProvider CreateSettingsProvider()
        {
            var connectionService = KiloVisualStudioExtensionPackage.GetConnectionService();
            var provider = new SettingsEditorProvider(connectionService);
            return provider;
        }

        /// <summary>
        /// Create KiloClawProvider for chat panel.
        /// </summary>
        public static KiloClawProvider CreateKiloClaw(Uri extensionUri)
        {
            var connectionService = KiloVisualStudioExtensionPackage.GetConnectionService();
            var provider = new KiloClawProvider(extensionUri, connectionService);
            return provider;
        }
    }
}
