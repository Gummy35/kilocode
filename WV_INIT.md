# VS Code Webview Initialization Process

This document analyzes the complete initialization workflow of the Kilo Code VS Code extension webview, from webview creation to the moment it is ready to accept user prompts.

## Overview

The initialization process involves a handshake between the webview (SolidJS frontend) and the extension host (TypeScript backend), followed by parallel data loading from the CLI backend. The entire flow is designed to be resilient to race conditions and webview refreshes.

## Architecture Components

### Webview Side (SolidJS)

- **`vscode.tsx`** - VS Code API wrapper (`acquireVsCodeApi()`)
- **`server.tsx`** - Server connection context (connection state, profile, device auth)
- **`config.tsx`** - Configuration context (config, features, settings)
- **`provider.tsx`** - Provider/model catalog context
- **`session.tsx`** - Session state, messages, permissions, questions, suggestions
- **`agent-requirements.tsx`** - Agent requirement checking

### Extension Host Side (TypeScript)

- **`KiloProvider.ts`** - Main provider handling webview messages and CLI backend communication
- **`KiloConnectionService.ts`** - CLI backend connection management (spawns `kilo serve`)

## Initialization Sequence

### Phase 1: Webview Mount (0ms)

**File: `server.tsx:56-147`**

```typescript
onMount(() => {
  // 1. Set up message listeners
  const unsubscribe = vscode.onMessage((message: ExtensionMessage) => {
    switch (message.type) {
      case "ready":
        setServerInfo(message.serverInfo)
        setConnectionState("connected")
        // ... set language, workspace directory, etc.
        break
      case "connectionState":
        setConnectionState(message.state)
        break
      case "profileData":
        setProfileData(message.data)
        break
      // ... other message handlers
    }
  })

  // 2. Send webviewReady to extension
  console.log("[Kilo New] Webview ready")
  vscode.postMessage({ type: "webviewReady" })
})
```

**What happens:**
1. Webview mounts and registers message handlers
2. Immediately sends `webviewReady` message to extension
3. This handshake prevents message loss during webview refresh

### Phase 2: Extension Receives `webviewReady` (1-5ms)

**File: `KiloProvider.ts:992-1001`**

```typescript
case "webviewReady":
  console.log("[Kilo New] KiloProvider: ✅ webviewReady received")
  this.isWebviewReady = true
  this.visibleTaskStreams.clear()
  this.flushPendingKiloModel()
  await this.syncWebviewState("webviewReady")
  this.flushPendingReviewComments()
  this.recoverPendingPrompts()
  this.readyResolvers.splice(0).forEach((r) => r())
  break
```

**What happens:**
1. Sets `isWebviewReady = true` flag
2. Clears visible task streams (Agent Manager feature)
3. Flushes any pending Kilo model selections
4. Calls `syncWebviewState("webviewReady")` - the core initialization
5. Flushes pending review comments
6. Recovers pending permission/question prompts
7. Resolves all waiting `waitForReady()` promises

### Phase 3: `syncWebviewState` Execution (5-50ms)

**File: `KiloProvider.ts:634-710`**

```typescript
private async syncWebviewState(reason: string): Promise<void> {
  const serverInfo = this.connectionService.getServerInfo()
  
  // 1. Always push connection state first
  this.postConnectionState()
  pushTelemetryState((m) => this.postMessage(m))

  // 2. Re-send ready so webview can recover after refresh
  if (serverInfo) {
    this.postMessage({
      type: "ready",
      serverInfo,
      extensionVersion: this.extensionVersion,
      vscodeLanguage: vscode.env.language,
      languageOverride: langConfig.get<string>("language"),
      workspaceDirectory: this.getProjectDirectory(this.currentSession?.id),
    })
  }

  // 3. If connected, fetch and push profile
  if (this.connectionState === "connected" && this.client) {
    const profileResult = await retry(() => this.client!.kilo.profile())
    this.postMessage({ type: "profileData", data: profileResult.data ?? null })
    
    // 4. Refresh session details
    if (this.currentSession) {
      this.refreshSessionDetails(this.currentSession.id, ...)
    }
    
    // 5. Re-send cached stats and git status
    if (this.cachedStats) this.postMessage(this.cachedStats)
    this.postMessage({ type: "gitStatus", repo: this.cachedGitRepo })
    
    // 6. Seed session status map
    const reconcile = this.sessionStatusMap.size === 0
    void this.seedSessionStatusMap(reconcile)
    
    this.sendRemoteStatus()
  }
  
  // 7. Show migration wizard if connected
  if (this.connectionState === "connected") {
    void checkAndShowMigrationWizard(this.migrationCtx)
  }
}
```

**Messages sent to webview:**
1. `connectionState` - Current connection status
2. `ready` - Server info, extension version, language, workspace directory
3. `profileData` - User profile from Kilo Gateway
4. `gitStatus` - Whether workspace is a git repo
5. Cached worktree stats (if any)
6. Session status updates

### Phase 4: Webview Receives `ready` (5-10ms)

**File: `server.tsx:58-75`**

```typescript
case "ready":
  console.log("[Kilo New] Server ready:", message.serverInfo)
  setServerInfo(message.serverInfo)
  setConnectionState("connected")
  setErrorMessage(undefined)
  if (message.vscodeLanguage) setVscodeLanguage(message.vscodeLanguage)
  if (message.languageOverride) setLanguageOverride(message.languageOverride)
  if (message.workspaceDirectory) setWorkspaceDirectory(message.workspaceDirectory)
  break
```

**What happens:**
1. Webview now has server info and knows it's connected
2. Language and workspace directory are set
3. Error state is cleared

### Phase 5: Parallel Data Requests (10-100ms)

After receiving `ready`, the webview contexts immediately request their data:

#### 5a. Config Context
**File: `config.tsx:206-230`**

```typescript
// Request config immediately
requestInitialData()

// Fallback retry after 3 seconds
const fallback = setTimeout(() => {
  if (loading()) requestInitialData()
}, 3000)

// Listen for extensionDataReady to retry once
const unsubReady = vscode.onMessage((message: ExtensionMessage) => {
  if (message.type !== "extensionDataReady") return
  unsubReady()
  clearTimeout(fallback)
  if (loading()) requestInitialData()
})
```

Requests:
- `requestConfig`
- `requestAutocompleteSettings`
- `requestIndexingSettings`
- `requestChatSettings`
- `requestThroughputSetting`

#### 5b. Provider Context
**File: `provider.tsx:68-90`**

```typescript
// Request providers immediately
vscode.postMessage({ type: "requestProviders" })

// Fallback after 3 seconds
const fallback = setTimeout(() => {
  if (Object.keys(providers()).length === 0) {
    vscode.postMessage({ type: "requestProviders" })
  }
}, 3000)

// Retry on extensionDataReady
const unsubReady = vscode.onMessage((message: ExtensionMessage) => {
  if (message.type !== "extensionDataReady") return
  unsubReady()
  clearTimeout(fallback)
  if (Object.keys(providers()).length === 0) {
    vscode.postMessage({ type: "requestProviders" })
  }
})
```

#### 5c. Session Context
**File: `session.tsx:850-878`**

```typescript
// Request agents immediately
vscode.postMessage({ type: "requestAgents" })

// Request MCP status
vscode.postMessage({ type: "requestMcpStatus" })

// Fallback after 3 seconds
const fallback = setTimeout(() => {
  if (agents().length === 0) vscode.postMessage({ type: "requestAgents" })
  if (Object.keys(mcpStatus()).length === 0) vscode.postMessage({ type: "requestMcpStatus" })
}, 3000)

// Retry on extensionDataReady
const unsubReady = vscode.onMessage((message: ExtensionMessage) => {
  if (message.type !== "extensionDataReady") return
  unsubReady()
  clearTimeout(fallback)
  if (agents().length === 0) vscode.postMessage({ type: "requestAgents" })
  if (Object.keys(mcpStatus()).length === 0) vscode.postMessage({ type: "requestMcpStatus" })
})
```

Requests:
- `requestAgents`
- `requestMcpStatus`
- `requestSkills`
- `requestCommands`
- `requestIndexingStatus`
- `requestNotifications`
- `requestWorkStyle`
- `requestRecents`
- `requestFavorites`
- `requestVariants`
- `requestModelSelections`

### Phase 6: Extension Processes Requests (10-100ms)

**File: `KiloProvider.ts` - Various handlers**

Each request triggers a fetch from the CLI backend:

- `requestProviders` → `GET /provider` → `providersLoaded`
- `requestAgents` → `GET /experimental/tool/ids` → `agentsLoaded`
- `requestConfig` → `GET /config` → `configLoaded`
- `requestMcpStatus` → `GET /mcp` → `mcpStatusLoaded`
- `requestSkills` → `GET /skill` → `skillsLoaded`
- `requestCommands` → `GET /command` → `commandsLoaded`
- etc.

### Phase 7: Webview Receives Data (50-200ms)

Each context handles its response:

**Config Context (`config.tsx:121-140`):**
```typescript
case "configLoaded":
  if (saving()) return  // Skip if save is in-flight
  setConfig(resolveConfig(message.config, draft(), has(draft())))
  setFeatures(message.features)
  setSaved(message.config)
  if (message.settings) mergeSettings(message.settings)
  setLoading(false)
  break
```

**Provider Context (`provider.tsx:54-60`):**
```typescript
case "providersLoaded":
  setProviders(message.providers)
  setConnected(message.connected)
  setDefaults(message.defaults)
  setDefaultSelection(message.defaultSelection)
  setAuthMethods(message.authMethods)
  setAuthStates(message.authStates)
  break
```

**Session Context (`session.tsx:776-803`):**
```typescript
case "agentsLoaded":
  setAgents(message.agents)
  setAllAgents(message.allAgents ?? message.agents)
  setDefaultAgent(message.defaultAgent)
  // Reset pending selection if agent no longer exists
  // Clear per-session selections for unavailable agents
  break
```

### Phase 8: Webview Ready for User Input (200-500ms)

At this point:
- ✅ Connection state is `connected`
- ✅ Server info is available
- ✅ Config is loaded (or fallback defaults are in place)
- ✅ Providers and models are available
- ✅ Agents are loaded
- ✅ MCP status is known
- ✅ Skills and commands are available
- ✅ Session state is initialized
- ✅ Permission/question recovery is complete

The webview can now:
- Display the chat interface
- Show available models and agents
- Accept user prompts
- Handle permission requests
- Process questions and suggestions

## Race Condition Handling

### Pattern 1: Immediate Registration + Fallback

All contexts register message handlers **immediately** (not in `onMount`) to catch messages that arrive before DOM mount:

```typescript
// Good - registered immediately
const unsubscribe = vscode.onMessage((message) => {
  if (message.type === "providersLoaded") {
    setProviders(message.providers)
  }
})

// Then request data
vscode.postMessage({ type: "requestProviders" })
```

### Pattern 2: `extensionDataReady` Retry

Contexts listen for `extensionDataReady` to retry requests if the initial request was too early:

```typescript
const unsubReady = vscode.onMessage((message) => {
  if (message.type !== "extensionDataReady") return
  unsubReady()
  clearTimeout(fallback)
  if (dataNotLoaded()) {
    vscode.postMessage({ type: "requestProviders" })
  }
})
```

### Pattern 3: 3-Second Fallback Timeout

Every context has a 3-second fallback to re-request if data hasn't arrived:

```typescript
const fallback = setTimeout(() => {
  if (dataNotLoaded()) {
    vscode.postMessage({ type: "requestProviders" })
  }
}, 3000)
```

### Pattern 4: `waitForReady()` for Dependent Operations

Code that needs the webview to be ready can await:

```typescript
await provider.waitForReady()
// Now safe to post messages
```

## Message Flow Diagram

```
┌─────────────────┐                    ┌──────────────────┐
│    Webview      │                    │  Extension Host  │
│  (SolidJS)      │                    │  (TypeScript)    │
└────────┬────────┘                    └────────┬─────────┘
         │                                      │
         │  1. onMount()                        │
         │  2. postMessage(webviewReady) ──────▶│
         │                                      │ 3. isWebviewReady = true
         │                                      │ 4. syncWebviewState()
         │                                      │ 5. postMessage(connectionState)
         │  6. postMessage(connectionState) ◀───│
         │  7. postMessage(ready) ◀─────────────│
         │                                      │
         │  8. Handle ready (set connection)    │
         │  9. requestConfig                    │
         │  10. requestProviders                │
         │  11. requestAgents                   │
         │  12. requestMcpStatus                │
         │         ... (parallel requests)      │
         │                                      │ 13. Fetch from CLI backend
         │  14. configLoaded                    │
         │  15. providersLoaded ◀───────────────│
         │  16. agentsLoaded                    │
         │  17. mcpStatusLoaded                 │
         │         ... (parallel responses)     │
         │                                      │
         │  18. Webview ready for user input    │
         └──────────────────────────────────────┘
```

## Key Design Decisions

### 1. Two-Phase Initialization

- **Phase 1**: Basic connection state (`ready` message)
- **Phase 2**: Full data loading (parallel requests)

This allows the UI to render quickly while data loads in the background.

### 2. Resilient to Refresh

The `webviewReady` handshake ensures that messages posted during a webview refresh are not lost. The extension waits for this message before considering the webview ready.

### 3. Multiple Fallback Strategies

- Immediate request + `extensionDataReady` retry
- 3-second timeout fallback
- Both strategies ensure data loads even if timing is off

### 4. No Blocking

All data requests are fire-and-forget. The UI renders with loading states and updates as data arrives.

### 5. State Preservation

The extension saves webview state (`setState`/`getState`) which is restored on refresh, allowing the UI to recover its previous state.

## Performance Characteristics

- **Fast path** (all data cached): ~100-200ms
- **Normal path** (backend fetch): ~200-500ms
- **Slow path** (network latency): ~500-1000ms

The webview is interactive within 100ms, with full data available within 500ms in typical cases.
