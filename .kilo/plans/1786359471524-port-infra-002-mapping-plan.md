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

**File:** `packages/kilo-vscode/src/extension.ts`

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

## 4. VS Code → Visual Studio Mappings

### 4.1 Extension Entry Point

| VS Code Element | Visual Studio Counterpart | Status | Notes |
|-----------------|--------------------------|--------|-------|
| `extension.ts` | `KiloVisualStudioExtensionPackage.cs` | PARTIAL | VS Package entry point exists but lacks command registration parity |
| `activate()` | `InitializeAsync()` | PARTIAL | Missing provider registrations, command handlers |
| `deactivate()` | No direct equivalent | MISSING | Cleanup logic not implemented |

### 4.2 Core Services

| VS Code Element | Visual Studio Counterpart | Status | Notes |
|-----------------|--------------------------|--------|-------|
| `KiloConnectionService` (TS) | `KiloConnectionService.cs` | ADAPTED | Similar singleton pattern, C# implementation |
| `ServerManager` | `CliBackendManager.cs` | ADAPTED | Similar process management, different API |
| `SdkSSEAdapter` | UNKNOWN | MISSING | SSE client implementation not found |
| `HttpClient` wrapper | `HttpClientWrapper.cs`, `CachedHttpClient.cs` | EXISTING | Equivalent HTTP client layer |

### 4.3 Providers

| VS Code Element | Visual Studio Counterpart | Status | Notes |
|-----------------|--------------------------|--------|-------|
| `KiloProvider` (sidebar) | `KiloWebViewControl.cs` | ADAPTED | WebView2-based, different message protocol |
| `AgentManagerProvider` | `AgentManager/AgentManagerProvider.cs` | PARTIAL | Exists but may lack feature parity |
| `KiloClawProvider` | `KiloClawProvider.cs` | EXISTING | Direct equivalent |
| `DiffViewerProvider` | UNKNOWN | MISSING | Not found in Visual Studio |
| `SettingsEditorProvider` | Unknown (possibly `OpenSettingsCommand.cs`) | UNKNOWN | Needs verification |
| `MarketplacePanelProvider` | UNKNOWN | MISSING | Not found |
| `SubAgentViewerProvider` | UNKNOWN | MISSING | Not found |

### 4.4 Message Handlers (VS Code → Visual Studio)

| VS Code Handler Location | Visual Studio Handler | Status |
|--------------------------|----------------------|--------|
| `kilo-provider/handlers/auth` | `Services/Handlers/Auth/AuthHandlerService.cs` | EXISTING |
| `kilo-provider/handlers/cloud-session` | `Services/Handlers/CloudSession/CloudSessionService.cs` | EXISTING |
| `kilo-provider/handlers/permission` | `Services/Handlers/Session/SessionHandlerService.cs` | PARTIAL |
| `kilo-provider/handlers/question` | `Services/Handlers/Interaction/InteractionHandlerService.cs` | PARTIAL |
| `kilo-provider/handlers/suggestion` | UNKNOWN | MISSING |
| `kilo-provider/handlers/migration` | UNKNOWN | MISSING |
| `kilo-provider/handlers/mcp-oauth` | `Services/Handlers/Mcp/McpHandlerService.cs` | PARTIAL |

### 4.5 Agent Manager Components

| VS Code Element | Visual Studio Counterpart | Status | Notes |
|-----------------|--------------------------|--------|-------|
| `WorktreeManager` | `AgentManager/WorktreeStateManager.cs` | ADAPTED | Similar responsibility |
| `GitService` | `AgentManager/GitService.cs` | EXISTING | Direct equivalent |
| `SessionTerminalManager` | `AgentManager/SessionTerminalManager.cs` | EXISTING | Direct equivalent |
| `VsHost` | `AgentManager/VsHost.cs` | EXISTING | Direct equivalent |
| `SetupScriptService` | UNKNOWN | MISSING | Not found |
| `BranchNamingController` | UNKNOWN | MISSING | Not found |
| `WorktreeDiffController` | UNKNOWN | MISSING | Not found |

### 4.6 CLI/HTTP Client Mapping

| VS Code Element | Visual Studio Counterpart | Status | Notes |
|-----------------|--------------------------|--------|-------|
| `@kilocode/sdk` client | C# HTTP client calls | ADAPTED | Visual Studio uses raw HTTP instead of SDK |
| SSE event handling | UNKNOWN | MISSING | No SSE adapter found |
| `connection-service.ts` | `KiloConnectionService.cs` | ADAPTED | Similar pattern, different implementation |
| `server-manager.ts` | `CliBackendManager.cs` | ADAPTED | Similar process management |

**HTTP Endpoints Used (from VS Code):**
- `/global/health` — Health check
- `/session` — Session CRUD
- `/session/:id/messages` — Message retrieval
- `/config` — Configuration
- `/auth` — Authentication
- `/notifications` — Notifications
- `/permissions` — Permissions
- `/questions` — Questions
- `/suggestions` — Suggestions
- `/mcp` — MCP server management

### 4.7 WebView Mapping

| VS Code Element | Visual Studio Counterpart | Status | Notes |
|-----------------|--------------------------|--------|-------|
| `KiloProvider` webview | `KiloWebViewControl.cs` | ADAPTED | WebView2 instead of VS Code webview |
| `postMessage()` | `CoreWebView2.PostMessage()` | ADAPTED | Similar API, different platform |
| Message serialization | JSON (both) | EXACT | Same format |
| CSP handling | UNKNOWN | UNKNOWN | Needs verification |
| Font loading | Local fonts (both) | EXACT | Same approach |

### 4.8 Test Mapping

| VS Code Test | Visual Studio Test | Status | Notes |
|--------------|-------------------|--------|-------|
| `connection-service.test.ts` | `KiloProviderSessionRefreshTests.cs` | PARTIAL | Similar coverage, different focus |
| `AgentManagerProvider.spec.ts` | `AgentManagerArchTests.cs` | PARTIAL | Architecture tests exist |
| Abort tests | `AbortTests.cs`, `AbortStateTests.cs` | EXISTING | Direct equivalents |
| Session tests | `Session*.cs` (multiple) | EXISTING | Comprehensive coverage |
| SSE tests | `SSEEventFilteringTests.cs` | EXISTING | SSE filtering tested |
| Stream scheduler tests | `SessionStreamSchedulerTests.cs` | EXISTING | Direct equivalent |

---

## 5. Missing Functionality

### 5.1 Critical Missing Components

1. **SSE Event Adapter** — No equivalent to `SdkSSEAdapter.ts`
2. **Diff Viewer Provider** — No diff viewing capability found
3. **Marketplace Panel** — No marketplace integration found
4. **Sub-Agent Viewer** — No sub-agent session viewer found
5. **Settings Editor Provider** — Unclear if equivalent exists
6. **Session Stream Scheduler** — Exists in tests but unclear if in production
7. **Remote Status Service** — No equivalent found
8. **Browser Automation Service** — No equivalent found
9. **Attention Service** — No equivalent found
10. **Autocomplete Integration** — No autocomplete provider found

### 5.2 Partial Implementations

1. **Agent Manager** — Core structure exists but may lack features
2. **Message Handlers** — Many exist but may have different signatures
3. **Git Integration** — Basic Git service exists but may lack advanced features
4. **MCP Handling** — Handler exists but may lack full MCP protocol support

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

## 7. CLI/HTTP Mapping Details

### 7.1 Request/Response Models

Both implementations use the same JSON schemas from the CLI backend:

| Endpoint | Method | Request | Response |
|----------|--------|---------|----------|
| `/global/health` | GET | None | `{ status: "ok" }` |
| `/session` | POST | `{ directory, model, agent }` | `{ sessionID }` |
| `/session/:id` | GET | None | `{ session: Session }` |
| `/session/:id/messages` | GET | `{ page, limit }` | `{ messages: Message[] }` |
| `/config` | GET | None | `{ config: Config }` |
| `/auth/profile` | GET | None | `{ profile: Profile }` |

### 7.2 SSE Event Types

| Event Type | Extension→Webview | Webview→Extension |
|------------|-------------------|-------------------|
| `session.created` | Yes | No |
| `session.status` | Yes | No |
| `message.created` | Yes | No |
| `message.updated` | Yes | No |
| `message.completed` | Yes | No |
| `tool.request` | Yes | Yes |
| `tool.response` | No | Yes |
| `permission.request` | Yes | Yes |
| `question.request` | Yes | Yes |

---

## 8. WebView Message Protocol

### 8.1 Extension→Webview Messages

| Message Type | Purpose | VS Code | Visual Studio |
|--------------|---------|---------|---------------|
| `action` | UI actions | Yes | Yes |
| `error` | Error display | Yes | Yes |
| `session.status` | Session state | Yes | Yes |
| `config` | Configuration | Yes | Yes |
| `auth.profile` | User profile | Yes | Yes |
| `notifications` | Notifications | Yes | Yes |
| `permissions` | Permissions | Yes | Yes |
| `questions` | Questions | Yes | Yes |

### 8.2 Webview→Extension Messages

| Message Type | Purpose | VS Code | Visual Studio |
|--------------|---------|---------|---------------|
| `submit` | Send message | Yes | Yes |
| `abort` | Cancel session | Yes | Yes |
| `approve` | Approve tool | Yes | Yes |
| `reject` | Reject tool | Yes | Yes |
| `answer` | Answer question | Yes | Yes |
| `dismiss` | Dismiss notification | Yes | Yes |
| `request.*` | Request data | Yes | Yes |

---

## 9. Recommended Implementation Order

### Phase 1: Core Infrastructure (PORT-CLI-001)

1. **SSE Event Adapter** — Implement C# equivalent of `SdkSSEAdapter.ts`
2. **Connection Service Enhancement** — Add missing event filtering, directory tracking
3. **HTTP Client Verification** — Ensure all endpoints are accessible
4. **Health Polling** — Implement health check polling (VS Code polls every 10s)

### Phase 2: WebView Integration (PORT-WEBVIEW-001)

1. **Message Handler Registry** — Verify complete message routing
2. **Provider Pattern** — Ensure all providers register correctly
3. **Serialization** — Implement webview panel serialization for restoration
4. **CSP Configuration** — Verify content security policy

### Phase 3: Core Features (PORT-CORE-001)

1. **Diff Viewer** — Implement diff viewing capability
2. **Settings Editor** — Verify/implement settings panel
3. **Marketplace** — Implement marketplace integration (if required)
4. **Sub-Agent Viewer** — Implement sub-agent session viewer (if required)

### Phase 4: Agent Manager Completion

1. **Setup Script Service** — Implement setup script execution
2. **Branch Naming** — Implement automatic branch naming
3. **Worktree Diff** — Implement worktree diff controller
4. **Multi-version** — Verify multi-version support

### Phase 5: Tests (PORT-TEST-001)

1. **Port VS Code Tests** — Create 1:1 semantic ports of critical tests
2. **Integration Tests** — Add Visual Studio-specific integration tests
3. **Validation** — Ensure all tests pass

---

## 10. Files to Create/Modify in Next Task

### 10.1 Documentation Files

| File | Action | Purpose |
|------|--------|---------|
| `porting/docs/prompts/PORT-INFRA-002.md` | CREATE | Historical record of this task |
| `porting/mapping/vscode-baseline.json` | CREATE | VS Code file/symbol baseline |
| `porting/mapping/file-mappings.json` | CREATE | File-to-file mappings |
| `porting/mapping/symbol-mappings.json` | CREATE | Symbol-to-symbol mappings |
| `porting/mapping/test-mappings.json` | CREATE | Test-to-test mappings |
| `porting/mapping/missing-components.json` | CREATE | List of missing functionality |
| `porting/mapping/divergences.json` | CREATE | Platform-specific differences |

### 10.2 Implementation Files (Future Tasks)

| File | Action | Phase |
|------|--------|-------|
| `Services/SdkSseAdapter.cs` | CREATE | Phase 1 |
| `Services/RemoteStatusService.cs` | CREATE | Phase 1 |
| `Diff/DiffViewerProvider.cs` | CREATE | Phase 3 |
| `MarketplacePanelProvider.cs` | CREATE | Phase 3 |
| `SubAgentViewerProvider.cs` | CREATE | Phase 3 |
| `Services/AutocompleteProvider.cs` | CREATE | Phase 3 |

---

## 11. Unresolved Items (UNKNOWN)

1. **Settings Editor Implementation** — Unclear if `OpenSettingsCommand.cs` provides full settings editor functionality
2. **Session Stream Scheduler** — Tests exist but production usage unclear
3. **CSP Configuration** — Visual Studio WebView2 CSP handling not verified
4. **Font Loading** — Font loading mechanism not verified
5. **Telemetry Integration** — Telemetry proxy implementation not verified
6. **Commit Message Service** — Implementation status unclear
7. **Code Actions** — Registration and implementation unclear
8. **URI Handler** — Deep link handling not verified

---

## 12. Validation Checklist

- [x] VS Code extension entry point identified
- [x] Extension.ts dependency graph analyzed
- [x] CLI/HTTP client implementation identified
- [x] WebView communication identified
- [x] Relevant tests identified
- [x] Visual Studio counterparts mapped
- [x] Missing functionality listed
- [x] Divergent functionality documented
- [x] Mapping evidence based on source code
- [x] No production/test code modified
- [x] No upstream/fork dependency introduced
- [x] Implementation order justified
- [ ] Plan reviewed and accepted

---

## 13. Status

**PORT-INFRA-002 → REVIEW**

This plan is ready for human review. The next step is to validate the mappings and approve the implementation order before proceeding with PORT-CLI-001.

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

**Providers:**
- `KiloWebViewControl.cs` — WebView control
- `KiloClawProvider.cs` — KiloClaw
- `ProviderFactory.cs` — Provider factory
- `AgentManager/AgentManagerProvider.cs` — Agent Manager

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
