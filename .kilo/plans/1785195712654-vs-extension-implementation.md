# Visual Studio Extension Implementation Plan

## Goal

Complete the Kilo Visual Studio extension by implementing webview request handlers that forward messages to the CLI backend and return responses to the webview.

## Current State

### What Works ✅

**Extension Infrastructure:**
- Extension loads successfully with `[ProvideAutoLoad(UIContextGuids.SolutionExists)]`
- Menu appears at `Tools > Kilo Code > Open Kilo Code`
- Tool window opens with WebView2 control
- `CliBackendManager` spawns `kilo serve --port 0` and parses the port
- `HttpClientWrapper` - HTTP client with Basic Auth (fully implemented)
- `SseClient` - SSE event stream with reconnection and heartbeat (fully implemented)
- `KiloConnectionService` - Connection lifecycle with health polling (fully implemented)
- `KiloWebViewControl` - WebView2 host + request handlers (fully implemented)

**Message Bridge:**
- `vscode-api.js` - VS Code API compatibility layer with `postMessage`, `getState`, `setState`, `onMessage`
- `onMessage` supports multiple handlers with unsubscribe functions
- Messages broadcast to all registered handlers
- WebView2 uses `WebMessageAsJson` for safe message parsing

**Request Handlers (Implemented):**
- `requestProviders` → `/provider` → `providersLoaded` with `all`, `connected`, `default` fields
- `requestAgents` → `/experimental/tool/ids` → `agentsLoaded` with agent objects
- `requestConfig` → `/config` → `configLoaded` with config and features
- `requestMcpStatus` → `/mcp` → `mcpStatusLoaded` with status object
- `requestRecents` → `/session` → `recentsLoaded` with session list
- `requestFavorites` → `favoritesLoaded` (empty placeholder)
- `requestModelSelections` → `modelSelectionsLoaded` (empty placeholder)
- `requestVariants` → `variantsLoaded` (empty placeholder)
- `requestNotifications` → `notificationsLoaded` (empty placeholder)
- `webviewReady` → Triggers initial data load (config + providers)
- `setState` → Logged for state persistence
- `retryConnection` → Reconnects to backend

**Message Formats (Match VS Code Webview):**
- All response types use `*Loaded` suffix (e.g., `providersLoaded`, `agentsLoaded`)
- Flat property structure (not nested `{ type, payload }`)
- Error responses: `{ type: "error", message, code }`

### What's Missing ❌

**Placeholder Handlers (Return Empty Data):**
- `requestFavorites` - No favorites API endpoint identified
- `requestModelSelections` - No model selections API endpoint identified
- `requestVariants` - Variants stored in extension globalState, not CLI
- `requestNotifications` - Notifications from SSE, not REST API
- `requestAutocompleteSettings` - Settings from VS Code extension settings
- `requestIndexingSettings` - Settings from VS Code extension settings
- `requestChatSettings` - Settings from VS Code extension settings
- `requestWorkStyle` - Settings from VS Code extension settings
- `requestKiloEmbeddingModels` - No dedicated endpoint
- `requestImageModels` - No dedicated endpoint
- `requestModelSelectorExpanded` - Stored in extension globalState

**Settings Handlers:**
These settings come from VS Code extension settings (not CLI), so they need to:
1. Read from `vscode.workspace.getConfiguration('kilo-code')`
2. Return appropriate settings objects

**SSE Event Forwarding:**
- `SseClient_OnSseEvent` forwards events to webview via `SendSseEventToWebview`
- Events sent as `{ type: "sse", payload: { eventType, data } }`
- Webview needs to handle SSE events

**Permission/Question Handling:**
- `permission/reply` - Already implemented, calls `/permission/:requestID/reply`
- `question/reply` - Already implemented, calls `/question/:requestID/reply`
- `prompt` - Already implemented, calls `/session/prompt`

## Architecture

```
Extension (C#)                              CLI Backend (child process)
┌─────────────────────────────┐            ┌────────────────────────┐
│ KiloConnectionService       │── HTTP/SSE─>│ kilo serve --port 0    │
│   ├── CliBackendManager     │            │   Hono REST API        │
│   ├── HttpClient (API)      │            │   SSE event stream     │
│   ├── HttpClient (Health)   │            │   Session management   │
│   └── SSE Event Parser      │            │   AI agent runtime     │
│                             │            └────────────────────────┘
│ KiloWebViewControl          │
│   ├── WebView2 host         │
│   ├── PostMessage bridge    │
│   └── WebMessageReceived    │
│                             │
│ SolidJS Webview (bundled)   │
│   ├── Chat UI               │
│   ├── Message rendering     │
│   └── Input handling        │
└─────────────────────────────┘
```

## Implementation Status

### Phase 1: Request Handlers ✅ COMPLETE

All core request handlers implemented with correct CLI API endpoints:

| Request | Endpoint | Response | Status |
|---|---|---|---|
| `requestProviders` | `/provider` | `providersLoaded` | ✅ Implemented |
| `requestAgents` | `/experimental/tool/ids` | `agentsLoaded` | ✅ Implemented |
| `requestConfig` | `/config` | `configLoaded` | ✅ Implemented |
| `requestMcpStatus` | `/mcp` | `mcpStatusLoaded` | ✅ Implemented |
| `requestRecents` | `/session` | `recentsLoaded` | ✅ Implemented |
| `requestFavorites` | N/A | `favoritesLoaded` | ⚠️ Placeholder |
| `requestModelSelections` | N/A | `modelSelectionsLoaded` | ⚠️ Placeholder |
| `requestVariants` | N/A | `variantsLoaded` | ⚠️ Placeholder |
| `requestNotifications` | N/A | `notificationsLoaded` | ⚠️ Placeholder |
| `webviewReady` | `/config`, `/provider` | Init sequence | ✅ Implemented |

### Phase 2: Settings Handlers ⚠️ INCOMPLETE

Settings come from VS Code extension settings, not CLI:

| Request | Source | Status |
|---|---|---|
| `requestAutocompleteSettings` | VS Code settings | ❌ Not implemented |
| `requestIndexingSettings` | VS Code settings | ❌ Not implemented |
| `requestChatSettings` | VS Code settings | ❌ Not implemented |
| `requestWorkStyle` | VS Code settings | ❌ Not implemented |

### Phase 3: Testing ⏳ PENDING

- [ ] Test extension loads in Visual Studio
- [ ] Test webview receives `providersLoaded` message
- [ ] Test webview receives `agentsLoaded` message
- [ ] Test webview receives `configLoaded` message
- [ ] Test SSE events forwarded correctly
- [ ] Test permission/question reply flow

## Key Files

**Extension Code:**
- `packages/kilo-visualstudio/KiloVisualStudioExtension/KiloWebViewControl.cs` - Request handlers
- `packages/kilo-visualstudio/KiloVisualStudioExtension/HttpClientWrapper.cs` - HTTP client
- `packages/kilo-visualstudio/KiloVisualStudioExtension/SseClient.cs` - SSE client
- `packages/kilo-visualstudio/KiloVisualStudioExtension/KiloConnectionService.cs` - Connection manager
- `packages/kilo-visualstudio/KiloVisualStudioExtension/CliBackendManager.cs` - CLI process manager

**Webview:**
- `packages/kilo-visualstudio/KiloVisualStudioExtension/webview/vscode-api.js` - VS Code API compatibility
- `packages/kilo-visualstudio/KiloVisualStudioExtension/webview/index.html` - Entry point

**CLI API Reference:**
- `packages/opencode/src/server/routes/instance/httpapi/groups/provider.ts` - Provider endpoints
- `packages/opencode/src/server/routes/instance/httpapi/groups/config.ts` - Config endpoints
- `packages/opencode/src/server/routes/instance/httpapi/groups/session.ts` - Session endpoints
- `packages/opencode/src/server/routes/instance/httpapi/groups/mcp.ts` - MCP endpoints
- `packages/sdk/openapi.json` - Full API spec

## Next Steps

1. **Build and Test Extension:**
   ```bash
   dotnet build -c Debug packages/kilo-visualstudio/KiloVisualStudioExtension/KiloVisualStudioExtension.csproj
   ```

2. **Install VSIX and Test:**
   - Install the `.vsix` file in Visual Studio
   - Open `Tools > Kilo Code > Open Kilo Code`
   - Open DevTools and verify messages received

3. **Implement Settings Handlers:**
   - Read VS Code extension settings
   - Return appropriate settings objects for each request type

4. **Implement Remaining Placeholders:**
   - Favorites: Store in extension globalState
   - Model Selections: Read from model.json
   - Variants: Read from extension globalState
   - Notifications: Forward from SSE events

## Notes

- CLI binary path: `c:\prog\kilocode\kilocode\packages\opencode\dist\@kilocode\cli-windows-x64\bin\kilo.exe`
- CLI runs on dynamic port with password auth: `http://127.0.0.1:PORT` with `Basic kilo:{password}`
- All message types match VS Code webview expectations (`*Loaded` suffix)
- `onMessage` returns unsubscribe function for cleanup (SolidJS pattern)
