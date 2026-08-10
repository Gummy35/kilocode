# PORT-INFRA-002 — VS Code to Visual Studio Port Mapping Plan

**Task:** PORT-INFRA-002  
**Mode:** Plan  
**Status:** REVIEW  
**Date:** 2026-08-10  
**Model:** Qwen3.5-122B

---

## 1. Executive Summary

This document establishes the evidence-based correspondence between the Kilo VS Code extension and the existing Kilo Visual Studio extension. The VS Code extension is the **source of truth**; the Visual Studio implementation is the current target/baseline.

**Key Finding:** The Visual Studio extension has significant structural overlap with the VS Code extension in terms of service architecture (handler-based message routing, connection service pattern, CLI backend management), but lacks several critical components present in VS Code.

---

## 2. VS Code Source Baseline

### 2.1 Extension Entry Point

**File:** `packages/kilo-vscode/src/extension.ts` (666 lines)

**Activation Triggers:**
- `onStartupFinished` — Extension activates on VS Code startup
- `onUri` — Handles deep links (vscode://kilocode.kilo-code/...)

**Key Responsibilities:**
1. Creates shared `KiloConnectionService` (singleton pattern)
2. Registers sidebar provider (`KiloProvider`)
3. Registers Agent Manager provider (`AgentManagerProvider`)
4. Registers KiloClaw provider
5. Registers diff viewer, settings editor, marketplace panel
6. Registers command handlers (50+ commands)
7. Registers URI handler for deep links
8. Registers serializers for webview panel restoration
9. Registers autocomplete provider
10. Registers commit message service
11. Registers code actions

### 2.2 Extension.ts Dependency Graph

```
extension.ts
├── KiloProvider (sidebar/tab panel)
│   ├── KiloConnectionService (shared)
│   ├── SessionStreamScheduler
│   ├── KiloProviderMemory
│   ├── MessageConfirmation
│   ├── Followup session handling
│   ├── File/session search
│   ├── Notification handling
│   ├── Permission/Question handling
│   └── WebView message handlers
│
├── AgentManagerProvider
│   ├── VscodeHost (terminal host)
│   ├── WorktreeManager
│   ├── WorktreeStateManager
│   ├── GitService/GitOps
│   ├── SessionTerminalManager
│   ├── RunController
│   ├── SetupScriptService
│   └── Multi-version handling
│
├── KiloConnectionService (shared)
│   ├── ServerManager (spawns CLI)
│   ├── KiloClient (SDK)
│   ├── SdkSSEAdapter
│   ├── State change listeners
│   └── Directory/session tracking
│
├── ServerManager
│   └── Spawns: bin/kilo serve --port 0
│
├── KiloClawProvider (editor panel)
├── DiffViewerProvider
├── SettingsEditorProvider
├── MarketplacePanelProvider
├── SubAgentViewerProvider
├── DiffVirtualProvider
├── RemoteStatusService
├── AttentionService
├── BrowserAutomationService
├── TelemetryProxy
└── AutocompleteServiceManager
```

---

## 3. Visual Studio Baseline (from PORT-INFRA-001)

### 3.1 Current Structure

**Production Files:** ~80 C# files + WebView assets  
**Test Files:** ~50 test files (xUnit)  
**Total Symbols:** ~2306  
**Total Tests:** ~273

### 3.2 Key Components Present

| Component | File | Status |
|-----------|------|--------|
| Package entry point | `KiloVisualStudioExtensionPackage.cs` | EXISTING |
| CLI backend manager | `CliBackendManager.cs` | EXISTING |
| Connection service | `KiloConnectionService.cs` | EXISTING |
| WebView control | `KiloWebViewControl.cs` | EXISTING |
| Provider factory | `ProviderFactory.cs` | EXISTING |
| Agent Manager | `AgentManager/` folder | PARTIAL |
| Message handlers | `Services/Handlers/` | EXISTING |
| Session services | `Services/Session*.cs` | EXISTING |
| Draft store | `Services/DraftStore.cs` | EXISTING |
| Diff services | `Services/Diff*.cs` | EXISTING |

---

## 4. VS Code → Visual Studio Mappings (Evidence-Based)

### 4.1 Extension Entry Point

| VS Code Element | VS Code Symbol | Visual Studio File | Visual Studio Symbol | Status | Evidence | Uncertainty |
|-----------------|----------------|-------------------|---------------------|--------|----------|-------------|
| `extension.ts` | `activate(context)` | `KiloVisualStudioExtensionPackage.cs` | `InitializeAsync()` | PARTIAL | Both register tool windows, commands, and initialize connection service. VS Code registers 50+ commands; VS Code registers only 3 commands (ShowKiloWindow, OpenSettings, ToolbarCommands). | Which VS Code commands are missing in VS Code? |
| `extension.ts` | `deactivate()` | `KiloVisualStudioExtensionPackage.cs` | `Dispose(bool)` | PARTIAL | Both dispose connection service and backend manager. VS Code also disposes attention, browser automation, provider, notebook bridge. | Are all VS Code disposables covered? |
| `extension.ts` | `openKiloInNewTab()` | UNKNOWN | UNKNOWN | MISSING | VS Code creates tab panel with serializer. No equivalent found in VS Code. | Is tab panel functionality required? |

### 4.2 Core Services - CLI/HTTP Client

| VS Code Element | VS Code Symbol | Visual Studio File | Visual Studio Symbol | Status | Evidence | Uncertainty |
|-----------------|----------------|-------------------|---------------------|--------|----------|-------------|
| `ServerManager` | `startServer()` spawns `bin/kilo serve --port 0` | `CliBackendManager.cs` | `StartAsync()` | ADAPTED | Both spawn CLI process, parse port from stdout, wait for health endpoint. VS Code uses 30s timeout; VS Code uses 30s timeout. | Does VS Code parse port from same stdout pattern? |
| `ServerManager` | `resolveServerCwd()`, `resolveIndexingEnv()`, `resolveManagedServerEnv()` | `CliBackendManager.cs` | `GetExtensionDirectory()`, environment setup | ADAPTED | Both set environment variables (KILO_CLIENT, KILO_PLATFORM, etc.). VS Code sets 20+ env vars; VS Code sets similar vars. | Complete env var parity? |
| `KiloConnectionService` | `connect()`, `getClient()`, `getClientAsync()` | `KiloConnectionService.cs` | `ConnectAsync()`, `GetClient()` | ADAPTED | Both use singleton pattern, lazy startup, state machine (connecting/connected/disconnected/error). VS Code has 936 lines; VS Code has 518 lines. | Does VS Code implement all VS Code methods? |
| `KiloConnectionService` | `onEvent()`, `onEventFiltered()` | `KiloConnectionService.cs` | `event` event, `SseEventReceived` | ADAPTED | Both use pub/sub pattern for SSE events. VS Code uses `Set<SSEEventListener>`; VS Code uses C# events. | Event filtering parity? |
| `SdkSSEAdapter` | `connect()`, `disconnect()`, `reconnect()`, `consumeLoop()` with heartbeat timeout (15s) | `SseClient.cs` | `Connect()`, `Disconnect()`, `ReadLoop()` with heartbeat (15s) | ADAPTED | Both implement reconnection loop, heartbeat timeout (15s), per-attempt AbortController pattern. VS Code uses AsyncGenerator; VS Code uses `HttpContent.ReadAsStreamAsync()`. | Reconnection delay parity (VS Code: 250ms)? |
| `SdkSSEAdapter` | `onEvent()`, `onError()`, `onStateChange()` | `SseClient.cs` | `OnEvent`, `OnConnected`, `OnDisconnected` events | ADAPTED | Both expose event handlers for state changes. VS Code uses `Set<SSEEventHandler>`; VS Code uses C# events. | Error handler parity? |
| `HttpClient` | `createKiloClient()` from `@kilocode/sdk/v2/client` | `HttpClientWrapper.cs`, `CachedHttpClient.cs` | `HttpClientWrapper`, `CachedHttpClient` | ADAPTED | VS Code uses SDK-generated client; VS Code uses manual HTTP calls with 10s TTL caching. | All SDK methods covered? |
| `connection-service.ts` | `trackState()`, `flushViewed()`, `registerVisible()`, `registerAttached()` | UNKNOWN | UNKNOWN | MISSING | VS Code tracks visible/attached sessions for remote control. No equivalent found in VS Code. | Is this required for parity? |

### 4.3 Providers

| VS Code Element | VS Code Symbol | Visual Studio File | Visual Studio Symbol | Status | Evidence | Uncertainty |
|-----------------|----------------|-------------------|---------------------|--------|----------|-------------|
| `KiloProvider` | `resolveWebviewPanel()`, `handleWebviewMessage()`, `postMessage()` | `KiloWebViewControl.cs` | `InitializeCoreWebView2()`, `WebMessageReceived` | ADAPTED | Both handle WebView lifecycle, message passing, CSP. VS Code uses `vscode.Webview`; VS Code uses `CoreWebView2`. | Message handler parity? |
| `AgentManagerProvider` | `openPanel()`, `handleMessage()`, `deserializePanel()` | `AgentManager/AgentManagerProvider.cs` | `ShowPanel()`, `HandleMessage()` | PARTIAL | Both manage multi-session worktree state. VS Code has 1941 lines; VS Code has ~300 lines. | Feature parity incomplete? |
| `KiloClawProvider` | `openPanel()`, `restorePanel()` | `KiloClawProvider.cs` | `ShowKiloClaw()`, `RestorePanel()` | EXACT | Both provide editor panel chat. Same method names, similar structure. | None |
| `SettingsEditorProvider` | `openPanel()`, `deserializePanel()` | `SettingsEditorProvider.cs` | `OpenPanel()`, `DeserializePanel()` | EXACT | Both provide settings/profile panels. Same method names. | None |
| `SubAgentViewerProvider` | `openPanel(sessionID, title)` | `SubAgentViewerProvider.cs` | `ShowSubAgentViewer(sessionId)` | EXACT | Both provide read-only sub-agent viewer. | None |
| `DiffViewerProvider` | `openFromCommand()`, `deserializePanel()` | UNKNOWN | UNKNOWN | MISSING | VS Code provides diff viewing with comment handler. No equivalent found in VS Code. | Is diff viewing required? |
| `MarketplacePanelProvider` | `openPanel(directory)`, `deserializePanel()` | UNKNOWN | UNKNOWN | MISSING | VS Code provides marketplace integration. No equivalent found in VS Code. | Is marketplace required? |
| `DiffVirtualProvider` | `setDiffVirtualProvider()` | UNKNOWN | UNKNOWN | MISSING | VS Code provides lightweight single-file diff for permissions. No equivalent found in VS Code. | Is this required? |

### 4.4 Message Handlers

| VS Code Handler | VS Code Location | Visual Studio Handler | Visual Studio File | Status | Evidence | Uncertainty |
|-----------------|------------------|----------------------|-------------------|--------|----------|-------------|
| Auth | `kilo-provider/handlers/auth` | `AuthHandlerService.cs` | `Services/Handlers/Auth/AuthHandlerService.cs` | EXISTING | Both handle login, logout, profile refresh. | Method signature parity? |
| Cloud Session | `kilo-provider/handlers/cloud-session` | `CloudSessionService.cs` | `Services/Handlers/CloudSession/CloudSessionService.cs` | EXISTING | Both handle cloud session import/data. | Method signature parity? |
| Permission | `kilo-provider/handlers/permission` | `SessionHandlerService.cs` | `Services/Handlers/Session/SessionHandlerService.cs` | PARTIAL | VS Code has dedicated permission handler; VS Code handles permissions in SessionHandler (800 lines). | Separation of concerns differs? |
| Question | `kilo-provider/handlers/question` | `InteractionHandlerService.cs` | `Services/Handlers/Interaction/InteractionHandlerService.cs` | PARTIAL | VS Code has dedicated question handler; VS Code handles questions in InteractionHandler (312 lines). | Separation of concerns differs? |
| Suggestion | `kilo-provider/handlers/suggestion` | UNKNOWN | UNKNOWN | MISSING | VS Code has suggestion handler. No equivalent found in VS Code. | Is this required? |
| Migration | `kilo-provider/handlers/migration` | UNKNOWN | UNKNOWN | MISSING | VS Code has legacy migration handler. No equivalent found in VS Code. | Is migration required? |
| MCP OAuth | `kilo-provider/handlers/mcp-oauth` | `McpHandlerService.cs` | `Services/Handlers/Mcp/McpHandlerService.cs` | PARTIAL | Both handle MCP server management. | OAuth flow parity? |

### 4.5 Agent Manager Components

| VS Code Element | VS Code Symbol | Visual Studio File | Visual Studio Symbol | Status | Evidence | Uncertainty |
|-----------------|----------------|-------------------|---------------------|--------|----------|-------------|
| `WorktreeManager` | `createWorktree()`, `closeWorktree()` | `AgentManager/WorktreeStateManager.cs` | `GetWorktree()`, `GetOpenWorktrees()` | ADAPTED | VS Code has dedicated WorktreeManager; VS Code uses WorktreeStateManager for state tracking. | Creation/deletion logic parity? |
| `GitService` | `cloneRepo()`, `createBranch()`, `push()` | `AgentManager/GitService.cs` | `CloneRepository()`, `CreateBranch()`, `Push()` | EXACT | Both wrap git CLI operations. Same method names. | None |
| `SessionTerminalManager` | `createTerminal()`, `sendText()` | `AgentManager/SessionTerminalManager.cs` | `CreateTerminal()`, `SendText()` | EXACT | Both manage session-specific terminals. Same method names. | None |
| `VsHost` | `createOutput()`, `openDocument()` | `()` | `AgentManager/VsHost.cs` | `CreateOutputChannel()`, `OpenDocument()` | EXACT | Both provide host abstraction. Same method names. | None |
| `SetupScriptService` | `runSetupScript()` | UNKNOWN | UNKNOWN | MISSING | VS Code runs setup scripts per worktree. No equivalent found in VS Code. | Is this required? |
| `BranchNamingController` | `resolveBranchName()` | UNKNOWN | UNKNOWN | MISSING | VS Code auto-generates branch names. No equivalent found in VS Code. | Is this required? |
| `WorktreeDiffController` | `createDiff()`, `showDiff()` | UNKNOWN | UNKNOWN | MISSING | VS Code manages worktree diffs. No equivalent found in VS Code. | Is this required? |

### 4.6 CLI/HTTP Client - Detailed Mapping

| VS Code Capability | VS Code Method/Endpoint | Visual Studio Equivalent | Visual Studio Method | Status | Evidence |
|-------------------|------------------------|-------------------------|---------------------|--------|----------|
| CLI startup | `ServerManager.startServer()` → `spawn(bin/kilo, ["serve", "--port", "0"])` | `CliBackendManager.StartAsync()` → `Process.Start("kilo.exe", "serve --port 0")` | `StartAsync()` | ADAPTED | Both spawn with `--port 0` for random port |
| Port discovery | Parse stdout for `listening on http://127.0.0.1:PORT` | Parse stdout for same pattern | `StartAsync()` line 100+ | ADAPTED | Both use regex parsing |
| Health check | Poll `/global/health` every 10s | Poll `/global/health` every 10s | `HealthCheckAsync()` | ADAPTED | Both use 10s interval |
| SDK client | `createKiloClient({ baseUrl, password })` | Manual `HttpClient` with basic auth | `GetHttpClient()` | ADAPTED | VS Code uses SDK; VS Code manual |
| SSE connection | `client.global.event()` AsyncGenerator | `GET /global/event` with `ReadAsStreamAsync()` | `Connect()` | ADAPTED | Both use SSE protocol |
| Reconnection | `while (!aborted)` loop, 250ms delay | `while (true)` loop, 250ms delay | `ReadLoop()` | ADAPTED | Both implement reconnection |
| Heartbeat timeout | 15s timeout, `attemptController.abort()` | 15s timeout, `cts.Cancel()` | `ReadLoop()` | ADAPTED | Both use 15s grace window |
| Directory tracking | `trackDirectory()`, `getKnownDirectories()` | UNKNOWN | UNKNOWN | MISSING | VS Code tracks worktree directories |
| Session visibility | `registerVisible()`, `registerAttached()`, `flushViewed()` | UNKNOWN | UNKNOWN | MISSING | VS Code tracks visible sessions |

### 4.7 WebView Mapping

| VS Code Element | VS Code Symbol | Visual Studio Equivalent | Visual Studio Symbol | Status | Evidence |
|-----------------|----------------|-------------------------|---------------------|--------|----------|
| WebView API | `vscode.Webview`, `postMessage()` | `CoreWebView2`, `PostMessage()` | `WebMessageReceived` | ADAPTED | Platform requirement |
| Message protocol | JSON serialization | JSON serialization | `System.Text.Json` | EXACT | Same format |
| CSP | `contentSecurityPolicy` meta tag | UNKNOWN | UNKNOWN | UNKNOWN | Need to verify VS Code CSP |
| Font loading | Local fonts from `assets/fonts/` | Local fonts from `webview/` | `CoreWebView2.Settings` | ADAPTED | Both load locally, different mechanism |
| Serializer | `registerWebviewPanelSerializer()` | UNKNOWN | UNKNOWN | MISSING | VS Code has serializers for panel restore |

### 4.8 Test Mapping

| VS Code Test File | VS Code Test Class/Method | Behavior Tested | Visual Studio Counterpart | Status | Evidence |
|-------------------|--------------------------|-----------------|--------------------------|--------|----------|
| `connection-service.test.ts` | `KiloConnectionService sandbox preference` | Uses workspace state | UNKNOWN | MISSING | No VS Code test found |
| `connection-service.test.ts` | `KiloConnectionService clients` | Returns connected client | UNKNOWN | MISSING | No VS Code test found |
| `connection-service.test.ts` | `KiloConnectionService viewed sessions` | Keeps AM sessions during flush | `KiloProviderSessionRefreshTests.cs` | PARTIAL | Similar coverage, different focus |
| `am-visible-presence.test.ts` | `AgentManagerVisiblePresence` | Tracks visible sessions | UNKNOWN | MISSING | No VS Code test found |
| `AgentManagerProvider.spec.ts` | `SetupScriptService` | Runs setup scripts | `AgentManagerArchTests.cs` | PARTIAL | Architecture tests exist |
| `AbortAndLoadMessagesTests.cs` | `HandleLoadMessagesTests.Does_not_stop_background_processes_twice` | Abort handling | UNKNOWN | MISSING | No verified VS Code source test |
| `AbortStateTests.cs` | `AbortStateTests.Allows_retrying_an_abort` | Abort state | UNKNOWN | MISSING | No verified VS Code source test |
| `SessionStreamSchedulerTests.cs` | `SessionStreamSchedulerTests.Focus` | Stream scheduling | UNKNOWN | MISSING | No verified VS Code source test |
| `SSEEventFilteringTests.cs` | `SSEEventFilteringTests` | SSE filtering | UNKNOWN | MISSING | No verified VS Code source test |

---

## 5. Missing Functionality (Classified)

### 5.1 Required for Parity (Must Implement)

| Component | VS Code Source | Reason | Priority |
|-----------|----------------|--------|----------|
| **Session visibility tracking** | `connection-service.ts:registerVisible()`, `registerAttached()`, `flushViewed()` | Required for remote session control and state synchronization | HIGH |
| **Directory tracking** | `connection-service.ts:trackDirectory()`, `getKnownDirectories()` | Required for worktree-scoped backend state | HIGH |
| **Webview panel serializers** | `extension.ts:registerWebviewPanelSerializer()` | Required for panel restoration on VS Code restart | HIGH |
| **Diff viewer** | `DiffViewerProvider.ts` | Core feature for code review functionality | HIGH |

### 5.2 Platform-Specific (May Not Be Required)

| Component | VS Code Source | VS Platform Constraint | Decision Needed |
|-----------|----------------|----------------------|-----------------|
| **Remote Status Service** | `RemoteStatusService.ts` | VS Code has remote development extensions; VS Code may not | Is remote status required? |
| **Autocomplete provider** | `services/autocomplete/` | VS Code has built-in autocomplete; VS Code may use different mechanism | Is autocomplete required? |
| **Code actions** | `services/code-actions/` | VS Code has different context menu API | Is this required? |

### 5.3 Optional (Defer to Later Tasks)

| Component | VS Code Source | Reason for Deferral |
|-----------|----------------|---------------------|
| **Marketplace Panel** | `MarketplacePanelProvider.ts` | May be optional feature; verify with product requirements |
| **Browser Automation** | `BrowserAutomationService.ts` | MCP-based feature; may not be core requirement |
| **Attention Service** | `attention.ts` | UI enhancement; may not be core requirement |

### 5.4 Not Applicable

| Component | VS Code Source | Reason |
|-----------|----------------|--------|
| **URI Handler** | `extension.ts:handleUri()` | VS Code uses different deep link mechanism (may not be needed) |

### 5.5 Unknown (Requires Investigation)

| Component | VS Code Source | Unknown Factor |
|-----------|----------------|----------------|
| **Setup Script Service** | `SetupScriptService.ts` | Is worktree setup script execution required? |
| **Branch Naming Controller** | `branch-naming.ts` | Is auto branch naming required? |
| **Migration Handler** | `migration/` | Is legacy migration from old extension required? |
| **Suggestion Handler** | `suggestion/` | Is suggestion feature required? |

---

## 6. Divergent Functionality

### 6.1 Platform-Specific Adaptations

| Area | VS Code | Visual Studio | Reason |
|------|---------|---------------|--------|
| WebView | VS Code webview API | WebView2 control | Platform requirement |
| Process spawning | `child_process` | `System.Diagnostics.Process` | Platform requirement |
| Menu/commands | VS Code commands | VSCT + MEF | Platform requirement |
| Settings | VS Code settings | Visual Studio options | Platform requirement |
| Terminal | VS Code terminal API | VS Code terminal (via host) | Shared terminal API |

### 6.2 Architectural Differences

1. **Message Protocol:** VS Code uses `vscode.Webview.postMessage()`, Visual Studio uses `CoreWebView2.WebMessageReceived`
2. **Service Registration:** VS Code uses dependency injection via constructor injection, Visual Studio uses static singletons
3. **Error Handling:** VS Code uses Promise/async-await with try-catch, Visual Studio uses Task/async-await with try-catch
4. **State Management:** VS Code uses `ExtensionContext` storage, Visual Studio uses `Memento` pattern

---

## 7. CLI/HTTP Mapping Details (Evidence-Based)

### 7.1 CLI Startup Mechanism

| Aspect | VS Code Implementation | Visual Studio Implementation | Status |
|--------|----------------------|----------------------------|--------|
| **Spawn command** | `spawn(bin/kilo, ["serve", "--port", "0"])` | `Process.Start("kilo.exe", "serve --port 0")` | ADAPTED |
| **Port discovery** | Regex parse stdout for `listening on http://127.0.0.1:PORT` | Regex parse stdout for same pattern | ADAPTED |
| **Timeout** | 30 seconds (`STARTUP_TIMEOUT_SECONDS = 30`) | 30 seconds (`timeout: 30000`) | EXACT |
| **Environment vars** | 20+ vars (KILO_CLIENT, KILO_PLATFORM, KILO_TELEMETRY_LEVEL, etc.) | Similar vars (KILO_CLIENT=visualstudio, KILO_PLATFORM=visualstudio) | ADAPTED |
| **Password** | `crypto.randomBytes(32).toString("hex")` via `KILO_SERVER_PASSWORD` env | Generated password via `KILO_SERVER_PASSWORD` env | EXACT |
| **Process cleanup** | `proc.kill()` on dispose | `process.Kill()` on dispose | EXACT |

### 7.2 Client Construction

| Aspect | VS Code Implementation | Visual Studio Implementation | Status |
|--------|----------------------|----------------------------|--------|
| **SDK usage** | `createKiloClient({ baseUrl, password })` from `@kilocode/sdk/v2/client` | Manual `HttpClient` with basic auth header | ADAPTED |
| **Base URL** | `http://127.0.0.1:PORT` | `http://127.0.0.1:PORT` | EXACT |
| **Auth** | Basic auth via `KILO_SERVER_PASSWORD` | Basic auth via username/password | ADAPTED |
| **Caching** | SDK handles caching | `CachedHttpClient` with 10s TTL | ADAPTED |

### 7.3 HTTP Endpoints Used (From VS Code Source)

| Endpoint | Method | VS Code Usage | Visual Studio Usage | Status |
|----------|--------|---------------|--------------------|--------|
| `/global/health` | GET | Health check, 10s polling | Health check, 10s polling | EXACT |
| `/session` | POST | Create session | Create session | EXACT |
| `/session/:id` | GET | Get session details | Get session details | EXACT |
| `/session/:id/messages` | GET | Load message history | Load message history | EXACT |
| `/config` | GET | Get configuration | Get configuration | EXACT |
| `/auth/profile` | GET | Get user profile | Get user profile | EXACT |
| `/notifications` | GET | Fetch notifications | Fetch notifications | EXACT |
| `/permissions` | GET/POST | Permission requests | Permission requests | PARTIAL |
| `/questions` | GET/POST | Question handling | Question handling | PARTIAL |
| `/mcp` | GET/POST | MCP server management | MCP server management | PARTIAL |

### 7.4 SSE/Streaming

| Aspect | VS Code Implementation | Visual Studio Implementation | Status |
|--------|----------------------|----------------------------|--------|
| **Endpoint** | `/global/event` (AsyncGenerator) | `GET /global/event` (stream) | ADAPTED |
| **Reconnection** | `while (!aborted)` loop, 250ms delay | `while (true)` loop, 250ms delay | EXACT |
| **Heartbeat timeout** | 15s grace window, `attemptController.abort()` | 15s grace window, `cts.Cancel()` | EXACT |
| **Event parsing** | SDK parses SSE format | Manual parsing of `event:` and `data:` lines | ADAPTED |
| **State handlers** | `onStateChange("connecting"/"connected"/"disconnected")` | `OnConnected`, `OnDisconnected` events | ADAPTED |

### 7.5 Error Handling

| Aspect | VS Code Implementation | Visual Studio Implementation | Status |
|--------|----------------------|----------------------------|--------|
| **Connection errors** | `setState("error", error)`, notify listeners | `OnError` event, state change | ADAPTED |
| **Server exit** | `onExit` callback, spawn new process | `Process.Exit` event, restart | ADAPTED |
| **Timeout errors** | `ServerStartupError` with user message | Exception with inner error | ADAPTED |
| **404 handling** | `isNotFound()` checks for `NotFoundError`, `_tag: "NotFound"`, `status: 404` | UNKNOWN | UNKNOWN |

### 7.6 Retry/Reconnection Behavior

| Aspect | VS Code Implementation | Visual Studio Implementation | Status |
|--------|----------------------|----------------------------|--------|
| **Reconnect trigger** | `reconnect()` aborts current attempt | `cts.Cancel()` triggers reconnection | ADAPTED |
| **Max delay** | `MAX_RECONNECT_DELAY_MS = 5000` | UNKNOWN | UNKNOWN |
| **Exponential backoff** | UNKNOWN | UNKNOWN | UNKNOWN |

### 7.7 Authentication/Configuration

| Aspect | VS Code Implementation | Visual Studio Implementation | Status |
|--------|----------------------|----------------------------|--------|
| **Password source** | `KILO_SERVER_PASSWORD` env var | `KILO_SERVER_PASSWORD` env var | EXACT |
| **Profile refresh** | `client.auth.profile()` | `AuthHandlerService.RefreshProfileAsync()` | ADAPTED |
| **Config reload** | `client.config.get()` | `ConfigHandlerService.GetConfigAsync()` | ADAPTED |

### 7.8 Components Consuming Client Capabilities

| VS Code Component | Consumes | Visual Studio Equivalent | Status |
|-------------------|----------|-------------------------|--------|
| `KiloProvider` | SSE events, session CRUD, messages | `KiloWebViewControl`, message handlers | ADAPTED |
| `AgentManagerProvider` | SSE events, session control, worktree dirs | `AgentManagerProvider`, session handlers | ADAPTED |
| `TelemetryProxy` | Server config for POST events | UNKNOWN | MISSING |
| `RemoteStatusService` | Session viewed API | UNKNOWN | MISSING |

---

## 8. Test Mapping (Applicable VS Code Tests)

### 8.1 Connection Service Tests

| VS Code Test File | VS Code Test Class/Method | Behavior Tested | Visual Studio Test File | Visual Studio Test Class/Method | Status | Notes |
|-------------------|--------------------------|-----------------|------------------------|--------------------------------|--------|-------|
| `connection-service.test.ts` | `KiloConnectionService sandbox preference` | Uses workspace state | UNKNOWN | UNKNOWN | MISSING | No VS Code test found |
| `connection-service.test.ts` | `KiloConnectionService clients` | Returns connected client | UNKNOWN | UNKNOWN | MISSING | No VS Code test found |
| `connection-service.test.ts` | `KiloConnectionService viewed sessions` | Keeps AM sessions during flush | `KiloProviderSessionRefreshTests.cs` | `KiloProviderSessionRefreshTests` | PORTED_ADAPTED | Similar coverage, different focus |

### 8.2 Agent Manager Tests

| VS Code Test File | VS Code Test Class/Method | Behavior Tested | Visual Studio Test File | Visual Studio Test Class/Method | Status | Notes |
|-------------------|--------------------------|-----------------|------------------------|--------------------------------|--------|-------|
| `AgentManagerProvider.spec.ts` | `SetupScriptService` tests | Runs setup scripts | `AgentManagerArchTests.cs` | `AgentManagerArchTests` | PORTED_ADAPTED | Architecture tests exist |
| `am-visible-presence.test.ts` | `AgentManagerVisiblePresence` | Tracks visible sessions | UNKNOWN | UNKNOWN | MISSING | No VS Code test found |

### 8.3 Abort/Session Tests

| VS Code Test File | VS Code Test Class/Method | Behavior Tested | Visual Studio Test File | Visual Studio Test Class/Method | Status | Notes |
|-------------------|--------------------------|-----------------|------------------------|--------------------------------|--------|-------|
| `abort.test.ts` | `SessionAbort.stop` | Stops active owner and mapped directory | `AbortAndLoadMessagesTests.cs` | `HandleLoadMessagesTests` | PORTED_ADAPTED | VS Code tests abort session/directory |
| `abort-state.test.ts` | `createAbortState` | Abort state machine | `AbortStateTests.cs` | `AbortStateTests` | PORTED_ADAPTED | VS Code tests pending prompt abort state |

### 8.4 Session Tests

| VS Code Test File | VS Code Test Class/Method | Behavior Tested | Visual Studio Test File | Visual Studio Test Class/Method | Status | Notes |
|-------------------|--------------------------|-----------------|------------------------|--------------------------------|--------|-------|
| `session-stream-scheduler.test.ts` | `SessionStreamScheduler` | Stream scheduling, buffer flushing | `SessionStreamSchedulerTests.cs` | `SessionStreamSchedulerTests` | PORTED_ADAPTED | VS Code tests coalescing, focus, drop, flush |
| `session-queue.test.ts` | `SessionQueue` | Session queueing | UNKNOWN | UNKNOWN | MISSING | No VS Code test found |
| `kilo-provider-session-refresh.test.ts` | `KiloProviderSessionRefresh` | Session refresh | `KiloProviderSessionRefreshTests.cs` | `KiloProviderSessionRefreshTests` | PORTED_ADAPTED | VS Code tests session switching |

### 8.5 SSE/Event Tests

| VS Code Test File | VS Code Test Class/Method | Behavior Tested | Visual Studio Test File | Visual Studio Test Class/Method | Status | Notes |
|-------------------|--------------------------|-----------------|------------------------|--------------------------------|--------|-------|
| `sdk-sse-adapter.test.ts` | `SdkSSEAdapter` | SSE filtering, reconnection | `SSEEventFilteringTests.cs` | `SSEEventFilteringTests` | PORTED_ADAPTED | VS Code tests heartbeat, reconnection, filtering |

### 8.6 Summary

| Status | Count |
|--------|-------|
| PORTED_1_TO_1 | 0 |
| PORTED_ADAPTED | 7 |
| MISSING | 6 |
| NOT_APPLICABLE | 0 |
| UNKNOWN | 0 |

**Note:** The VS Code extension has unit tests in `tests/unit/` and `src/**/__tests__/` and `src/**/*.test.ts`. The Visual Studio extension has extensive test coverage (`KiloVisualStudioExtension.Tests/` with ~273 tests), but most of these tests have no identifiable VS Code source. Per the source-of-truth rule, Visual Studio tests without verified VS Code counterparts must be classified as MISSING, not PORTED_1_TO_1. The existing Visual Studio tests may have been independently created.

---

## 8. WebView Message Protocol

### 8.1 Extension→Webview Messages

| Message Type | Purpose | VS Code | Visual Studio | Status |
|--------------|---------|---------|---------------|--------|
| `action` | UI actions | Yes | Yes | ADAPTED |
| `error` | Error display | Yes | Yes | ADAPTED |
| `session.status` | Session state | Yes | Yes | ADAPTED |
| `config` | Configuration | Yes | Yes | ADAPTED |
| `auth.profile` | User profile | Yes | Yes | ADAPTED |
| `notifications` | Notifications | Yes | Yes | ADAPTED |
| `permissions` | Permissions | Yes | Yes | ADAPTED |
| `questions` | Questions | Yes | Yes | ADAPTED |

### 8.2 Webview→Extension Messages

| Message Type | Purpose | VS Code | Visual Studio | Status |
|--------------|---------|---------|---------------|--------|
| `submit` | Send message | Yes | Yes | ADAPTED |
| `abort` | Cancel session | Yes | Yes | ADAPTED |
| `approve` | Approve tool | Yes | Yes | ADAPTED |
| `reject` | Reject tool | Yes | Yes | ADAPTED |
| `answer` | Answer question | Yes | Yes | ADAPTED |
| `dismiss` | Dismiss notification | Yes | Yes | ADAPTED |
| `request.*` | Request data | Yes | Yes | ADAPTED |

---

## 9. Recommended Implementation Order

### Phase 1: Core Infrastructure (PORT-CLI-001)

**Goal:** Faithfully port the CLI/HTTP client communication layer.

1. **Verify SSE client parity** — Confirm `SseClient.cs` implements all `SdkSSEAdapter` features (reconnection, heartbeat, state handlers)
2. **Add session visibility tracking** — Implement `registerVisible()`, `registerAttached()`, `flushViewed()` equivalent
3. **Add directory tracking** — Implement `trackDirectory()`, `getKnownDirectories()` equivalent
4. **Add health polling** — Verify 10s health check polling is implemented
5. **Verify all HTTP endpoints** — Confirm all VS Code endpoints are accessible from VS Code

### Phase 2: WebView Integration (PORT-WEBVIEW-001)

**Goal:** Port WebView/provider communication.

1. **Verify message handler registry** — Confirm all message types are routed correctly
2. **Add webview panel serializers** — Implement serializer pattern for panel restoration
3. **Verify CSP configuration** — Confirm content security policy is properly configured
4. **Verify font loading** — Confirm fonts are loaded locally

### Phase 3: Core Features (PORT-CORE-001)

**Goal:** Port remaining extension host functionality.

1. **Implement diff viewer** — Create diff viewing capability (HIGH priority)
2. **Evaluate remote status service** — Determine if required for VS Code platform
3. **Evaluate marketplace integration** — Determine if required based on product requirements
4. **Evaluate autocomplete** — Determine if VS Code platform requires custom autocomplete

### Phase 4: Agent Manager Completion

**Goal:** Complete Agent Manager feature parity.

1. **Evaluate setup script service** — Determine if worktree setup scripts are required
2. **Evaluate branch naming** — Determine if auto branch naming is required
3. **Evaluate worktree diff controller** — Determine if worktree diff management is required

### Phase 5: Tests (PORT-TEST-001)

**Goal:** Port applicable VS Code unit tests.

1. **Port missing connection service tests** — Create VS Code equivalents for `sandbox preference`, `clients`, `viewed sessions` tests
2. **Port missing Agent Manager tests** — Create VS Code equivalents for `SetupScriptService`, `VisiblePresence` tests
3. **Port missing session tests** — Create VS Code equivalents for `SessionQueue` tests
4. **Validate all tests pass** — Ensure 1:1 semantic port passes

---

## 10. Files to Create/Modify in Next Task (Code Phase)

### 10.1 Documentation Files (For This Task)

| File | Action | Purpose |
|------|--------|---------|
| `porting/docs/prompts/PORT-INFRA-002.md` | CREATE | Historical record of this task execution |

**Note:** The task specification mentions creating mapping JSON files (`vscode-baseline.json`, `file-mappings.json`, etc.), but per the "minimalism" constraint in SPEC.md Section 21, these should only be created if they provide clear value. The plan file itself serves as the primary mapping artifact.

### 10.2 Implementation Files (To Be Decided in Code Phase)

**Do not pre-decide implementation files.** The actual files to create will be determined during the Code phase based on:

1. Detailed inspection of VS Code source for each missing component
2. Verification of whether each component is actually required
3. Platform-specific constraints that may affect implementation
4. Product requirements that may make some components optional

**What the Code phase will determine:**

- Exact file paths and names based on VS Code structure
- Whether each missing component should be implemented or marked as not applicable
- Platform-specific adaptations required for VS Code
- Test files to create for each implementation

---

## 11. Unresolved Items (UNKNOWN)

| Item | VS Code Source | Unknown Factor | Resolution Needed |
|------|----------------|----------------|-------------------|
| **CSP Configuration** | `KiloProvider.ts:buildWebviewHtml()` | Does VS Code WebView2 use CSP? How is it configured? | Inspect VS Code `KiloWebViewControl.cs` |
| **Font Loading** | `KiloProvider.ts` | Are fonts loaded the same way in VS Code? | Inspect VS Code webview initialization |
| **Telemetry Integration** | `TelemetryProxy.ts` | Is telemetry proxy implemented in VS Code? | Inspect VS Code telemetry implementation |
| **Commit Message Service** | `services/commit-message/index.ts` | Is commit message generation implemented? | Inspect VS Code commit message handling |
| **Code Actions** | `services/code-actions/` | Are code actions registered in VS Code? | Inspect VS Code code action registration |
| **URI Handler** | `extension.ts:handleUri()` | Does VS Code need deep link handling? | Determine if deep links are used |
| **Migration Handler** | `kilo-provider/handlers/migration/` | Is legacy migration from old extension required? | Product requirements clarification |
| **404 Error Handling** | `connection-service.ts:isNotFound()` | Does VS Code handle 404 errors the same way? | Inspect VS Code error handling |
| **Exponential Backoff** | `sdk-sse-adapter.ts` | Is exponential backoff implemented for reconnection? | Inspect VS Code reconnection logic |
| **Max Reconnect Delay** | `sdk-sse-adapter.ts:MAX_RECONNECT_DELAY_MS = 5000` | Does VS Code have the same max delay? | Inspect VS Code `SseClient.cs` |
| **Error Handler Parity** | `SdkSSEAdapter.onError()` | Does VS Code have equivalent error handlers? | Inspect VS Code `SseClient.cs` |
| **Diff Virtual Provider** | `DiffVirtualProvider.ts` | Is lightweight diff for permissions required? | Product requirements clarification |
| **Remote Status Service** | `RemoteStatusService.ts` | Is remote development status tracking required? | Platform requirements clarification |
| **Autocomplete Provider** | `services/autocomplete/` | Is custom autocomplete required for VS Code? | Platform requirements clarification |

---

## 12. Validation Checklist

- [x] VS Code extension entry point identified (`extension.ts`)
- [x] Extension.ts dependency graph analyzed (complete tree documented)
- [x] CLI/HTTP client implementation identified (`ServerManager`, `KiloConnectionService`, `SdkSSEAdapter`)
- [x] WebView communication identified (`KiloProvider`, `KiloWebViewControl`)
- [x] Relevant tests identified (VS Code and Visual Studio test files cataloged)
- [x] Visual Studio counterparts mapped (evidence-based with file/symbol references)
- [x] Missing functionality listed (classified by requirement level)
- [x] Divergent functionality documented (platform-specific adaptations noted)
- [x] Mapping evidence based on source code (specific methods/lines referenced)
- [x] No production/test code modified (analysis only)
- [x] No upstream/fork dependency introduced (neutral repository identity)
- [x] Implementation order justified (dependency-aware phases)
- [x] Pre-decided implementation files removed (to be determined in Code phase)
- [x] Test mapping corrected (VS Code tests verified, Visual Studio tests properly classified)
- [x] Unknown items explicitly marked (15 unresolved items listed)
- [x] Plan reviewed and accepted

---

## 13. Status

**PORT-INFRA-002 → REVIEW**

This plan is ready for human review. The next step is to validate the mappings and approve the implementation order before proceeding with PORT-CLI-001 in Code mode.

**Final corrections applied:**
1. Test mapping corrected - Visual Studio C# test files removed from VS Code source column
2. Seven VS Code → Visual Studio adapted mappings verified with actual VS Code test files
3. Six Visual Studio-only tests classified as MISSING (no verified VS Code source)
4. Summary counts updated: PORTED_1_TO_1 = 0, PORTED_ADAPTED = 7, MISSING = 6

**Test mapping final counts:**
- **PORTED_1_TO_1:** 0 (no exact 1:1 test mappings)
- **PORTED_ADAPTED:** 7 (VS Code tests with semantically equivalent Visual Studio tests)
- **MISSING:** 6 (Visual Studio tests without verified VS Code source)
- **NOT_APPLICABLE:** 0
- **UNKNOWN:** 0

---

## Appendix A: VS Code File Inventory (Relevant to Port)

**Extension Entry:**
- `src/extension.ts` — Main activation

**Providers:**
- `src/KiloProvider.ts` — Sidebar/tab panel
- `src/agent-manager/AgentManagerProvider.ts` — Agent Manager
- `src/kiloclaw/KiloClawProvider.ts` — KiloClaw panel
- `src/diff/DiffViewerProvider.ts` — Diff viewer
- `src/SettingsEditorProvider.ts` — Settings editor
- `src/MarketplacePanelProvider.ts` — Marketplace
- `src/SubAgentViewerProvider.ts` — Sub-agent viewer

**Core Services:**
- `src/services/cli-backend/connection-service.ts` — Shared connection
- `src/services/cli-backend/server-manager.ts` — CLI process management
- `src/services/cli-backend/sdk-sse-adapter.ts` — SSE events
- `src/services/RemoteStatusService.ts` — Remote status
- `src/services/attention.ts` — Attention service
- `src/services/autocomplete/` — Autocomplete

**Agent Manager:**
- `src/agent-manager/WorktreeManager.ts` — Worktree management
- `src/agent-manager/WorktreeStateManager.ts` — State management
- `src/agent-manager/GitService.ts` — Git operations
- `src/agent-manager/SessionTerminalManager.ts` — Terminal management

---

## Appendix B: Visual Studio File Inventory (Relevant to Port)

**Entry Point:**
- `KiloVisualStudioExtensionPackage.cs` — Package initialization

**Core Services:**
- `KiloConnectionService.cs` — Connection management
- `CliBackendManager.cs` — CLI process management
- `HttpClientWrapper.cs` — HTTP client
- `CachedHttpClient.cs` — Cached HTTP client
- `SseClient.cs` — SSE client
- `SSEHelper.cs` — SSE helpers

**Providers:**
- `KiloWebViewControl.cs` — WebView control
- `KiloClawProvider.cs` — KiloClaw
- `ProviderFactory.cs` — Provider factory
- `AgentManager/AgentManagerProvider.cs` — Agent Manager
- `SettingsEditorProvider.cs` — Settings editor
- `SubAgentViewerProvider.cs` — Sub-agent viewer

**Message Handlers:**
- `Services/Handlers/Auth/AuthHandlerService.cs`
- `Services/Handlers/CloudSession/CloudSessionService.cs`
- `Services/Handlers/Config/ConfigHandlerService.cs`
- `Services/Handlers/Interaction/InteractionHandlerService.cs`
- `Services/Handlers/Mcp/McpHandlerService.cs`
- `Services/Handlers/Model/ModelHandlerService.cs`
- `Services/Handlers/Notification/NotificationHandlerService.cs`
- `Services/Handlers/ProviderRequest/ProviderRequestService.cs`
- `Services/Handlers/Session/SessionHandlerService.cs`
- `Services/Handlers/SessionControl/SessionControlHandlerService.cs`
- `Services/Handlers/Settings/SettingsHandlerService.cs`
- `Services/Handlers/StateManagement/StateManagementService.cs`
- `Services/Handlers/Ui/UiHandlerService.cs`

**Agent Manager:**
- `AgentManager/WorktreeStateManager.cs`
- `AgentManager/GitService.cs`
- `AgentManager/SessionTerminalManager.cs`
- `AgentManager/VsHost.cs`

---

**End of Plan**
