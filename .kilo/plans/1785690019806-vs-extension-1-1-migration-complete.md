# Visual Studio Extension 1:1 Complete Migration Plan

## Implementation Rules (Mandatory)

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

---

## Goal

Achieve 100% feature parity between the Visual Studio extension and VS Code extension by implementing all missing message handlers, providers, commands, and fixing critical data structure mismatches identified in MESSAGE_FLOWS.md analysis.

---

## Critical Issues to Fix First

### Issue 1: ServerInfo Field Name Mismatch

**Problem**: VS Code sends `{ port: number }` but VS Extension sends `{ Port: number }`

**Location**: `KiloConnectionService.cs` - `ServerInfo` struct

**Fix Required**:
```csharp
// Change from:
public struct ServerInfo { public int Port; }

// To:
public struct ServerInfo { public int port; }  // lowercase to match VS Code
```

**Impact**: Webview expects `serverInfo.port` but receives `serverInfo.Port`, causing connection state confusion.

---

### Issue 2: Session Structure Mismatch

**Problem**: VS Code session structure has fields that VS Extension is missing

**VS Code Structure** (`KiloProvider.ts:780-786`):
```typescript
{
  id: string,
  parentID: string | null,
  title: string,
  createdAt: string,
  updatedAt: string,
  revert: { messageID, snapshot, diff, workspace } | null,
  summary: { additions, deletions, files, diffs } | null
}
```

**VS Extension Current** (`VSProvider.cs:968-984`):
```csharp
{
  id: string,
  directory: string,  // WRONG - should not be here
  title: string,
  updated: number,    // WRONG - should be updatedAt
  status: string      // WRONG - should be separate event
}
```

**Fix Required**: Update `SessionHandlerService.HandleCreateSessionAsync()` to include all required fields with correct names.

---

### Issue 3: SSE Event Handling Gap

**Problem**: VS Extension receives SSE events but doesn't transform them into webview messages

**VS Code Pattern** (`KiloProvider.ts:260-293`):
1. `unwrapSyncEvent()` - transforms sync events to ProviderEvent
2. `mapSSEEventToWebviewMessage()` - maps to webview message format
3. `postMessage()` - sends to webview
4. Session filtering via `trackedSessionIds`

**VS Extension Current**: Only calls `_sseHelper.HandleEvent()` without transformation or posting

**Fix Required**: Implement `SSEEventTransformer` class with:
- `UnwrapSyncEvent()` method
- `MapEventToWebviewMessage()` method
- Integration with `SessionStreamScheduler`
- Session filtering logic

---

## Implementation Phases

### Phase 0: Critical Bug Fixes (Blockers)

**Priority**: Must complete before any other phase

#### Task 0.1: Fix ServerInfo Field Name
- **File**: `KiloConnectionService.cs`
- **Action**: Change `Port` → `port` in ServerInfo struct
- **Test**: Verify webview receives `{ port: number }` in ready message

#### Task 0.2: Fix Session Structure
- **File**: `Services/Handlers/Session/SessionHandlerService.cs`
- **Action**: Update session creation to include:
  - `parentID` (null for new sessions)
  - `createdAt` (ISO 8601 timestamp)
  - `updatedAt` (ISO 8601 timestamp)
  - `revert` (null initially)
  - `summary` (null initially)
- **Remove**: `directory`, `updated`, `status` from session object
- **Test**: Verify sessionCreated message matches VS Code structure

#### Task 0.3: Implement SSE Event Transformer
- **New File**: `Services/SSEEventTransformer.cs`
- **Action**: Create class with methods:
  - `UnwrapSyncEvent(SSEPayload event)` → ProviderEvent
  - `MapEventToWebviewMessage(ProviderEvent, string sessionID)` → object
  - `ShouldSendToWebview(string sessionID)` → bool (checks tracked sessions)
- **Modify**: `VSProvider.HandleSseEvent()` to use transformer and post messages
- **Test**: Verify message.created, message.part.updated, session.updated events are sent

---

### Phase 1: Core Infrastructure (Already Partially Complete)

**Status**: Phase 1 from original plan is mostly complete. Verify and fix.

#### Task 1.1: Verify Singleton Pattern
- **Check**: `KiloConnectionService` is singleton in `KiloVisualStudioExtensionPackage`
- **Verify**: All providers receive shared instance via constructor
- **Test**: Open sidebar, tab panel, Agent Manager - all should use same backend port

#### Task 1.2: Verify Lazy Backend Startup
- **Check**: Backend does NOT start on package init
- **Verify**: Backend starts on first `ConnectAsync()` call
- **Test**: Install extension, verify no kilo.exe process until opening sidebar

#### Task 1.3: Verify ProviderFactory
- **Check**: `ProviderFactory.cs` exists with factory methods
- **Verify**: All factory methods inject shared `KiloConnectionService`
- **Missing Methods to Add**:
  - `CreateTabPanelProvider()`
  - `CreateMarketplaceProvider()`
  - `CreateDiffViewer()`
  - `CreateSubAgentViewer()`

---

### Phase 2: Missing Message Handlers (40+ handlers)

**Priority**: Critical - these prevent core functionality

#### Task 2.1: Session Management Handlers (6 handlers)

**Files to Modify**: `VSProvider.cs` + create handler services

| Message Type | Handler Method | VS Code Reference |
|--------------|----------------|-------------------|
| `loadSessions` | `HandleLoadSessions()` | `KiloProvider.ts:1088` |
| `syncSession` | `HandleSyncSession()` | `KiloProvider.ts:1084` |
| `requestSessionModelUsage` | `HandleRequestSessionModelUsage()` | `KiloProvider.ts:1092` |
| `revertSession` | `HandleRevertSession()` | `KiloProvider.ts:1043` |
| `unrevertSession` | `HandleUnrevertSession()` | `KiloProvider.ts:1047` |
| `compact` | `HandleCompact()` | `KiloProvider.ts:1172` |

**Implementation Steps**:
1. Read each VS Code handler implementation
2. Create corresponding C# handler service in `Services/Handlers/Session/`
3. Add case to `VSProvider.ProcessMessageAsync()` switch statement
4. Test each handler with webview

---

#### Task 2.2: Agent Manager Command Handlers (30+ handlers)

**Files to Modify**: `VSProvider.cs` + `AgentManager/AgentManagerProvider.cs`

**Required Handlers** (partial list - see MESSAGE_FLOWS.md for complete list):

| Message Type | Handler Method | VS Code Reference |
|--------------|----------------|-------------------|
| `agentManager.createWorktree` | `HandleCreateWorktree()` | `AgentManagerProvider.ts:980` |
| `agentManager.deleteWorktree` | `HandleDeleteWorktree()` | `AgentManagerProvider.ts:1019` |
| `agentManager.promoteSession` | `HandlePromoteSession()` | `AgentManagerProvider.ts:1080` |
| `agentManager.forkSession` | `HandleForkSession()` | `AgentManagerProvider.ts:1199` |
| `agentManager.openLocally` | `HandleOpenLocally()` | `AgentManagerProvider.ts:460` |
| `agentManager.requestState` | `HandleRequestState()` | `AgentManagerProvider.ts:720` |
| `agentManager.setTabOrder` | `HandleSetTabOrder()` | `AgentManagerProvider.ts:631` |
| `agentManager.showTerminal` | `HandleShowTerminal()` | `AgentManagerProvider.ts:582` |
| `agentManager.requestWorktreeDiff` | `HandleRequestWorktreeDiff()` | `AgentManagerProvider.ts:687` |
| `agentManager.applyWorktreeDiff` | `HandleApplyWorktreeDiff()` | `AgentManagerProvider.ts:697` |
| `agentManager.startDiffWatch` | `HandleStartDiffWatch()` | `AgentManagerProvider.ts:703` |
| `agentManager.openFile` | `HandleOpenFile()` | `AgentManagerProvider.ts:707` |

**Implementation Steps**:
1. Read `AgentManagerProvider.ts` to understand message routing
2. Create `AgentManagerMessageHandler.cs` with all handler methods
3. Add case statements to `VSProvider.ProcessMessageAsync()`
4. Integrate with existing `AgentManagerProvider` instance
5. Test each command via webview

---

#### Task 2.3: Marketplace Handlers (4 handlers)

**Files to Create**: `MarketplacePanelProvider.cs`, `Services/Handlers/Marketplace/MarketplaceHandlerService.cs`

| Message Type | Handler Method | VS Code Reference |
|--------------|----------------|-------------------|
| `fetchMarketplaceData` | `HandleFetchMarketplaceData()` | `MarketplacePanelProvider.ts:199` |
| `installMarketplaceItem` | `HandleInstallMarketplaceItem()` | `MarketplacePanelProvider.ts:203` |
| `removeInstalledMarketplaceItem` | `HandleRemoveMarketplaceItem()` | `MarketplacePanelProvider.ts:207` |
| `dismissAgentMigrationBanner` | `HandleDismissMigrationBanner()` | `MarketplacePanelProvider.ts:211` |

**Implementation Steps**:
1. Read `MarketplacePanelProvider.ts:1-338` completely
2. Create `MarketplacePanelProvider.cs` matching structure
3. Create `MarketplaceHandlerService.cs` with 4 handler methods
4. Implement `MarketplaceService` to handle API calls
5. Add case statements to `VSProvider.ProcessMessageAsync()`
6. Test marketplace panel open, data fetch, install, remove

---

#### Task 2.4: KiloClaw Handlers (20+ handlers)

**Files to Modify**: `KiloClawProvider.cs` (currently stub)

**Required**: Complete implementation matching `KiloClawProvider.ts:1-1296`

**Key Handlers**:
- `kiloclaw.ready` → Initialize chat client and event service
- `kiloclaw.selectConversation` → Load conversation messages
- `kiloclaw.createConversation` → Create new conversation
- `kiloclaw.sendMessage` → Send message with optimistic update
- `kiloclaw.editMessage` → Edit message with rollback
- `kiloclaw.deleteMessage` → Delete message with rollback
- `kiloclaw.loadMoreMessages` → Paginate messages
- `kiloclaw.addReaction` / `removeReaction` → Manage reactions
- `kiloclaw.executeAction` → Handle approval actions
- `kiloclaw.sendTyping` / `sendTypingStop` → Typing indicators

**Implementation Steps**:
1. Read `KiloClawProvider.ts` completely (1296 lines)
2. Create `KiloChatClient.cs` - HTTP client for kilo-chat API
3. Create `EventServiceClient.cs` - WebSocket client for real-time events
4. Create `TokenManager.cs` - Handle chat token refresh
5. Rewrite `KiloClawProvider.cs` with full implementation
6. Implement all 20+ message handlers
7. Test conversation creation, messaging, reactions, typing

**Note**: This is the most complex provider - 1296 lines of TypeScript to port

---

#### Task 2.5: Sub-Agent Viewer Handler (1 handler)

**Files to Create**: `SubAgentViewerProvider.cs`

| Message Type | Handler Method | VS Code Reference |
|--------------|----------------|-------------------|
| `openSubAgentViewer` | Route to `SubAgentViewerProvider.OpenPanel()` | `KiloProvider.ts:1143` |
| `viewSubAgentSession` | Load session messages | `SubAgentViewerProvider.ts:54` |
| `closePanel` | Dispose panel | `SubAgentViewerProvider.ts:76` |

**Implementation Steps**:
1. Read `SubAgentViewerProvider.ts:1-98`
2. Create `SubAgentViewerProvider.cs` matching structure
3. Add `openSubAgentViewer` case to `VSProvider.ProcessMessageAsync()`
4. Test opening sub-agent viewer, loading messages, closing

---

#### Task 2.6: Provider & Model Management Handlers (8 handlers)

| Message Type | Handler Method | VS Code Reference |
|--------------|----------------|-------------------|
| `connectProvider` | `HandleConnectProvider()` | `provider-actions.ts` |
| `authorizeProviderOAuth` | `HandleAuthorizeOAuth()` | `provider-actions.ts` |
| `completeProviderOAuth` | `HandleCompleteOAuth()` | `provider-actions.ts` |
| `disconnectProvider` | `HandleDisconnectProvider()` | `provider-actions.ts` |
| `saveCustomProvider` | `HandleSaveCustomProvider()` | `provider-actions.ts` |
| `fetchCustomProviderModels` | `HandleFetchCustomProviderModels()` | `KiloProvider.ts:1168` |
| `removeSkill` | `HandleRemoveSkill()` | `KiloProvider.ts:1196` |
| `removeAgent` | `HandleRemoveAgent()` | `KiloProvider.ts:1200` |

**Implementation Steps**:
1. Read `provider-actions.ts` for OAuth flow
2. Create `ProviderActionHandler.cs` with all 8 methods
3. Add case statements to `VSProvider.ProcessMessageAsync()`
4. Test provider connection, OAuth flow, custom provider creation

---

#### Task 2.7: UI & Interaction Handlers (8 handlers)

| Message Type | Handler Method | VS Code Reference |
|--------------|----------------|-------------------|
| `saveImage` | `HandleSaveImage()` | `KiloProvider.ts:1125` |
| `previewImage` | `HandlePreviewImage()` | Route to image preview |
| `openExternal` | `HandleOpenExternal()` | Use `Process.Start()` |
| `forkSession` | `HandleForkSession()` | `KiloProvider.ts:1121` |
| `cycleAgentMode` | `HandleCycleAgentMode()` | Broadcast to all providers |
| `toggleMemory` | `HandleToggleMemory()` | Toggle session memory |
| `showMemory` | `HandleShowMemory()` | Show memory panel |
| `reload` | `HandleReload()` | `KiloProvider.ts:1138` |

**Implementation Steps**:
1. Read each handler in `KiloProvider.ts`
2. Create corresponding C# implementations
3. Add case statements to `VSProvider.ProcessMessageAsync()`
4. Test each UI interaction

---

### Phase 3: Missing Providers (6 providers)

#### Task 3.1: MarketplacePanelProvider (Complete)

**Status**: Not implemented

**VS Code Reference**: `MarketplacePanelProvider.ts:1-338`

**Required Files**:
- `MarketplacePanelProvider.cs` (338 lines equivalent)
- `MarketplaceHandlerService.cs` (message handlers)
- `MarketplaceService.cs` (API calls)
- `MarketplaceNotifier.cs` (workspace detection)

**Test**: Open marketplace panel, fetch data, install item, remove item

---

#### Task 3.2: KiloClawProvider (Complete)

**Status**: Stub only (83 lines vs 1296 in VS Code)

**VS Code Reference**: `KiloClawProvider.ts:1-1296`

**Required Files**:
- `KiloClawProvider.cs` (rewrite - 1200+ lines)
- `KiloChatClient.cs` (new)
- `EventServiceClient.cs` (new)
- `TokenManager.cs` (new)
- `KiloClawTypes.cs` (new - message types)

**Test**: Open KiloClaw panel, create conversation, send messages, reactions, typing

---

#### Task 3.3: SubAgentViewerProvider (Complete)

**Status**: Not implemented

**VS Code Reference**: `SubAgentViewerProvider.ts:1-98`

**Required Files**:
- `SubAgentViewerProvider.cs` (~100 lines)

**Test**: Open sub-agent viewer with session ID, view messages, close

---

#### Task 3.4: DiffViewerProvider (Complete)

**Status**: Not implemented

**VS Code Reference**: `DiffViewerProvider.ts` (read to determine line count)

**Required Files**:
- `DiffViewerProvider.cs`
- `DiffCommentHandler.cs`
- `DiffRenderer.cs`

**Test**: Open diff viewer, view file changes, add comments

---

#### Task 3.5: DiffVirtualProvider (Complete)

**Status**: Not implemented

**VS Code Reference**: `DiffVirtualProvider.ts`

**Required Files**:
- `DiffVirtualProvider.cs`
- Virtual document provider for VS diff UI

**Test**: Show virtual diff for permission approval preview

---

#### Task 3.6: Complete AgentManagerProvider

**Status**: Simplified (296 lines vs 2000+ in VS Code)

**VS Code Reference**: `AgentManagerProvider.ts:1-2000+`

**Missing Components**:
- `WorktreeManager.cs` (complete implementation)
- `SetupScriptService.cs` + `SetupScriptRunner.cs`
- `SessionTerminalManager.cs` (integrate with existing)
- `WorktreeDiffController.cs`
- `GitOps.cs`
- `GitStatsPoller.cs`
- `PRStatusBridge.cs`
- `BranchNamingController.cs`
- `WorktreeImporter.cs`
- `MultiVersionController.cs`

**Test**: Create worktree, promote session, fork session, run setup script, view diff

---

### Phase 4: Command Registration (78+ commands)

**Current**: Only 2 commands registered (`cmdShowKiloWindow`, `cmdOpenSettings`)

**Target**: 80+ commands matching VS Code

#### Task 4.1: Core Commands (10 commands)

Register in `KiloVisualStudioExtensionPackage.cs`:

```csharp
// Already exist - verify
CommandID cmdShowKiloWindow;
CommandID cmdOpenSettings;

// Add these:
CommandID cmdNewTask;
CommandID cmdHistoryButtonClicked;
CommandID cmdProfileButtonClicked;
CommandID cmdShowChanges;
CommandID cmdReload;
CommandID cmdToggleRemote;
CommandID cmdShowMemory;
CommandID cmdToggleMemory;
CommandID cmdOpenIndexingSettings;
CommandID cmdGenerateTerminalCommand;
```

---

#### Task 4.2: Agent Manager Commands (30+ commands)

```csharp
CommandID cmdAgentManagerOpen;
CommandID cmdAgentManagerShowTerminal;
CommandID cmdAgentManagerRunScript;
CommandID cmdAgentManagerToggleDiff;
CommandID cmdAgentManagerShowShortcuts;
CommandID cmdAgentManagerNewTab;
CommandID cmdAgentManagerCloseTab;
CommandID cmdAgentManagerNewWorktree;
CommandID cmdAgentManagerQuickWorktree;
CommandID cmdAgentManagerOpenWorktree;
CommandID cmdAgentManagerCloseWorktree;
CommandID cmdAgentManagerOpenPR;
CommandID cmdAgentManagerAdvancedWorktree;
CommandID cmdAgentManagerJumpTo1;  // Repeat for 1-9
CommandID cmdAgentManagerJumpTo2;
// ... jumpTo3-9
CommandID cmdAgentManagerPreviousSession;
CommandID cmdAgentManagerNextSession;
CommandID cmdAgentManagerPreviousTab;
CommandID cmdAgentManagerNextTab;
CommandID cmdAgentManagerSearch;
```

**Handler Implementation**: Each command calls corresponding method on `AgentManagerProvider`

---

#### Task 4.3: Marketplace & KiloClaw Commands (3 commands)

```csharp
CommandID cmdMarketplaceButtonClicked;
CommandID cmdKiloClawOpen;
CommandID cmdOpenSubAgentViewer;
```

---

#### Task 4.4: Utility Commands (10+ commands)

```csharp
CommandID cmdGenerateCommitMessage;
CommandID cmdTakeHeapSnapshot;
CommandID cmdOpenMigrationWizard;
CommandID cmdShowDiffViewer;
// ... plus code actions, terminal actions
```

---

### Phase 5: State Persistence & Serialization

#### Task 5.1: Implement State Persistence Service

**New File**: `Services/StatePersistenceService.cs`

**Purpose**: Replace VS Code's `registerWebviewPanelSerializer()` with file-based persistence

**Methods**:
```csharp
public Task SavePanelStateAsync(string panelType, string sessionId, object state);
public Task<object?> LoadPanelStateAsync(string panelType, string sessionId);
public Task SaveWorktreeStateAsync(WorktreeStateManager state);
public Task<WorktreeStateManager?> LoadWorktreeStateAsync();
```

**Storage Location**: `.kilo/agent-manager.json`

**Panels to Support**:
- Agent Manager
- KiloClaw
- Tab panels
- Settings
- Marketplace
- Diff Viewer

---

#### Task 5.2: Implement Panel Restore Logic

**Modify**: Each provider's constructor/initialization

**Pattern**:
```csharp
public async Task InitializeAsync()
{
    var state = await _statePersistence.LoadPanelStateAsync(GetPanelType(), SessionId);
    if (state != null)
    {
        RestoreFromState(state);
    }
}
```

---

### Phase 6: URI Handlers (Deep Links)

#### Task 6.1: Implement URI Handler

**New File**: `Services/KiloUriHandler.cs`

**Methods**:
```csharp
public void Register();
public async Task HandleUriAsync(string uri);
```

**Supported URLs**:
- `kilocode://kilocode/s/{sessionId}` → Open cloud session
- `kilocode://kilocode/switch?model=X&agent=Y` → Switch model/agent

**Integration**: Register in `KiloVisualStudioExtensionPackage.InitializeAsync()`

---

### Phase 7: Additional Services

#### Task 7.1: RemoteStatusService (Complete)

**Status**: Partial implementation exists

**Missing**:
- Status bar UI item
- Toggle command handler
- VS Code pattern matching

---

#### Task 7.2: BrowserAutomationService

**New File**: `Services/BrowserAutomationService.cs`

**Purpose**: Playwright MCP server registration

**Methods**:
- `RegisterPlaywrightMCP()`
- `EnableSync()` / `DisableSync()`
- `ReregisterOnReconnect()`

---

#### Task 7.3: AttentionService

**New File**: `Services/AttentionService.cs`

**Purpose**: Approve requests with attention

**Methods**:
- `ApproveWithAttention(PermissionRequest)`
- `Dispose()` on extension deactivation

---

#### Task 7.4: AutocompleteServiceManager

**New File**: `Services/AutocompleteServiceManager.cs`

**Purpose**: Register autocomplete provider

**Methods**:
- `RegisterAutocompleteProvider()`
- `EnsureBackendForAutocomplete()`
- `LoadAutocompleteService()`
- `RefreshAutocompleteService()`

---

#### Task 7.5: CommitMessageService

**New File**: `Services/CommitMessageService.cs`

**Purpose**: Generate commit messages from session context

**Methods**:
- `GenerateCommitMessage(Session)`
- Register command handler

---

#### Task 7.6: HeapSnapshotService

**New File**: `Services/HeapSnapshotService.cs`

**Purpose**: Capture heap snapshot for debugging

**Methods**:
- `CaptureHeapSnapshot()`
- Register command handler

---

## Validation Plan

### Unit Tests

1. **Run existing test suite**:
   ```bash
   dotnet test packages\kilo-visualstudio\KiloVisualStudioExtension.Tests\
   ```

2. **Verify architectural tests pass**:
   - `AgentManagerArchTests.cs`
   - `AbortTests.cs`
   - All existing contract tests

3. **Add new tests for**:
   - Each new message handler
   - Each new provider
   - State persistence/restore
   - URI handler routing

---

### Integration Tests (Manual)

#### Test 1: Message Protocol Parity
- Open VS Code and VS Extension side-by-side
- Perform same actions in both
- Compare message logs (use browser dev tools for VS Code, Debug output for VS)
- Verify message types, field names, and values match exactly

#### Test 2: Session Creation Flow
1. Create new session in VS Extension
2. Verify `sessionCreated` message structure matches VS Code
3. Send message, verify `messageCreated` structure
4. Stream response, verify `partUpdated` structure
5. Complete turn, verify `sessionStatus` and `sessionTurnClosed`

#### Test 3: Agent Manager Worktree
1. Create worktree session
2. Verify worktree created on disk (`git worktree list`)
3. Verify `.kilo/agent-manager.json` updated
4. Switch sessions, verify terminal switches
5. Close session, verify worktree cleanup option shown

#### Test 4: Marketplace Panel
1. Open marketplace panel
2. Verify marketplace data fetches
3. Install marketplace item
4. Verify item installed in `.kilo/` directory
5. Remove marketplace item
6. Verify item removed

#### Test 5: KiloClaw Chat
1. Open KiloClaw panel
2. Verify WebSocket connection established
3. Create conversation
4. Send message, verify optimistic update
5. Receive server response, verify message replaced
6. Add reaction, verify reaction added
7. Type message, verify typing indicator shown

#### Test 6: State Persistence
1. Open multiple panels (sidebar, Agent Manager, settings)
2. Create sessions, switch tabs, modify state
3. Restart Visual Studio
4. Verify all panels restore with correct state
5. Verify `.kilo/agent-manager.json` contains all state

#### Test 7: URI Handlers
1. Launch `kilocode://kilocode/s/{sessionId}` URL
2. Verify cloud session opens
3. Launch `kilocode://kilocode/switch?model=X&agent=Y` URL
4. Verify model/agent switches

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| **Message structure mismatch** | Critical | Test each message against VS Code logs; use JSON comparison tools |
| **SSE event filtering bugs** | High | Implement session tracking tests; verify events only sent for tracked sessions |
| **KiloClaw WebSocket complexity** | High | Implement incrementally; test connection, subscription, message flow separately |
| **Visual Studio API limitations** | Medium | Use VS SDK patterns; fallback to alternative approaches if needed |
| **Thread affinity violations** | High | Use `[MainThread]` annotations; wrap UI code with `SwitchToMainThreadAsync()` |
| **State persistence data loss** | High | Implement versioned state schema; add migration logic for old formats |
| **Command registration conflicts** | Medium | Use unique command IDs; test each command independently |

---

## Success Criteria

1. **Message Protocol**: All 40+ missing message handlers implemented and tested
2. **Provider Parity**: All 6 missing providers fully implemented (not stubs)
3. **Command Coverage**: 80+ commands registered and functional
4. **Data Structures**: Session, serverInfo, and all message structures match VS Code 100%
5. **SSE Events**: All SSE events properly transformed and filtered by session
6. **State Persistence**: Panel state survives Visual Studio restart
7. **URI Handlers**: Deep links work for cloud sessions and model switching
8. **Tests**: All architectural tests pass; new tests added for new functionality
9. **Documentation**: All code extensively documented with XML comments
10. **No TODOs**: Zero TODO comments in codebase

---

## Deployment Strategy

Deploy in phases, each phase is a releasable milestone:

1. **Phase 0 Deploy**: Critical bug fixes (serverInfo, session structure, SSE transformer)
2. **Phase 1 Deploy**: Verify infrastructure (singleton, lazy startup, factory)
3. **Phase 2 Deploy**: Core message handlers (session management, UI interactions)
4. **Phase 3 Deploy**: Sub-Agent Viewer (small, isolated feature)
5. **Phase 4 Deploy**: Marketplace Panel (medium complexity)
6. **Phase 5 Deploy**: Agent Manager completion (worktree management, terminals)
7. **Phase 6 Deploy**: KiloClaw (highest complexity - deploy with caution)
8. **Phase 7 Deploy**: Remaining services and commands (polish)

Each deployment should include:
- Unit tests for new functionality
- Manual integration testing
- Comparison with VS Code behavior
- Rollback plan if critical issues found

---

## Open Questions

1. **KiloClaw Dependencies**: Does KiloClaw require backend features not yet implemented? Verify `/kilo/claw/status` endpoint exists before starting Task 3.2.

2. **Playwright MCP**: Does the backend support Playwright MCP server? Verify before implementing `BrowserAutomationService`.

3. **Visual Studio Limitations**: Are there VS SDK limitations that prevent 1:1 parity (e.g., no URI handler support)? Research before starting Phase 6.

4. **Test Infrastructure**: Are there existing test utilities for mocking webview messages, or do we need to create them?

5. **State Schema Versioning**: What versioning strategy for `.kilo/agent-manager.json` to handle future schema changes?

---

## Estimated Scope

**Total Lines of Code to Write**: ~8,000-10,000 lines C#

**Breakdown**:
- Phase 0 (Bug Fixes): 200 lines
- Phase 2 (Message Handlers): 3,000 lines
- Phase 3 (Providers): 4,000 lines (KiloClaw = 1,200 lines alone)
- Phase 4 (Commands): 500 lines (registration boilerplate)
- Phase 5 (State Persistence): 400 lines
- Phase 6 (URI Handlers): 200 lines
- Phase 7 (Services): 1,500 lines

**Timeline**: This represents approximately 3-4 months of focused development for a single developer familiar with the codebase.

---

*Plan created: Aug 2 2026*  
*Based on MESSAGE_FLOWS.md analysis and original migration plan*  
*Ready for implementation*