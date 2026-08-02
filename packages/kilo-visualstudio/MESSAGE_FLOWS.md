# Message Flow Analysis: VS Code vs Visual Studio Extension

## Executive Summary

This report analyzes the message flows between the webview and extension backend in both the VS Code extension (`packages/kilo-vscode/`) and the Visual Studio extension (`packages/kilo-visualstudio/`). The analysis identifies critical gaps in the VS extension's implementation that cause missing messages and incomplete session state.

## Implementation Status

### Completed (as of latest update - Aug 2 2026)

1. **SessionStreamScheduler.cs** - ✅ Implemented
   - Full C# implementation matching TypeScript SessionStreamScheduler
   - Active session flushing (16ms)
   - Background session throttling (150ms base, adaptive up to 400ms)
   - Visible session handling (50ms)
   - Part update coalescing
   - `focus()`, `drop()`, `flush()` methods

2. **Stream integration** - ✅ Implemented
   - VSProvider now uses SessionStreamScheduler for all stream updates
   - `DropSessionStream()` and `FlushSessionStream()` integrated with scheduler
   - `partUpdated` messages now flow correctly with proper structure

3. **Session creation flow** - ✅ Code fixed
   - `sessionCreated` message includes proper structure with `parentID`, `createdAt`, `updatedAt`, `revert`, `summary`
   - ⚠️ **Issue**: Message is sent but webview may not receive it due to timing (sent before webviewReady)

4. **messagesLoaded handler** - ✅ Code fixed
   - Now sends `since` timestamp matching VS Code format
   - ⚠️ **Issue**: May not be called during normal message flow

### Remaining Gaps (Critical for Full Parity)

5. **`sessionCreated` delivery** - ⚠️ Sent but not received by webview
   - Message is sent from SessionHandlerService but webview doesn't show it
   - Likely timing issue: message sent before webviewReady handler is registered

6. **`messageCreated`** - ❌ Not implemented
   - No user message creation events sent to webview
   - No assistant message creation events sent to webview

7. **`messagesLoaded`** - ❌ Not sent during normal flow
   - Only sent when explicitly triggered by createSession
   - VS Code sends this after every session creation

8. **`memoryLoaded`** - ❌ Not implemented
   - No memory state sent to webview

9. **`sessionModelUsageLoaded`** - ❌ Not implemented
   - No token usage tracking sent to webview

10. **`workspaceDirectoryChanged`** - ❌ Not implemented
    - No workspace context sent to webview

11. **Editor context** - ❌ Not captured
    - No `editorContext` with `visibleFiles`, `openTabs`, `activeFile`, `shell`

12. **Step start/finish events** - ❌ Not implemented
    - No `step-start` parts with snapshot hash
    - No `step-finish` parts with metrics

13. **Part removal events** - ❌ Not implemented
    - No `partRemoved` for cleanup of transient parts like "Initializing snapshot..."

## Observed Behavior

### VS Code Extension (Working)

When typing "test" in the VS Code extension, the following message flow occurs:

1. **Session Creation**
   - `sessionCreated` (sent twice: once with draftID, once without)
   - Contains: `id`, `parentID`, `title`, `createdAt`, `updatedAt`, `revert`, `summary`

2. **User Message**
   - `messageCreated` with `role: "user"`
   - Contains: `editorContext` with `visibleFiles`, `openTabs`, `activeFile`, `shell`

3. **Assistant Response**
   - `messageCreated` with `role: "assistant"`
   - `partUpdated` with synthetic "Initializing snapshot..." loading indicator
   - `sessionUpdated` with status changes
   - `workspaceDirectoryChanged`
   - `sessionStatus: "busy"` / `"idle"`

4. **Session State**
   - `sessionModelUsageLoaded` (multiple times)
   - `memoryLoaded` with full memory state
   - `messagesLoaded` with `mode: "reconcile"`
   - `partUpdated` with `step-start` containing snapshot hash
   - `partUpdated` with `reasoning` part
   - `partUpdated` with actual response text
   - `partsUpdated` batch updates
   - `sessionTurnClosed` with `reason: "completed"`

5. **Final State**
   - `sessionUpdated` with `summary` (additions, deletions, files)
   - `messageCreated` with `summary.diffs`
   - `sessionStatus: "idle"`

### Visual Studio Extension (Broken)

When typing "test" in the VS extension, the following occurs:

1. **Missing Initial Messages**
   - NO `sessionCreated` message
   - NO `messageCreated` for user message
   - NO `workspaceDirectoryChanged`

2. **Partial Response**
   - Only `sessionStatus: "busy"` (twice)
   - `partUpdated` with text deltas starting mid-response ("The user is just testing...")
   - NO loading indicators ("Initializing snapshot...")
   - NO `sessionModelUsageLoaded`
   - NO `memoryLoaded`
   - NO `messagesLoaded`

3. **Incomplete Flow**
   - `sessionStatus: "idle"`
   - `sessionTurnClosed`
   - Missing final state updates

## Critical Gaps Identified

### 1. Missing Session Creation Flow

**VS Code** (`KiloProvider.ts:1061-1064`):
```typescript
case "createSession":
  await this.handleCreateSession()
  break
```

**VS Extension** (`VSProvider.cs:451-453`):
```csharp
case "createSession":
  await _sessionHandler.HandleCreateSessionAsync(payload);
  break
```

**Problem**: The VS extension's `HandleCreateSessionAsync` does not send `sessionCreated` message to webview after creating session. The VS Code implementation sends this message immediately after session creation to notify the webview of the new session.

**Location**: VS extension needs to add:
```csharp
PostMessage(JsonSerializer.Serialize(new { 
    type = "sessionCreated", 
    session = sessionData,
    draftID = draftId 
}));
```

### 2. Missing Editor Context

**VS Code** sends `editorContext` with every user message:
```json
{
  "editorContext": {
    "visibleFiles": [...],
    "openTabs": [...],
    "activeFile": "...",
    "shell": "..."
  }
}
```

**VS Extension**: No equivalent implementation. The `sendMessage` handler does not capture or send editor context.

**Location**: Need to implement editor context collection in `SessionControlHandlerService.HandleSendMessageAsync`.

### 3. Missing SessionStreamScheduler

**VS Code** (`kilo-provider-utils.ts:9`):
```typescript
import { SessionStreamScheduler } from "./kilo-provider/session-stream-scheduler"
private readonly streams = new SessionStreamScheduler((msg) => this.postMessage(msg))
```

**VS Extension**: NO equivalent implementation. The VS extension posts messages directly without coalescing, throttling, or session prioritization.

**Impact**: 
- No "Initializing snapshot..." loading indicators
- No proper batching of part updates
- No focus/background session prioritization
- Missing `partRemoved` events for cleanup

**Required Implementation**: Create `SessionStreamScheduler.cs` matching the TypeScript implementation with:
- Active session flushing (16ms)
- Background session throttling (150ms base, adaptive up to 400ms)
- Visible session handling (50ms)
- Part update coalescing
- `focus()`, `drop()`, `flush()` methods

### 4. Missing messagesLoaded Flow

**VS Code** (`KiloProvider.ts:1073-1078`):
```typescript
case "loadMessages":
  void this.handleLoadMessages(sessionID, {
    mode: message.mode,
    before: message.before,
    limit: message.limit,
  })
  break
```

**VS Extension** (`VSProvider.cs:465-467`):
```csharp
case "loadMessages":
  await _sessionHandler.HandleLoadMessagesAsync(payload);
  break
```

**Problem**: The VS extension's `HandleLoadMessagesAsync` does not send `messagesLoaded` response with the message history. This causes the webview to never receive the full conversation history.

**Required**: Send `messagesLoaded` message with:
```json
{
  "type": "messagesLoaded",
  "sessionID": "...",
  "messages": [...],
  "mode": "reconcile",
  "hasMore": false,
  "since": timestamp
}
```

### 5. Missing Memory Loading

**VS Code** sends `memoryLoaded` with full memory state:
```json
{
  "type": "memoryLoaded",
  "sessionID": "...",
  "status": {
    "root": "...",
    "state": {...},
    "exists": {...},
    "index": {...}
  }
}
```

**VS Extension**: NO implementation. No memory state is sent to webview.

**Required**: Implement memory state retrieval and send `memoryLoaded` message.

### 6. Missing Session Model Usage

**VS Code** sends `sessionModelUsageLoaded` multiple times during session:
```json
{
  "type": "sessionModelUsageLoaded",
  "sessionID": "...",
  "requestID": "...",
  "data": {
    "sessionIDs": [...],
    "totals": {...},
    "models": [...]
  }
}
```

**VS Extension**: NO implementation.

**Required**: Implement model usage tracking and send `sessionModelUsageLoaded` messages.

### 7. Missing Workspace Directory Change

**VS Code** sends `workspaceDirectoryChanged` when workspace changes:
```json
{
  "type": "workspaceDirectoryChanged",
  "directory": "c:\\prog\\kilocode\\kilocode"
}
```

**VS Extension**: NO implementation.

**Required**: Track workspace directory changes and send `workspaceDirectoryChanged` message.

### 8. Missing Part Removal Events

**VS Code** sends `partRemoved` to clean up transient parts:
```json
{
  "type": "partRemoved",
  "sessionID": "...",
  "messageID": "...",
  "partID": "..."
}
```

**VS Extension**: NO implementation. The "Initializing snapshot..." part is never removed.

**Required**: Implement part removal logic in `SessionStreamScheduler` equivalent.

### 9. Missing Step Start/Finish Events

**VS Code** sends `step-start` and `step-finish` parts:
```json
{
  "type": "partUpdated",
  "part": {
    "type": "step-start",
    "snapshot": "c3c51c14d9e6d75760f7818d782efc09191b6dcc",
    "time": {"start": timestamp}
  }
}
```

**VS Extension**: NO implementation.

**Required**: Track step boundaries and send step-start/step-finish events.

### 10. Missing Session Status Map

**VS Code** maintains `sessionStatusMap` and seeds it on connection:
```typescript
private sessionStatusMap = new Map<string, SessionStatus["type"]>()
await this.seedSessionStatusMap(reconcile)
```

**VS Extension** has `_sessionStatusMap` but does not seed it or send status updates properly.

**Required**: Implement proper session status tracking and send `sessionStatus` messages.

## Message Handler Comparison

| Message Type | VS Code | VS Extension | Status |
|--------------|---------|--------------|--------|
| `sessionCreated` | ✅ Implemented | ⚠️ Partial (not sent to webview) | **MISSING** |
| `messageCreated` | ✅ Implemented | ❌ Not implemented | **MISSING** |
| `messagesLoaded` | ✅ Implemented | ❌ Not sending response | **MISSING** |
| `memoryLoaded` | ✅ Implemented | ❌ Not implemented | **MISSING** |
| `sessionModelUsageLoaded` | ✅ Implemented | ❌ Not implemented | **MISSING** |
| `workspaceDirectoryChanged` | ✅ Implemented | ❌ Not implemented | **MISSING** |
| `partRemoved` | ✅ Implemented | ❌ Not implemented | **MISSING** |
| `sessionStatus` | ✅ Implemented | ⚠️ Partial (no details) | **INCOMPLETE** |
| `partUpdated` | ✅ With scheduler | ⚠️ Direct posting | **INCOMPLETE** |
| `ready` | ✅ With serverInfo | ✅ Basic implementation | OK |
| `connectionState` | ✅ Implemented | ✅ Implemented | OK |
| `profileData` | ✅ Implemented | ✅ Implemented | OK |
| `sessionUpdated` | ✅ Implemented | ⚠️ Missing summary | **INCOMPLETE** |
| `sessionTurnClosed` | ✅ Implemented | ✅ Implemented | OK |

## Required Implementations

### High Priority (Critical for Basic Functionality)

1. **SessionStreamScheduler.cs** - Match TypeScript implementation for message coalescing and throttling
2. **Session creation flow** - Send `sessionCreated` message after session creation
3. **messagesLoaded handler** - Send full message history to webview
4. **Editor context** - Capture and send editor context with user messages

### Medium Priority (Required for Full Parity)

5. **Memory loading** - Implement memory state retrieval and `memoryLoaded` message
6. **Session model usage** - Track and send `sessionModelUsageLoaded` messages
7. **Workspace directory tracking** - Send `workspaceDirectoryChanged` messages
8. **Part removal events** - Implement `partRemoved` for cleanup

### Low Priority (Polish)

9. **Step start/finish events** - Track step boundaries
10. **Session status map seeding** - Properly initialize and send status updates
11. **Session summary** - Include `summary` in `sessionUpdated` messages

## Conclusion

The Visual Studio extension is missing approximately 10 critical message types and the entire `SessionStreamScheduler` infrastructure that VS Code uses to manage streaming messages. This results in:

1. Webview not receiving session creation notifications
2. No conversation history loading
3. No memory state
4. No model usage tracking
5. No proper message batching/coalescing
6. Missing editor context

These gaps must be addressed to achieve 1:1 message protocol compatibility as required by the migration plan.
