# Message Flow Analysis: VS Code vs Visual Studio Extension

## Executive Summary

This report provides a comprehensive analysis of message flows between the webview and extension backend in both the VS Code extension (`packages/kilo-vscode/`) and the Visual Studio extension (`packages/kilo-visualstudio/`). The analysis identifies critical gaps in message handler implementations, data structure mismatches, and missing providers that prevent feature parity between the two extensions.

**Key Finding**: The VS extension has fundamental message protocol mismatches (e.g., `Port` vs `port` in serverInfo) and lacks 40+ message handlers that exist in VS Code, causing silent message drops and incomplete functionality.

---

## 1. Message Protocol Mismatches

### 1.1 ServerInfo Field Name Mismatch

**VS Code** (`KiloProvider.ts:655-663`):
```typescript
this.postMessage({
  type: "ready",
  serverInfo,  // { port: number }
  extensionVersion,
  vscodeLanguage,
  languageOverride,
  workspaceDirectory
})
```

**VS Extension** (`VSProvider.cs:613-622`):
```csharp
var readyMessage = new 
{ 
    type = "ready",
    serverInfo,  // { Port: number } - WRONG!
    extensionVersion,
    vscodeLanguage,
    languageOverride,
    workspaceDirectory
}
```

**Impact**: Webview expects `serverInfo.port` but receives `serverInfo.Port`, causing connection state confusion.

**Fix Required**: Change `serverInfo` property name from `Port` to `port` in `KiloConnectionService.GetServerInfo()`.

---

### 1.2 Missing `extensionDataReady` Message Timing

**VS Code**: Sends `extensionDataReady` after all initial data loads complete (providers, agents, skills, commands, config, etc.)

**VS Extension** (`VSProvider.cs:678-679`):
```csharp
var extensionReady = new { type = "extensionDataReady" };
_webView.PostMessage(JsonSerializer.Serialize(extensionReady));
```

**Problem**: Sent unconditionally in `syncWebviewState`, but VS Code only sends it after all cached data is populated. The log shows VS extension sends it immediately without waiting for data loads.

---

## 2. Missing Message Handlers in VS Provider

The following message types from VS Code's `handleWebviewMessage()` switch statement are **completely missing** from VS Provider's `ProcessMessageAsync()`:

### 2.1 Session Management (Critical)

| VS Code Message | VS Extension Status | Impact |
|----------------|---------------------|--------|
| `loadSessions` | ❌ Missing | Cannot load session history |
| `syncSession` | ❌ Missing | Cannot sync session state |
| `requestSessionModelUsage` | ❌ Missing | Cannot display model usage |
| `revertSession` | ❌ Missing | Cannot revert to snapshot |
| `unrevertSession` | ❌ Missing | Cannot unrevert session |
| `compact` | ❌ Missing | Cannot compact context |

### 2.2 Agent Manager Commands (Critical)

| VS Code Message | VS Extension Status | Impact |
|----------------|---------------------|--------|
| `agentManager.createWorktree` | ❌ Missing | Cannot create worktree sessions |
| `agentManager.deleteWorktree` | ❌ Missing | Cannot delete worktrees |
| `agentManager.promoteSession` | ❌ Missing | Cannot promote session to worktree |
| `agentManager.forkSession` | ❌ Missing | Cannot fork sessions |
| `agentManager.openLocally` | ❌ Missing | Cannot open session locally |
| `agentManager.requestState` | ❌ Missing | Cannot request Agent Manager state |
| `agentManager.setTabOrder` | ❌ Missing | Cannot reorder tabs |
| `agentManager.showTerminal` | ❌ Missing | Cannot show session terminal |
| `agentManager.requestWorktreeDiff` | ❌ Missing | Cannot request worktree diff |

### 2.3 Marketplace & KiloClaw (High Priority)

| VS Code Message | VS Extension Status | Impact |
|----------------|---------------------|--------|
| `openMarketplacePanel` | ❌ Missing | Cannot open marketplace |
| `fetchMarketplaceData` | ❌ Missing | Marketplace panel non-functional |
| `installMarketplaceItem` | ❌ Missing | Cannot install marketplace items |
| `removeInstalledMarketplaceItem` | ❌ Missing | Cannot remove marketplace items |
| `kiloclaw.ready` | ❌ Missing | KiloClaw panel non-functional |
| `kiloclaw.selectConversation` | ❌ Missing | Cannot select conversations |
| `kiloclaw.sendMessage` | ❌ Missing | Cannot send messages in KiloClaw |

### 2.4 Sub-Agent Viewer (High Priority)

| VS Code Message | VS Extension Status | Impact |
|----------------|---------------------|--------|
| `openSubAgentViewer` | ❌ Missing | Cannot open sub-agent viewer |
| `viewSubAgentSession` | ❌ Missing | Cannot view sub-agent sessions |
| `closePanel` | ❌ Missing | Cannot close viewer panels |

### 2.5 Configuration & Settings (Medium Priority)

| VS Code Message | VS Extension Status | Impact |
|----------------|---------------------|--------|
| `requestGlobalConfig` | ✅ Partial | Implemented but may lack features |
| `requestSkills` | ✅ Implemented | Working |
| `requestCommands` | ✅ Implemented | Working |
| `removeSkill` | ❌ Missing | Cannot remove skills |
| `removeAgent` | ❌ Missing | Cannot remove agents |
| `openIndexingSettings` | ❌ Missing | Cannot open indexing settings |

### 2.6 UI & Interaction (Medium Priority)

| VS Code Message | VS Extension Status | Impact |
|----------------|---------------------|--------|
| `saveImage` | ❌ Missing | Cannot save images |
| `previewImage` | ❌ Missing | Cannot preview images |
| `openExternal` | ❌ Missing | Cannot open external URLs |
| `forkSession` | ❌ Missing | Cannot fork sessions |
| `cycleAgentMode` | ❌ Missing | Cannot cycle agent modes |
| `toggleMemory` | ❌ Missing | Cannot toggle memory |
| `showMemory` | ❌ Missing | Cannot show memory panel |

### 2.7 Provider & Model Management (Medium Priority)

| VS Code Message | VS Extension Status | Impact |
|----------------|---------------------|--------|
| `connectProvider` | ❌ Missing | Cannot connect providers |
| `authorizeProviderOAuth` | ❌ Missing | Cannot authorize OAuth |
| `completeProviderOAuth` | ❌ Missing | Cannot complete OAuth flow |
| `disconnectProvider` | ❌ Missing | Cannot disconnect providers |
| `saveCustomProvider` | ❌ Missing | Cannot save custom providers |
| `fetchCustomProviderModels` | ❌ Missing | Cannot fetch custom provider models |

### 2.8 Legacy Migration (Low Priority)

| VS Code Message | VS Extension Status | Impact |
|----------------|---------------------|--------|
| `requestMigrationData` | ❌ Missing | Legacy migration non-functional |
| `startMigration` | ❌ Missing | Cannot start migration |
| `finalizeLegacyMigration` | ❌ Missing | Cannot finalize migration |
| `skipLegacyMigration` | ❌ Missing | Cannot skip migration |

---

## 3. Data Structure Differences

### 3.1 ServerInfo Structure

**VS Code**:
```typescript
serverInfo: { port: number }
```

**VS Extension** (`KiloConnectionService.cs`):
```csharp
public struct ServerInfo { public int Port; }  // Capital P!
```

**Impact**: JSON serialization produces `{ "Port": 58860 }` instead of `{ "port": 58860 }`, breaking webview expectations.

### 3.2 Session Structure

**VS Code** (`KiloProvider.ts:780-786`):
```typescript
{
  type: "sessionCreated",
  session: {
    id: string,
    parentID: string | null,
    title: string,
    createdAt: string,
    updatedAt: string,
    revert: { messageID, snapshot, diff, workspace } | null,
    summary: { additions, deletions, files, diffs } | null
  },
  draftID?: string
}
```

**VS Extension** (`VSProvider.cs:968-984`):
```csharp
var sessionCreated = new 
{ 
    type = "sessionCreated",
    session = new 
    { 
        id = sessionID,
        directory = dir,  // WRONG field!
        title = "New Chat",
        updated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        status = "idle"
    }
}
```

**Missing Fields**: `parentID`, `createdAt`, `updatedAt`, `revert`, `summary`  
**Wrong Fields**: `directory` (should not be here), `updated` (should be `updatedAt`), `status` (should be in separate event)

### 3.3 Message Structure

**VS Code** (`handleSendMessage` → `message.part.created`):
```typescript
{
  id: string,
  sessionID: string,
  role: "user" | "assistant",
  time: { created: number, completed?: number },
  editorContext?: { visibleFiles, openTabs, activeFile, shell },
  parts: Array<{ type, text, reasoning, citations, tools }>
}
```

**VS Extension** (`SessionHandlerService.cs` - inferred from logs):
```csharp
// Based on logs, sends:
{
  type: "messageCreated",
  message: {
    id: string,
    sessionID: string,
    role: string,
    time: { created: number }
    // Missing: editorContext, parts array structure
  }
}
```

**Missing**: `editorContext`, proper `parts` array structure with type discrimination

### 3.4 Provider Data Structure

**VS Code** (`fetchAndSendProviders`):
```typescript
{
  type: "providersLoaded",
  providers: {
    [providerID]: {
      id: string,
      name: string,
      source: string,
      env: string[],
      metadata: { icon?: string },
      options: object,
      models: {
        [modelID]: {
          id: string,
          providerID: string,
          api: { id, url, npm },
          name: string,
          family: string,
          capabilities: { temperature, reasoning, attachment, toolcall, input, output },
          cost: { input, output, cache },
          limit: { context, output },
          status: string,
          variants: { low, medium, high }
        }
      }
    }
  }
}
```

**VS Extension** (`ProviderRequestService.cs`):
```csharp
// Based on logs, sends simplified structure:
{
  type: "providersLoaded",
  providers: {
    [providerID]: {
      id: string,
      name: string,
      // Likely missing: env, metadata, options, full model structure
    }
  }
}
```

**Impact**: Webview may not render provider options correctly, model capabilities may be incomplete.

---

## 4. Provider Implementation Gaps

### 4.1 MarketplacePanelProvider

**VS Code** (`MarketplacePanelProvider.ts:1-338`):
- Full implementation with 338 lines
- Handles marketplace data fetch, install, remove operations
- Subscribes to SSE events for session status
- Implements panel serialization/deserialization

**VS Extension**: ❌ **Not Implemented**

**Required**:
```csharp
public class MarketplacePanelProvider : IDisposable
{
    public void OpenPanel(string? directory);
    public void DeserializePanel(KiloToolWindow panel);
    // + 20+ message handlers for marketplace operations
}
```

### 4.2 KiloClawProvider

**VS Code** (`KiloClawProvider.ts:1-1296`):
- Full implementation with 1296 lines
- Manages KiloChatClient and EventServiceClient
- Handles conversation/message/reaction operations
- Implements WebSocket event subscriptions
- Real-time typing indicators and bot status

**VS Extension** (`KiloClawProvider.cs:1-83`):
```csharp
// STUB IMPLEMENTATION - 83 lines
public class KiloClawProvider : IDisposable
{
    public void OpenPanel() { /* Shows empty tool window */ }
    // No message handlers, no chat client, no event service
}
```

**Missing**:
- KiloChatClient implementation
- EventServiceClient implementation  
- All 20+ KiloClaw message handlers
- WebSocket event subscription logic
- Conversation/message state management

### 4.3 SubAgentViewerProvider

**VS Code** (`SubAgentViewerProvider.ts:1-98`):
```typescript
export class SubAgentViewerProvider implements vscode.Disposable {
  openPanel(sessionID: string, title?: string): void {
    const provider = new KiloProvider(this.extensionUri, this.connectionService, this.context);
    provider.trackSession(sessionID);
    provider.resolveWebviewPanel(panel);
    // Loads messages, registers session
  }
}
```

**VS Extension**: ❌ **Not Implemented**

**Required**:
```csharp
public class SubAgentViewerProvider : IDisposable
{
    public void OpenPanel(string sessionID, string? title);
    // Tracks session, loads messages, displays read-only transcript
}
```

### 4.4 DiffViewerProvider

**VS Code**: Full implementation with diff rendering, comment handling

**VS Extension**: ❌ **Not Implemented**

### 4.5 DiffVirtualProvider

**VS Code**: Single-file diff for permission approval preview

**VS Extension**: ❌ **Not Implemented**

### 4.6 AgentManagerProvider

**VS Code** (`AgentManagerProvider.ts:1-2000+`):
- Multi-session orchestration panel
- Worktree management (create, delete, promote, fork)
- Terminal integration per session
- Diff viewer integration
- Setup script execution
- State persistence via `.kilo/agent-manager.json`

**VS Extension** (`AgentManagerProvider.cs:1-296`):
```csharp
// SIMPLIFIED IMPLEMENTATION - 296 lines
public class AgentManagerProvider : IDisposable
{
    public void OpenPanel();
    public void PostMessage(object message);
    public void RegisterSession(string sessionId, string directory);
    public void CloseSessionAsync(string sessionId);
    // Basic session navigation (next/previous/jump)
    // MISSING: Worktree management, terminal integration, diff integration
}
```

**Missing**:
- WorktreeManager implementation
- SetupScriptService/SetupScriptRunner
- SessionTerminalManager (partial implementation exists but not integrated)
- WorktreeDiffController
- GitOps, GitStatsPoller, PRStatusBridge
- Multi-version worktree creation
- State recovery/restore logic

---

## 5. Command Registration Gaps

### 5.1 VS Code Commands (80+)

From `extension.ts` registration:
```typescript
// Core commands
registerCommand('newTask')
registerCommand('historyButtonClicked')
registerCommand('settingsButtonClicked')
registerCommand('profileButtonClicked')
registerCommand('showChanges')
registerCommand('reload')

// Agent Manager (30+)
registerCommand('agentManagerOpen')
registerCommand('agentManager.showTerminal')
registerCommand('agentManager.runScript')
registerCommand('agentManager.toggleDiff')
registerCommand('agentManager.newTab')
registerCommand('agentManager.closeTab')
registerCommand('agentManager.newWorktree')
registerCommand('agentManager.quickWorktree')
registerCommand('agentManager.openWorktree')
registerCommand('agentManager.closeWorktree')
registerCommand('agentManager.openPR')
registerCommand('agentManager.advancedWorktree')
registerCommand('agentManager.jumpTo1')
// ... jumpTo2-9
registerCommand('agentManager.previousSession')
registerCommand('agentManager.nextSession')
registerCommand('agentManager.previousTab')
registerCommand('agentManager.nextTab')
registerCommand('agentManager.search')

// Marketplace
registerCommand('marketplaceButtonClicked')

// KiloClaw
registerCommand('kiloClawOpen')

// Sub-agent viewer
registerCommand('openSubAgentViewer')

// Diff viewer
registerCommand('showDiffViewer')

// Settings panels
registerCommand('openSettingsPanel')
registerCommand('openIndexingSettings')

// Memory
registerCommand('toggleMemory')
registerCommand('showMemory')

// Remote status
registerCommand('toggleRemote')

// Commit message
registerCommand('generateCommitMessage')

// Heap snapshot
registerCommand('takeHeapSnapshot')
```

### 5.2 VS Extension Commands (2)

From `Guids.cs` and `KiloToolbarCommands.cs`:
```csharp
// Only 2 commands registered
const int cmdShowKiloWindow = 0x0100;
const int cmdOpenSettings = 0x0101;
```

**Missing**: 78+ commands including all Agent Manager, Marketplace, KiloClaw, Sub-agent viewer commands

---

## 6. SSE Event Handling Differences

### 6.1 VS Code SSE Handling

**File**: `KiloProvider.ts:260-293` (unwrapSyncEvent)

```typescript
export function unwrapSyncEvent(event: SSEPayload | RawSyncPayload): ProviderEvent | undefined {
  if (event.type !== "sync") return event
  const payload = "syncEvent" in event ? normalize(event) : event

  switch (payload.name) {
    case "message.updated.1":
      return { id: payload.id, type: "message.updated", properties: payload.data }
    case "message.removed.1":
      return { id: payload.id, type: "message.removed", properties: payload.data }
    case "message.part.updated.1":
      return { id: payload.id, type: "message.part.updated", properties: payload.data }
    case "message.part.removed.1":
      return { id: payload.id, type: "message.part.removed", properties: payload.data }
    case "session.created.1":
      return { id: payload.id, type: "session.created", properties: payload.data }
    case "session.updated.1":
      return { source: "sync", id: payload.id, seq: payload.seq, type: "session.updated", properties: payload.data }
    case "session.deleted.1":
      return { id: payload.id, type: "session.deleted", properties: payload.data }
    default:
      return undefined
  }
}
```

**Key Features**:
- Legacy sync event transformation
- Proper event type mapping
- Session tracking via `trackedSessionIds`
- Session-scoped event filtering

### 6.2 VS Extension SSE Handling

**File**: `SSEHelper.cs` + `VSProvider.cs:1020-1028`

```csharp
private void HandleSseEvent(object? sender, SseEventReceivedEventArgs e)
{
    _sseHelper.HandleEvent(e.EventType, e.Data);
}
```

**Problem**: Events are passed to `SSEHelper` but not transformed into webview messages. The VS Code pattern of `unwrapSyncEvent` → `mapSSEEventToWebviewMessage` → `postMessage` is missing.

**Missing**:
- Sync event unwrapping
- Event-to-webview-message mapping
- Session filtering (only send events for tracked sessions)
- Legacy event transformation

---

## 7. State Persistence & Serialization

### 7.1 VS Code Approach

**File**: `extension.ts` + provider `deserializePanel()` methods

```typescript
// Register serializers for each panel type
vscode.window.registerWebviewPanelSerializer(KiloProvider.viewType, {
  async deserializeWebviewPanel(panel, state) {
    const provider = new KiloProvider(...);
    provider.deserializePanel(panel, state);
  }
});
```

**State stored**: Panel visibility, session IDs, worktree state, user preferences

### 7.2 VS Extension Approach

**Current**: No serialization support

**Problem**: Visual Studio WebView2 does not have VS Code's `registerWebviewPanelSerializer()` API.

**Proposed** (per plan file Task 5.7):
```csharp
// Use state persistence via .kilo/agent-manager.json
public class StatePersistenceService
{
    public void SavePanelState(string panelType, string sessionId, object state);
    public Task<object?> LoadPanelState(string panelType, string sessionId);
}
```

**Status**: ❌ **Not Implemented**

---

## 8. URI Handlers (Deep Links)

### 8.1 VS Code Implementation

**File**: `extension.ts` + `handleUri()`

```typescript
const disposable = vscode.window.registerUriHandler({
  async handleUri(uri: vscode.Uri) {
    if (uri.path === '/kilocode/s') {
      // Open cloud session
      const sessionId = uri.query;
      await openCloudSession(sessionId);
    } else if (uri.path === '/kilocode/switch') {
      // Switch model/agent
      const { model, agent } = querystring.parse(uri.query);
      await selectKiloModel(model, agent);
    }
  }
});
```

**Supported URLs**:
- `kilocode://kilocode/s/{sessionId}` - Open cloud session
- `kilocode://kilocode/switch?model=X&agent=Y` - Switch model/agent

### 8.2 VS Extension Implementation

**Status**: ❌ **Not Implemented**

**Required**:
```csharp
public class KiloUriHandler : IDisposable
{
    public void Register();
    public Task HandleUriAsync(string uri);
}
```

---

## 9. Summary of Critical Gaps

| Category | Count | Severity |
|----------|-------|----------|
| Missing Message Handlers | 40+ | Critical |
| Missing Providers | 6 | Critical |
| Command Registration Gaps | 78+ | Critical |
| Data Structure Mismatches | 5+ | High |
| SSE Event Handling Gaps | 3 | High |
| State Persistence | 0/6 panels | High |
| URI Handlers | 0/2 | Medium |

---

## 10. Recommendations

### Immediate (Blockers)

1. **Fix serverInfo field name**: Change `Port` → `port` in `KiloConnectionService.ServerInfo`
2. **Add missing session fields**: Implement proper session structure with `parentID`, `createdAt`, `updatedAt`, `revert`, `summary`
3. **Implement message handler registry**: Create `MessageHandlerRegistry` to route all 40+ missing message types
4. **Fix SSE event handling**: Implement `unwrapSyncEvent` equivalent and session filtering

### Short-term (High Priority)

5. **Implement MarketplacePanelProvider**: Full marketplace browsing and installation
6. **Implement SubAgentViewerProvider**: Read-only sub-agent session viewer
7. **Complete AgentManagerProvider**: Worktree management, terminal integration, diff integration
8. **Add command registration**: Register all 78+ missing commands

### Medium-term (Medium Priority)

9. **Implement KiloClawProvider**: Full chat panel with WebSocket event subscription
10. **Add state persistence**: Implement `.kilo/agent-manager.json` based state storage
11. **Implement URI handlers**: Support deep links for cloud sessions
12. **Add DiffViewerProvider & DiffVirtualProvider**: Diff rendering for permissions/reviews

### Long-term (Low Priority)

13. **Legacy migration support**: Implement migration from old extension settings
14. **Advanced features**: Commit message generation, heap snapshot, browser automation

---

## 11. Testing Recommendations

### Message Flow Tests

1. **Session creation flow**: Verify `sessionCreated` → `messageCreated` → `partUpdated` → `sessionUpdated` sequence
2. **Session switching**: Verify `loadMessages` with `mode=replace` aborts previous loads
3. **SSE event filtering**: Verify events only sent for tracked sessions
4. **Stream coalescing**: Verify `SessionStreamScheduler` throttles background sessions

### Integration Tests

5. **Provider creation**: Verify all providers share single `KiloConnectionService` instance
6. **Backend lazy startup**: Verify backend only starts on first provider connection
7. **State persistence**: Verify panel state survives Visual Studio restart
8. **Command execution**: Verify all 80+ commands execute without errors

---

## 12. Conclusion

The Visual Studio extension has made significant progress in core infrastructure (shared connection service, lazy backend startup, handler services architecture) but lacks **feature parity** with the VS Code extension. The 40+ missing message handlers and 6 missing providers represent approximately **60-80 hours** of implementation work to achieve full parity.

The most critical gaps are:
1. **Message protocol mismatches** (serverInfo field names, session structure)
2. **Missing Agent Manager integration** (worktree management, terminal, diff)
3. **Missing Marketplace and KiloClaw providers**
4. **Incomplete command registration** (only 2/80+ commands)

Addressing these gaps will require systematic implementation following the VS Code source of truth, with careful attention to message structure, event handling, and state management patterns.

---

*Report generated: Aug 2 2026*  
*Based on analysis of VS Code commit history and VS extension current state*
