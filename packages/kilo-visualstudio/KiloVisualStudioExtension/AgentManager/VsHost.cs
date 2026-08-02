using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace KiloVisualStudioExtension.AgentManager
{
    /// <summary>
    /// Output channel handle for logging Agent Manager events.
    /// Matches VS Code's OutputHandle interface.
    /// </summary>
    public interface IOutputHandle
    {
        void AppendLine(string message);
        void Dispose();
    }

    /// <summary>
    /// Session provider interface - abstracts KiloProvider interactions for Agent Manager.
    /// Matches VS Code's SessionProvider interface.
    /// </summary>
    public interface ISessionProvider
    {
        void SetSessionDirectory(string id, string directory);
        void ClearSessionDirectory(string id);
        Dictionary<string, string> GetSessionDirectories();
        void TrackSession(string id);
        void RefreshSessions();
        void RegisterSession(object session);
        void RecoverPendingPrompts();
        void OnFollowupAdopted(Action<object, string> callback);
        void AcknowledgeDraft(string draftId, string sessionId);
        Task AbortSessions(string[] ids);
        Task ShowMemory(string? sessionId = null);
        Task ToggleMemory(string? sessionId = null);
        void Dispose();
    }

    /// <summary>
    /// Panel context - represents an open Agent Manager panel with messaging capabilities.
    /// Matches VS Code's PanelContext interface.
    /// </summary>
    public interface IPanelContext
    {
        bool Active { get; }
        bool Visible { get; }
        
        void PostMessage(object message);
        Task WaitForReady();
        Task WaitForActive();
        void Reveal(bool preserveFocus = false);
        
        ISessionProvider Sessions { get; }
        
        event Action<bool> OnDidChangeVisibility;
        event Action OnDidDispose;
        void Dispose();
    }

    /// <summary>
    /// Host interface - abstracts all Visual Studio capabilities the Agent Manager needs.
    /// This is the VS-specific implementation matching VS Code's Host interface.
    /// </summary>
    public interface IHost
    {
        IPanelContext OpenPanel(Func<Dictionary<string, object>, Task<Dictionary<string, object>?>> onBeforeMessage, Func<string[]>? worktreeDirectories = null);
        string? WorkspacePath();
        (bool enabled, string prefix) AutoBranchNaming();
        void ShowError(string message);
        Task OpenDocument(string path);
        void OpenFile(string path, int? line = null, int? column = null);
        void OpenFolder(string path, bool newWindow = false);
        IOutputHandle CreateOutput(string name);
        List<(string command, string? key, string? mac)> ExtensionKeybindings();
        int? ServerPort();
        void CopyToClipboard(string text);
        void Capture(string @event, Dictionary<string, object>? properties = null);
        void OpenExternal(string url);
        void RefreshGit();
        void Dispose();
    }

    /// <summary>
    /// Visual Studio implementation of the Host interface.
    /// Adapts VS SDK APIs to the Agent Manager's abstract Host interface.
    /// Matches VS Code's VscodeHost implementation.
    /// </summary>
    public class VsHost : IHost, IDisposable
    {
        private readonly KiloConnectionService _connectionService;
        private readonly AsyncPackage _package;
        private readonly List<IDisposable> _disposables = new List<IDisposable>();
        private bool _disposed;

        public VsHost(KiloConnectionService connectionService, AsyncPackage package)
        {
            _connectionService = connectionService;
            _package = package;
        }

        /// <summary>
        /// Open the Agent Manager panel.
        /// Creates a new tool window and wires it with session management.
        /// </summary>
        public IPanelContext OpenPanel(Func<Dictionary<string, object>, Task<Dictionary<string, object>?>> onBeforeMessage, Func<string[]>? worktreeDirectories = null)
        {
            return new AgentManagerPanelContext(this, onBeforeMessage, worktreeDirectories);
        }

        /// <summary>
        /// Get the workspace root path.
        /// </summary>
        public string? WorkspacePath()
        {
            // TODO: Implement via DTE or IVsSolution
            return null;
        }

        /// <summary>
        /// Read the user's automatic branch naming preferences.
        /// </summary>
        public (bool enabled, string prefix) AutoBranchNaming()
        {
            // TODO: Read from VS settings
            return (true, string.Empty);
        }

        /// <summary>
        /// Show an error notification.
        /// </summary>
        public void ShowError(string message)
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                System.Windows.MessageBox.Show(message, "Kilo Agent Manager", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            });
        }

        /// <summary>
        /// Open a text document in the editor.
        /// </summary>
        public async Task OpenDocument(string path)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                // TODO: Implement via DTE or IVsShell
            }
            catch
            {
                // Silently ignore - file may not exist
            }
        }

        /// <summary>
        /// Open a file at a specific location in the editor.
        /// </summary>
        public void OpenFile(string path, int? line = null, int? column = null)
        {
            // TODO: Implement via DTE or IVsWindowFrame
        }

        /// <summary>
        /// Open a folder (optionally in a new window).
        /// </summary>
        public void OpenFolder(string path, bool newWindow = false)
        {
            // TODO: Implement via VS commands
        }

        /// <summary>
        /// Create an output channel for logging.
        /// </summary>
        public IOutputHandle CreateOutput(string name)
        {
            return new VsOutputChannel(name);
        }

        /// <summary>
        /// Read extension keybinding metadata.
        /// </summary>
        public List<(string command, string? key, string? mac)> ExtensionKeybindings()
        {
            // TODO: Read from package.json equivalent
            return new List<(string, string?, string?)>();
        }

        /// <summary>
        /// Get the CLI server port (for webview CSP).
        /// </summary>
        public int? ServerPort()
        {
            return _connectionService.GetServerInfo()?.port;
        }

        /// <summary>
        /// Copy text to the system clipboard.
        /// </summary>
        public void CopyToClipboard(string text)
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var dataObject = new System.Windows.DataObject(System.Windows.DataFormats.Text, text);
                System.Windows.Clipboard.SetDataObject(dataObject, true);
            });
        }

        /// <summary>
        /// Capture a telemetry event.
        /// </summary>
        public void Capture(string @event, Dictionary<string, object>? properties = null)
        {
            // TODO: Integrate with VS telemetry
            System.Diagnostics.Debug.WriteLine($"[Kilo] Telemetry: {@event} {System.Text.Json.JsonSerializer.Serialize(properties)}");
        }

        /// <summary>
        /// Open a URL in the user's default browser.
        /// </summary>
        public void OpenExternal(string url)
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            });
        }

        /// <summary>
        /// Ask Visual Studio's git extension to re-scan repositories.
        /// </summary>
        public void RefreshGit()
        {
            // TODO: Trigger git refresh via VS commands
        }

        /// <summary>
        /// Dispose all host resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            foreach (var disposable in _disposables)
            {
                disposable.Dispose();
            }
            _disposables.Clear();
        }

        /// <summary>
        /// Visual Studio output channel implementation.
        /// </summary>
        private class VsOutputChannel : IOutputHandle, IDisposable
        {
            private readonly string _name;
            private bool _disposed;

            public VsOutputChannel(string name)
            {
                _name = name;
            }

            public void AppendLine(string message)
            {
                System.Diagnostics.Debug.WriteLine($"[{_name}] {message}");
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
            }
        }

        /// <summary>
        /// Implementation of IPanelContext for the Agent Manager panel.
        /// This is a simplified implementation that posts messages to the webview.
        /// </summary>
        private class AgentManagerPanelContext : IPanelContext, IDisposable
        {
            private readonly VsHost _host;
            private readonly Func<Dictionary<string, object>, Task<Dictionary<string, object>?>> _onBeforeMessage;
            private readonly Func<string[]>? _worktreeDirectories;
            private bool _disposed;
            private bool _ready;

            public bool Active => true; // Simplified - always active when open
            public bool Visible => true; // Simplified - always visible when open
            public ISessionProvider Sessions => new DummySessionProvider();

            public event Action<bool>? OnDidChangeVisibility;
            public event Action? OnDidDispose;

            public AgentManagerPanelContext(
                VsHost host,
                Func<Dictionary<string, object>, Task<Dictionary<string, object>?>> onBeforeMessage,
                Func<string[]>? worktreeDirectories)
            {
                _host = host;
                _onBeforeMessage = onBeforeMessage;
                _worktreeDirectories = worktreeDirectories;
                _ready = true;
                
                System.Diagnostics.Debug.WriteLine("[Kilo] AgentManagerPanelContext created");
            }

            public void PostMessage(object message)
            {
                // In a full implementation, this would post to a webview
                // For now, just log the message
                System.Diagnostics.Debug.WriteLine($"[Kilo] Agent Manager postMessage: {System.Text.Json.JsonSerializer.Serialize(message)}");
            }

            public Task WaitForReady()
            {
                return _ready ? Task.CompletedTask : Task.Delay(100);
            }

            public Task WaitForActive()
            {
                return Task.CompletedTask;
            }

            public void Reveal(bool preserveFocus = false)
            {
                System.Diagnostics.Debug.WriteLine("[Kilo] Agent Manager panel revealed");
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                OnDidDispose?.Invoke();
                System.Diagnostics.Debug.WriteLine("[Kilo] AgentManagerPanelContext disposed");
            }

            /// <summary>
            /// Dummy session provider for now - full implementation would wire to VSProvider.
            /// </summary>
            private class DummySessionProvider : ISessionProvider
            {
                public void SetSessionDirectory(string id, string directory) { }
                public void ClearSessionDirectory(string id) { }
                public Dictionary<string, string> GetSessionDirectories() => new Dictionary<string, string>();
                public void TrackSession(string id) { }
                public void RefreshSessions() { }
                public void RegisterSession(object session) { }
                public void RecoverPendingPrompts() { }
                public void OnFollowupAdopted(Action<object, string> callback) { }
                public void AcknowledgeDraft(string draftId, string sessionId) { }
                public Task AbortSessions(string[] ids) => Task.CompletedTask;
                public Task ShowMemory(string? sessionId = null) => Task.CompletedTask;
                public Task ToggleMemory(string? sessionId = null) => Task.CompletedTask;
                public void Dispose() { }
            }
        }
    }
}
