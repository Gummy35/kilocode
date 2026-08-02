using Microsoft.VisualStudio.Shell;
using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace KiloVisualStudioExtension
{
    /// <summary>
    /// Command handler for Kilo toolbar actions.
    /// Provides menu commands for New Task, History, Agent Manager, etc.
    /// </summary>
    internal sealed class KiloToolbarCommands
    {
        private readonly AsyncPackage _package;
        private readonly OleMenuCommandService _commandService;

        public KiloToolbarCommands(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package;
            _commandService = commandService;

            // Register all toolbar commands
            InitializeCommand(cmdidNewTask, OnNewTaskClicked);
            InitializeCommand(cmdidHistory, OnHistoryClicked);
            InitializeCommand(cmdidAgentManager, OnAgentManagerClicked);
            InitializeCommand(cmdidKiloClaw, OnKiloClawClicked);
            InitializeCommand(cmdidMarketplace, OnMarketplaceClicked);
            InitializeCommand(cmdidProfile, OnProfileClicked);
            InitializeCommand(cmdidSettings, OnSettingsClicked);
            InitializeCommand(cmdidAgentManagerShowTerminal, (s, e) => SendWebviewMessageAsync("showTerminal").Wait());
            InitializeCommand(cmdidAgentManagerRunScript, (s, e) => SendWebviewMessageAsync("runScript").Wait());
            InitializeCommand(cmdidAgentManagerNewWorktree, (s, e) => SendWebviewMessageAsync("newWorktree").Wait());
            InitializeCommand(cmdidAgentManagerPreviousSession, OnSessionPreviousClicked);
            InitializeCommand(cmdidAgentManagerNextSession, OnSessionNextClicked);
            InitializeCommand(cmdidAgentManagerPreviousTab, OnTabPreviousClicked);
            InitializeCommand(cmdidAgentManagerNextTab, OnTabNextClicked);
        }

        private async void OnSettingsClicked(object sender, EventArgs e)
        {
            await SendWebviewMessageAsync("settingsButtonClicked");
        }

        private async void OnSessionPreviousClicked(object sender, EventArgs e)
        {
            await SendWebviewMessageAsync("sessionPrevious");
        }

        private async void OnSessionNextClicked(object sender, EventArgs e)
        {
            await SendWebviewMessageAsync("sessionNext");
        }

        private async void OnTabPreviousClicked(object sender, EventArgs e)
        {
            await SendWebviewMessageAsync("tabPrevious");
        }

        private async void OnTabNextClicked(object sender, EventArgs e)
        {
            await SendWebviewMessageAsync("tabNext");
        }

        private void InitializeCommand(int commandId, EventHandler callback)
        {
            var menuCommandId = new CommandID(GuidSet, commandId);
            var menuItem = new MenuCommand(callback, menuCommandId);
            _commandService.AddCommand(menuItem);
        }

        private async void OnNewTaskClicked(object sender, EventArgs e)
        {
            await SendWebviewMessageAsync("plusButtonClicked");
        }

        private async void OnHistoryClicked(object sender, EventArgs e)
        {
            await SendWebviewMessageAsync("historyButtonClicked");
        }

        private async void OnAgentManagerClicked(object sender, EventArgs e)
        {
            await SendWebviewMessageAsync("agentManagerOpen");
        }

        private async void OnKiloClawClicked(object sender, EventArgs e)
        {
            await SendWebviewMessageAsync("kiloClawOpen");
        }

        private async void OnMarketplaceClicked(object sender, EventArgs e)
        {
            await SendWebviewMessageAsync("marketplaceButtonClicked");
        }

        private async void OnProfileClicked(object sender, EventArgs e)
        {
            await SendWebviewMessageAsync("profileButtonClicked");
        }

        private async Task SendWebviewMessageAsync(string action)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            
            var message = $"{{\"type\":\"action\",\"action\":\"{action}\"}}";
            
            // Try to find the Kilo tool window and send message to its webview
            var window = _package.FindToolWindow(typeof(KiloToolWindow), 0, false);
            if (window?.Content is KiloToolWindow kiloWindow && kiloWindow.WebView != null)
            {
                kiloWindow.WebView.PostMessage(message);
            }
            else
            {
                // If window is not open, open it first
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                window = await _package.ShowToolWindowAsync(typeof(KiloToolWindow), 0, true, _package.DisposalToken);
                if (window?.Content is KiloToolWindow newKiloWindow && newKiloWindow.WebView != null)
                {
                    // Small delay to let webview initialize
                    await Task.Delay(100);
                    newKiloWindow.WebView.PostMessage(message);
                }
            }
        }

        public static Guid GuidSet => new Guid("8a8f8e8c-1234-5678-9abc-def012345679");
        public const int cmdidNewTask = 0x0102;
        public const int cmdidHistory = 0x0103;
        public const int cmdidAgentManager = 0x0104;
        public const int cmdidKiloClaw = 0x0105;
        public const int cmdidMarketplace = 0x0106;
        public const int cmdidProfile = 0x0107;
        public const int cmdidSettings = 0x0108;
        public const int cmdidAgentManagerShowTerminal = 0x0109;
        public const int cmdidAgentManagerRunScript = 0x010A;
        public const int cmdidAgentManagerNewWorktree = 0x010E;
        public const int cmdidAgentManagerPreviousSession = 0x0113;
        public const int cmdidAgentManagerNextSession = 0x0114;
        public const int cmdidAgentManagerPreviousTab = 0x0115;
        public const int cmdidAgentManagerNextTab = 0x0116;

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            
            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService != null)
            {
                new KiloToolbarCommands(package, commandService);
            }
        }
    }
}
