using EnvDTE;
using KiloVisualStudioExtension;
using KiloVisualStudioExtension.AgentManager;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.Services;
using KiloVisualStudioExtension.Services.WorkStyle;
using MessagePack;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ApplicationInsights.Channel;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Policy;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using static KiloVisualStudioExtension.Services.CaptureService;
using static System.Net.Mime.MediaTypeNames;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
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

    private DTE _dte;

    // Keep this reference alive.
    private SolutionEvents _solutionEvents;

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


//      export function activate(context: vscode.ExtensionContext) {
//  console.log("Kilo Code extension is now active")
//  shuttingDown = false

//  const telemetry = TelemetryProxy.getInstance()

//  // Create shared connection service (one server for all webviews)
//  const connectionService = new KiloConnectionService(context)
//  const notebookBridge = createNotebookBridge(connectionService)
//  let restore = context.workspaceState.get<RestoreState>(RESTORE_KEY) ?? {}
//  const remember = (patch: RestoreState) => {
//    const next = { ...restore, ...patch }
//    if (shuttingDown && patch.agentManager === false) next.agentManager = restore.agentManager
//    restore = next
//    void context.workspaceState.update(RESTORE_KEY, restore)
//  }

//  // Create browser automation service (manages Playwright MCP registration)
//  const browserAutomationService = new BrowserAutomationService(connectionService)
//  browserAutomationService.syncWithSettings()

//  // Create remote status service (one status bar item for all webviews)
//  const remoteService = new RemoteStatusService()
//  context.subscriptions.push(remoteService)
//  connectionService.setRemoteService(remoteService)

//  // Re-register browser automation MCP server on CLI backend reconnect, configure telemetry,
//  // set remote service client, and reload autocomplete so it picks up the now-available backend connection.
//  const unsubscribeStateChange = connectionService.onStateChange((state) => {
//    if (state === "connected") {
//      browserAutomationService.reregisterIfEnabled()
//      const config = connectionService.getServerConfig()
//      if (config) {
//        telemetry.configure(config.baseUrl, config.password)
//        // Sync the CLI's PostHog client with the current consent state. The
//        // CLI reads KILO_TELEMETRY_LEVEL once at spawn, so without this call
//        // a fresh CLI started while VS Code telemetry was off would stay
//        // opted out for the rest of the session.
//        telemetry.setEnabled(vscode.env.isTelemetryEnabled)
//      }
//      try {
//        remoteService.setClient(connectionService.getClient())
//        console.log("[Kilo New] CLI connected, calling remoteService.refresh()")
//        remoteService.refresh().catch((err) => console.warn("[Kilo New] initial remote refresh failed:", err))
//      } catch {
//        remoteService.setClient(null)
//      }
//      AutocompleteServiceManager.getInstance()?.load()
//    } else {
//      remoteService.clearState()
//      remoteService.setClient(null)
//    }
//  })

//  // Propagate runtime telemetry consent changes to the CLI subprocess so its
//  // PostHog client stays in sync with the user's VS Code telemetry setting.
//  context.subscriptions.push(
//    vscode.env.onDidChangeTelemetryEnabled((enabled) => {
//      telemetry.setEnabled(enabled)
//    }),
//  )

//  for (const folder of vscode.workspace.workspaceFolders ?? []) {
//    void markWorkspace(folder.uri.fsPath, (msg) => console.warn(`[Kilo New] ${msg}`))
//  }

//  // Track all open tab panel providers so toolbar button commands can target them.
//  // NOTE: The editor/title toolbar for tab panels intentionally omits Agent Manager
//  // and Marketplace buttons (unlike the sidebar). Too many icons causes VS Code to
//  // collapse them into a "..." overflow menu, hiding important buttons like Settings.
//  const tabPanels = new Map<vscode.WebviewPanel, KiloProvider>()
//  const activeTabProvider = () => {
//    for (const [panel, p] of tabPanels) {
//      if (panel.active) return p
//    }
//    return undefined
//  }

//  // Create the provider with shared service
//  const provider = new KiloProvider(context.extensionUri, connectionService, context, {
//    focusContext: "kilo-code.new.sidebarFocused",
//  })
//  provider.setRemoteService(remoteService)

//  // Register the webview view provider for the sidebar.
//  // retainContextWhenHidden keeps the webview alive when switching to other sidebar panels.
//  context.subscriptions.push(
//    vscode.window.registerWebviewViewProvider(KiloProvider.viewType, provider, {
//      webviewOptions: { retainContextWhenHidden: true },
//    }),
//  )

//  // Ensure Agent Manager navigation keybindings work when a VS Code terminal has focus.
//  // The terminal intercepts all keystrokes unless the command is listed in
//  // terminal.integrated.commandsToSkipShell, which only contains built-in
//  // commands by default.
//  const skip = ["kilo-code.new.agentManagerOpen", "kilo-code.new.agentManager.showTerminal"]
//  if (process.platform === "darwin") skip.push("kilo-code.new.agentManager.runScript")
//  ensureCommandsSkipShell(skip)

//  // Create KiloClaw chat provider for editor panel
//  const kiloClawProvider = new KiloClawProvider(context.extensionUri, connectionService)
//  context.subscriptions.push(kiloClawProvider)

//  // Create Agent Manager provider for editor panel
//  const agentManagerHost = new VscodeHost(context.extensionUri, connectionService, context, remoteService)
//  const agentManagerProvider = new AgentManagerProvider(agentManagerHost, connectionService)
//  agentManagerProvider.onPanelVisibilityChange((visible) => remember({ agentManager: visible }))
//  agentManager = agentManagerProvider
//  context.subscriptions.push(agentManagerProvider)

//  // Wire "Continue in Worktree" from sidebar → Agent Manager
//  provider.setContinueInWorktreeHandler((sessionId, progress) =>
//    agentManagerProvider.continueFromSidebar(sessionId, progress),
//  )
//  provider.setCreateWorktreeHandler((baseBranch, branchName) =>
//    agentManagerProvider.createFromSidebar(baseBranch, branchName),
//  )

//  // Register toggle auto-approve shortcut (Ctrl+Alt+A / Cmd+Alt+A)
//  const defaultDir = () => vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ?? process.cwd()
//  const autoApprove = registerToggleAutoApprove(
//    context,
//    connectionService,
//    (sessionId) => {
//      if (sessionId) {
//        const dir =
//          provider.getSessionDirectories().get(sessionId) ?? agentManagerProvider.getSessionDirectories().get(sessionId)
//        if (dir) return dir
//      }
//      return defaultDir()
//    },
//    () => {
//      const dirs = new Set([defaultDir()])
//      for (const dir of provider.getSessionDirectories().values()) dirs.add(dir)
//      for (const dir of agentManagerProvider.getSessionDirectories().values()) dirs.add(dir)
//      return [...dirs]
//    },
//  )
//  const attention = new AttentionService(connectionService, {
//    approve: (event, directory) => autoApprove.approve(event, directory),
//  })

//  // Prewarm only after all global event consumers are ready.
//  ensureBackendForAutocomplete(connectionService)

//  provider.setAutoApproveController(autoApprove)
//  agentManagerHost.setAutoApproveController(autoApprove)

//  // Register serializer so Agent Manager restores when VS Code restarts
//  context.subscriptions.push(
//    vscode.window.registerWebviewPanelSerializer(AgentManagerProvider.viewType, {
//      deserializeWebviewPanel(panel: vscode.WebviewPanel) {
//        if (restore.agentManager === false) {
//          panel.dispose()
//          return Promise.resolve()
//        }
//        const ctx = agentManagerHost.wrapExistingPanel(panel, {
//          onBeforeMessage: (msg) => agentManagerProvider.handleMessage(msg),
//          worktreeDirectories: () => agentManagerProvider.getWorktreeDirectories(),
//        })
//        agentManagerProvider.deserializePanel(ctx)
//        return Promise.resolve()
//      },
//    }),
//  )

//  // Register serializer so KiloClaw panel restores when VS Code restarts
//  context.subscriptions.push(
//    vscode.window.registerWebviewPanelSerializer(KiloClawProvider.viewType, {
//      deserializeWebviewPanel(panel: vscode.WebviewPanel) {
//        kiloClawProvider.restorePanel(panel)
//        return Promise.resolve()
//      },
//    }),
//  )

//  // Register serializer so "Open in Tab" restores when VS Code restarts
//  context.subscriptions.push(
//    vscode.window.registerWebviewPanelSerializer("kilo-code.new.TabPanel", {
//      deserializeWebviewPanel(panel: vscode.WebviewPanel) {
//        const tabProvider = new KiloProvider(context.extensionUri, connectionService, context, {
//          tabTitle: panelTitleHandler(panel),
//        })
//        tabProvider.setRemoteService(remoteService)
//        tabProvider.setAutoApproveController(autoApprove)
//        tabProvider.setContinueInWorktreeHandler((sessionId, progress) =>
//          agentManagerProvider.continueFromSidebar(sessionId, progress),
//        )
//        tabProvider.setCreateWorktreeHandler((baseBranch, branchName) =>
//          agentManagerProvider.createFromSidebar(baseBranch, branchName),
//        )
//        tabProvider.setDiffVirtualProvider(diffVirtualProvider)
//        tabProvider.resolveWebviewPanel(panel)
//        tabPanels.set(panel, tabProvider)
//        panel.onDidDispose(
//          () => {
//            console.log("[Kilo New] Tab panel restored from restart disposed")
//            tabPanels.delete(panel)
//            tabProvider.dispose()
//          },
//          null,
//          context.subscriptions,
//        )
//        return Promise.resolve()
//      },
//    }),
//  )

//  const diffSourceCatalog = new DiffSourceCatalog(connectionService)
//  context.subscriptions.push(diffSourceCatalog)
//  const diffViewerProvider = new DiffViewerProvider(context.extensionUri, connectionService, diffSourceCatalog, {
//    sessionIdProvider: () => provider.getCurrentSessionId(),
//  })
//  diffViewerProvider.setCommentHandler((comments, autoSend) => {
//    void provider.appendReviewComments(comments, autoSend)
//  })
//  context.subscriptions.push(diffViewerProvider)

//  // Create diff virtual provider (lightweight single-file diff for permission approval)
//  const diffVirtualProvider = new DiffVirtualProvider(context.extensionUri)
//  provider.setDiffVirtualProvider(diffVirtualProvider)
//  agentManagerHost.setDiffVirtualProvider(diffVirtualProvider)
//  context.subscriptions.push(diffVirtualProvider)

//  // Create standalone editor providers (open in editor area, not sidebar)
//  const settingsEditorProvider = new SettingsEditorProvider(context.extensionUri, connectionService, context)
//  settingsEditorProvider.setRemoteService(remoteService)
//  const marketplacePanelProvider = new MarketplacePanelProvider(context.extensionUri, connectionService, context)
//  context.subscriptions.push(settingsEditorProvider, marketplacePanelProvider)

//  // Surface a discardable notification when a marketplace item matches the workspace.
//  const marketplaceNotifier = new MarketplaceNotifier(connectionService, context, (item) =>
//    marketplacePanelProvider.openInstall(item),
//  )
//  context.subscriptions.push(marketplaceNotifier)
//  marketplaceNotifier.start()

//  // Create sub-agent viewer provider (read-only editor panel for sub-agent sessions)
//  const subAgentViewerProvider = new SubAgentViewerProvider(context.extensionUri, connectionService, context)
//  context.subscriptions.push(subAgentViewerProvider)

//  // Register serializers so standalone panels restore on restart
//  const settingsViews = ["settingsPanel", "profilePanel"] as const
//  for (const suffix of settingsViews) {
//    context.subscriptions.push(
//      vscode.window.registerWebviewPanelSerializer(`kilo-code.new.${suffix}`, {
//        deserializeWebviewPanel(panel: vscode.WebviewPanel) {
//          settingsEditorProvider.deserializePanel(panel)
//          return Promise.resolve()
//        },
//      }),
//    )
//  }

//  context.subscriptions.push(
//    vscode.window.registerWebviewPanelSerializer(MarketplacePanelProvider.viewType, {
//      deserializeWebviewPanel(panel: vscode.WebviewPanel) {
//        marketplacePanelProvider.deserializePanel(panel)
//        return Promise.resolve()
//      },
//    }),
//  )

//  context.subscriptions.push(
//    vscode.window.registerWebviewPanelSerializer(DiffViewerProvider.viewType, {
//      deserializeWebviewPanel(panel: vscode.WebviewPanel) {
//        diffViewerProvider.deserializePanel(panel)
//        return Promise.resolve()
//      },
//    }),
//  )

//  context.subscriptions.push(
//    vscode.window.registerWebviewPanelSerializer("kilo-code.new.SubAgentViewerPanel", {
//      deserializeWebviewPanel(panel: vscode.WebviewPanel) {
//        // Sub-agent viewer requires a session ID that can't be recovered
//        // after restart, so dispose the stale panel cleanly.
//        panel.dispose()
//        return Promise.resolve()
//      },
//    }),
//  )

//  // Sidebar menus use wrapper commands so this event measures real title button presses,
//  // not programmatic opens, shortcuts, or editor title commands.
//  const track = (button: string, command: string) => {
//    TelemetryProxy.capture(TelemetryEventName.TITLE_BUTTON_CLICKED, {
//      button,
//      surface: "sidebar_title",
//    })
//    void vscode.commands.executeCommand(command)
//  }

//  // Register toolbar button command handlers
//  context.subscriptions.push(
//    vscode.commands.registerCommand("kilo-code.new.sidebarTitle.plusButtonClicked", () => {
//      track("new_task", "kilo-code.new.plusButtonClicked")
//    }),
//    vscode.commands.registerCommand("kilo-code.new.sidebarTitle.historyButtonClicked", () => {
//      track("history", "kilo-code.new.historyButtonClicked")
//    }),
//    vscode.commands.registerCommand("kilo-code.new.sidebarTitle.agentManagerOpen", () => {
//      track("agent_manager", "kilo-code.new.agentManagerOpen")
//    }),
//    vscode.commands.registerCommand("kilo-code.new.sidebarTitle.kiloClawOpen", () => {
//      track("kiloclaw", "kilo-code.new.kiloClawOpen")
//    }),
//    vscode.commands.registerCommand("kilo-code.new.sidebarTitle.marketplaceButtonClicked", () => {
//      track("marketplace", "kilo-code.new.marketplaceButtonClicked")
//    }),
//    vscode.commands.registerCommand("kilo-code.new.sidebarTitle.profileButtonClicked", () => {
//      track("profile", "kilo-code.new.profileButtonClicked")
//    }),
//    vscode.commands.registerCommand("kilo-code.new.sidebarTitle.settingsButtonClicked", () => {
//      track("settings", "kilo-code.new.settingsButtonClicked")
//    }),
//    vscode.commands.registerCommand("kilo-code.new.plusButtonClicked", () => {
//      const tab = activeTabProvider()
//      if (tab) tab.postMessage({ type: "action", action: "plusButtonClicked" })
//      else provider.postMessage({ type: "action", action: "plusButtonClicked" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManagerOpen", () => {
//      agentManagerProvider.openPanel()
//    }),
//    vscode.commands.registerCommand("kilo-code.new.marketplaceButtonClicked", (directory?: string | null) => {
//      marketplacePanelProvider.openPanel(directory)
//    }),
//    vscode.commands.registerCommand("kilo-code.new.kiloClawOpen", () => {
//      kiloClawProvider.openPanel()
//    }),
//    vscode.commands.registerCommand("kilo-code.new.historyButtonClicked", () => {
//      const tab = activeTabProvider()
//      if (tab) tab.postMessage({ type: "action", action: "historyButtonClicked" })
//      else provider.postMessage({ type: "action", action: "historyButtonClicked" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.cycleAgentMode", () => {
//      const tab = activeTabProvider()
//      if (tab) tab.postMessage({ type: "action", action: "cycleAgentMode" })
//      else provider.postMessage({ type: "action", action: "cycleAgentMode" })
//      agentManagerProvider.postMessage({ type: "action", action: "cycleAgentMode" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.cyclePreviousAgentMode", () => {
//      const tab = activeTabProvider()
//      if (tab) tab.postMessage({ type: "action", action: "cyclePreviousAgentMode" })
//      else provider.postMessage({ type: "action", action: "cyclePreviousAgentMode" })
//      agentManagerProvider.postMessage({ type: "action", action: "cyclePreviousAgentMode" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.profileButtonClicked", () => {
//      settingsEditorProvider.openPanel("profile")
//    }),
//    vscode.commands.registerCommand("kilo-code.new.settingsButtonClicked", (tab?: string) => {
//      settingsEditorProvider.openPanel("settings", tab)
//    }),
//    vscode.commands.registerCommand("kilo-code.new.openIndexingSettings", () => {
//      settingsEditorProvider.openPanel("settings", "indexing")
//    }),
//    vscode.commands.registerCommand("kilo-code.new.showMemory", async () => {
//      if (agentManagerProvider.isActive()) {
//        await agentManagerProvider.showMemory()
//        return
//      }
//      const target = activeTabProvider() ?? provider
//      if (target === provider) await vscode.commands.executeCommand("kilo-code.SidebarProvider.focus")
//      await target.waitForReady()
//      await target.showMemory()
//    }),
//    vscode.commands.registerCommand("kilo-code.new.toggleMemory", async () => {
//      if (agentManagerProvider.isActive()) {
//        await agentManagerProvider.toggleMemory()
//        return
//      }
//      const target = activeTabProvider() ?? provider
//      if (target === provider) await vscode.commands.executeCommand("kilo-code.SidebarProvider.focus")
//      await target.waitForReady()
//      await target.toggleMemory()
//    }),
//    // legacy-migration start
//    vscode.commands.registerCommand("kilo-code.new.openMigrationWizard", () => {
//      provider.postMessage({ type: "migrationState", needed: true, source: "legacy" })
//    }),
//    // legacy-migration end
//    vscode.commands.registerCommand("kilo-code.new.generateTerminalCommand", async () => {
//      const input = await vscode.window.showInputBox({
//        prompt: "Describe the terminal command you want to generate",
//        placeHolder: "e.g., find all .ts files modified in the last 24 hours",
//      })
//      if (!input) return
//      await vscode.commands.executeCommand("kilo-code.SidebarProvider.focus")
//      await provider.waitForReady()
//      provider.postMessage({ type: "triggerTask", text: `Generate a terminal command: ${input}` })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.toggleRemote", () => {
//      remoteService.toggle().catch((err) => console.error("[Kilo New] toggleRemote command failed:", err))
//    }),
//    vscode.commands.registerCommand("kilo-code.new.openInTab", () => {
//      return openKiloInNewTab(
//        context,
//        connectionService,
//        agentManagerProvider,
//        tabPanels,
//        diffVirtualProvider,
//        remoteService,
//        autoApprove,
//      )
//    }),
//    vscode.commands.registerCommand(
//      "kilo-code.new.showChanges",
//      (arg?: { sessionId?: string; turnId?: string; initialSourceId?: string }) => {
//        diffViewerProvider.openFromCommand(arg)
//      },
//    ),
//    vscode.commands.registerCommand("kilo-code.new.openSubAgentViewer", (sessionID: string, title?: string) => {
//      subAgentViewerProvider.openPanel(sessionID, title)
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.previousSession", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "sessionPrevious" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.nextSession", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "sessionNext" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.previousTab", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "tabPrevious" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.nextTab", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "tabNext" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.search", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "search" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.showTerminal", () => {
//      // Route through the webview so it can reach into the active session
//      // state and open the VS Code integrated terminal for it.
//      agentManagerProvider.postMessage({ type: "action", action: "showTerminal" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.runScript", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "runScript" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.toggleDiff", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "toggleDiff" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.showShortcuts", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "showShortcuts" })
//    }),

//    vscode.commands.registerCommand("kilo-code.new.agentManager.newTab", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "newTab" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.newTerminal", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "newTerminal" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.closeTab", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "closeTab" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.newWorktree", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "newWorktree" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.quickWorktree", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "quickWorktree" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.openWorktree", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "openWorktree" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.openPR", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "openPR" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.closeWorktree", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "closeWorktree" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.advancedWorktree", () =>
//      agentManagerProvider.openAdvancedWorktree(),
//    ),
//    ...Array.from({ length: 9 }, (_, i) =>
//      vscode.commands.registerCommand(`kilo-code.new.agentManager.jumpTo${i + 1}`, () => {
//        agentManagerProvider.postMessage({ type: "action", action: `jumpTo${i + 1}` })
//      }),
//    ),
//  )

//  // Register URI handler for extension deep links (vscode://kilocode.kilo-code/kilocode/...)
//  context.subscriptions.push(
//    vscode.window.registerUriHandler({
//      async handleUri(uri: vscode.Uri) {
//        const sessionMatch = uri.path.match(/^\/kilocode\/s\/([a-zA-Z0-9_-]+)$/)
//        const sessionId = sessionMatch?.[1]
//        if (sessionId) {
//          console.log("[Kilo New] URI handler: opening cloud session:", sessionId)
//          await vscode.commands.executeCommand(`${KiloProvider.viewType}.focus`)
//          provider.openCloudSession(sessionId)
//          return
//        }

//        if (uri.path !== "/kilocode/switch" && uri.path !== "/kilocode/model") return
//        const params = new URLSearchParams(uri.query)
//        const modelID = params.get("model") || undefined
//        const agent = params.get("agent") || undefined
//        if (!modelID && !agent) return
//        console.log("[Kilo New] URI handler: applying linked Kilo selection:", { modelID, agent })
//        await vscode.commands.executeCommand(`${KiloProvider.viewType}.focus`)
//        provider.selectKiloModel(modelID, agent)
//      },
//    }),
//  )

//  // Register autocomplete provider
//  void registerAutocompleteProvider(context, connectionService)

//  // Register commit message generation
//  registerCommitMessageService(context, connectionService)

//  registerHeapSnapshot(context, connectionService)

//  context.subscriptions.push(
//    vscode.commands.registerCommand("kilo-code.new.reload", () => {
//      provider.reload().catch((e) => console.error("[Kilo New] reload command failed:", e))
//    }),
//  )

//  // Register code actions (editor context menus, terminal context menus, keyboard shortcuts)
//  registerCodeActions(context, provider, agentManagerProvider, activeTabProvider)
//  registerTerminalActions(context, provider, agentManagerProvider)

//  // Register CodeActionProvider (lightbulb quick fixes)
//  context.subscriptions.push(
//    vscode.languages.registerCodeActionsProvider(
//      { scheme: "file" },
//      new KiloCodeActionProvider(),
//      KiloCodeActionProvider.metadata,
//    ),
//  )

//  // Dispose services when extension deactivates (kills the server)
//  context.subscriptions.push({
//    dispose: () => {
//      shuttingDown = true
//      unsubscribeStateChange()
//      attention.dispose()
//      browserAutomationService.dispose()
//      provider.dispose()
//      notebookBridge.dispose()
//      connectionService.dispose()
//    },
//  })
//}
//export function activate(context: vscode.ExtensionContext) {
//  console.log("Kilo Code extension is now active")
//  shuttingDown = false

//  const telemetry = TelemetryProxy.getInstance()

//  // Create shared connection service (one server for all webviews)
//  const connectionService = new KiloConnectionService(context)
//  const notebookBridge = createNotebookBridge(connectionService)
//  let restore = context.workspaceState.get<RestoreState>(RESTORE_KEY) ?? {}
//  const remember = (patch: RestoreState) => {
//    const next = { ...restore, ...patch }
//    if (shuttingDown && patch.agentManager === false) next.agentManager = restore.agentManager
//    restore = next
//    void context.workspaceState.update(RESTORE_KEY, restore)
//  }

//  // Create browser automation service (manages Playwright MCP registration)
//  const browserAutomationService = new BrowserAutomationService(connectionService)
//  browserAutomationService.syncWithSettings()

//  // Create remote status service (one status bar item for all webviews)
//  const remoteService = new RemoteStatusService()
//  context.subscriptions.push(remoteService)
//  connectionService.setRemoteService(remoteService)

//  // Re-register browser automation MCP server on CLI backend reconnect, configure telemetry,
//  // set remote service client, and reload autocomplete so it picks up the now-available backend connection.
//  const unsubscribeStateChange = connectionService.onStateChange((state) => {
//    if (state === "connected") {
//      browserAutomationService.reregisterIfEnabled()
//      const config = connectionService.getServerConfig()
//      if (config) {
//        telemetry.configure(config.baseUrl, config.password)
//        // Sync the CLI's PostHog client with the current consent state. The
//        // CLI reads KILO_TELEMETRY_LEVEL once at spawn, so without this call
//        // a fresh CLI started while VS Code telemetry was off would stay
//        // opted out for the rest of the session.
//        telemetry.setEnabled(vscode.env.isTelemetryEnabled)
//      }
//      try {
//        remoteService.setClient(connectionService.getClient())
//        console.log("[Kilo New] CLI connected, calling remoteService.refresh()")
//        remoteService.refresh().catch((err) => console.warn("[Kilo New] initial remote refresh failed:", err))
//      } catch {
//        remoteService.setClient(null)
//      }
//      AutocompleteServiceManager.getInstance()?.load()
//    } else {
//      remoteService.clearState()
//      remoteService.setClient(null)
//    }
//  })

//  // Propagate runtime telemetry consent changes to the CLI subprocess so its
//  // PostHog client stays in sync with the user's VS Code telemetry setting.
//  context.subscriptions.push(
//    vscode.env.onDidChangeTelemetryEnabled((enabled) => {
//      telemetry.setEnabled(enabled)
//    }),
//  )

//  for (const folder of vscode.workspace.workspaceFolders ?? []) {
//    void markWorkspace(folder.uri.fsPath, (msg) => console.warn(`[Kilo New] ${msg}`))
//  }

//  // Track all open tab panel providers so toolbar button commands can target them.
//  // NOTE: The editor/title toolbar for tab panels intentionally omits Agent Manager
//  // and Marketplace buttons (unlike the sidebar). Too many icons causes VS Code to
//  // collapse them into a "..." overflow menu, hiding important buttons like Settings.
//  const tabPanels = new Map<vscode.WebviewPanel, KiloProvider>()
//  const activeTabProvider = () => {
//    for (const [panel, p] of tabPanels) {
//      if (panel.active) return p
//    }
//    return undefined
//  }

//  // Create the provider with shared service
//  const provider = new KiloProvider(context.extensionUri, connectionService, context, {
//    focusContext: "kilo-code.new.sidebarFocused",
//  })
//  provider.setRemoteService(remoteService)

//  // Register the webview view provider for the sidebar.
//  // retainContextWhenHidden keeps the webview alive when switching to other sidebar panels.
//  context.subscriptions.push(
//    vscode.window.registerWebviewViewProvider(KiloProvider.viewType, provider, {
//      webviewOptions: { retainContextWhenHidden: true },
//    }),
//  )

//  // Ensure Agent Manager navigation keybindings work when a VS Code terminal has focus.
//  // The terminal intercepts all keystrokes unless the command is listed in
//  // terminal.integrated.commandsToSkipShell, which only contains built-in
//  // commands by default.
//  const skip = ["kilo-code.new.agentManagerOpen", "kilo-code.new.agentManager.showTerminal"]
//  if (process.platform === "darwin") skip.push("kilo-code.new.agentManager.runScript")
//  ensureCommandsSkipShell(skip)

//  // Create KiloClaw chat provider for editor panel
//  const kiloClawProvider = new KiloClawProvider(context.extensionUri, connectionService)
//  context.subscriptions.push(kiloClawProvider)

//  // Create Agent Manager provider for editor panel
//  const agentManagerHost = new VscodeHost(context.extensionUri, connectionService, context, remoteService)
//  const agentManagerProvider = new AgentManagerProvider(agentManagerHost, connectionService)
//  agentManagerProvider.onPanelVisibilityChange((visible) => remember({ agentManager: visible }))
//  agentManager = agentManagerProvider
//  context.subscriptions.push(agentManagerProvider)

//  // Wire "Continue in Worktree" from sidebar → Agent Manager
//  provider.setContinueInWorktreeHandler((sessionId, progress) =>
//    agentManagerProvider.continueFromSidebar(sessionId, progress),
//  )
//  provider.setCreateWorktreeHandler((baseBranch, branchName) =>
//    agentManagerProvider.createFromSidebar(baseBranch, branchName),
//  )

//  // Register toggle auto-approve shortcut (Ctrl+Alt+A / Cmd+Alt+A)
//  const defaultDir = () => vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ?? process.cwd()
//  const autoApprove = registerToggleAutoApprove(
//    context,
//    connectionService,
//    (sessionId) => {
//      if (sessionId) {
//        const dir =
//          provider.getSessionDirectories().get(sessionId) ?? agentManagerProvider.getSessionDirectories().get(sessionId)
//        if (dir) return dir
//      }
//      return defaultDir()
//    },
//    () => {
//      const dirs = new Set([defaultDir()])
//      for (const dir of provider.getSessionDirectories().values()) dirs.add(dir)
//      for (const dir of agentManagerProvider.getSessionDirectories().values()) dirs.add(dir)
//      return [...dirs]
//    },
//  )
//  const attention = new AttentionService(connectionService, {
//    approve: (event, directory) => autoApprove.approve(event, directory),
//  })

//  // Prewarm only after all global event consumers are ready.
//  ensureBackendForAutocomplete(connectionService)

//  provider.setAutoApproveController(autoApprove)
//  agentManagerHost.setAutoApproveController(autoApprove)

//  // Register serializer so Agent Manager restores when VS Code restarts
//  context.subscriptions.push(
//    vscode.window.registerWebviewPanelSerializer(AgentManagerProvider.viewType, {
//      deserializeWebviewPanel(panel: vscode.WebviewPanel) {
//        if (restore.agentManager === false) {
//          panel.dispose()
//          return Promise.resolve()
//        }
//        const ctx = agentManagerHost.wrapExistingPanel(panel, {
//          onBeforeMessage: (msg) => agentManagerProvider.handleMessage(msg),
//          worktreeDirectories: () => agentManagerProvider.getWorktreeDirectories(),
//        })
//        agentManagerProvider.deserializePanel(ctx)
//        return Promise.resolve()
//      },
//    }),
//  )

//  // Register serializer so KiloClaw panel restores when VS Code restarts
//  context.subscriptions.push(
//    vscode.window.registerWebviewPanelSerializer(KiloClawProvider.viewType, {
//      deserializeWebviewPanel(panel: vscode.WebviewPanel) {
//        kiloClawProvider.restorePanel(panel)
//        return Promise.resolve()
//      },
//    }),
//  )

//  // Register serializer so "Open in Tab" restores when VS Code restarts
//  context.subscriptions.push(
//    vscode.window.registerWebviewPanelSerializer("kilo-code.new.TabPanel", {
//      deserializeWebviewPanel(panel: vscode.WebviewPanel) {
//        const tabProvider = new KiloProvider(context.extensionUri, connectionService, context, {
//          tabTitle: panelTitleHandler(panel),
//        })
//        tabProvider.setRemoteService(remoteService)
//        tabProvider.setAutoApproveController(autoApprove)
//        tabProvider.setContinueInWorktreeHandler((sessionId, progress) =>
//          agentManagerProvider.continueFromSidebar(sessionId, progress),
//        )
//        tabProvider.setCreateWorktreeHandler((baseBranch, branchName) =>
//          agentManagerProvider.createFromSidebar(baseBranch, branchName),
//        )
//        tabProvider.setDiffVirtualProvider(diffVirtualProvider)
//        tabProvider.resolveWebviewPanel(panel)
//        tabPanels.set(panel, tabProvider)
//        panel.onDidDispose(
//          () => {
//            console.log("[Kilo New] Tab panel restored from restart disposed")
//            tabPanels.delete(panel)
//            tabProvider.dispose()
//          },
//          null,
//          context.subscriptions,
//        )
//        return Promise.resolve()
//      },
//    }),
//  )

//  const diffSourceCatalog = new DiffSourceCatalog(connectionService)
//  context.subscriptions.push(diffSourceCatalog)
//  const diffViewerProvider = new DiffViewerProvider(context.extensionUri, connectionService, diffSourceCatalog, {
//    sessionIdProvider: () => provider.getCurrentSessionId(),
//  })
//  diffViewerProvider.setCommentHandler((comments, autoSend) => {
//    void provider.appendReviewComments(comments, autoSend)
//  })
//  context.subscriptions.push(diffViewerProvider)

//  // Create diff virtual provider (lightweight single-file diff for permission approval)
//  const diffVirtualProvider = new DiffVirtualProvider(context.extensionUri)
//  provider.setDiffVirtualProvider(diffVirtualProvider)
//  agentManagerHost.setDiffVirtualProvider(diffVirtualProvider)
//  context.subscriptions.push(diffVirtualProvider)

//  // Create standalone editor providers (open in editor area, not sidebar)
//  const settingsEditorProvider = new SettingsEditorProvider(context.extensionUri, connectionService, context)
//  settingsEditorProvider.setRemoteService(remoteService)
//  const marketplacePanelProvider = new MarketplacePanelProvider(context.extensionUri, connectionService, context)
//  context.subscriptions.push(settingsEditorProvider, marketplacePanelProvider)

//  // Surface a discardable notification when a marketplace item matches the workspace.
//  const marketplaceNotifier = new MarketplaceNotifier(connectionService, context, (item) =>
//    marketplacePanelProvider.openInstall(item),
//  )
//  context.subscriptions.push(marketplaceNotifier)
//  marketplaceNotifier.start()

//  // Create sub-agent viewer provider (read-only editor panel for sub-agent sessions)
//  const subAgentViewerProvider = new SubAgentViewerProvider(context.extensionUri, connectionService, context)
//  context.subscriptions.push(subAgentViewerProvider)

//  // Register serializers so standalone panels restore on restart
//  const settingsViews = ["settingsPanel", "profilePanel"] as const
//  for (const suffix of settingsViews) {
//    context.subscriptions.push(
//      vscode.window.registerWebviewPanelSerializer(`kilo-code.new.${suffix}`, {
//        deserializeWebviewPanel(panel: vscode.WebviewPanel) {
//          settingsEditorProvider.deserializePanel(panel)
//          return Promise.resolve()
//        },
//      }),
//    )
//  }

//  context.subscriptions.push(
//    vscode.window.registerWebviewPanelSerializer(MarketplacePanelProvider.viewType, {
//      deserializeWebviewPanel(panel: vscode.WebviewPanel) {
//        marketplacePanelProvider.deserializePanel(panel)
//        return Promise.resolve()
//      },
//    }),
//  )

//  context.subscriptions.push(
//    vscode.window.registerWebviewPanelSerializer(DiffViewerProvider.viewType, {
//      deserializeWebviewPanel(panel: vscode.WebviewPanel) {
//        diffViewerProvider.deserializePanel(panel)
//        return Promise.resolve()
//      },
//    }),
//  )

//  context.subscriptions.push(
//    vscode.window.registerWebviewPanelSerializer("kilo-code.new.SubAgentViewerPanel", {
//      deserializeWebviewPanel(panel: vscode.WebviewPanel) {
//        // Sub-agent viewer requires a session ID that can't be recovered
//        // after restart, so dispose the stale panel cleanly.
//        panel.dispose()
//        return Promise.resolve()
//      },
//    }),
//  )

//  // Sidebar menus use wrapper commands so this event measures real title button presses,
//  // not programmatic opens, shortcuts, or editor title commands.
//  const track = (button: string, command: string) => {
//    TelemetryProxy.capture(TelemetryEventName.TITLE_BUTTON_CLICKED, {
//      button,
//      surface: "sidebar_title",
//    })
//    void vscode.commands.executeCommand(command)
//  }

//  // Register toolbar button command handlers
//  context.subscriptions.push(
//    vscode.commands.registerCommand("kilo-code.new.sidebarTitle.plusButtonClicked", () => {
//      track("new_task", "kilo-code.new.plusButtonClicked")
//    }),
//    vscode.commands.registerCommand("kilo-code.new.sidebarTitle.historyButtonClicked", () => {
//      track("history", "kilo-code.new.historyButtonClicked")
//    }),
//    vscode.commands.registerCommand("kilo-code.new.sidebarTitle.agentManagerOpen", () => {
//      track("agent_manager", "kilo-code.new.agentManagerOpen")
//    }),
//    vscode.commands.registerCommand("kilo-code.new.sidebarTitle.kiloClawOpen", () => {
//      track("kiloclaw", "kilo-code.new.kiloClawOpen")
//    }),
//    vscode.commands.registerCommand("kilo-code.new.sidebarTitle.marketplaceButtonClicked", () => {
//      track("marketplace", "kilo-code.new.marketplaceButtonClicked")
//    }),
//    vscode.commands.registerCommand("kilo-code.new.sidebarTitle.profileButtonClicked", () => {
//      track("profile", "kilo-code.new.profileButtonClicked")
//    }),
//    vscode.commands.registerCommand("kilo-code.new.sidebarTitle.settingsButtonClicked", () => {
//      track("settings", "kilo-code.new.settingsButtonClicked")
//    }),
//    vscode.commands.registerCommand("kilo-code.new.plusButtonClicked", () => {
//      const tab = activeTabProvider()
//      if (tab) tab.postMessage({ type: "action", action: "plusButtonClicked" })
//      else provider.postMessage({ type: "action", action: "plusButtonClicked" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManagerOpen", () => {
//      agentManagerProvider.openPanel()
//    }),
//    vscode.commands.registerCommand("kilo-code.new.marketplaceButtonClicked", (directory?: string | null) => {
//      marketplacePanelProvider.openPanel(directory)
//    }),
//    vscode.commands.registerCommand("kilo-code.new.kiloClawOpen", () => {
//      kiloClawProvider.openPanel()
//    }),
//    vscode.commands.registerCommand("kilo-code.new.historyButtonClicked", () => {
//      const tab = activeTabProvider()
//      if (tab) tab.postMessage({ type: "action", action: "historyButtonClicked" })
//      else provider.postMessage({ type: "action", action: "historyButtonClicked" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.cycleAgentMode", () => {
//      const tab = activeTabProvider()
//      if (tab) tab.postMessage({ type: "action", action: "cycleAgentMode" })
//      else provider.postMessage({ type: "action", action: "cycleAgentMode" })
//      agentManagerProvider.postMessage({ type: "action", action: "cycleAgentMode" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.cyclePreviousAgentMode", () => {
//      const tab = activeTabProvider()
//      if (tab) tab.postMessage({ type: "action", action: "cyclePreviousAgentMode" })
//      else provider.postMessage({ type: "action", action: "cyclePreviousAgentMode" })
//      agentManagerProvider.postMessage({ type: "action", action: "cyclePreviousAgentMode" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.profileButtonClicked", () => {
//      settingsEditorProvider.openPanel("profile")
//    }),
//    vscode.commands.registerCommand("kilo-code.new.settingsButtonClicked", (tab?: string) => {
//      settingsEditorProvider.openPanel("settings", tab)
//    }),
//    vscode.commands.registerCommand("kilo-code.new.openIndexingSettings", () => {
//      settingsEditorProvider.openPanel("settings", "indexing")
//    }),
//    vscode.commands.registerCommand("kilo-code.new.showMemory", async () => {
//      if (agentManagerProvider.isActive()) {
//        await agentManagerProvider.showMemory()
//        return
//      }
//      const target = activeTabProvider() ?? provider
//      if (target === provider) await vscode.commands.executeCommand("kilo-code.SidebarProvider.focus")
//      await target.waitForReady()
//      await target.showMemory()
//    }),
//    vscode.commands.registerCommand("kilo-code.new.toggleMemory", async () => {
//      if (agentManagerProvider.isActive()) {
//        await agentManagerProvider.toggleMemory()
//        return
//      }
//      const target = activeTabProvider() ?? provider
//      if (target === provider) await vscode.commands.executeCommand("kilo-code.SidebarProvider.focus")
//      await target.waitForReady()
//      await target.toggleMemory()
//    }),
//    // legacy-migration start
//    vscode.commands.registerCommand("kilo-code.new.openMigrationWizard", () => {
//      provider.postMessage({ type: "migrationState", needed: true, source: "legacy" })
//    }),
//    // legacy-migration end
//    vscode.commands.registerCommand("kilo-code.new.generateTerminalCommand", async () => {
//      const input = await vscode.window.showInputBox({
//        prompt: "Describe the terminal command you want to generate",
//        placeHolder: "e.g., find all .ts files modified in the last 24 hours",
//      })
//      if (!input) return
//      await vscode.commands.executeCommand("kilo-code.SidebarProvider.focus")
//      await provider.waitForReady()
//      provider.postMessage({ type: "triggerTask", text: `Generate a terminal command: ${input}` })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.toggleRemote", () => {
//      remoteService.toggle().catch((err) => console.error("[Kilo New] toggleRemote command failed:", err))
//    }),
//    vscode.commands.registerCommand("kilo-code.new.openInTab", () => {
//      return openKiloInNewTab(
//        context,
//        connectionService,
//        agentManagerProvider,
//        tabPanels,
//        diffVirtualProvider,
//        remoteService,
//        autoApprove,
//      )
//    }),
//    vscode.commands.registerCommand(
//      "kilo-code.new.showChanges",
//      (arg?: { sessionId?: string; turnId?: string; initialSourceId?: string }) => {
//        diffViewerProvider.openFromCommand(arg)
//      },
//    ),
//    vscode.commands.registerCommand("kilo-code.new.openSubAgentViewer", (sessionID: string, title?: string) => {
//      subAgentViewerProvider.openPanel(sessionID, title)
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.previousSession", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "sessionPrevious" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.nextSession", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "sessionNext" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.previousTab", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "tabPrevious" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.nextTab", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "tabNext" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.search", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "search" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.showTerminal", () => {
//      // Route through the webview so it can reach into the active session
//      // state and open the VS Code integrated terminal for it.
//      agentManagerProvider.postMessage({ type: "action", action: "showTerminal" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.runScript", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "runScript" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.toggleDiff", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "toggleDiff" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.showShortcuts", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "showShortcuts" })
//    }),

//    vscode.commands.registerCommand("kilo-code.new.agentManager.newTab", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "newTab" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.newTerminal", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "newTerminal" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.closeTab", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "closeTab" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.newWorktree", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "newWorktree" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.quickWorktree", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "quickWorktree" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.openWorktree", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "openWorktree" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.openPR", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "openPR" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.closeWorktree", () => {
//      agentManagerProvider.postMessage({ type: "action", action: "closeWorktree" })
//    }),
//    vscode.commands.registerCommand("kilo-code.new.agentManager.advancedWorktree", () =>
//      agentManagerProvider.openAdvancedWorktree(),
//    ),
//    ...Array.from({ length: 9 }, (_, i) =>
//      vscode.commands.registerCommand(`kilo-code.new.agentManager.jumpTo${i + 1}`, () => {
//        agentManagerProvider.postMessage({ type: "action", action: `jumpTo${i + 1}` })
//      }),
//    ),
//  )

//  // Register URI handler for extension deep links (vscode://kilocode.kilo-code/kilocode/...)
//  context.subscriptions.push(
//    vscode.window.registerUriHandler({
//      async handleUri(uri: vscode.Uri) {
//        const sessionMatch = uri.path.match(/^\/kilocode\/s\/([a-zA-Z0-9_-]+)$/)
//        const sessionId = sessionMatch?.[1]
//        if (sessionId) {
//          console.log("[Kilo New] URI handler: opening cloud session:", sessionId)
//          await vscode.commands.executeCommand(`${KiloProvider.viewType}.focus`)
//          provider.openCloudSession(sessionId)
//          return
//        }

//        if (uri.path !== "/kilocode/switch" && uri.path !== "/kilocode/model") return
//        const params = new URLSearchParams(uri.query)
//        const modelID = params.get("model") || undefined
//        const agent = params.get("agent") || undefined
//        if (!modelID && !agent) return
//        console.log("[Kilo New] URI handler: applying linked Kilo selection:", { modelID, agent })
//        await vscode.commands.executeCommand(`${KiloProvider.viewType}.focus`)
//        provider.selectKiloModel(modelID, agent)
//      },
//    }),
//  )

//  // Register autocomplete provider
//  void registerAutocompleteProvider(context, connectionService)

//  // Register commit message generation
//  registerCommitMessageService(context, connectionService)

//  registerHeapSnapshot(context, connectionService)

//  context.subscriptions.push(
//    vscode.commands.registerCommand("kilo-code.new.reload", () => {
//      provider.reload().catch((e) => console.error("[Kilo New] reload command failed:", e))
//    }),
//  )

//  // Register code actions (editor context menus, terminal context menus, keyboard shortcuts)
//  registerCodeActions(context, provider, agentManagerProvider, activeTabProvider)
//  registerTerminalActions(context, provider, agentManagerProvider)

//  // Register CodeActionProvider (lightbulb quick fixes)
//  context.subscriptions.push(
//    vscode.languages.registerCodeActionsProvider(
//      { scheme: "file" },
//      new KiloCodeActionProvider(),
//      KiloCodeActionProvider.metadata,
//    ),
//  )

//  // Dispose services when extension deactivates (kills the server)
//  context.subscriptions.push({
//    dispose: () => {
//      shuttingDown = true
//      unsubscribeStateChange()
//      attention.dispose()
//      browserAutomationService.dispose()
//      provider.dispose()
//      notebookBridge.dispose()
//      connectionService.dispose()
//    },
//  })
//}




      _dte = (EnvDTE.DTE)await GetServiceAsync(typeof(EnvDTE.DTE));
      _solutionEvents = _dte.Events.SolutionEvents;

      _solutionEvents.Opened += OnSolutionOpened;
      _solutionEvents.AfterClosing += OnSolutionClosed;

      // The package may have loaded after the solution was already opened.
      if (await IsSolutionOpenAsync())
      {
        SetWorkspaceRoot();
      }

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

    private void OnSolutionOpened()
    {
      ThreadHelper.ThrowIfNotOnUIThread();

      SetWorkspaceRoot();
    }

    private void OnSolutionClosed()
    {
      ThreadHelper.ThrowIfNotOnUIThread();

      VSExtensionSettings.SetWorkspaceRoot(null);
    }

    private async Task<bool> IsSolutionOpenAsync()
    {
      await JoinableTaskFactory.SwitchToMainThreadAsync();

      var solution =
          await GetServiceAsync(
              typeof(SVsSolution)) as IVsSolution;

      if (solution is null)
      {
        return false;
      }

      ErrorHandler.ThrowOnFailure(
          solution.GetProperty(
              (int)__VSPROPID.VSPROPID_IsSolutionOpen,
              out var value));

      return value is bool isOpen && isOpen;
    }


    private void SetWorkspaceRoot()
    {
      ThreadHelper.ThrowIfNotOnUIThread();

      var _dte = ThreadHelper.JoinableTaskFactory.Run(async () =>
      {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        return (EnvDTE.DTE?)await GetServiceAsync(typeof(EnvDTE.DTE));
      });

      var solutionPath =
          _dte.Solution?.FullName;

      if (string.IsNullOrEmpty(solutionPath))
      {
        VSExtensionSettings.SetWorkspaceRoot(null);
        return;
      }

      VSExtensionSettings.SetWorkspaceRoot(System.IO.Path.GetDirectoryName(solutionPath));
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
        _solutionEvents.Opened -= OnSolutionOpened;
        _solutionEvents.AfterClosing -= OnSolutionClosed;
      }
      base.Dispose(disposing);
    }
    #endregion
  }
}
