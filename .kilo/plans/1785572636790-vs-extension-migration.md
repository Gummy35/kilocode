# Visual Studio Extension 1:1 Migration Plan

## Implementation Rules

**These rules are mandatory for all implementation work:**

1. **Source of truth**: The original implementation in `packages/kilo-vscode/` files is the source of truth. Never deviate from the VS Code implementation.

2. **Read VS Code first**: For each task, read the corresponding VS Code file to understand the workflow, structure, messages, and logic before creating or modifying any C# files.

3. **Create/modify C# files**: All implementation goes in `packages/kilo-visualstudio/KiloVisualStudioExtension/`. Create new C# files to match VS Code TypeScript files.

4. **100% match requirement**: The code ported from VS Code to Visual Studio must match 100% in workflow, structure, logic, and messages. Do not deviate, ever. If VS Code sends message type X with fields A, B, C, the VS Extension must send message type X with fields A, B, C.

5. **No TODOs**: No TODO comments allowed. All implementations must be complete. If a feature cannot be fully implemented, mark the entire task as incomplete rather than leaving TODO stubs.

6. **Context limit handling**: If the current context size exceeds 200,000 tokens, you must:
   - Document current progress in the plan file
   - Provide a full prompt to continue the work in another session
   - Include which tasks are complete, which are in progress, and what remains

7. **Extensive documentation**: Every project code file in the extension must be extensively documented in English so that anyone can comprehend it. Use XML documentation comments (`///`) for all public types and members. Explain complex logic, message flows, and architectural decisions.

8. **Test alignment**: Existing test files in `KiloVisualStudioExtension.Tests/` are architectural/contract tests. Implement code to make tests pass - do not modify tests unless they contradict the VS Code implementation.

9. **No refactoring of shared code**: Do not refactor or modify settings-related code in Visual Studio extension without explicit authorization. Match the existing patterns.

10. **Error handling**: Match VS Code's error handling patterns. If VS Code logs an error and continues, the VS Extension must do the same. If VS Code throws, the VS Extension must throw.

11. **Message ordering**: Preserve message ordering semantics from VS Code. If VS Code posts message A before message B, the VS Extension must do the same.

12. **Fire-and-forget pattern**: Match VS Code's fire-and-forget pattern for non-critical operations (using `void` or `_ =` for async calls that don't need awaiting).

13. **Thread affinity**: Use `JoinableTaskFactory.SwitchToMainThreadAsync()` for UI operations. Match VS Code's threading model where applicable.

14. **Dispose pattern**: Implement proper IDisposable patterns for all services. Match VS Code's cleanup order in deactivation/disposal.

15. **Logging**: Use `System.Diagnostics.Debug.WriteLine("[Kilo] ...")` for all debug logging to match VS Code's `[Kilo New]` prefix convention.

## Goal

Migrate the Visual Studio extension to feature parity with the VS Code extension by implementing a shared connection service, lazy backend startup, and all missing providers (Agent Manager, KiloClaw, Marketplace, Diff Viewer, Sub-Agent Viewer) with 1:1 message protocol compatibility.

## Current State

### Implemented
- `CliBackendManager` - backend process management (starts eagerly on package init)
- `KiloConnectionService` - HTTP + SSE (created per tool window, not shared)
- `VSProvider` - partial KiloProvider port with core message handlers
- `SSEHelper` - session tracking and event filtering
- `SettingsToolWindow` + `SettingsEditorProvider` - stub implementation
- 45+ test files in `KiloVisualStudioExtension.Tests/`

### Missing (Critical Gaps)
1. **Shared connection service** - VS Code uses one `KiloConnectionService` instance; VS Extension creates one per tool window
2. **Lazy backend startup** - VS Code spawns `kilo serve` on first connection; VS Extension starts on package init
3. **Provider factory pattern** - No mechanism to create multiple provider instances (tab panels, Agent Manager, etc.)
4. **Missing providers** - Agent Manager, KiloClaw, Marketplace, Diff Viewer, Sub-Agent Viewer, Diff Virtual
5. **Command registration** - Only 2 commands vs VS Code's 80+
6. **Serialization** - No panel serializers for restore-on-restart
7. **URI handlers** - No deep link support for cloud sessions

## Migration Phases

### Phase 1: Core Infrastructure Refactor

**Task 1.1: Refactor KiloConnectionService to singleton**
- Move from per-tool-window to extension-wide singleton pattern
- Store in `KiloVisualStudioExtensionPackage` as static field
- Add `GetConnectionService()` static accessor method
- All providers (VSProvider, AgentManagerProvider, etc.) receive shared instance via constructor

**Task 1.2: Implement lazy backend startup**
- Remove `_backendManager.StartAsync()` from `KiloVisualStudioExtensionPackage.InitializeAsync()`
- Move backend start to `KiloConnectionService.ConnectAsync()` - only start when first provider connects
- Add health check verification before marking state as `Connected`
- Match VS Code's `ServerManager` lazy spawn pattern

**Task 1.3: Create ProviderFactory**
- New class `ProviderFactory.cs` with static factory methods:
  - `CreateSidebarProvider()` - returns VSProvider for sidebar tool window
  - `CreateTabPanelProvider()` - returns VSProvider for "Open in Tab" panels
  - `CreateAgentManager()` - returns AgentManagerProvider
  - `CreateKiloClaw()` - returns KiloClawProvider
  - `CreateSettingsProvider()` - returns SettingsEditorProvider
  - `CreateMarketplaceProvider()` - returns MarketplacePanelProvider
  - `CreateDiffViewer()` - returns DiffViewerProvider
  - `CreateSubAgentViewer()` - returns SubAgentViewerProvider
- All factory methods inject shared `KiloConnectionService`

**Task 1.4: Refactor KiloToolWindow**
- Remove direct `KiloConnectionService` instantiation
- Use `ProviderFactory.CreateSidebarProvider()` to get VSProvider instance
- Store provider reference for disposal on window close

### Phase 2: Provider Parity

**Task 2.1: Complete VSProvider message handlers**
- Create `MessageHandlerRegistry.cs` to extract handler logic into service classes (matching VS Code's `kilo-provider/handlers/` pattern)
- Add missing message handlers to match VS Code's `KiloProvider.handleWebviewMessage()`:
  - `openMarketplacePanel` - route to MarketplacePanelProvider
  - `forkSession` - implement fork session flow
  - `openSubAgentViewer` - route to SubAgentViewerProvider
  - `cycleAgentMode` - broadcast to all providers
  - `toggleMemory` - toggle session memory
  - `showMemory` - show session memory panel
  - `reload` - reload extension
  - All Agent Manager commands (sessionPrevious, sessionNext, tabPrevious, tabNext, etc.)
- Match VS Code's fire-and-forget pattern for non-critical operations
- Keep VSProvider under 1500 lines by delegating to handler services

**Task 2.2: Implement tab panel support**
- Create `TabPanelManager.cs` to track open tab panels (like VS Code's `tabPanels` Map)
- Implement `openInTab` command handler that:
  - Creates new WebViewPanel with `retainContextWhenHidden: true`
  - Calls `ProviderFactory.CreateTabPanelProvider()`
  - Registers panel in TabPanelManager
  - Disposes panel and provider on panel close
- Add webview panel serializer for tab panels (restore on VS restart)

**Task 2.3: Complete SettingsEditorProvider**
- Match VS Code's singleton pattern
- Support multiple panel views (settings, profile, indexing)
- Track open settings panels
- Add serializer for restore on restart
- Implement `OpenPanel(tab, view)` method matching VS Code

**Task 2.4: Create KiloClawProvider**
- Port from VS Code's `KiloClawProvider.ts`
- Implement chat panel in editor area
- Add serializer for restore on restart
- Register `kiloClawOpen` command

### Phase 3: Agent Manager Implementation

**Task 3.1: Core Agent Manager infrastructure**
- Create `AgentManagerProvider.cs` matching VS Code's `AgentManagerProvider.ts`:
  - Multi-session orchestration panel with tabbed UI
  - Session state management
  - Panel visibility tracking
  - Deserialization support
- Create `VsHost.cs` matching VS Code's `vscode-host.ts`:
  - VS-specific host implementation for Agent Manager
  - Terminal integration
  - Diff integration
  - Worktree management hooks

**Task 3.2: Worktree management**
- Create `GitService.cs` using `System.Diagnostics.Process` to wrap git CLI commands (matching VS Code's approach):
  - Create worktree (`git worktree add`)
  - Delete worktree (`git worktree remove`)
  - Switch worktree (`git checkout`)
  - List worktrees (`git worktree list`)
- Port `WorktreeStateManager.ts` to `WorktreeStateManager.cs`:
  - Track worktree state per session
  - Persist state to `.kilo/agent-manager.json`
  - Restore state on restart

**Task 3.3: Session management**
- Implement session tab switching (matching VS Code's tab navigation)
- Implement multi-version mode (fork sessions with same prompt)
- Add setup script runner (execute `.kilo/setup-script` in worktree)
- Implement session close/dispose flow

**Task 3.4: Terminal integration**
- Enhance existing `TerminalManager.cs`:
  - Create terminal per session
  - Route terminal output to session state
  - Handle terminal commands (showTerminal, newTerminal, runScript)
- Match VS Code's `SessionTerminalManager` pattern

**Task 3.5: UI components**
- Agent Manager webview already shares same webview code (no changes needed)
- Implement terminal tab integration in C# host
- Add diff viewer integration (hook into DiffViewerProvider)

**Task 3.6: Command registration**
- Register all Agent Manager commands:
  - `agentManagerOpen` - open panel
  - `agentManager.showTerminal` - show session terminal
  - `agentManager.runScript` - run setup script
  - `agentManager.toggleDiff` - toggle diff view
  - `agentManager.showShortcuts` - show keyboard shortcuts
  - `agentManager.newTab`, `closeTab` - tab management
  - `agentManager.newWorktree`, `quickWorktree`, `openWorktree`, `closeWorktree`
  - `agentManager.openPR` - open pull request
  - `agentManager.advancedWorktree` - advanced worktree creation
  - `agentManager.jumpTo1-9` - quick navigation
  - `agentManager.previousSession`, `nextSession` - session navigation
  - `agentManager.previousTab`, `nextTab` - tab navigation
  - `agentManager.search` - search sessions

### Phase 4: Advanced Features

**Task 4.1: Marketplace integration**
- Create `MarketplacePanelProvider.cs` matching VS Code's `MarketplacePanelProvider.ts`:
  - Marketplace browser panel
  - Install marketplace items
  - Deserialize support
- Create `MarketplaceNotifier.cs` matching VS Code's `MarketplaceNotifier`:
  - Detect workspace-matching marketplace items
  - Show discardable notification
  - Track notification dismissal

**Task 4.2: Diff viewer**
- Create `DiffViewerProvider.cs` matching VS Code's `DiffViewerProvider.ts`:
  - Diff viewing panel for file changes
  - Comment handling
  - Deserialize support
- Complete `DiffSourceCatalog.cs` (already exists, needs completion):
  - Track diff sources (permission approvals, review comments)
  - Provide diff data to viewer

**Task 4.3: Diff Virtual (permission preview)**
- Create `DiffVirtualProvider.cs` matching VS Code's `DiffVirtualProvider.ts`:
  - Single-file diff for permission approval preview
  - Virtual document provider for VS Code diff UI
- Hook into VSProvider's permission approval flow

**Task 4.4: Sub-agent viewer**
- Create `SubAgentViewerProvider.cs` matching VS Code's `SubAgentViewerProvider.ts`:
  - Read-only session viewer for sub-agent sessions
  - Open panel with session ID
  - No serializer needed (session ID can't be recovered after restart)

**Task 4.5: URI handlers**
- Implement URI handler in `KiloVisualStudioExtensionPackage`:
  - Handle `kilocode://` deep links
  - Support cloud session opening (`/kilocode/s/{sessionId}`)
  - Support model/agent linking (`/kilocode/switch?model=X&agent=Y`)
- Match VS Code's `handleUri()` pattern

### Phase 5: Services & Polish

**Task 5.1: RemoteStatusService**
- Complete existing partial implementation:
  - Add UI (status bar item)
  - Implement toggle command
  - Match VS Code's `RemoteStatusService` pattern

**Task 5.2: BrowserAutomationService**
- Create `BrowserAutomationService.cs` matching VS Code's:
  - Playwright MCP server registration
  - Enable/disable sync with settings
  - Reregister on backend reconnect

**Task 5.3: AttentionService**
- Create `AttentionService.cs` matching VS Code's:
  - Approve requests with attention
  - Dispose on extension deactivation

**Task 5.4: AutocompleteServiceManager**
- Create `AutocompleteServiceManager.cs` matching VS Code's:
  - Register autocomplete provider
  - Ensure backend for autocomplete
  - Load/refresh autocomplete service

**Task 5.5: Command registration (remaining)**
- Register all remaining commands:
  - `plusButtonClicked` - new task
  - `historyButtonClicked` - open history
  - `profileButtonClicked` - open profile settings
  - `settingsButtonClicked` - open settings
  - `showChanges` - open diff viewer
  - `openSubAgentViewer` - open sub-agent viewer
  - `reload` - reload extension
  - `toggleRemote` - toggle remote status
  - `showMemory`, `toggleMemory` - memory panel
  - `openIndexingSettings` - open indexing settings
  - `generateTerminalCommand` - terminal command generator
  - `openMigrationWizard` - legacy migration (if needed)

**Task 5.6: Code actions & terminal actions**
- Register code actions (editor context menus)
- Register terminal actions (terminal context menus)
- Match VS Code's `registerCodeActions()` and `registerTerminalActions()`

**Task 5.7: Serialization for all panel types**
- Visual Studio WebView2 does not have VS Code's `registerWebviewPanelSerializer()` API
- Use state persistence via `.kilo/agent-manager.json` (project-local state file)
- On panel creation, read state from `.kilo/agent-manager.json` and restore panel configuration
- On panel state change, write state to `.kilo/agent-manager.json`
- Match VS Code's approach where state is stored in project files, not through serializer APIs
- Panels to support: Agent Manager, KiloClaw, Tab panels, Settings, Marketplace, Diff Viewer

**Task 5.8: Commit message generation**
- Create `CommitMessageService.cs` matching VS Code's `registerCommitMessageService()`:
  - Generate commit messages from session context
  - Register command handler

**Task 5.9: Heap snapshot**
- Create `HeapSnapshotService.cs` matching VS Code's `registerHeapSnapshot()`:
  - Capture heap snapshot for debugging
  - Register command handler

## Validation Plan

### Unit Tests
- Run existing test suite: `dotnet test packages\kilo-visualstudio\KiloVisualStudioExtension.Tests\`
- **Important:** Existing test files (e.g., `AgentManagerArchTests.cs`, `AbortTests.cs`) are architectural/contract tests that define expected behavior - implement code to make tests pass
- Add tests for new providers as implemented
- Match VS Code test patterns from `packages/kilo-vscode/tests/unit/`

### Integration Tests (Manual)
1. **Shared connection service**
   - Open sidebar, verify backend starts
   - Open tab panel, verify same backend port
   - Open Agent Manager, verify same backend port
   - Close sidebar, verify backend stays alive (other providers connected)
   - Close all providers, verify backend stops

2. **Lazy backend startup**
   - Install extension, verify backend NOT started
   - Open sidebar, verify backend starts
   - Check backend process count (should be 1)

3. **Provider creation**
   - Create sidebar provider, verify singleton connection
   - Create tab panel, verify new provider instance shares connection
   - Create Agent Manager, verify new provider instance shares connection
   - Verify all providers receive SSE events correctly

4. **Message protocol**
   - Test all 80+ command handlers
   - Verify message ordering under concurrency
   - Test error handling (backend down, network errors)

5. **State persistence/restore**
   - Open multiple panels (sidebar, tab, Agent Manager, settings)
   - Create sessions, switch tabs, modify state
   - Restart Visual Studio
   - Verify panels restore with correct state from `.kilo/agent-manager.json`

6. **Agent Manager**
   - Create worktree session
   - Switch between sessions
   - Test multi-version mode
   - Test terminal integration
   - Test diff viewer integration
   - Test setup script execution

7. **URI handlers**
   - Test cloud session deep link
   - Test model/agent linking

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Visual Studio API differences | High | Use VS SDK patterns, test on VS2022/VS2026 |
| WebView2 limitations | Medium | Test all webview features, fallback gracefully |
| Process spawning on Windows | Medium | Use `windowsHide: true`, match VS Code process.ts |
| Memory pressure with multiple panels | Medium | Implement panel disposal, lazy loading |
| Thread affinity (main thread requirements) | High | Use `JoinableTaskFactory`, `[MainThread]` annotations |
| Git operations in worktrees | Medium | Use libgit2sharp or process wrapper, handle errors |
| Serialization complexity | Medium | Start with simple state, iterate on complexity |

## Out of Scope

- Webview UI changes (shared with VS Code via WebView2)
- Backend CLI changes (packages/opencode/)
- VS Code extension changes (packages/kilo-vscode/)
- New features not present in VS Code extension

## Success Criteria

1. **Feature parity**: All VS Code features available in VS Extension
2. **Message compatibility**: Same message protocol, same behavior
3. **User experience**: Indistinguishable from VS Code extension
4. **Performance**: No regression in response times
5. **Stability**: No crashes, proper error handling
6. **Shared connection**: Single backend process for all providers
7. **Lazy startup**: Backend only starts when first provider connects
8. **State persistence**: Panel state survives Visual Studio restart via `.kilo/agent-manager.json`
9. **Test coverage**: Existing architectural tests pass; new tests added for new functionality

## Deployment Strategy

Each phase is a deployable milestone - deploy incrementally rather than as a complete rewrite:

- **Phase 1 deploy**: Shared connection service + lazy backend startup (improves architecture, no user-facing changes)
- **Phase 2 deploy**: Tab panels + completed VSProvider (users can open "Open in Tab" panels)
- **Phase 3 deploy**: Agent Manager (major feature release)
- **Phase 4 deploy**: Marketplace + Diff Viewer + Sub-Agent Viewer (advanced features)
- **Phase 5 deploy**: Services + remaining commands (polish and completeness)

This approach reduces risk and allows early validation at each phase.

## Open Questions

1. **Test expectations**: Existing Agent Manager tests (`AgentManagerArchTests.cs`, etc.) are architectural/contract tests - verify during Phase 3 implementation that test expectations match intended behavior
2. **VS-specific features**: Are there features that should be added beyond VS Code parity (e.g., Visual Studio-specific integrations)?
3. **Rollback strategy**: If a phase has critical issues, what is the rollback plan (feature flags, A/B deployment)?
