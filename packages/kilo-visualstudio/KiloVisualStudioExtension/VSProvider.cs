using EnvDTE80;
using KiloExtensionDTOs;
using KiloExtensionDTOs.Agents;
using KiloExtensionDTOs.ExtensionMessages;
using KiloExtensionDTOs.KiloConfig;
using KiloExtensionDTOs.Profile;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.Services;
using KiloVisualStudioExtension.Services.Handlers.AgentRequest;
using KiloVisualStudioExtension.Services.Handlers.Auth;
using KiloVisualStudioExtension.Services.Handlers.CloudSession;
using KiloVisualStudioExtension.Services.Handlers.Config;
using KiloVisualStudioExtension.Services.Handlers.Interaction;
using KiloVisualStudioExtension.Services.Handlers.Mcp;
using KiloVisualStudioExtension.Services.Handlers.Memory;
using KiloVisualStudioExtension.Services.Handlers.Provider;
using KiloVisualStudioExtension.Services.Handlers.MiscRequest;
using KiloVisualStudioExtension.Services.Handlers.Model;
using KiloVisualStudioExtension.Services.Handlers.Notification;
using KiloVisualStudioExtension.Services.Handlers.ProviderRequest;
using KiloVisualStudioExtension.Services.Handlers.Session;
using KiloVisualStudioExtension.Services.Handlers.SessionControl;
using KiloVisualStudioExtension.Services.Handlers.Settings;
using KiloVisualStudioExtension.Services.Handlers.StateManagement;
using KiloVisualStudioExtension.Services.Handlers.Ui;
using KiloVisualStudioExtension.Services.Handlers.Followup;
using KiloVisualStudioExtension.Services.Handlers.Indexing;
using KiloVisualStudioExtension.Services.Handlers.Sandbox;
using KiloVisualStudioExtension.Services.Handlers.Network;
using KiloVisualStudioExtension.Services;
using KiloVisualStudioExtension.Utils;
using MessagePack;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Newtonsoft.Json.Linq;
using StreamJsonRpc.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VSLangProj110;
using static Microsoft.VisualStudio.Shell.ThreadedWaitDialogHelper;
using static System.Net.Mime.MediaTypeNames;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ListView;
using SessionCreateRequest = KiloVisualStudioExtension.ApiClient.Body18;
using ApiImageModel = KiloVisualStudioExtension.ApiClient.Anonymous10;


namespace KiloVisualStudioExtension
{
  /// <summary>
  /// Main provider class that handles communication between the webview and the Kilo backend.
  /// Message handling logic is delegated to specialized handler services to keep this class
  /// under 1500 lines, matching the VS Code pattern where handlers are extracted into separate modules.
  /// 
  /// Handler services:
  /// - SessionHandlerService: session create/delete/rename/loadMessages
  /// - AuthHandlerService: login/logout/refreshProfile
  /// - ConfigHandlerService: requestConfig/updateSetting/updateConfig
  /// - ProviderRequestService: requestProviders
  /// - AgentRequestService: requestAgents
  /// - StateManagementService: setState/getState
  /// - InteractionHandlerService: prompt/permission/reply/question/reply
  /// - SessionControlHandlerService: abort/sendMessage
  /// - UiHandlerService: openSettingsPanel/settingsTabChanged
  /// </summary>
  public class VSProvider : ServiceProviderServiceBase, IDisposable
  {
    public static string viewType = "kilo-code.SidebarProvider";
    private bool disposedValue;
    private readonly string instanceId = Guid.NewGuid().ToString();

    //  private webview: vscode.Webview | null = null
    //  private currentSession: Session | null = null
    //  /** Remembers the last selected session so /new can stay in the same worktree after clearSession. */
    //  private contextSessionID: string | undefined
    //  private connectionState: "connecting" | "connected" | "disconnected" | "error" = "connecting"
    //  private connectionGeneration = 0
    //  private loginAttempt = 0
    //  private isWebviewReady = false
    //  private readonly extensionVersion =
    //    vscode.extensions.getExtension("kilocode.kilo-code")?.packageJSON?.version ?? "unknown"
    //  private cachedProvidersMessage: unknown = null
    //  /**
    //   * Provider API keys retained extension-side for authenticated model
    //   * fetches (#10139). Keys are stripped before provider data reaches the
    //   * webview, so fetch requests for an existing provider carry a providerID
    //   * and the key is resolved here. Refreshed on every provider fetch.
    //   */
    //  private storedProviderKeys: Record<string, StoredProviderKey> = {}
    //  /** Coalesce provider refreshes — at most one follow-up rerun when a request lands mid-flight. */
    //  private providersRefresh: Promise<void> | null = null
    //  private providersQueued = false
    //  private providersGeneration = 0
    //  private sandboxRevision = 0
    //  private cachedAgentsMessage: unknown = null
    //  /** Cached skillsLoaded payload so requestSkills can be served before client is ready */
    //  private cachedSkillsMessage: unknown = null
    //  /** Cached commandsLoaded payload so requestCommands can be served before client is ready */
    //  private cachedCommandsMessage: unknown = null
    //  /** Cached configLoaded payload so requestConfig can be served before client is ready */
    //  private cachedConfigMessage: unknown = null
    //  private cachedGlobalConfig: Config | null = null
    //  /** Cached indexingStatusLoaded payload so requestIndexingStatus can be served before client is ready */
    //  private cachedIndexingStatusMessage: unknown = null
    //  /** Cached kiloEmbeddingModelsLoaded payload so requestKiloEmbeddingModels is resilient offline. */
    //  private cachedKiloEmbeddingModelsMessage: unknown = null
    //  /** Cached imageModelsLoaded payload so requestImageModels is resilient offline. */
    //  private cachedImageModelsMessage: unknown = null
    //  /** Cached mcpStatusLoaded payload so requestMcpStatus can be served before client is ready */
    //  private cachedMcpStatusMessage: unknown = null
    //  /** Ref-count of in-flight handleUpdateConfig calls; prevents fetchAndSendConfig from sending stale data */
    //  private pending = 0
    //  private configWarningsShown = false
    //  /** Cached notificationsLoaded payload */
    //  private cachedNotificationsMessage: NotificationsMessage | null = null
    //  private pendingKiloModel: { modelID?: string; agent?: string
    //  } | null = null
    //  private pendingReviewComments: { comments: unknown[]; autoSend: boolean
    //}
    //[] = []
    //  private readyResolvers: (() => void)[] = []
    //  private promptRecoveryQueued = false
    //  private promptRecovery: Promise<void> | null = null
    //  private trackedSessionIds: Set<string> = new Set()
    private readonly HashSet<string> _openSessionIds = [];

    //  private modelUsageSessionIds: Set<string> = new Set()
    //  private syncedChildSessions: Set<string> = new Set()
    //  private readonly checkpoints = new Map<string, Promise<void>>()
    //  private readonly sessionCreations = new Map<string, Promise<{ sid: string; dir: string } | undefined >> ()
    //  private readonly draftSessions = new Map<string, { sid: string; dir: string; expires: number } > ()
    private readonly Dictionary<string, DraftSession> _draftSessions = new Dictionary<string, DraftSession>();
    private int _loginAttempt = 0;

    //  private readonly sandboxTransitions = new Map<string, Promise<void>>()
    //  private readonly revisions = new Map<string, { id: string; seq: number } > ()
    //  private readonly refreshes = new Map<string, number>()
    //  private readonly anacondaDesktop = new AnacondaDesktopBridge()
    //  private sessionStatusMap = new Map<string, SessionStatus["type"]>() // Latest status used for destructive config warnings.
    //  private sessionDirectories = new Map<string, string>() // Per-session directory overrides, such as Agent Manager worktrees.
    //  private readonly aborts = new SessionAbort()
    //  private projectID: string | undefined // Current workspace project ID used to filter sessions.
    //  private loadMessagesAbort: AbortController | null = null // Current load request cancellation.
    //  private pendingSessionRefresh = false // Refresh requested before the client is ready.
    //  private readonly streams = new SessionStreamScheduler((msg) => this.postMessage(msg))
    //  private readonly confirmations = new MessageConfirmation()
    //  private readonly costs = new MaxCostNudge()
    //  private readonly activeAlerts = new Map<string, number>() // sid -> limit currently shown in UI
    //  private readonly memory = new KiloProviderMemory({
    //    client: () => this.client ?? undefined,
    //    session: () => this.currentSession ?? undefined,
    //    // Honor disabled project scope (null in a multi-root panel): no workspace fallback,
    //    // so memory operations never silently target an arbitrary folder.
    //    dir: (sessionID) => this.getProjectDirectory(sessionID),
    //    post: (message) => this.postMessage(message),
    //  })
    //  private unsubscribeEvent: (() => void) | null = null
    //  private unsubscribeState: (() => void) | null = null
    //  /** Cached migration data so migration doesn't re-read from disk/SecretStorage. */ // legacy-migration
    //  private migrationCache: MigrationContext["migrationCache"] = new Map()
    //  /** Guard to prevent checkAndShowMigrationWizard running concurrently. */ // legacy-migration
    //  private migrationCheckInFlight = false // legacy-migration
    //  private unsubscribeNotificationDismiss: (() => void) | null = null
    //  private unsubscribeLanguageChange: (() => void) | null = null
    //  private unsubscribeProfileChange: (() => void) | null = null
    //  private unsubscribeFavoritesChange: (() => void) | null = null
    //  private unsubscribeModelSelectorExpanded: (() => void) | null = null
    //  private unsubscribeMigrationComplete: (() => void) | null = null // legacy-migration
    //  private unsubscribeClearPendingPrompts: (() => void) | null = null
    //  private unsubscribeDirectoryProvider: (() => void) | null = null
    //  private unsubscribeSandboxPreference: (() => void) | null = null
    //  private initConnectionPromise: Promise<void> | null = null
    //  private webviewMessageDisposable: vscode.Disposable | null = null
    //  private autocompleteConfigDisposable: vscode.Disposable | null = null
    //  private indexingConfigDisposable: vscode.Disposable | null = null
    //  private chatConfigDisposable: vscode.Disposable | null = null
    //  private throughputConfigDisposable: vscode.Disposable | null = null
    //  private telemetryStateDisposable: vscode.Disposable | null = null
    //  private viewStateDisposable: vscode.Disposable | null = null
    //  private visibilityDisposable: vscode.Disposable | null = null
    //  private autoApproveBridge: ReturnType < typeof createAutoApproveBridge> | null = null
    //  private readonly marketplaceRemove = createMarketplaceRemover()

    //  private ignoreController: FileIgnoreController | null = null
    //  private ignoreControllerDir: string | null = null
    //  private chatAutocomplete: ChatTextAreaAutocomplete | null = null
    //  private projectDirectory: string | null | undefined
    //  private slimEditMetadata = true

    //  private pendingFollowup: Followup | null = null
    //  private followupListeners: Array < (session: Session, directory: string) => void> = []
    //private statsPoller: GitStatsPoller | null = null
    //  private statsGitOps: GitOps | null = null
    //  private cachedStats: unknown = null
    //  private cachedGitRepo = false

    //  private onBeforeMessage: ((msg: Record<string, unknown>) => Promise < Record<string, unknown> | null >) | null = null

    //  private continueInWorktreeHandler:
    //    | ((sessionId: string, progress: (status: string, detail ?: string, error ?: string) => void) => Promise<void>)
    //    | null = null

    //  private createWorktreeHandler: ((baseBranch ?: string, branchName ?: string) => Promise<void>) | null = null

    //  private diffVirtualProvider: import("./DiffVirtualProvider").DiffVirtualProvider | undefined
    //  private remoteService: RemoteStatusService | null = null
    //  private unsubscribeRemote: (() => void) | null = null
    //  private readonly requirements: AgentRequirementsController
    protected readonly KiloWebViewControl _webView;
    protected readonly KiloConnectionService _connectionService;
    private readonly KiloProviderOptions _opts;
    protected readonly SSEHandlerService _sseHelper;
    private readonly SessionStreamScheduler _streamScheduler;
    private readonly SessionHandlerService _sessionService;
    private readonly ProjectDirectoryProvider _projectDirectoryService;
    private readonly AuthHandlerService _authService;
    private readonly ConfigHandlerService _configService;
    private readonly ProviderRequestService _providerRequestService;
    private readonly FollowupService _followupService;
    private readonly IndexingService _indexingService;
    private readonly SandboxService _sandboxService;
    private readonly NetworkService _networkService;
    private readonly AgentRequestService _agentRequestService;
    private readonly StateManagementService _stateManagementService;
    private readonly McpHandlerService _mcpHandlerService;
    private readonly NotificationService _notificationService;
    private readonly ModelService _modelService;
    private readonly SettingsService _settingsService;
    private readonly MiscRequestService _miscRequestService;
    private readonly InteractionService _interactionService;
    private readonly SessionControlService _sessionControlService;
    private readonly UiService _uiService;
    private readonly MemoryService _memoryService;
    private readonly ProviderService _providerActionService;
    private readonly RemoteStatusService _remoteService;
    private readonly VisibleTaskStreams _visibleTaskStreams;

    private bool _isWebviewReady = false;
    private bool _disposed;
    private JsonElement? _webviewState;
    private string? _contextSessionID;

    private ApiClient.Session? _currentSession;

    private readonly List<System.Action> _readyResolvers = new List<System.Action>();
    private List<JsonElement>? _pendingReviewComments = null;
    private bool _promptRecoveryQueued = false;
    private Task? _promptRecovery;
    private JsonElement? _pendingKiloModel = null;
    private JsonElement? _cachedStats = null;
    private bool _cachedGitRepo = false;
    private readonly Dictionary<string, string> _sessionStatusMap = new Dictionary<string, string>();
    private readonly Dictionary<string, double> _activeAlerts = new Dictionary<string, double>();
    private readonly Dictionary<string, int> _refreshes = new Dictionary<string, int>();
    private readonly Dictionary<string, DateTimeOffset> _lastReconciledAt = new();  // Per-session focus-mode reconcile timestamp.
    private readonly Dictionary<string, Task> _checkpoints = new();
    private object? _cachedConfigMessage = null;
    // private KiloExtensionDTOs.KiloConfig.Config? _cachedGlobalConfig = null;
    private int _pending = 0;


    private EnvDTE.DTE? GetDTE()
    {
      return ThreadHelper.JoinableTaskFactory.Run(async () =>
      {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        return (EnvDTE.DTE?)await KiloProvider.Package.GetServiceAsync(typeof(EnvDTE.DTE));
      });
    }

    /// <summary>
    /// Constructor for factory creation (webView may be null initially).
    /// Initializes all handler services using dependency injection.
    /// </summary>
    public VSProvider(KiloWebViewControl? webView, KiloConnectionService connectionService, KiloProviderOptions? opts = null) : base(new ServiceProvider())
    {
      _webView = webView!;
      _connectionService = connectionService;
      _opts = opts ?? new KiloProviderOptions();

      _serviceProvider.AddService(this);
      _serviceProvider.AddService(connectionService);
      _serviceProvider.AddService(webView ?? throw new ArgumentNullException(nameof(webView)));
     
      _sseHelper = _serviceProvider.AddService(new SSEHandlerService(_serviceProvider, PostMessage));
      _connectionService.RegisterSSEHelper(instanceId, _sseHelper);
      _streamScheduler = _serviceProvider.AddService(new SessionStreamScheduler(PostMessage));

      _visibleTaskStreams = new VisibleTaskStreams((id, visible) => _streamScheduler.SetVisible(id, visible));

      // Initialize CacheService - it manages its own internal storage
      var cacheService = _serviceProvider.AddService<ICacheService>(new CacheService(_serviceProvider));
      

      _sessionService = _serviceProvider.GetService<SessionHandlerService>();

      var dte = GetDTE();
      if (dte != null)
      {
        var vsProvider = _serviceProvider.AddService(new VisualStudioDirectoryProvider(_serviceProvider, dte));
        _projectDirectoryService = vsProvider.CreateProvider(
            projectDirectoryOverride: null // or specify a path like @"C:\MyProject"
                                           //sessionDirectories: _sessionDirectories
            );
        _serviceProvider.AddService(_projectDirectoryService);
      }
//      _projectDirectoryService = _serviceProvider.GetService<ProjectDirectoryProvider>();
      _authService = _serviceProvider.GetService<AuthHandlerService>();
      _configService = _serviceProvider.GetService<ConfigHandlerService>();
      _providerRequestService = _serviceProvider.GetService<ProviderRequestService>();

      // Register new handler services for state that was previously in SSEHelper
      _followupService = _serviceProvider.GetService<FollowupService>();
      _indexingService = _serviceProvider.GetService<IndexingService>();
      _sandboxService = _serviceProvider.GetService<SandboxService>();
      _networkService = _serviceProvider.GetService<NetworkService>();
      _agentRequestService = _serviceProvider.GetService<AgentRequestService>();
      _stateManagementService = _serviceProvider.GetService<StateManagementService>();
      _mcpHandlerService = _serviceProvider.GetService<McpHandlerService>();
      _notificationService = _serviceProvider.GetService<NotificationService>();
      _modelService = _serviceProvider.GetService<ModelService>();
      _settingsService = _serviceProvider.GetService<SettingsService>();
      _miscRequestService = _serviceProvider.GetService<MiscRequestService>();
      _interactionService = _serviceProvider.GetService<InteractionService>();
      _sessionControlService = _serviceProvider.GetService<SessionControlService>();
      _uiService = _serviceProvider.GetService<UiService>();

      _memoryService = _serviceProvider.GetService<MemoryService>();
      _providerActionService = _serviceProvider.GetService<ProviderService>();

      _remoteService = _serviceProvider.GetService<RemoteStatusService>();

      _sseHelper.SetRemoteStatusService(_remoteService);

      if (webView != null)
      {
        webView.OnMessageReceived += HandleMessageReceived;
        _connectionService.OnStateChange += HandleStateChange;
        _connectionService.RegisterSSEEventHandler(instanceId, HandleSseEvent);
      }
      _connectionService.RegisterDirectoryProvider(() =>
      {
        var res = new List<string> { _projectDirectoryService.GetWorkspaceDirectory() };
        res.AddRange(_projectDirectoryService.GetSessionDirectories().Values);
        return res.ToArray();
      });
    }

    #region Internal Helper Methods for Handler Services


    internal void PostMessage(string message)
    {
      _webView.PostMessage(message);
    }

    internal void PostMessage(object message)
    {
      try
      {
        if (message == null) return;
        var s = (message is string) ? (string)message : JsonSerializer.Serialize(message);
        _webView.PostMessage(s);
      }
      catch
      {

      }
    }

    internal string GetWorkspaceDirectory(string? sessionID = null)
    {
      return System.Environment.CurrentDirectory;
    }

    internal string GetConnectionState()
    {
      return _connectionService.State.ToString().ToLowerInvariant();
    }


    /// <summary>
    /// Queues an operation for a session, ensuring sequential execution.
    /// Matches VS Code's checkpoint pattern for serializing async operations per session.
    /// </summary>
    private async Task Checkpoint(string sid, Func<Task> run)
    {
      // Get prior task or completed task if none exists
      var prior = _checkpoints.TryGetValue(sid, out var existing) ? existing : Task.CompletedTask;

      // Chain the new operation after the prior one
      var pending = prior.ContinueWith(async _ =>
      {
        await run();
      }, TaskContinuationOptions.ExecuteSynchronously).Unwrap();

      // Cleanup: remove from dictionary if still the current task
      void Cleanup()
      {
        if (_checkpoints.TryGetValue(sid, out var current) && current == pending)
        {
          _checkpoints.Remove(sid);
        }
      }

      // Register the new task
      _checkpoints[sid] = pending;

      // Fire-and-forget completion handler
      _ = pending.ContinueWith(t =>
      {
        if (t.IsFaulted && t.Exception != null)
        {
          System.Diagnostics.Debug.WriteLine($"[Kilo New] checkpoint mutation failed: {t.Exception.GetBaseException().Message}");
        }
        Cleanup();
      }, TaskContinuationOptions.ExecuteSynchronously);
    }

    /// <summary>
    /// Waits for any pending checkpoint operation for a session.
    /// </summary>
    private async Task WaitForCheckpoint(string sid)
    {
      if (_checkpoints.TryGetValue(sid, out var task))
      {
        await task;
      }
    }

    // Validation functions (matching provider-actions.ts in TypeScript)
    internal bool IsModelSelection(JsonElement? raw)
    {
      if (!raw.HasValue || raw.Value.ValueKind != JsonValueKind.Object) return false;
      var obj = raw.Value;
      return obj.TryGetProperty("providerID", out var pid) && pid.ValueKind == JsonValueKind.String &&
             obj.TryGetProperty("modelID", out var mid) && mid.ValueKind == JsonValueKind.String;
    }

    internal JsonElement ValidateRecents(JsonElement? raw)
    {
      if (!raw.HasValue || raw.Value.ValueKind != JsonValueKind.Array)
      {
        return JsonSerializer.SerializeToElement(new List<object>());
      }

      var array = raw.Value.EnumerateArray();
      var validItems = new List<object>();
      int count = 0;

      foreach (var item in array)
      {
        if (IsModelSelection(item) && count < 5)
        {
          var pid = item.GetProperty("providerID").GetString() ?? "";
          var mid = item.GetProperty("modelID").GetString() ?? "";
          validItems.Add(new { providerID = pid, modelID = mid });
          count++;
        }
      }

      return JsonSerializer.SerializeToElement(validItems);
    }

    internal JsonElement ValidateFavorites(JsonElement? raw)
    {
      if (!raw.HasValue || raw.Value.ValueKind != JsonValueKind.Array)
      {
        return JsonSerializer.SerializeToElement(new List<object>());
      }

      var array = raw.Value.EnumerateArray();
      var validItems = new List<object>();

      foreach (var item in array)
      {
        if (IsModelSelection(item))
        {
          var pid = item.GetProperty("providerID").GetString() ?? "";
          var mid = item.GetProperty("modelID").GetString() ?? "";
          validItems.Add(new { providerID = pid, modelID = mid });
        }
      }

      return JsonSerializer.SerializeToElement(validItems);
    }

    internal KiloApiClient? GetNswagClient()
    {
      return _connectionService.GetNswagClient();
    }

    internal int GetLoginAttempt()
    {
      return _loginAttempt;
    }

    internal bool IsConnected()
    {
      var nswagClient = _connectionService.GetNswagClient();
      return nswagClient != null;
    }

    internal void RequestCostAlert(string sessionID, double cost)
    {
      var costSvc = ServiceProvider.GetService<CostService>();
      var limit = costSvc.Limit;
      if (limit == null || limit == 0.0 || double.IsNaN(limit.Value) || double.IsInfinity(limit.Value) || cost < limit) return;
      
      costSvc.SetSessionCost(sessionID, cost);
      var alert = costSvc.Check(sessionID);
      if (alert == null) return;
      _activeAlerts[sessionID] = alert.Limit;
      PostMessage(new SessionCostAlertMessage
      {
        SessionID = sessionID,
        Limit = alert.Limit,
        Cost = CostService.FormatCost(alert.Cost)
      });
    }


    internal async Task SendErrorAsync(string title, string message)
    {
      PostMessage(new ErrorMessage { Message = $"{title}: {message}" });
      await Task.CompletedTask;
    }

    internal async Task SendProfileDataAsync(ProfileData profile)
    {
      PostMessage(new ProfileDataMessage { Data = profile });
      await Task.CompletedTask;
    }

    internal async Task SendConfigLoadedAsync(KiloExtensionDTOs.KiloConfig.Config config, FeatureFlags features)
    {
      PostMessage(new ConfigLoadedMessage { Config = config, Features = features });
      await Task.CompletedTask;
    }

    internal async Task SendMcpStatusAsync(JsonElement status)
    {
      // todo : use strong typed data
      PostMessage(new McpStatusLoadedMessage { Status = status });
      await Task.CompletedTask;
    }

    internal async Task SendNotificationsAsync(List<KilocodeNotification> notifications)
    {
      PostMessage(new NotificationsLoadedMessage { Notifications = notifications });
      await Task.CompletedTask;
    }

    internal async Task SendKiloEmbeddingModelsAsync(object[] models)
    {
      // TODO : check strong typed
      PostMessage(new KiloEmbeddingModelsLoadedMessage { Catalog = models });
      await Task.CompletedTask;
    }

    internal async Task SendImageModelsAsync(List<ApiImageModel> models)
    {
      PostMessage(new ImageModelsLoadedMessage
      {
        Models = models.Select(m => new ModelsItemType
        {
          Id = m.Id,
          Name = m.Name
        }).ToList()
      });
      await Task.CompletedTask;
    }

    internal async Task SendSkillsAsync(List<SkillInfo> skills)
    {
      PostMessage(new SkillsLoadedMessage { Skills = skills });
      await Task.CompletedTask;
    }

    internal async Task SendCommandsAsync(List<SlashCommandInfo> commands)
    {
      PostMessage(new CommandsLoadedMessage { Commands = commands });
      await Task.CompletedTask;
    }

    internal async Task DisposeGlobal()
    {
      await _serviceProvider.GetService<ICacheService>().ClearAsync();
      await Task.CompletedTask;
    }

    internal async Task FetchAndSendProvidersAsync()
    {
      await _providerRequestService.FetchAndSendProvidersAsync();
    }

    internal async Task FetchAndSendConfigUpdatedAsync()
    {
      await _configService.FetchAndSendConfigUpdatedAsync();
    }

    internal async Task FetchAndSendAgentsAsync()
    {
      await _agentRequestService.FetchAndSendAgentsAsync();
    }
    internal async Task FetchAndSendConfigAsync()
    {
      await _configService.FetchAndSendConfigAsync();
    }

    internal async Task FetchAndSendSkillsAsync()
    {
      await _miscRequestService.FetchAndSendSkillsAsync();
    }

    internal async Task FetchAndSendMcpStatusAsync()
    {
      await _mcpHandlerService.HandleRequestMcpStatusAsync();
    }

    internal async Task FetchAndSendCommandsAsync()
    {
      await _miscRequestService.FetchAndSendCommandsAsync();
    }
    internal async Task FetchAndSendIndexingStatusAsync()
    {
      await _indexingService.FetchAndSendIndexingStatusAsync();
    }

    internal async Task FetchAndSendNotificationsAsync()
    {
      await _notificationService.FetchAndSendNotificationsAsync();
    }

    //internal IReadOnlyDictionary<string, string> GetSessionDirectories()
    //{
    //  return _sseHelper.GetSessionDirectories();
    //}

    //internal bool IsTrackedSession(string sessionID)
    //{
    //  return _sseHelper.IsTrackedSession(sessionID);
    //}

    //internal SSEHandlerService GetSSEHelper()
    //{
    //  return _sseHelper;
    //}

    internal async Task SendGlobalConfigAsync(KiloExtensionDTOs.KiloConfig.Config config)
    {
      PostMessage(new GlobalConfigLoadedMessage { Config = config });
      await Task.CompletedTask;
    }

    internal async Task SendIndexingStatusAsync(JsonElement status)
    {
      PostMessage(new IndexingStatusLoadedMessage { Status = status });
      await Task.CompletedTask;
    }

    internal async Task SendWorkStyleLoadedAsync(WorkStyleStateEnum style)
    {
      PostMessage(new WorkStyleLoadedMessage { Style = style });
      await Task.CompletedTask;
    }

    internal async Task SendSessionCreatedAsync(KiloExtensionDTOs.Sessions.SessionInfo session)
    {
      PostMessage(new SessionCreatedMessage { Session = session });
      await Task.CompletedTask;
    }

    internal async Task SendSessionDeletedAsync(string sessionID)
    {
      PostMessage(new SessionDeletedMessage { SessionID = sessionID });
      await Task.CompletedTask;
    }

    internal async Task SendSessionUpdatedAsync(KiloExtensionDTOs.Sessions.SessionUpdate session)
    {
      PostMessage(new SessionUpdatedMessage { Session = session });
      await Task.CompletedTask;
    }

    internal async Task SendMessageDeletedAsync(string sessionID, string messageID)
    {
      PostMessage(new MessageRemovedMessage { MessageID = messageID, SessionID = sessionID });
      await Task.CompletedTask;
    }

    internal void ClearCurrentSession()
    {
      _currentSession = null;
//      _contextSessionID = null;
    }

    //internal void RemoveTrackedSession(string sessionID)
    //{
    //  SessionHandler.UntrackSession(sessionID);
    //}

    //internal void TrackSession(string sessionID)
    //{
    //  SessionHandler.TrackSession(sessionID);
    //}

    //internal bool IsSessionTracked(string sessionID)
    //{
    //  return SessionHandler.IsTrackedSession(sessionID);
    //}

    //internal void UntrackSession(string sessionID)
    //{
    //  SessionHandler.UntrackSession(sessionID);
    //}

    private SessionHandlerService SessionHandler => _sessionService;

    internal void FocusSession(string? sessionID)
    {
      _streamScheduler.Focus(sessionID);
      RegisterPresence();
    }

    /**
   * Drops every per-session cache entry we hold for the given id. Shared between
   * the user-initiated delete path (handleDeleteSession, after the backend
   * confirms) and the SSE session.deleted path (cascaded child deletes and
   * external CLI/TUI deletes that arrive via the event stream), so both paths
   * leave trackedSessionIds, sessionDirectories, and the related Maps in the
   * same state — including currentSession / contextSessionID / focused-session
   * registration. Without clearing those three, resolveSession() would still
   * see the deleted id via this.currentSession and the next send would target
   * a session the backend has already deleted.
   */
    internal void PruneDeletedSession(string sessionID)
    {
      SessionHandler.UntrackSession(sessionID);
      _openSessionIds.Remove(sessionID);
      foreach (var key in _draftSessions.Keys.Where(k => _draftSessions[k].Sid == sessionID).ToArray())
      {
        _draftSessions.Remove(key);
      }

      _streamScheduler.Drop(sessionID);
      _visibleTaskStreams.Delete(sessionID);
      SessionHandler.RemoveSyncedChildSession(sessionID);
      _projectDirectoryService.ClearSessionDirectory(sessionID);
      SessionHandler.Aborts.Delete(sessionID);
      _lastReconciledAt.Remove(sessionID);
      _checkpoints.Remove(sessionID);
      _refreshes.Remove(sessionID);
      if (_activeAlerts.ContainsKey(sessionID))
      {
        var limit = _activeAlerts[sessionID];
        _activeAlerts.Remove(sessionID);
        PostMessage(new SessionCostAlertResolvedMessage { Limit = limit, SessionID = sessionID });
      }
      SessionHandler.PruneSession(sessionID);

      if (GetCurrentSessionID() == sessionID)
      {
        SetContextSessionID(null);
        SetCurrentSession(null);
      }
      if (_streamScheduler.Focused == sessionID) FocusSession(null);
    }

    internal void TrackOpenSessions(List<string> ids)
    {
      var next = new HashSet<string>(ids);
      foreach (var id in _openSessionIds)
        if (!next.Contains(id))
          SessionHandler.UntrackSession(id);
      _openSessionIds.Clear();
      foreach (var id in _openSessionIds)
      {
        _openSessionIds.Add(id);
        SessionHandler.TrackSession(id);
      }
      var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
      foreach (var kv in _draftSessions)
      {
        if (next.Contains(kv.Value.Sid) || kv.Value.Expires <= now)
          _draftSessions.Remove(kv.Key);
      }
      RegisterPresence();
      RecoverPendingPrompts();
    }

    /**
    * Report presence for this provider: the focused session is visible, and
    * open local tab sessions (plus the focused one) stay attached even while
    * the view is hidden.
*/
    internal void RegisterPresence()
    {
      if (_opts.DisableViewedRegistration) return;
      var focused = _streamScheduler.Focused;
      _connectionService.RegisterVisible(this.instanceId, focused != null ? [focused] : []);
      var attached = new HashSet<string>(this._openSessionIds.ToArray());
      if (focused != null)
        attached.Add(focused);
      _connectionService.RegisterAttached(this.instanceId, attached);
    }


    internal void StopCurrentSessionProcesses(string? next)
    {
      var sid = _contextSessionID ?? GetCurrentSessionID();
      if (string.IsNullOrEmpty(sid) || sid == next) return;
      System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: stopping processes for session {sid}");
    }

    internal void DropSessionStream(string sessionID)
    {
      _streamScheduler.Drop(sessionID);
      System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: dropping stream for session {sessionID}");
    }

    internal void FlushSessionStream(string sessionID)
    {
      _streamScheduler.Flush(sessionID);
      System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: flushing stream for session {sessionID}");
    }

    internal void RecoverPendingPrompts()
    {
      _promptRecoveryQueued = true;
      if (!_isWebviewReady) return;
      var nswagClient = _connectionService.GetNswagClient();
      if (nswagClient == null) return;
      if (_promptRecovery != null) return;

      _promptRecovery = FlushPendingPromptsAsync().ContinueWith(_ =>
      {
        _promptRecovery = null;
        if (_promptRecoveryQueued && _isWebviewReady)
        {
          RecoverPendingPrompts();
        }
      });
    }

    public string? GetCurrentSessionID()
    {
      return _currentSession?.Id ?? null;
    }

    public ApiClient.Session GetCurrentSession()
    {
      return _currentSession;
    }

    public void SetCurrentSession(ApiClient.Session? session)
    {
      var ids = new HashSet<string> { _currentSession?.Id, session?.Id };
      foreach (var id in ids)
        if (id != null) _refreshes[id] = (_refreshes.ContainsKey(id) ? _refreshes[id] : 0) + 1;

      _currentSession = session;
      _opts.TabTitle?.Invoke(NativeTabTitle.GetTitle(session));
      //if (!string.IsNullOrEmpty(sessionID))
      //  SessionHandler.TrackSession(sessionID);
    }

    public void SetContextSessionID(string? sessionID)
    {
      _contextSessionID = sessionID;
    }

    internal async Task StoreStateAsync(JsonElement state)
    {
      _webviewState = state;
      await Task.CompletedTask;
    }

    internal async Task<JsonElement?> GetStoredStateAsync()
    {
      return _webviewState;
    }

    internal async Task<bool> CreateSessionInternalAsync()
    {
      return await CreateSessionInternalAsync(System.Environment.CurrentDirectory);
    }

    internal async Task HandlePromptAsync(JsonElement? payload)
    {
      await _interactionService.HandlePromptAsync(payload);
    }

    /// <summary>
    /// Loads messages for a session. Called from SubAgentViewerProvider.
    /// </summary>
    /// <param name="sessionID">The session ID to load messages for.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    internal async Task LoadMessagesAsync(string sessionID)
    {
      await _sessionService.HandleLoadMessagesAsync(JsonSerializer.SerializeToElement(new { sessionID, mode = "replace", limit = 80 }));
    }

    #endregion

    private void HandleMessageReceived(object? sender, WebViewMessageEventArgs e)
    {
      System.Diagnostics.Debug.WriteLine($"[Kilo] KiloProvider: received message type={e.Type}");
      _ = ProcessMessageAsync(e.Type, e.Payload);
    }


    /// <summary>
    /// Re-fetch all server-side state after an auth change.
    /// Matches TypeScript's reloadAfterAuthChange pattern.
    /// </summary>
    internal async Task ReloadAfterAuthChangeAsync()
    {
    //   _requirements.Clear(); AgentsRequirementController => Future plan
      await FetchAndSendConfigAsync();
      await Task.WhenAll(
        FetchAndSendProvidersAsync(),
        FetchAndSendAgentsAsync(),
        FetchAndSendSkillsAsync(),
        FetchAndSendCommandsAsync(),
        FetchAndSendIndexingStatusAsync(),
        FetchAndSendNotificationsAsync()
      );
    }


    /// <summary>
    /// Deserializes a WebView message from JsonElement to a strongly-typed DTO using WebViewMessageFactory.
    /// Returns null if the message type is not recognized or deserialization fails.
    /// </summary>
    private T? DeserializeWebViewMessage<T>(JsonElement? payload) where T : class
    {
      if (!payload.HasValue)
        return null;

      try
      {
        // Convert JsonElement to JSON string, then to JToken for WebViewMessageFactory
        var json = payload.Value.GetRawText();
        var jToken = Newtonsoft.Json.Linq.JToken.Parse(json);
        return WebViewMessageFactory.Deserialize<T>(jToken);
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: Failed to deserialize message to {typeof(T).Name}: {ex.Message}");
        return null;
      }
    }

    private async Task ProcessMessageAsync(string type, JsonElement? payload)
    {
      try
      {
        switch (type)
        {
          case "webviewReady":
            System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: webviewReady received");
            _isWebviewReady = true;
            await HandleWebviewReadyAsync();
            break;

          case "requestProviders":
            await FetchAndSendProvidersAsync();
            break;

          case "requestAgents":
            await FetchAndSendAgentsAsync();
            break;

          case "requestConfig":
            await FetchAndSendConfigAsync();
            break;

          case "requestMcpStatus":
            await FetchAndSendMcpStatusAsync();
            break;

          //////case "requestMemory":
          //////  await FetchAndSendMemoryAsync();
          //////  break;

          //    case "requestRecents":
          //      _miscRequestHandler.HandleRequestRecents(payload);
          //      break;

          //    case "requestFavorites":
          //      _miscRequestHandler.HandleRequestFavorites(payload);
          //      break;

          //    case "requestVariants":
          //      _miscRequestHandler.HandleRequestVariants(payload);
          //      break;

          //    case "requestNotifications":
          //      await _notificationHandler.FetchAndSendNotificationsAsync();
          //      break;

          //    case "requestModelSelections":
          //      _modelHandler.HandleRequestModelSelections(payload);
          //      break;

          case "requestIndexingSettings":
            await _settingsService.FetchAndSendIndexingSettings();
            break;

          case "requestChatSettings":
            _settingsHandler.HandleRequestChatSettings(payload);
            break;

          //    case "requestThroughputSetting":
          //      _settingsHandler.HandleRequestThroughputSetting(payload);
          //      break;

          case "requestAutocompleteSettings":
            _settingsHandler.HandleRequestAutocompleteSettings(payload);
            break;

          case "requestWorkStyle":
            await _settingsHandler.HandleRequestWorkStyleAsync(payload);
            break;

          //    case "retryConnection":
          //      System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: retryConnection requested");
          //      await _connectionService.ConnectAsync();
          //      break;

          //    case "prompt":
          //      await _interactionHandler.HandlePromptAsync(payload);
          //      break;

          //    case "permission/reply":
          //      await _interactionHandler.HandlePermissionReplyAsync(payload);
          //      break;

          //    case "permissionResponse":
          //      await _interactionHandler.HandlePermissionResponseAsync(payload);
          //      break;

          //    case "question/reply":
          //      await _interactionHandler.HandleQuestionReplyAsync(payload);
          //      break;

          //    case "questionReject":
          //      await _interactionHandler.HandleQuestionRejectAsync(payload);
          //      break;

          //    case "questionReply":
          //      await _interactionHandler.HandleQuestionReplyAsync(payload);
          //      break;

          //    case "cancelLogin":
          //      _loginAttempt++;
          //      PostMessage(JsonSerializer.Serialize(new { type = "deviceAuthCancelled" }));
          //      break;

          //    case "connectProvider":
          //      await _providerActionService.HandleConnectProviderAsync(payload);
          //      break;

          //    case "disconnectProvider":
          //      await _providerActionService.HandleDisconnectProviderAsync(payload);
          //      break;

          //    case "authorizeProviderOAuth":
          //      await _providerActionService.HandleAuthorizeProviderOAuthAsync(payload);
          //      break;

          //    case "completeProviderOAuth":
          //      await _providerActionService.HandleCompleteProviderOAuthAsync(payload);
          //      break;

          //    case "saveCustomProvider":
          //      await _providerActionService.HandleSaveCustomProviderAsync(payload);
          //      break;

          //    case "createSession":
          //      await _sessionHandler.HandleCreateSessionAsync(payload);
          //      break;

          //    case "clearSession":
          //      _sessionHandler.HandleClearSession(payload);
          //      break;

          //    case "setState":
          //      await _stateManagementHandler.HandleSetStateAsync(payload);
          //      break;

          //    case "getState":
          //      await _stateManagementHandler.HandleGetStateAsync();
          //      break;

          //    case "loadMessages":
          //      await _sessionHandler.HandleLoadMessagesAsync(payload);
          //      break;

          //    case "deleteMessage":
          //      await _sessionHandler.HandleDeleteMessageAsync(payload);
          //      break;

          //    case "deleteSession":
          //      await _sessionHandler.HandleDeleteSessionAsync(payload);
          //      break;

          //    case "renameSession":
          //      await _sessionHandler.HandleRenameSessionAsync(payload);
          //      break;

          //    case "loadSessions":
          //      await _sessionHandler.HandleLoadSessionsAsync(payload);
          //      break;

          //    case "syncSession":
          //      await _sessionHandler.HandleSyncSessionAsync(payload);
          //      break;

          //    case "requestSessionModelUsage":
          //      await _sessionHandler.HandleRequestSessionModelUsageAsync(payload);
          //      break;

          //    case "revertSession":
          //      await _sessionHandler.HandleRevertSessionAsync(payload);
          //      break;

          //    case "unrevertSession":
          //      await _sessionHandler.HandleUnrevertSessionAsync(payload);
          //      break;

          //    case "compact":
          //      await _sessionHandler.HandleCompactAsync(payload);
          //      break;

          //    case "abort":
          //      await _sessionControlHandler.HandleAbortAsync(payload);
          //      break;

          //    case "sendMessage":
          //      await _sessionControlHandler.HandleSendMessageAsync(payload);
          //      break;

          //    case "login":
          //      _loginAttempt++;
          //      await _authHandler.HandleLoginAsync(payload);
          //      break;

          //    case "refreshProfile":
          //      await _authHandler.HandleRefreshProfileAsync(payload);
          //      break;

          //    case "logout":
          //      await _authHandler.HandleLogoutAsync(payload);
          //      break;

          //    case "setOrganization":
          //      await _authHandler.HandleSetOrganizationAsync(payload);
          //      break;

          //    case "openSettingsPanel":
          //      await _uiHandler.HandleOpenSettingsPanelAsync(payload);
          //      break;

          //    case "openConfigFile":
          //      await _configHandler.HandleOpenConfigFileAsync(payload);
          //      break;

          //    case "updateSetting":
          //      await _configHandler.HandleUpdateSettingAsync(payload);
          //      break;

          //    case "updateConfig":
          //      await _configHandler.HandleUpdateConfigAsync(payload);
          //      break;

          //    case "requestSkills":
          //      await _miscRequestHandler.FetchAndSendSkillsAsync();
          //      break;

          //    case "requestCommands":
          //      await _miscRequestHandler.FetchAndSendCommandsAsync();
          //      break;

          //    case "requestGlobalConfig":
          //  //    await _configHandler.HandleRequestGlobalConfigAsync();
          //      break;

          //    case "requestIndexingStatus":
          //      await _settingsHandler.HandleRequestIndexingStatusAsync(payload);
          //      break;

          //    case "requestKiloEmbeddingModels":
          //      await _modelHandler.HandleRequestKiloEmbeddingModelsAsync(payload);
          //      break;

          //    case "requestImageModels":
          //      await _modelHandler.HandleRequestImageModelsAsync(payload);
          //      break;

          //    case "settingsTabChanged":
          //      _uiHandler.HandleSettingsTabChanged(payload);
          //      break;

          //    case "forkSession":
          //      await _sessionControlHandler.HandleForkSessionAsync(payload);
          //      break;

          //    case "reload":
          //      await _uiHandler.HandleReloadAsync(payload);
          //      break;

          //    case "saveImage":
          //      await _uiHandler.HandleSaveImageAsync(payload);
          //      break;

          //    case "openExternal":
          //      await HandleOpenExternalAsync(payload);
          //      break;

          //    case "cycleAgentMode":
          //      // Fire-and-forget: broadcast to all providers without awaiting
          //      HandleCycleAgentModeAsync(payload);
          //      break;

          //    case "toggleMemory":
          //      await HandleToggleMemoryAsync(payload);
          //      break;

          //    case "showMemory":
          //      await HandleShowMemoryAsync(payload);
          //      break;

          //    case "fetchCustomProviderModels":
          //      await HandleFetchCustomProviderModelsAsync(payload);
          //      break;

          //    case "removeSkill":
          //      await HandleRemoveSkillAsync(payload);
          //      break;

          //    case "removeAgent":
          //      await HandleRemoveAgentAsync(payload);
          //      break;

          //    case "openSubAgentViewer":
          //      await _uiHandler.HandleOpenSubAgentViewerAsync(payload);
          //      break;

          //    case "openMarketplacePanel":
          //      // Fire-and-forget: execute marketplace command without awaiting
          //      HandleOpenMarketplacePanelAsync(payload);
          //      break;

          //    case "agentManager.createWorktree":
          //      await HandleCreateWorktreeAsync(payload);
          //      break;

          //    case "agentManager.deleteWorktree":
          //      await HandleDeleteWorktreeAsync(payload);
          //      break;

          //    case "agentManager.promoteSession":
          //      await HandlePromoteSessionAsync(payload);
          //      break;

          //    case "agentManager.forkSession":
          //      await HandleForkSessionToWorktreeAsync(payload);
          //      break;

          //    case "agentManager.openLocally":
          //      await HandleOpenWorktreeLocallyAsync(payload);
          //      break;

          //    case "agentManager.requestState":
          //      await HandleRequestAgentManagerStateAsync(payload);
          //      break;

          //    case "agentManager.setTabOrder":
          //      await HandleSetTabOrderAsync(payload);
          //      break;

          //    case "agentManager.showTerminal":
          //      await HandleShowTerminalAsync(payload);
          //      break;

          //    case "agentManager.requestWorktreeDiff":
          //      await HandleRequestWorktreeDiffAsync(payload);
          //      break;

          //    case "agentManager.applyWorktreeDiff":
          //      await HandleApplyWorktreeDiffAsync(payload);
          //      break;

          //    case "agentManager.startDiffWatch":
          //      await HandleStartDiffWatchAsync(payload);
          //      break;

          //    case "agentManager.openFile":
          //      await HandleOpenFileAsync(payload);
          //      break;

          default:
            System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: unhandled message type={type}");
            break;
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error processing message {type}: {ex.Message}");
        await SendErrorAsync("Message processing error", ex.Message);
      }
    }

    protected virtual async Task HandleWebviewReadyAsync()
    {
      System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: webviewReady received");
      _isWebviewReady = true;
      _visibleTaskStreams.Clear();
      FlushPendingKiloModel();
      await SyncWebviewStateAsync("webviewReady");
      FlushPendingReviewComments();
      RecoverPendingPrompts();
      ResolveReadyResolvers();
      System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: webviewReady initialization complete");
    }

    private void FlushPendingKiloModel()
    {
      if (!_isWebviewReady || _pendingKiloModel == null) return;

      var pending = _pendingKiloModel;
      _pendingKiloModel = null;

      if (pending is JsonElement element)
      {
        var message = new { type = "selectKiloModel", modelID = element.TryGetProperty("modelID", out var modelId) ? modelId.GetString() : null, agent = element.TryGetProperty("agent", out var agent) ? agent.GetString() : null };
        _webView.PostMessage(JsonSerializer.Serialize(message));
      }
    }

    public void SelectKiloModel(string? modelID = null, string? agent = null)
    {
      if (string.IsNullOrEmpty(modelID) && string.IsNullOrEmpty(agent)) return;
      _pendingKiloModel = JsonSerializer.SerializeToElement(new { modelID, agent });
      FlushPendingKiloModel();
    }

    private async Task SyncWebviewStateAsync(string reason)
    {
      System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: syncWebviewState({reason})");

      if (!_isWebviewReady)
      {
        System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: syncWebviewState skipped (webview not ready)");
        return;
      }

      var connState = new { type = "connectionState", state = _connectionService.State.ToString().ToLowerInvariant() };
      _webView.PostMessage(JsonSerializer.Serialize(connState));

      var serverInfo = _connectionService.GetServerInfo();

      if (serverInfo != null)
      {
        var langConfig = "en";
        var extensionVersion = "7.4.17";
        var readyMessage = new
        {
          type = "ready",
          serverInfo,
          extensionVersion,
          vscodeLanguage = langConfig,
          languageOverride = (string?)null,
          workspaceDirectory = System.Environment.CurrentDirectory
        };
        _webView.PostMessage(JsonSerializer.Serialize(readyMessage));
      }

      if (_connectionService.State == ConnectionState.Connected)
      {
        try
        {
          var nswagClient = _connectionService.GetNswagClient();
          if (nswagClient != null)
          {
            var profile = await nswagClient.Kilo_profileAsync(System.Environment.CurrentDirectory, "");
            var profileData = profile != null ? JsonSerializer.SerializeToElement(profile) : (JsonElement?)null;

            var profileMessage = new { type = "profileData", data = profileData };
            _webView.PostMessage(JsonSerializer.Serialize(profileMessage));
          }
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error fetching profile: {ex.Message}");
        }

        await RefreshSessionDetailsAsync();

        if (_cachedStats != null)
        {
          _webView.PostMessage(JsonSerializer.Serialize(_cachedStats));
        }
        var gitStatusMessage = new { type = "gitStatus", repo = _cachedGitRepo };
        _webView.PostMessage(JsonSerializer.Serialize(gitStatusMessage));

        var reconcile = _sessionStatusMap.Count == 0;
        await SeedSessionStatusMapAsync(reconcile);

        SendRemoteStatus();
      }

      if (_webviewState.HasValue)
      {
        var stateMessage = new { type = "setState", state = _webviewState.Value };
        _webView.PostMessage(JsonSerializer.Serialize(stateMessage));
        System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: restored state to webview");
      }

      var extensionReady = new { type = "extensionDataReady" };
      _webView.PostMessage(JsonSerializer.Serialize(extensionReady));
      System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: extensionDataReady sent");
    }

    private async Task RefreshSessionDetailsAsync()
    {
      var _currentSessionId = GetCurrentSessionID();
      if (string.IsNullOrEmpty(_currentSessionId)) return;

      var nswagClient = _connectionService.GetNswagClient();
      if (nswagClient == null) return;

      try
      {
        var sessions = await nswagClient.Session_listAsync(System.Environment.CurrentDirectory, "", null, "", null, null, null, null);
        if (sessions != null)
        {
          var session = sessions.FirstOrDefault(s => s.Id == _currentSessionId);
          if (session != null)
          {
            var updatedMessage = new { type = "sessionUpdated", session = JsonSerializer.SerializeToElement(session) };
            _webView.PostMessage(JsonSerializer.Serialize(updatedMessage));
          }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error refreshing session details: {ex.Message}");
      }
    }

    private async Task SeedSessionStatusMapAsync(bool reconcile)
    {
      var nswagClient = _connectionService.GetNswagClient();
      if (nswagClient == null) return;

      try
      {
        var sessions = await nswagClient.Session_listAsync(System.Environment.CurrentDirectory, "", null, "", null, null, null, null);
        if (sessions != null)
        {
          foreach (var session in sessions)
          {
            if (!string.IsNullOrEmpty(session.Id))
            {
              var sessionID = session.Id;
              if (!reconcile || !_sessionStatusMap.ContainsKey(sessionID))
              {
                _sessionStatusMap[sessionID] = "idle";
              }
            }
          }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error seeding session status map: {ex.Message}");
      }
    }

    private void SendRemoteStatus()
    {
    }

    public void SetCachedStats(JsonElement stats)
    {
      _cachedStats = stats;
    }

    public void SetCachedGitRepo(bool isRepo)
    {
      _cachedGitRepo = isRepo;
    }

    public void UpdateSessionStatus(string sessionID, string status)
    {
      _sessionStatusMap[sessionID] = status;
    }

    private void FlushPendingReviewComments()
    {
      if (!_isWebviewReady || _pendingReviewComments == null || _pendingReviewComments.Count == 0) return;

      var pending = _pendingReviewComments;
      _pendingReviewComments = null;

      foreach (var entry in pending)
      {
        if (entry is JsonElement element)
        {
          var autoSend = element.TryGetProperty("autoSend", out var autoSendProp) && autoSendProp.GetBoolean();
          JsonElement? comments = null;
          if (element.TryGetProperty("comments", out var commentsProp))
          {
            comments = commentsProp.Clone();
          }
          PostMessage(JsonSerializer.Serialize(new { type = "appendReviewComments", comments, autoSend }));
        }
      }
    }

    public async Task AppendReviewCommentsAsync(object comments, bool autoSend = false)
    {
      if (_pendingReviewComments == null)
      {
        _pendingReviewComments = new List<JsonElement>();
      }
      _pendingReviewComments.Add(JsonSerializer.SerializeToElement(new { comments, autoSend }));

      if (!_isWebviewReady)
      {
        return;
      }

      FlushPendingReviewComments();
    }

    private async Task FlushPendingPromptsAsync()
    {
      while (_promptRecoveryQueued && _isWebviewReady)
      {
        var nswagClient = _connectionService.GetNswagClient();
        if (nswagClient == null) return;

        _promptRecoveryQueued = false;

        var dirs = new[] { System.Environment.CurrentDirectory };
        var seen = new HashSet<string>();

        foreach (var dir in dirs)
        {
          try
          {
            var permissions = await nswagClient.Permission_listAsync(dir, "");
            if (permissions != null)
            {
              foreach (var perm in permissions)
              {
                if (!string.IsNullOrEmpty(perm.Id) && !seen.Contains(perm.Id))
                {
                  var requestId = perm.Id;
                  seen.Add(requestId);

                  if (!string.IsNullOrEmpty(perm.SessionID))
                  {
                    var sessionID = perm.SessionID;
                    var permission = perm.Permission ?? "";
                    var patterns = perm.Patterns;
                    var always = perm.Always?.Count > 0;
                    var metadata = perm.Metadata;
                    var tool = perm.Tool != null ? JsonSerializer.SerializeToElement(perm.Tool) : (JsonElement?)null;

                    PostMessage(JsonSerializer.Serialize(new
                    {
                      type = "permissionRequest",
                      permission = new
                      {
                        id = requestId,
                        sessionID,
                        toolName = permission,
                        patterns,
                        always,
                        args = metadata,
                        message = $"Permission required: {permission}",
                        tool
                      }
                    }));
                  }
                }
              }
            }
          }
          catch (Exception ex)
          {
            System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error fetching permissions for {dir}: {ex.Message}");
          }
        }

        foreach (var dir in dirs)
        {
          try
          {
            var questions = await nswagClient.Question_listAsync(dir, "");
            if (questions != null)
            {
              foreach (var q in questions)
              {
                if (!string.IsNullOrEmpty(q.Id) && !seen.Contains(q.Id))
                {
                  var requestId = q.Id;
                  seen.Add(requestId);

                  if (!string.IsNullOrEmpty(q.SessionID))
                  {
                    var sessionID = q.SessionID;
                    var blocking = q.Blocking;
                    var tool = q.Tool != null ? JsonSerializer.SerializeToElement(q.Tool) : (JsonElement?)null;

                    PostMessage(JsonSerializer.Serialize(new
                    {
                      type = "questionRequest",
                      question = new
                      {
                        id = requestId,
                        sessionID,
                        questions = q.Questions != null ? JsonSerializer.SerializeToElement(q.Questions) : (JsonElement?)null,
                        blocking,
                        tool
                      }
                    }));
                  }
                }
              }
            }
          }
          catch (Exception ex)
          {
            System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error fetching questions for {dir}: {ex.Message}");
          }
        }

        foreach (var dir in dirs)
        {
          try
          {
            var suggestions = await nswagClient.Suggestion_listAsync(dir, "");
            if (suggestions != null)
            {
              foreach (var suggestion in suggestions)
              {
                if (!string.IsNullOrEmpty(suggestion.Id) && !seen.Contains(suggestion.Id))
                {
                  seen.Add(suggestion.Id);
                  PostMessage(JsonSerializer.Serialize(new { type = "suggestionRequest", suggestion = JsonSerializer.SerializeToElement(suggestion) }));
                }
              }
            }
          }
          catch (Exception ex)
          {
            System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error fetching suggestions for {dir}: {ex.Message}");
          }
        }
      }
    }

    private async Task<bool> CreateSessionInternalAsync(string dir)
    {
      var nswagClient = _connectionService.GetNswagClient();
      if (nswagClient == null)
      {
        System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: cannot create session - no NSwag client");
        return false;
      }
      try
      {
        var body = new SessionCreateRequest();
        var response = await nswagClient.Session_createAsync(dir, "", body);
        if (response != null && !string.IsNullOrEmpty(response.Id))
        {
          var sessionID = response.Id;
          System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: session created: {sessionID}");

          //_currentSessionID = sessionID;
          _contextSessionID = sessionID;

          var now = DateTimeOffset.UtcNow;
          var sessionCreated = new
          {
            type = "sessionCreated",
            session = new
            {
              id = sessionID,
              parentID = (string?)null,
              title = "New Chat",
              createdAt = now.UtcDateTime.ToString("o"),
              updatedAt = now.UtcDateTime.ToString("o"),
              revert = (object?)null,
              summary = (object?)null
            }
          };
          _webView.PostMessage(JsonSerializer.Serialize(sessionCreated));

          FocusSession(sessionID);
          return true;
        }
        System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: session creation failed - no ID in response");
        return false;
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error creating session: {ex.Message}");
        PostMessage(JsonSerializer.Serialize(new { type = "error", message = $"Failed to create session: {ex.Message}" }));
        return false;
      }
    }

    public Task WaitForReadyAsync()
    {
      if (_isWebviewReady)
      {
        return Task.CompletedTask;
      }
      var tcs = new TaskCompletionSource<bool>();
      _readyResolvers.Add(() => tcs.SetResult(true));
      return tcs.Task;
    }

    private void ResolveReadyResolvers()
    {
      var resolvers = _readyResolvers.ToArray();
      _readyResolvers.Clear();
      foreach (var resolver in resolvers)
      {
        resolver();
      }
    }

    private void HandleStateChange(object? sender, ConnectionStateEventArgs e)
    {
      var message = new { type = "connectionState", state = e.State.ToString().ToLowerInvariant(), errorMessage = e.ErrorMessage };
      _webView.PostMessage(JsonSerializer.Serialize(message));

      if (e.State == ConnectionState.Connected)
      {
        if (_interactionService != null)
        {
          _ = _interactionService.FetchAndSendPendingPermissionsAsync();
          _ = _interactionService.FetchAndSendPendingQuestionsAsync();
        }
      }
    }

    private void HandleSseEvent(object? sender, SseEventReceivedEventArgs e)
    {
      _sseHelper.HandleEvent(e);
    }

    #region Additional Message Handlers

    /// <summary>
    /// Handles openExternal message - opens a URL externally.
    /// Matches VS Code's openExternal pattern.
    /// </summary>
    private async Task HandleOpenExternalAsync(JsonElement? payload)
    {
      if (payload == null) return;

      var uri = payload.Value.TryGetProperty("uri", out var u) ? u.GetString() : "";

      if (string.IsNullOrEmpty(uri)) return;

      try
      {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
          FileName = uri,
          UseShellExecute = true
        });
        System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: opened external: {uri}");
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error opening external: {ex.Message}");
        await SendErrorAsync("Open failed", $"Failed to open URL: {ex.Message}");
      }
    }

    /// <summary>
    /// Handles cycleAgentMode message - cycles through agent modes.
    /// Matches VS Code's cycleAgentMode pattern - fire-and-forget broadcast to all providers.
    /// </summary>
    private void HandleCycleAgentModeAsync(JsonElement? payload)
    {
      System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: cycleAgentMode requested");
      // Fire-and-forget: broadcast to all providers without awaiting
      PostMessage(JsonSerializer.Serialize(new { type = "agentModeCycled" }));
    }

    /// <summary>
    /// Handles toggleMemory message - toggles session memory.
    /// Matches VS Code's toggleMemory pattern - uses memory service with user feedback.
    /// </summary>
    private async Task HandleToggleMemoryAsync(JsonElement? payload)
    {
      //var sessionID = payload != null && payload.Value.TryGetProperty("sessionID", out var sid) && !string.IsNullOrEmpty(sid.GetString())
      //    ? sid.GetString()
      //    : _currentSessionID;

      //try
      //{
      //  // TODO: Implement memory service toggle with proper user feedback
      //  // For now, send placeholder response matching VS Code's postMessage pattern
      //  System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: toggleMemory requested for session {sessionID}");
      //  PostMessage(JsonSerializer.Serialize(new { type = "memoryToggled", sessionID }));
      //}
      //catch (Exception ex)
      //{
      //  System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error toggling memory: {ex.Message}");
      //  // Match VS Code: show error message but don't throw
      //}
    }

    /// <summary>
    /// Handles showMemory message - shows memory panel.
    /// Matches VS Code's showMemory pattern - uses memory service.
    /// </summary>
    private async Task HandleShowMemoryAsync(JsonElement? payload)
    {
      //var sessionID = payload != null && payload.Value.TryGetProperty("sessionID", out var sid) && !string.IsNullOrEmpty(sid.GetString())
      //    ? sid.GetString()
      //    : _currentSessionID;

      //System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: showMemory requested for session {sessionID}");
      //// TODO: Implement memory service show with proper panel
      //PostMessage(JsonSerializer.Serialize(new { type = "memoryShown", sessionID }));
    }

    /// <summary>
    /// Handles fetchCustomProviderModels message - fetches models from a custom provider.
    /// Matches VS Code's handleFetchCustomProviderModels pattern.
    /// </summary>
    private async Task HandleFetchCustomProviderModelsAsync(JsonElement? payload)
    {
      if (payload == null) return;

      var providerID = payload.Value.TryGetProperty("providerID", out var pid) ? pid.GetString() : "";

      if (string.IsNullOrEmpty(providerID)) return;

      var nswagClient = _connectionService.GetNswagClient();
      if (nswagClient == null)
      {
        await SendErrorAsync("Not connected", "Not connected to CLI backend");
        return;
      }

      try
      {
        var response = await nswagClient.Provider_listAsync(System.Environment.CurrentDirectory, "");
        if (response?.All != null)
        {
          var provider = response.All.FirstOrDefault(p => p.Id == providerID);
          if (provider != null)
          {
            PostMessage(JsonSerializer.Serialize(new { type = "customProviderModelsFetched", providerID, models = provider.Models != null ? JsonSerializer.SerializeToElement(provider.Models) : JsonSerializer.SerializeToElement(new List<object>()) }));
          }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error fetching custom provider models: {ex.Message}");
        await SendErrorAsync("Fetch failed", $"Failed to fetch models: {ex.Message}");
      }
    }

    /// <summary>
    /// Handles removeSkill message - removes a skill.
    /// Matches VS Code's removeSkill pattern.
    /// </summary>
    private async Task HandleRemoveSkillAsync(JsonElement? payload)
    {
      if (payload == null) return;

      var location = payload.Value.TryGetProperty("location", out var loc) ? loc.GetString() : "";

      if (string.IsNullOrEmpty(location)) return;

      System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: skill remove endpoint not available in generated API: {location}");
      await SendErrorAsync("Remove failed", "Skill remove endpoint not available");
    }

    /// <summary>
    /// Handles removeAgent message - removes an agent.
    /// Matches VS Code's handleRemoveAgent pattern.
    /// </summary>
    private async Task HandleRemoveAgentAsync(JsonElement? payload)
    {
      if (payload == null) return;

      var name = payload.Value.TryGetProperty("name", out var n) ? n.GetString() : "";

      if (string.IsNullOrEmpty(name)) return;

      System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: agent remove endpoint not available in generated API: {name}");
      await SendErrorAsync("Remove failed", "Agent remove endpoint not available");
    }

    /// <summary>
    /// Handles openMarketplacePanel message - opens the marketplace panel.
    /// Matches VS Code's openMarketplacePanel pattern - executes marketplace command.
    /// </summary>
    private void HandleOpenMarketplacePanelAsync(JsonElement? payload)
    {
      var directory = payload != null && payload.Value.TryGetProperty("directory", out var dir) && !string.IsNullOrEmpty(dir.GetString())
          ? dir.GetString()
          : System.Environment.CurrentDirectory;

      System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: openMarketplacePanel requested: {directory}");
      // Fire-and-forget: execute marketplace command (matches VS Code's executeCommand pattern)
      // TODO: Implement marketplace panel opening via command
    }

    /// <summary>
    /// Handles agentManager.createWorktree message - creates a new worktree.
    /// Matches VS Code's onCreateWorktree pattern.
    /// </summary>
    private async Task HandleCreateWorktreeAsync(JsonElement? payload)
    {
      var baseBranch = payload != null && payload.Value.TryGetProperty("baseBranch", out var bb) ? bb.GetString() : null;
      var branchName = payload != null && payload.Value.TryGetProperty("branchName", out var bn) ? bn.GetString() : null;

      System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: createWorktree requested: baseBranch={baseBranch}, branchName={branchName}");

      // TODO: Implement actual worktree creation using git worktree commands
      // For now, send a placeholder response
      PostMessage(JsonSerializer.Serialize(new { type = "worktreeCreated", branch = branchName ?? "worktree-" + Guid.NewGuid().ToString("N").Substring(0, 8) }));
    }

    /// <summary>
    /// Handles agentManager.deleteWorktree message - deletes a worktree.
    /// Matches VS Code's onDeleteWorktree pattern.
    /// </summary>
    private async Task HandleDeleteWorktreeAsync(JsonElement? payload)
    {
      if (payload == null) return;

      var worktreeId = payload.Value.TryGetProperty("worktreeId", out var wt) ? wt.GetString() : "";

      if (string.IsNullOrEmpty(worktreeId)) return;

      System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: deleteWorktree requested: {worktreeId}");

      // TODO: Implement actual worktree deletion using git worktree commands
      PostMessage(JsonSerializer.Serialize(new { type = "worktreeDeleted", worktreeId }));
    }

    /// <summary>
    /// Handles agentManager.promoteSession message - promotes a session to a worktree.
    /// Matches VS Code's onPromoteSession pattern.
    /// </summary>
    private async Task HandlePromoteSessionAsync(JsonElement? payload)
    {
      if (payload == null) return;

      var sessionId = payload.Value.TryGetProperty("sessionId", out var sid) ? sid.GetString() : "";

      if (string.IsNullOrEmpty(sessionId)) return;

      System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: promoteSession requested: {sessionId}");

      // TODO: Implement actual session promotion
      PostMessage(JsonSerializer.Serialize(new { type = "sessionPromoted", sessionId }));
    }

    /// <summary>
    /// Handles agentManager.forkSession message - forks a session into a new worktree.
    /// Matches VS Code's fork session pattern.
    /// </summary>
    private async Task HandleForkSessionToWorktreeAsync(JsonElement? payload)
    {
      if (payload == null) return;

      var sessionId = payload.Value.TryGetProperty("sessionId", out var sid) ? sid.GetString() : "";
      var messageId = payload.Value.TryGetProperty("messageId", out var mid) ? mid.GetString() : "";

      if (string.IsNullOrEmpty(sessionId)) return;

      System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: forkSession requested: {sessionId}");

      // TODO: Implement actual session forking to worktree
      PostMessage(JsonSerializer.Serialize(new { type = "sessionForkedToWorktree", sessionId, messageId }));
    }

    /// <summary>
    /// Handles agentManager.openLocally message - opens a worktree in the system file explorer.
    /// Matches VS Code's openLocally pattern.
    /// </summary>
    private async Task HandleOpenWorktreeLocallyAsync(JsonElement? payload)
    {
      if (payload == null) return;

      var worktreeId = payload.Value.TryGetProperty("worktreeId", out var wt) ? wt.GetString() : "";
      var path = payload.Value.TryGetProperty("path", out var p) ? p.GetString() : "";

      if (string.IsNullOrEmpty(path)) return;

      try
      {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
          FileName = path,
          UseShellExecute = true,
          Verb = "explore"
        });
        System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: opened worktree locally: {path}");
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: error opening worktree locally: {ex.Message}");
      }
    }

    /// <summary>
    /// Handles agentManager.requestState message - requests the current Agent Manager state.
    /// Matches VS Code's requestState pattern.
    /// </summary>
    private async Task HandleRequestAgentManagerStateAsync(JsonElement? payload)
    {
      System.Diagnostics.Debug.WriteLine("[Kilo] VSProvider: requestAgentManagerState requested");

      // Return current state from state manager
      var state = new
      {
        worktrees = new object[0],
        sessions = new object[0],
        activeWorktreeId = (string?)null,
        activeSessionId = (string?)null
      };

      PostMessage(JsonSerializer.Serialize(new { type = "agentManagerStateLoaded", state }));
    }

    /// <summary>
    /// Handles agentManager.setTabOrder message - sets the tab order.
    /// Matches VS Code's setTabOrder pattern.
    /// </summary>
    private async Task HandleSetTabOrderAsync(JsonElement? payload)
    {
      if (payload == null) return;

      var order = payload.Value.TryGetProperty("order", out var o) ? o : JsonDocument.Parse("[]").RootElement;

      System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: setTabOrder requested");

      PostMessage(JsonSerializer.Serialize(new { type = "tabOrderSet" }));
    }

    /// <summary>
    /// Handles agentManager.showTerminal message - shows the terminal for a worktree.
    /// Matches VS Code's showTerminal pattern.
    /// </summary>
    private async Task HandleShowTerminalAsync(JsonElement? payload)
    {
      if (payload == null) return;

      var worktreeId = payload.Value.TryGetProperty("worktreeId", out var wt) ? wt.GetString() : "";
      var sessionId = payload.Value.TryGetProperty("sessionId", out var sid) ? sid.GetString() : "";

      System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: showTerminal requested: {sessionId}");

      PostMessage(JsonSerializer.Serialize(new { type = "terminalShown", sessionId }));
    }

    /// <summary>
    /// Handles agentManager.requestWorktreeDiff message - requests the diff for a worktree.
    /// Matches VS Code's requestWorktreeDiff pattern.
    /// </summary>
    private async Task HandleRequestWorktreeDiffAsync(JsonElement? payload)
    {
      if (payload == null) return;

      var worktreeId = payload.Value.TryGetProperty("worktreeId", out var wt) ? wt.GetString() : "";
      var path = payload.Value.TryGetProperty("path", out var p) ? p.GetString() : "";

      if (string.IsNullOrEmpty(path)) return;

      System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: requestWorktreeDiff requested: {worktreeId}");

      // TODO: Implement actual diff calculation
      PostMessage(JsonSerializer.Serialize(new { type = "worktreeDiffLoaded", worktreeId, diff = new { hunks = new object[0] } }));
    }

    /// <summary>
    /// Handles agentManager.applyWorktreeDiff message - applies a diff to a worktree.
    /// Matches VS Code's applyWorktreeDiff pattern.
    /// </summary>
    private async Task HandleApplyWorktreeDiffAsync(JsonElement? payload)
    {
      if (payload == null) return;

      var worktreeId = payload.Value.TryGetProperty("worktreeId", out var wt) ? wt.GetString() : "";

      System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: applyWorktreeDiff requested: {worktreeId}");

      PostMessage(JsonSerializer.Serialize(new { type = "worktreeDiffApplied", worktreeId }));
    }

    /// <summary>
    /// Handles agentManager.startDiffWatch message - starts watching for diff changes.
    /// Matches VS Code's startDiffWatch pattern.
    /// </summary>
    private async Task HandleStartDiffWatchAsync(JsonElement? payload)
    {
      if (payload == null) return;

      var worktreeId = payload.Value.TryGetProperty("worktreeId", out var wt) ? wt.GetString() : "";

      System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: startDiffWatch requested: {worktreeId}");

      PostMessage(JsonSerializer.Serialize(new { type = "diffWatchStarted", worktreeId }));
    }

    /// <summary>
    /// Handles agentManager.openFile message - opens a file in the editor.
    /// Matches VS Code's openFile pattern.
    /// </summary>
    private async Task HandleOpenFileAsync(JsonElement? payload)
    {
      if (payload == null) return;

      var path = payload.Value.TryGetProperty("path", out var p) ? p.GetString() : "";
      var line = payload.Value.TryGetProperty("line", out var l) && l.TryGetInt32(out var lineNum) ? lineNum : 0;
      var column = payload.Value.TryGetProperty("column", out var c) && c.TryGetInt32(out var colNum) ? colNum : 0;

      if (string.IsNullOrEmpty(path)) return;

      System.Diagnostics.Debug.WriteLine($"[Kilo] VSProvider: openFile requested: {path}:{line},{column}");

      // TODO: Implement actual file opening in Visual Studio
      PostMessage(JsonSerializer.Serialize(new { type = "fileOpened", path, line, column }));
    }

    #endregion

    #region Dispose

    /// <summary>
    /// Disposes of all resources used by the VSProvider.
    /// Unsubscribes from events and disposes handler services.
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;
      _disposed = true;
      _webView.OnMessageReceived -= HandleMessageReceived;
      _connectionService.OnStateChange -= HandleStateChange;
      _connectionService.UnregisterSSEEventHandler(this.instanceId);
      _streamScheduler?.Dispose();
      _visibleTaskStreams.Clear();
      _sessionService?.Dispose();
      _authService?.Dispose();
      _configService?.Dispose();
      _providerRequestService?.Dispose();
      _agentRequestService?.Dispose();
      _stateManagementService?.Dispose();
      _mcpHandlerService?.Dispose();
      _notificationService?.Dispose();
      _modelService?.Dispose();
      _settingsService?.Dispose();
      _miscRequestService?.Dispose();
      _interactionService?.Dispose();
      _sessionControlService?.Dispose();
      _uiService?.Dispose();
    }

    #endregion

  }
}
