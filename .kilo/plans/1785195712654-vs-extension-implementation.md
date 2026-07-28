# Visual Studio Extension Implementation Plan

## Goal

Complete the Kilo Visual Studio extension by implementing webview request handlers that forward messages to the CLI backend and return responses to the webview.

## Current State

### What Works ✅
- Extension loads successfully with `[ProvideAutoLoad(UIContextGuids.SolutionExists)]`
- Menu appears at `Tools > Kilo Code > Open Kilo Code`
- Tool window opens with WebView2 control
- `CliBackendManager` spawns `kilo serve --port 0` and parses the port
- **`HttpClientWrapper`** - HTTP client with Basic Auth (fully implemented)
- **`SseClient`** - SSE event stream with reconnection and heartbeat (fully implemented)
- **`KiloConnectionService`** - Connection lifecycle with health polling (fully implemented)
- **`KiloWebViewControl`** - WebView2 message bridge receives and parses messages
- **`vscode-api.js`** - VS Code API compatibility layer for webview
- All `request*` messages are logged and parsed correctly

### What's Missing ❌
**Request handlers just log messages but don't respond:**
- `requestProviders` → Logs but doesn't fetch/return providers
- `requestAgents` → Logs but doesn't fetch/return agents
- `requestConfig` → Calls `HandleConfigRead` but may not work correctly
- `requestAutocompleteSettings`, `requestIndexingSettings`, `requestChatSettings` → No handlers
- `requestWorkStyle`, `requestKiloEmbeddingModels`, `requestImageModels` → No handlers
- `requestMcpStatus`, `requestVariants`, `requestModelSelections` → No handlers
- `requestRecents`, `requestFavorites`, `requestNotifications` → No handlers
- `webviewReady` → Logged but no initialization sequence triggered
- `setState` → Logged but state not persisted

The **core infrastructure is complete**. The remaining work is implementing the request handlers that bridge webview requests to CLI API calls.

## Architecture

The VS extension should follow the VS Code pattern (single shared connection service) rather than the JetBrains split-mode pattern:

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

## Implementation Phases

### Phase 1: Request Handlers (Critical Path - MVP)

The core infrastructure is complete. Now implement handlers for webview request messages.

#### Task 1.1: Implement `requestProviders` Handler
**File**: `packages/kilo-visualstudio/KiloVisualStudioExtension/KiloWebViewControl.cs`

In `OnWebMessageReceived`, replace the `requestProviders` case:
```csharp
case "requestProviders":
    await HandleRequestProviders(payload);
    break;
```

**Implementation** (`HandleRequestProviders`):
- Call `_httpClient.GetJsonAsync<object>("/providers")` 
- Send response: `PostMessage(json: {"type": "providers", "payload": providers})`
- Handle errors gracefully (send error response to webview)

**CLI endpoint reference**: Check VS Code extension for exact endpoint path and response format

#### Task 1.2: Implement `requestAgents` Handler
**File**: `packages/kilo-visualstudio/KiloVisualStudioExtension/KiloWebViewControl.cs`

**Implementation** (`HandleRequestAgents`):
- Call `_httpClient.GetJsonAsync<object>("/agents")`
- Send response: `PostMessage(json: {"type": "agents", "payload": agents})`

#### Task 1.3: Implement `requestConfig` Handler (Fix Existing)
**File**: `packages/kilo-visualstudio/KiloVisualStudioExtension/KiloWebViewControl.cs`

The existing `HandleConfigRead` may need fixes:
- Verify endpoint path (`/config` vs `/global/config`)
- Ensure response format matches webview expectations
- Add error handling

#### Task 1.4: Implement Settings Request Handlers
**Files**: Same file, add cases for:
- `requestAutocompleteSettings` → `/settings/autocomplete`
- `requestIndexingSettings` → `/settings/indexing`
- `requestChatSettings` → `/settings/chat`
- `requestWorkStyle` → `/settings/work-style`

Each follows the same pattern: GET endpoint → PostMessage response

#### Task 1.5: Implement Model/Feature Request Handlers
**Files**: Same file, add cases for:
- `requestKiloEmbeddingModels` → `/models/embedding`
- `requestImageModels` → `/models/image`
- `requestMcpStatus` → `/mcp/status`
- `requestVariants` → `/variants`
- `requestModelSelections` → `/models/selections`

#### Task 1.6: Implement Session/State Request Handlers
**Files**: Same file, add cases for:
- `requestRecents` → `/sessions/recents`
- `requestFavorites` → `/favorites`
- `requestNotifications` → `/notifications`
- `setState` → Store state (may persist to VS settings or CLI config)

#### Task 1.7: Handle `webviewReady` Event
**File**: `packages/kilo-visualstudio/KiloVisualStudioExtension/KiloWebViewControl.cs`

When webview signals ready:
- Trigger initial data loads (providers, agents, config)
- Send cached state if available
- Begin SSE event forwarding

### Phase 2: Testing and Validation

#### Task 2.1: Verify CLI Endpoints
Before implementing handlers, verify the exact CLI API endpoints:
- Check `packages/opencode/src/server/` for endpoint definitions
- Use `curl` or Postman to test endpoints manually
- Document exact paths, request/response formats

#### Task 2.2: Test Each Handler
For each request handler:
1. Open extension in debug mode
2. Trigger the request from webview (open DevTools, call `acquireVsCodeApi()...`)
3. Verify HTTP request is sent to CLI
4. Verify response is sent back to webview
5. Check webview DevTools for received message

#### Task 2.3: Error Handling
Add error handling to all handlers:
- HTTP errors (404, 500, timeout)
- JSON parse errors
- Network failures
- Send error responses to webview with meaningful messages

### Phase 3: Essential Features (Future)

Permission/question handling, settings UI, and telemetry can be added after the core request handlers are working.

### Phase 4: Advanced Features (Out of Scope for MVP)

- Autocomplete provider (requires editor integration APIs)
- Code actions (lightbulb quick fixes)
- Terminal integration
- Git integration
- Diff viewer
- Agent Manager (multi-session orchestration)
- Marketplace panel
- Sub-agent viewer

## Key Design Decisions

### Decision 1: Request Handler Pattern
**Recommendation**: Implement all handlers in `KiloWebViewControl.cs` for now

**Rationale**:
- Single file is easier to maintain for MVP
- Can extract to separate classes later if needed
- All handlers follow the same simple pattern

### Decision 2: CLI Endpoint Discovery
**Recommendation**: Check VS Code extension for endpoint patterns first

**Rationale**:
- VS Code uses `@kilocode/sdk` which documents all endpoints
- Check `packages/opencode/src/server/` for actual implementations
- JetBrains plugin also has endpoint references in `KiloBackendConnectionService`

### Decision 3: Error Response Format
**Recommendation**: Use consistent error format: `{"type": "error", "payload": {"message": "...", "code": "..."}}`

**Rationale**:
- Matches webview error handling expectations
- Easy to parse and display in UI
- Consistent across all handlers

## File Structure

```
packages/kilo-visualstudio/KiloVisualStudioExtension/
├── KiloVisualStudioExtensionPackage.cs    # Package initialization (existing)
├── ShowKiloWindowCommand.cs               # Command handler (existing)
├── KiloToolWindow.cs                      # Tool window pane (existing)
├── KiloWebViewControl.cs                  # WebView2 host + request handlers (UPDATE)
├── CliBackendManager.cs                   # CLI process manager (existing)
├── KiloConnectionService.cs               # Connection lifecycle (existing)
├── HttpClientWrapper.cs                   # HTTP API client (existing)
├── SseClient.cs                           # SSE event stream (existing)
├── Guids.cs                               # GUIDs (existing)
├── KiloPackage.vsct                       # Menu definition (existing)
└── webview/
    ├── index.html                         # Entry point (existing)
    └── vscode-api.js                      # VS Code API compatibility (existing)
```

**Note**: All core infrastructure files already exist. Only `KiloWebViewControl.cs` needs updates to add request handlers.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| CLI endpoint paths unknown | High | Check VS Code extension SDK or `packages/opencode/src/server/` for exact paths |
| Response format mismatch | Medium | Log both request and response; compare with VS Code webview expectations |
| WebView2 message timing | Low | Ensure `ConnectAsync` is called before webview sends requests |
| Thread safety (EDT vs background) | Medium | Use `ThreadHelper.JoinableTaskFactory` consistently; already implemented |

## Validation Plan

### Manual Testing
1. Install VSIX
2. Open Visual Studio with a solution
3. Verify menu appears at `Tools > Kilo Code > Open Kilo Code`
4. Verify tool window opens and shows webview (no "Webview Not Available" error)
5. Open DevTools (Command Palette → "Developer: Open Webview Developer Tools")
6. In DevTools console, verify no errors on load
7. Test each request handler:
   - `requestProviders`: Should receive `{"type": "providers", "payload": ...}`
   - `requestAgents`: Should receive `{"type": "agents", "payload": ...}`
   - `requestConfig`: Should receive `{"type": "config/read", "payload": ...}`
8. Verify extension logs show HTTP requests to CLI
9. Verify SSE events are forwarded to webview

### Debugging Commands
- Extension logs: `Output` window → `Kilo Code` channel (if configured)
- Debug output: `Debug` → `Output` → `Debug` (for `System.Diagnostics.Debug.WriteLine`)
- WebView2 logs: DevTools console

## Open Questions

1. **CLI endpoint paths**: What are the exact endpoint paths for providers, agents, settings, etc.?
   - **Action**: Check `packages/opencode/src/server/` or VS Code `@kilocode/sdk` for endpoint definitions

2. **Response formats**: What format does the webview expect for each response type?
   - **Action**: Check VS Code webview code (`webview-ui/src/`) for how it processes responses

## Next Steps

1. **First**: Verify CLI endpoint paths by checking `packages/opencode/src/server/` or VS Code SDK
2. **Then**: Implement handlers in order of importance:
   - `requestConfig` (already partially implemented, needs verification)
   - `requestProviders` (required for model selection)
   - `requestAgents` (required for agent selection)
   - Settings requests (`requestAutocompleteSettings`, etc.)
   - Model/feature requests
   - Session/state requests
3. **Test**: Each handler as it's implemented using DevTools
4. **Iterate**: Fix any format mismatches or endpoint issues

Once all request handlers are implemented, the webview should be fully functional and able to communicate with the CLI backend.
