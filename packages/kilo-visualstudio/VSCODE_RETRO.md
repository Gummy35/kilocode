# VS Code Extension Workflow Analysis

This document provides a comprehensive analysis of the Kilo VS Code extension's message flow, session management, and webview communication patterns. It serves as the reference for implementing equivalent functionality in the Visual Studio extension.

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Message Flow: Extension → Webview](#message-flow-extension--webview)
3. [Message Flow: Webview → Extension](#message-flow-webview--extension)
4. [Session Lifecycle](#session-lifecycle)
5. [SSE Event Handling](#sse-event-handling)
6. [Session Tracking](#session-tracking)
7. [Message Loading & Switching](#message-loading--switching)
8. [State Management](#state-management)
9. [Known Gaps in Visual Studio Extension](#known-gaps-in-visual-studio-extension)

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    VS Code Extension                         │
│  ┌─────────────────┐         ┌─────────────────────────┐    │
│  │   KiloProvider  │◄───────►│   KiloConnectionService │    │
│  │   (Webview)     │         │   (CLI Backend Client)  │    │
│  └────────┬────────┘         └───────────┬─────────────┘    │
│           │                              │                   │
│           │ postMessage()                │ SSE               │
│           ▼                              ▼                   │
│  ┌─────────────────┐         ┌─────────────────────────┐    │
│  │    Webview UI   │         │   kilo serve (localhost)│    │
│  │   (SolidJS)     │         │   (HTTP + SSE Server)   │    │
│  └─────────────────┘         └─────────────────────────┘    │
└─────────────────────────────────────────────────────────────┘
```

### Key Components

| Component | Purpose |
|-----------|---------|
| `KiloProvider` | Main extension class managing webview, session state, and message routing |
| `KiloConnectionService` | Manages CLI backend connection, HTTP client, and SSE subscription |
| `SSEHelper` (VS) | Parses SSE events and forwards to webview (Visual Studio only) |
| `SessionStreamScheduler` | Throttles part updates for performance |

---

## Message Flow: Extension → Webview

### Initial Load Sequence

1. **Webview loads** → `window.postMessage({ type: 'getState' }, '*')`
2. **Extension receives** → `webviewReady` handler triggers `syncWebviewState("webviewReady")`
3. **Connection state sent** → `{ type: "connectionState", state: "connecting" | "connected" | "disconnected" | "error" }`
4. **Ready message sent** → `{ type: "ready", serverInfo, extensionVersion, vscodeLanguage, workspaceDirectory }`
5. **Profile data sent** → `{ type: "profileData", data: {...} }` (if logged in)
6. **Cached data sent in parallel**:
   - `{ type: "providersLoaded", providers, connected, defaults, ... }`
   - `{ type: "agentsLoaded", agents, allAgents, defaultAgent }`
   - `{ type: "configLoaded", config, features }`
   - `{ type: "mcpStatusLoaded", status }`
   - `{ type: "skillsLoaded", skills }`
   - `{ type: "commandsLoaded", commands }`
   - `{ type: "indexingStatusLoaded", status }`
   - `{ type: "notificationsLoaded", notifications }`
   - `{ type: "workStyleLoaded", style }`
7. **Extension data ready** → `{ type: "extensionDataReady" }`
8. **Stored state restored** → `{ type: "setState", state: {...} }` (if exists)

### Session-Related Messages

| Message Type | Payload | When Sent |
|--------------|---------|-----------|
| `sessionCreated` | `{ session: {...}, draftID?: string }` | New session created |
| `sessionUpdated` | `{ session: {...} }` | Session metadata changed (rename, status, etc.) |
| `sessionDeleted` | `{ sessionID: string }` | Session deleted |
| `sessionStatus` | `{ sessionID, status, attempt?, message?, next? }` | Session status changed (streaming, idle, retry, etc.) |
| `sessionTurnClosed` | `{ sessionID, reason: "completed" | "error" | "interrupted" }` | AI turn finished |
| `sessionError` | `{ sessionID?, error? }` | Session error occurred |
| `messagesLoaded` | `{ sessionID, messages, mode, cursor?, hasMore, since? }` | Messages loaded for session |
| `messageCreated` | `{ message: {...} }` | New message created (via SSE) |
| `messageRemoved` | `{ sessionID, messageID }` | Message deleted |
| `partUpdated` | `{ sessionID, messageID, part, delta? }` | Part content updated (streaming) |
| `partRemoved` | `{ sessionID, messageID, partID }` | Part removed |

### Other Extension → Webview Messages

| Message Type | Purpose |
|--------------|---------|
| `connectionState` | Connection status changes |
| `error` | Error with title/message |
| `gitStatus` | Git repository status |
| `workspaceDirectoryChanged` | Working directory changed |
| `languageChanged` | VS Code language changed |
| `fontSizeChanged` | Font size setting changed |
| `permissionRequest` | Permission required (file access, etc.) |
| `permissionResolved` | Permission granted/denied |
| `questionRequest` | AI asking user questions |
| `questionResolved` | User answered questions |
| `suggestionRequest` | AI suggesting actions |
| `suggestionResolved` | Suggestion accepted/dismissed |
| `todoUpdated` | Task list updated |
| `sandboxStatus` | Sandbox availability/status |
| `remoteStatus` | Remote status service state |
| `telemetryState` | Telemetry enabled/disabled state |
| `memoryLoaded` | Session memory data |
| `memoryEvent` | Memory operation result |

---

## Message Flow: Webview → Extension

### Webview Initialization

1. **`getState`** → Request stored state (for recovery after reload)
2. **`webviewReady`** → Signal webview is ready, triggers extension data push

### Session Management

| Message Type | Payload | Extension Handler |
|--------------|---------|-------------------|
| `createSession` | `{ directory?: string }` | `handleCreateSession()` |
| `clearSession` | none | `clearSession` case: stops processes, clears context, focuses |
| `deleteSession` | `{ sessionID }` | `handleDeleteSession(sessionID)` |
| `renameSession` | `{ sessionID, title }` | `handleRenameSession(sessionID, title)` |
| `syncSession` | `{ sessionID, parentSessionID? }` | `handleSyncSession(sessionID, parentSessionID)` |
| `loadSessions` | none | `handleLoadSessions()` |
| `loadMessages` | `{ sessionID, mode?, before?, limit? }` | `handleLoadMessages(sessionID, options)` |
| `abort` | `{ sessionID? }` | `handleAbort(sessionID)` |
| `revertSession` | `{ sessionID, messageID, partID? }` | `handleRevertSession(...)` |
| `unrevertSession` | `{ sessionID }` | `handleUnrevertSession(sessionID)` |

### Messaging

| Message Type | Payload | Extension Handler |
|--------------|---------|-------------------|
| `sendMessage` | `{ text, sessionID?, messageID?, files?, review?, ... }` | `handleSendMessage(...)` |
| `sendCommand` | `{ command, arguments, sessionID?, ... }` | `handleSendCommand(...)` |

### Configuration & State

| Message Type | Payload | Extension Handler |
|--------------|---------|-------------------|
| `requestProviders` | none | `fetchAndSendProviders()` |
| `requestAgents` | none | `fetchAndSendAgents()` |
| `requestConfig` | none | `fetchAndSendConfig()` |
| `requestMcpStatus` | none | `fetchAndSendMcpStatus()` |
| `requestSkills` | none | `fetchAndSendSkills()` |
| `requestCommands` | none | `fetchAndSendCommands()` |
| `requestIndexingStatus` | none | `fetchAndSendIndexingStatus()` |
| `requestNotifications` | none | `fetchAndSendNotifications()` |
| `requestWorkStyle` | none | `fetchAndSendWorkStyle()` |
| `requestRecents` | none | Return cached recents from globalState |
| `requestFavorites` | none | Return cached favorites |
| `requestVariants` | none | Return cached variant selections |
| `requestModelSelections` | none | Return model selections |
| `persistRecents` | `{ recents }` | Update globalState |
| `persistVariant` | `{ key, value }` | Update variant selections |
| `setState` | `{ state }` | Store state for recovery |
| `getState` | none | Return stored state |

### Auth & Settings

| Message Type | Payload | Extension Handler |
|--------------|---------|-------------------|
| `login` | none | `handleLogin(authCtx, attempt)` |
| `logout` | none | `handleLogout(authCtx)` |
| `refreshProfile` | none | `handleRefreshProfile(authCtx)` |
| `setOrganization` | `{ organizationId }` | `handleSetOrganization(authCtx, organizationId)` |
| `openSettingsPanel` | `{ tab? }` | Execute settings command |
| `openConfigFile` | `{ scope, labels }` | `openConfig(scope, labels, directory)` |
| `updateSetting` | `{ key, value }` | `handleUpdateSetting(key, value)` |
| `updateConfig` | `{ scope, key, value }` | `handleUpdateConfig(...)` |

### MCP

| Message Type | Payload | Extension Handler |
|--------------|---------|-------------------|
| `connectMcp` | `{ name }` | `McpOAuth.connectMcpServer(...)` |
| `disconnectMcp` | `{ name }` | Disconnect MCP server |
| `removeMcp` | `{ name }` | `handleRemoveMcp(name)` |

### Other

| Message Type | Payload | Extension Handler |
|--------------|---------|-------------------|
| `retryConnection` | none | Reinitialize connection |
| `reload` | none | Reload webview |
| `saveImage` | `{ data, name }` | `saveImage(directory, message)` |
| `forkSession` | `{ sessionId, messageId }` | `handleForkSession(...)` |
| `compact` | `{ sessionID?, providerID?, modelID? }` | `handleCompact(...)` |
| `enhancePrompt` | `{ text }` | Enhance prompt via API |
| `dismissNotification` | `{ notificationId }` | `handleDismissNotification(...)` |
| `resetReadNotifications` | none | Reset notification read state |
| `telemetry` | `{ event, properties }` | Capture telemetry |
| `openSubAgentViewer` | `{ sessionID, title }` | Execute command |
| `openMarketplacePanel` | `{ directory? }` | Open marketplace |
| `requestFileSearch` | `{ query, requestId, sessionID? }` | Handle file search |
| `requestSessionSearch` | `{ requestId, sessionID? }` | Handle session search |
| `requestFilePicker` | `{ requestId }` | Handle file picker |
| `requestTerminalContext` | `{ requestId, sessionID? }` | Get terminal context |

---

## Session Lifecycle

### Session Creation

```typescript
// VS Code (handleCreateSession)
1. Check client connection
2. Get workspace directory
3. Get sandbox metadata
4. Call client.session.create({ directory, platform, metadata })
5. stopCurrentSessionProcesses(session.id)
6. setCurrentSession(session)
7. contextSessionID = session.id
8. focusSession(session.id)
9. trackDirectory(session.id, workspaceDir)
10. trackedSessionIds.add(session.id)
11. postMessage({ type: "sessionCreated", session: sessionToWebview(session) })
```

### Session Switching (loadMessages with mode="replace" or "focus")

```typescript
// VS Code (handleLoadMessages)
1. stopCurrentSessionProcesses(sessionID)
2. trackedSessionIds.add(sessionID)
3. focusSession(sessionID)
4. contextSessionID = sessionID
5. If mode === "focus":
   - refreshSessionDetails(sessionID, dir)
   - Reconcile tail (load messages with mode="reconcile")
6. If mode === "replace":
   - Abort previous load
   - refreshSessionDetails(sessionID, dir, abort.signal)
7. Fetch messages via fetchMessagePage()
8. Drop queued deltas: streams.drop(sessionID)
9. postMessage({ type: "messagesLoaded", sessionID, messages, mode, cursor, hasMore })
10. streams.flush(sessionID)
11. recoverPendingPrompts()
```

### Session Clearing

```typescript
// VS Code (clearSession case)
1. stopCurrentSessionProcesses()
2. contextSessionID = undefined
3. setCurrentSession(null)
4. focusSession()
```

### Session Deletion

```typescript
// VS Code (handleDeleteSession)
1. Check client connection
2. Get workspace directory
3. stopSessionProcesses(client, sessionID, workspaceDir)
4. Call client.session.delete({ sessionID, directory })
5. pruneDeletedSession(sessionID)
6. If deleting current session:
   - contextSessionID = undefined
   - setCurrentSession(null)
   - focusSession(undefined)
7. postMessage({ type: "sessionDeleted", sessionID })
```

### Session Aborting

```typescript
// VS Code (handleAbort)
1. Get sessionID (from payload or currentSession)
2. stopSession(sid) → calls client.backgroundProcess.stopSession()
3. sessionStatusMap.set(sid, "idle")
4. streams.flush(sid)
5. postMessage({ type: "sessionTurnClosed", sessionID: sid, reason: "interrupted" })
6. postMessage({ type: "sessionStatus", sessionID: sid, status: "idle" })
```

---

## SSE Event Handling

### Event Types & Mappings

| SSE Event | Webview Message | Handler |
|-----------|-----------------|---------|
| `session.created` | `sessionCreated` | `HandleSessionCreatedStream` |
| `session.updated` | `sessionUpdated` | `HandleSessionUpdatedStream` |
| `session.deleted` | `sessionDeleted` | `HandleSessionDeletedStream` |
| `session.status` | `sessionStatus` | `HandleSessionStatus` |
| `session.turn.close` | `sessionTurnClosed` | `HandleSessionTurnClosed` |
| `message.part.delta` | `partUpdated` | `HandlePartDelta` |
| `message.part.updated` | `partUpdated` | `HandlePartUpdatedSync` |
| `message.part.removed` | `partRemoved` | `HandlePartRemovedSync` |
| `message.updated` | `messageCreated` | `HandleMessageUpdatedSync` |
| `message.removed` | `messageRemoved` | `HandleMessageRemovedSync` |
| `permission.asked` | `permissionRequest` | Mapped in `mapSSEEventToWebviewMessage` |
| `permission.replied` | `permissionResolved` | Mapped |
| `question.asked` | `questionRequest` | Mapped |
| `question.replied` | `questionResolved` | Mapped |
| `suggestion.shown` | `suggestionRequest` | Mapped |
| `suggestion.accepted` | `suggestionResolved` | Mapped |
| `todo.updated` | `todoUpdated` | Mapped |
| `sandbox.status.changed` | `sandboxStatus` | Mapped |

### Session Tracking for SSE

```typescript
// SSE events are filtered by trackedSessionIds
// Only events for tracked sessions are forwarded to webview

// Sessions are tracked when:
// 1. Created via handleCreateSession()
// 2. Loaded via handleLoadMessages() with mode="replace" or "focus"
// 3. Synced via handleSyncSession() (child sessions)
// 4. Added via trackSession() (external tracking)

// Sessions are untracked when:
// 1. Deleted via handleDeleteSession()
// 2. Cleared via clearSession()
```

### Session Status Map

```typescript
// sessionStatusMap tracks current status of each session
// Updated via SSE events (session.status)
// Used to show streaming/idle/retry status in UI

sessionStatusMap: Map<sessionID, "idle" | "streaming" | "retry" | "offline" | ...>
```

---

## Session Tracking

### trackedSessionIds

```typescript
// Set of session IDs that should receive SSE events
// Used to filter SSE events in SSEHelper

trackedSessionIds.add(sessionID)   // Track a session
trackedSessionIds.delete(sessionID) // Untrack a session
trackedSessionIds.has(sessionID)   // Check if tracked
```

### contextSessionID

```typescript
// The "context" session - used for operations that need a directory
// Set when:
// - Session is created
// - Session is loaded (loadMessages with mode="replace" or "focus")
// - Session is synced (child session)

// Cleared when:
// - Session is cleared
// - Session is deleted (if it was the current session)
```

### currentSession

```typescript
// The currently active session object
// Contains full session metadata (title, directory, updated time, etc.)
// Updated when:
// - Session is created
// - Session is refreshed (refreshSessionDetails)
// - Session is renamed
// - Session is reverted/unreverted

setCurrentSession(session) {
  this.currentSession = session
  this.opts.tabTitle?.(nativeTitle(session))
}
```

### focusSession

```typescript
// Sets the "focused" session for stream scheduling
// Used by SessionStreamScheduler to prioritize updates

focusSession(id?: string) {
  this.streams.focus(id)
  this.registerPresence()
}
```

---

## Message Loading & Switching

### Load Modes

| Mode | Behavior |
|------|----------|
| `replace` | Clear existing messages, load new page, abort previous loads |
| `prepend` | Add messages to beginning (pagination) |
| `focus` | Switch focus, refresh session details, reconcile tail |
| `reconcile` | Load recent messages to sync with SSE state |

### Message Loading Flow

```typescript
handleLoadMessages(sessionID, { mode, before, limit }):
  if mode === "replace" || "focus":
    stopCurrentSessionProcesses(sessionID)
    trackedSessionIds.add(sessionID)
    focusSession(sessionID)
    contextSessionID = sessionID
  
  if mode === "focus":
    refreshSessionDetails(sessionID, dir)
    reconcile tail (load with mode="reconcile")
    return
  
  if mode === "replace":
    abort previous load
    refreshSessionDetails(sessionID, dir, abort.signal)
  
  page = fetchMessagePage(client, { sessionID, directory, limit, before })
  
  if mode === "replace" || "reconcile":
    resetMessageCosts(sessionID, messages)
    streams.drop(sessionID)  // Clear queued deltas
  
  postMessage({
    type: "messagesLoaded",
    sessionID,
    messages,
    mode,
    cursor: page.cursor,
    hasMore: Boolean(page.cursor),
    since: mode === "reconcile" ? Date.now() : undefined
  })
  
  if preserveStream:
    streams.flush(sessionID)  // Release queued deltas
  
  recoverPendingPrompts()
```

---

## State Management

### Webview State (acquireVsCodeApi)

```typescript
// Webview uses vscode.getState() / vscode.setState() to persist state
// State is stored in extension and restored on webview reload

// Typical state structure:
{
  sidebarSessionTabIDs: string[],  // Open session tabs
  // ... other UI state
}

// Extension stores state via:
case "setState":
  _webviewState = state
  // Stored in memory (lost on extension deactivation)

// Extension sends state on:
// 1. webviewReady (proactive restore)
// 2. getState request (on-demand)
```

### Extension State Storage

| Storage | Purpose |
|---------|---------|
| `globalState` | Persistent across sessions (variant selections, recents, favorites) |
| `workspaceState` | Per-workspace state |
| `SecretStorage` | Sensitive data (API keys, tokens) |
| In-memory | Current session, tracked sessions, status map |

---

## Known Gaps in Visual Studio Extension

### Critical Gaps

1. **Session Tracking**: `SetCurrentSession` now adds to `_trackedSessionIds`, but:
   - No `trackSession()` public method for external tracking
   - No `pruneDeletedSession()` to clean up after deletion
   - Child sessions not automatically tracked

2. **Message Loading**:
   - No abort controller for canceling in-flight loads
   - No `preserveStream` option for sub-agent viewers
   - No reconciliation mode for SSE sync
   - No `resetMessageCosts()` tracking

3. **Session Status**:
   - `sessionStatusMap` exists in SSEHelper but not fully integrated
   - No periodic status polling
   - No `session.error` event handling

4. **Abort Handling**:
   - No actual `backgroundProcess.stopSession()` call
   - Just posts messages without stopping backend processes

5. **Stream Scheduling**:
   - No `SessionStreamScheduler` equivalent
   - All part updates sent immediately (performance issue)

6. **Prompt Recovery**:
   - No `recoverPendingPrompts()` for missed permissions/questions
   - No `MessageConfirmation` tracking

7. **Directory Tracking**:
   - `sessionDirectories` map not implemented
   - Worktree directories not tracked per-session

8. **Refresh & Reconciliation**:
   - No `refreshSessionDetails()` for post-switch metadata refresh
   - No tail reconciliation after session switch

### Missing Message Types

| Message | Status |
|---------|--------|
| `sessionError` | Not implemented |
| `memoryLoaded` / `memoryEvent` | Not implemented |
| `remoteStatus` | Not implemented |
| `telemetryState` | Not implemented |
| `sandboxStatus` | Partial (no SSE integration) |
| `agentManager.*` | Not implemented (Agent Manager feature) |

### Missing Handlers

| Handler | Purpose |
|---------|---------|
| `handleRevertSession` | Revert to previous message |
| `handleUnrevertSession` | Redo after revert |
| `handleSyncSession` | Sync child session (task tool) |
| `handleExportSessionTranscript` | Export as Markdown |
| `handleCompact` | Context summarization |
| `handleEnhancePrompt` | Prompt enhancement |
| `handleForkSession` | Create forked session |
| `handleLoadSessions` | List all sessions |
| `fetchAndSendSessionModelUsage` | Get model usage stats |

### Infrastructure Gaps

1. **No abort controller pattern** for canceling requests
2. **No stream scheduler** for throttling part updates
3. **No message confirmation** system for destructive actions
4. **No cost tracking** for messages
5. **No project isolation** (projectID filtering for SSE)
6. **No followup session** handling
7. **No visible task streams** tracking

---

## Recommendations for Visual Studio Extension

### Phase 1: Core Session Management

1. Implement `trackSession()` and `untrackSession()` as public methods
2. Add `sessionDirectories` map for per-session directory tracking
3. Implement `refreshSessionDetails()` for post-switch metadata refresh
4. Add abort controller pattern for `handleLoadMessages`
5. Implement `pruneDeletedSession()` cleanup

### Phase 2: Message & Stream Handling

1. Implement `SessionStreamScheduler` equivalent for throttling
2. Add `preserveStream` option for sub-agent viewers
3. Implement reconciliation mode for SSE sync
4. Add `resetMessageCosts()` tracking
5. Implement `recoverPendingPrompts()` for missed events

### Phase 3: Advanced Features

1. Implement `handleRevertSession` / `handleUnrevertSession`
2. Add `handleSyncSession` for child sessions
3. Implement `handleAbort` with actual backend process stopping
4. Add `handleCompact`, `handleEnhancePrompt`, `handleForkSession`
5. Implement `handleLoadSessions` for session list

### Phase 4: Polish & Integration

1. Add missing message types (`sessionError`, `memoryEvent`, etc.)
2. Implement project isolation for SSE filtering
3. Add followup session handling
4. Implement visible task streams tracking
5. Add cost tracking and display
