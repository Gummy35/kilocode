# Visual Studio Extension 1:1 Migration Plan

This document outlines the complete migration plan to bring the Visual Studio extension to feature parity with the VS Code extension.

## Architecture Comparison

### VS Code Extension Architecture (`packages/kilo-vscode/src/extension.ts`)

```
+---------------------------------------------------------------------+
|                    VS Code Extension Host                           |
+---------------------------------------------------------------------+
|  activate()                                                          |
|  +-- KiloConnectionService (shared, lazy-start)                     |
|  |   +-- ServerManager (spawns kilo serve --port 0)                 |
|  |   +-- HttpClient                                                 |
|  |   +-- SSEClient                                                  |
|  +-- KiloProvider (sidebar)                                         |
|  +-- KiloProvider (tab panels)  ---- All share connectionService    |
|  +-- AgentManagerProvider                                           |
|  +-- KiloClawProvider                                                |
|  +-- SettingsEditorProvider                                         |
|  +-- MarketplacePanelProvider                                       |
|  +-- DiffViewerProvider                                             |
|  +-- SubAgentViewerProvider                                         |
|  +-- DiffVirtualProvider                                            |
|  +-- RemoteStatusService                                            |
|  +-- BrowserAutomationService                                       |
|  +-- AttentionService                                               |
|  +-- AutocompleteServiceManager                                     |
|  +-- Command Registrations (80+ commands)                           |
+---------------------------------------------------------------------+
```

### Visual Studio Extension Architecture (`packages/kilo-visualstudio/`)

```
+---------------------------------------------------------------------+
|                Visual Studio Extension Package                       |
+---------------------------------------------------------------------+
|  KiloVisualStudioExtensionPackage.InitializeAsync()                  |
|  +-- CliBackendManager (singleton, starts on init)                   |
|  |   +-- Spawns kilo serve (eager, not lazy)                        |
|  +-- ShowKiloWindowCommand                                          |
|  +-- OpenSettingsCommand                                           |
|                                                                     |
|  KiloToolWindow (single tool window)                                |
|  +-- KiloWebViewControl                                             |
|  +-- KiloConnectionService (created per tool window)                |
|  +-- VSProvider (single provider instance)                          |
|                                                                     |
|  SettingsToolWindow (separate tool window)                          |
|  +-- SettingsEditorProvider (limited implementation)                |
+---------------------------------------------------------------------+
```

## Gap Analysis

### 1. Extension Activation Pattern

| Aspect | VS Code | Visual Studio | Status |
|--------|---------|---------------|--------|
| Activation trigger | `onStartupFinished` + `onUri` | `SolutionExists` auto-load | Different |
| Backend startup | Lazy (on first connection) | Eager (on package init) | Missing lazy |
| Shared connection service | Yes (one per extension host) | No (per tool window) | Missing shared |
| URI handler | Yes (deep links) | No | Missing |

**Migration Priority:** HIGH

### 2. Provider Architecture

| Provider | VS Code | Visual Studio | Status |
|----------|---------|---------------|--------|
| KiloProvider (sidebar) | Yes | VSProvider (partial) | Partial |
| KiloProvider (tab panels) | Yes (Open in Tab) | No | Missing |
| AgentManagerProvider | Yes | No | Missing |
| KiloClawProvider | Yes | No | Missing |
| SettingsEditorProvider | Yes | SettingsEditorProvider (stub) | Stub |
| MarketplacePanelProvider | Yes | No | Missing |
| DiffViewerProvider | Yes | No | Missing |
| SubAgentViewerProvider | Yes | No | Missing |
| DiffVirtualProvider | Yes | No | Missing |

**Migration Priority:** HIGH

### 3. Message Protocol

| Message Type | VS Code | Visual Studio | Status |
|--------------|---------|---------------|--------|
| webviewReady | Yes | Yes | Implemented |
| createSession | Yes | Yes | Implemented |
| loadMessages | Yes | Yes | Implemented (with abort) |
| sendMessage | Yes | Yes | Implemented |
| abort | Yes | Yes | Implemented |
| deleteSession | Yes | Yes | Implemented |
| renameSession | Yes | Yes | Implemented |
| requestProviders | Yes | Yes | Implemented |
| requestAgents | Yes | Yes | Implemented |
| openSettingsPanel | Yes | Yes | Implemented |
| openMarketplacePanel | Yes | No | Missing handler |
| forkSession | Yes | No | Missing |
| openSubAgentViewer | Yes | No | Missing |
| cycleAgentMode | Yes | No | Missing |
| toggleMemory | Yes | No | Missing |
| showMemory | Yes | No | Missing |

**Migration Priority:** MEDIUM

### 4. Features Missing in VS Extension

| Feature | VS Code Implementation | VS Extension Status | Priority |
|---------|----------------------|---------------------|----------|
| Agent Manager | `AgentManagerProvider.ts` + 40+ helper modules | Not implemented | HIGH |
| Tab panels (Open in Tab) | WebviewPanel serializer + KiloProvider instances | Not implemented | HIGH |
| KiloClaw | `KiloClawProvider.ts` | Not implemented | MEDIUM |
| Marketplace | `MarketplacePanelProvider.ts` + `MarketplaceNotifier` | Not implemented | MEDIUM |
| Diff Viewer | `DiffViewerProvider.ts` + `DiffSourceCatalog` | Not implemented | MEDIUM |
| Sub-Agent Viewer | `SubAgentViewerProvider.ts` | Not implemented | LOW |
| Diff Virtual (permission preview) | `DiffVirtualProvider.ts` | Not implemented | MEDIUM |
| URI deep links | `handleUri()` for cloud sessions | Not implemented | LOW |
| Code actions | Context menu integrations | Not implemented | LOW |
| Terminal actions | Terminal context menu | Not implemented | LOW |
| Commit message generation | `registerCommitMessageService()` | Not implemented | LOW |
| Heap snapshot | `registerHeapSnapshot()` | Not implemented | LOW |
| Remote toggle | `RemoteStatusService` + toggle command | Partial (service exists) | LOW |

### 5. Command Registration

VS Code registers 80+ commands. VS Extension has only 2:
- `kilo-code.new.showKiloWindow` (ShowKiloWindowCommand)
- `kilo-code.new.openSettings` (OpenSettingsCommand)

**Missing Commands (high priority):**
- `kilo-code.new.plusButtonClicked` (New task)
- `kilo-code.new.historyButtonClicked` (Open history)
- `kilo-code.new.agentManagerOpen` (Open Agent Manager)
- `kilo-code.new.kiloClawOpen` (Open KiloClaw)
- `kilo-code.new.marketplaceButtonClicked` (Open Marketplace)
- `kilo-code.new.profileButtonClicked` (Open profile settings)
- `kilo-code.new.settingsButtonClicked` (Open settings)
- `kilo-code.new.openInTab` (Open current session in tab)
- `kilo-code.new.showChanges` (Open diff viewer)
- `kilo-code.new.openSubAgentViewer` (Open sub-agent viewer)

### 6. Serialization/Restore

| Panel Type | VS Code Serializer | VS Extension |
|------------|-------------------|--------------|
| Agent Manager | Yes (`deserializeWebviewPanel`) | Missing |
| KiloClaw | Yes | Missing |
| Tab panels | Yes | Missing |
| Settings | Yes (multiple views) | Missing |
| Marketplace | Yes | Missing |
| Diff Viewer | Yes | Missing |

### 7. Services Missing

| Service | VS Code | VS Extension |
|---------|---------|--------------|
| RemoteStatusService | Yes | Partial (no UI) |
| BrowserAutomationService | Yes | Missing |
| AttentionService | Yes | Missing |
| AutocompleteServiceManager | Yes | Missing |
| TelemetryProxy | Yes | Missing |

## Migration Phases

### Phase 1: Core Infrastructure (Week 1-2)

**Goal:** Establish shared connection service and lazy backend startup

1. **Refactor KiloConnectionService to singleton pattern**
   - Move from per-tool-window to extension-wide singleton
   - Match VS Code's `new KiloConnectionService(context)` pattern
   - Add `onStateChange` event propagation to all providers

2. **Implement lazy backend startup**
   - Move `CliBackendManager.StartAsync()` from package init to first connection
   - Match VS Code's `ServerManager` lazy spawn pattern
   - Add backend health check before marking connected

3. **Create extension package structure**
   - Split `KiloVisualStudioExtensionPackage` into:
     - Package initialization (commands, tool windows)
     - Shared service container
     - Provider factory

### Phase 2: Provider Parity (Week 3-4)

**Goal:** Implement missing providers with 1:1 message handling

1. **Enhance VSProvider to match KiloProvider**
   - Add all missing message handlers (80+ commands)
   - Implement `openInTab` pattern for tab panels
   - Add webview panel serializer support

2. **Implement SettingsEditorProvider fully**
   - Match VS Code's `SettingsEditorProvider` singleton pattern
   - Support multiple panel views (settings, profile, indexing)
   - Add serializer for restore on restart

3. **Create KiloClawProvider**
   - Port from VS Code's `KiloClawProvider.ts`
   - Implement chat panel in editor area

### Phase 3: Agent Manager (Week 5-8)

**Goal:** Implement Agent Manager feature (largest gap)

1. **Core Agent Manager infrastructure**
   - Create `AgentManagerProvider.cs` (matches `AgentManagerProvider.ts`)
   - Implement `VsHost.cs` (matches `vscode-host.ts`)
   - Add terminal integration (`TerminalManager.cs` already exists)

2. **Worktree management**
   - Port `WorktreeManager.ts` to `WorktreeManager.cs`
   - Port `WorktreeStateManager.ts` to `WorktreeStateManager.cs`
   - Implement git operations (create, delete, switch)

3. **Session management**
   - Port session tab switching
   - Implement multi-version mode
   - Add setup script runner

4. **UI components**
   - Port Agent Manager webview (already shares same webview code)
   - Implement terminal tab integration
   - Add diff viewer integration

### Phase 4: Advanced Features (Week 9-12)

**Goal:** Implement remaining features

1. **Marketplace integration**
   - Port `MarketplacePanelProvider.ts` to `MarketplacePanelProvider.cs`
   - Implement `MarketplaceNotifier` for workspace matches

2. **Diff viewer**
   - Port `DiffViewerProvider.ts` to `DiffViewerProvider.cs`
   - Implement `DiffSourceCatalog`
   - Add `DiffVirtualProvider` for permission preview

3. **Sub-agent viewer**
   - Port `SubAgentViewerProvider.ts` to `SubAgentViewerProvider.cs`
   - Implement read-only session viewer

4. **URI handlers**
   - Add deep link support for cloud sessions
   - Implement model/agent linking

### Phase 5: Polish & Services (Week 13-14)

**Goal:** Add remaining services and polish

1. **Services**
   - Implement `BrowserAutomationService`
   - Implement `AttentionService`
   - Add `AutocompleteServiceManager`

2. **Commands & actions**
   - Register all missing commands
   - Add code actions (editor context menus)
   - Add terminal actions

3. **Serialization**
   - Add serializers for all panel types
   - Test restore on Visual Studio restart

## Implementation Details

### Shared Connection Service Pattern

```csharp
// VS Code pattern (extension.ts:14)
const connectionService = new KiloConnectionService(context)

// VS Extension should match:
public class KiloVisualStudioExtensionPackage : AsyncPackage
{
    private static KiloConnectionService? _sharedConnectionService;
    
    protected override async Task InitializeAsync(...)
    {
        // Don't start backend here!
        // Create shared service that starts lazily
        _sharedConnectionService = new KiloConnectionService(this);
        
        // Register commands, tool windows, etc.
        // All providers will share this service
    }
    
    public static KiloConnectionService GetConnectionService()
    {
        return _sharedConnectionService ?? 
            throw new InvalidOperationException("Extension not initialized");
    }
}
```

### Lazy Backend Startup Pattern

```csharp
// VS Code pattern (ServerManager.ts)
// Backend spawns only when first webview connects

// VS Extension should match:
public class KiloConnectionService
{
    private readonly CliBackendManager _backendManager;
    private ConnectionState _state = Disconnected;
    
    public async Task ConnectAsync()
    {
        if (_state == Connected) return;
        
        // Lazy start - only start backend on first connection
        await _backendManager.StartAsync();
        
        // Then setup HTTP client, SSE, etc.
    }
}
```

### Provider Factory Pattern

```csharp
// VS Code creates providers on-demand:
// - Sidebar: registered at activation
// - Tab panels: created when "openInTab" command runs
// - Agent Manager: created when command runs

// VS Extension should match:
public static class ProviderFactory
{
    public static VSProvider CreateSidebarProvider() { }
    public static VSProvider CreateTabPanelProvider() { }
    public static AgentManagerProvider CreateAgentManager() { }
    public static KiloClawProvider CreateKiloClaw() { }
    public static SettingsEditorProvider CreateSettingsProvider() { }
    // ... etc
}
```

## Current Implementation Status

### Already Implemented (use as base)

1. **Core infrastructure**
   - `CliBackendManager` - backend process management
   - `KiloConnectionService` - HTTP + SSE (needs singleton refactor)
   - `SseClient` - SSE event handling
   - `HttpClientWrapper` - HTTP requests
   - `CachedHttpClient` - cached responses

2. **VSProvider** (partial KiloProvider port)
   - Message handling framework
   - Session creation/deletion/rename
   - Message loading with abort controller
   - SSE event handling
   - Focus session tracking
   - Prompt recovery

3. **SSEHelper**
   - Session tracking
   - Event filtering
   - Status management

4. **Settings infrastructure**
   - `SettingsToolWindow` - tool window frame
   - `SettingsEditorProvider` - stub implementation

5. **Services** (partial)
   - `TerminalManager` - terminal session management
   - `SessionCreatorService` - session creation
   - `DiffSourceCatalog` - diff sources
   - `DraftStore` - draft management

### Not Implemented (migration needed)

See "Features Missing in VS Extension" table above.

## Testing Strategy

1. **Unit tests** - Already have extensive test suite in `KiloVisualStudioExtension.Tests/`
   - Match VS Code test patterns from `packages/kilo-vscode/tests/unit/`
   - Add tests for new providers as they're implemented

2. **Integration tests** - Manual testing required
   - Test all command handlers
   - Test panel serialization/restore
   - Test multi-panel scenarios

3. **Message protocol tests**
   - Verify all message types are handled
   - Test message ordering and concurrency
   - Test error handling

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Visual Studio API differences | High | Use VS SDK patterns, test on VS2022/VS2026 |
| WebView2 limitations | Medium | Test all webview features, fallback gracefully |
| Process spawning on Windows | Medium | Use `windowsHide: true`, match VS Code process.ts |
| Memory pressure with multiple panels | Medium | Implement panel disposal, lazy loading |
| Thread affinity (main thread requirements) | High | Use `JoinableTaskFactory`, `[MainThread]` annotations |

## Success Criteria

1. **Feature parity**: All VS Code features available in VS Extension
2. **Message compatibility**: Same message protocol, same behavior
3. **User experience**: Indistinguishable from VS Code extension
4. **Performance**: No regression in response times
5. **Stability**: No crashes, proper error handling

## Appendix: File Mapping

### VS Code to Visual Studio File Mapping

| VS Code File | VS Extension File | Status |
|--------------|-------------------|--------|
| `src/extension.ts` | `KiloVisualStudioExtensionPackage.cs` | Partial |
| `src/KiloProvider.ts` | `VSProvider.cs` | Partial |
| `src/agent-manager/AgentManagerProvider.ts` | (not created) | Missing |
| `src/SettingsEditorProvider.ts` | `SettingsEditorProvider.cs` | Stub |
| `src/KiloClawProvider.ts` | (not created) | Missing |
| `src/DiffViewerProvider.ts` | (not created) | Missing |
| `src/services/cli-backend/KiloConnectionService.ts` | `KiloConnectionService.cs` | Implemented |
| `src/services/cli-backend/server-manager.ts` | `CliBackendManager.cs` | Implemented |
| `webview-ui/` | `webview/` | Shared |

## Notes

- The webview UI code is **shared** between VS Code and VS Extension via WebView2
- Focus migration efforts on C# extension infrastructure, not webview
- Use existing test suite as specification for expected behavior
- Match VS Code message protocol exactly - do not create VS-specific messages
