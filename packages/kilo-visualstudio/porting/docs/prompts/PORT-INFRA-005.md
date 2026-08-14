# PORT-INFRA-005 — SSE Event Processing Behavioral Parity Audit

## Status

REVIEW

## Type

Infrastructure / Architectural Analysis / Porting Audit

## Objective

Audit the current Visual Studio SSE event-processing pipeline against the current VS Code implementation and identify the minimum changes required to achieve behavioral parity.

The VS Code extension is the source of truth.

This task is an analysis and planning task only.

No production implementation changes are authorized by this task.

---

# Audit Scope

## Source Files Analyzed

### VS Code Extension (packages/kilo-vscode)

| File | Purpose | Lines |
|------|---------|-------|
| `src/KiloProvider.ts` | Main provider, SSE event handling, WebView message mapping | 2000+ |
| `src/kilo-provider-utils.ts` | Event normalization, session ID resolution, WebView message mapping | 672 |
| `src/services/cli-backend/connection-utils.ts` | `resolveEventSessionId()`, `resolveSyncSessionId()`, `resolveTransientSessionId()` | 62 |
| `src/services/cli-backend/connection-service.ts` | Shared SSE connection, event filtering, session tracking | 936 |
| `src/services/cli-backend/sdk-sse-adapter.ts` | SSE payload normalization | N/A |

### Visual Studio Extension (packages/kilo-visualstudio)

| File | Purpose | Lines |
|------|---------|-------|
| `KiloVisualStudioExtension/SSEHelper.cs` | SSE event handling, WebView message construction | 860 |
| `KiloVisualStudioExtension/SseClient.cs` | SSE transport, reconnection, heartbeat | 374 |
| `KiloVisualStudioExtension/ApiClient/Sse/SseEventDeserializer.cs` | Typed SSE event deserialization | 243 |
| `KiloVisualStudioExtension/ApiClient/Json/PolymorphicDeserializer.cs` | Polymorphic deserialization for Part, ToolState, Message | N/A |
| `KiloVisualStudioExtension/ApiClient/Json/KiloJsonSerializer.cs` | Shared Newtonsoft.Json configuration | N/A |

### Tests Analyzed

| VS Code Test | Visual Studio Test | Coverage |
|--------------|-------------------|----------|
| `tests/unit/abort.test.ts` | `KiloVisualStudioExtension.Tests/AbortTests.cs` | ✅ Exists |
| `tests/unit/abort-state.test.ts` | `KiloVisualStudioExtension.Tests/AbortStateTests.cs` | ✅ Exists |
| `test/kilocode/legacy-sse-event.test.ts` | ❌ Missing | ⚠️ No equivalent |

---

# Architecture Comparison

## VS Code SSE Pipeline

```
Backend SSE Stream
        │
        ▼
SdkSSEAdapter (normalize payload)
        │
        ▼
KiloConnectionService.onEvent()
        │
        ▼
resolveEventSessionId() ← Session ID resolution
        │
        ▼
Event filtering (onEventFiltered)
        │
        ▼
KiloProvider.handleEvent()
        │
        ▼
unwrapSyncEvent() ← Legacy sync format normalization
        │
        ▼
mapSSEEventToWebviewMessage()
        │
        ▼
WebView postMessage
```

## Visual Studio SSE Pipeline

```
Backend SSE Stream
        │
        ▼
SseClient (transport, reconnection)
        │
        ▼
SseEventDeserializer.Deserialize()
        │
        ▼
SSEHelper.HandleEvent()
        │
        ├─ HandleSyncEvent()
        │  └─ HandleMessageUpdatedSync()
        │  └─ HandleSessionCreatedSync()
        │  └─ ...
        │
        └─ HandleStreamEvent()
           └─ HandlePartDelta()
           └─ HandleSessionStatus()
           └─ ...
        │
        ▼
WebView postMessage (anonymous objects)
```

---

# Event Normalization Comparison

## VS Code: unwrapSyncEvent()

The VS Code implementation has explicit normalization for legacy sync events:

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

**Key behaviors:**
- Strips `.1` suffix from event names
- Extracts `id`, `seq`, `data` from sync envelope
- Adds `source: "sync"` marker for `session.updated.1`
- Returns `undefined` for unknown event names

## Visual Studio: SseEventDeserializer

The Visual Studio implementation uses typed deserialization:

```csharp
private static SseEvent DeserializeSyncEvent(JObject obj, JsonSerializer serializer)
{
    var name = obj["name"]?.Value<string>() ?? "";
    var id = obj["id"]?.Value<string>() ?? "";
    var seq = obj["seq"]?.Value<int>() ?? 0;
    var data = obj["data"];

    // Rebuild data compatible with NSwag generated types
    var d = new { id = id, type = name.Replace(".1", ""), properties = data };
    var s = JObject.FromObject(d);

    return name switch
    {
        "message.updated.1" => new MessageUpdatedSyncEvent { Data = PolymorphicDeserializer.DeserializeEventMessageUpdated(data, serializer) },
        "session.created.1" => new SessionCreatedSyncEvent { Data = PolymorphicDeserializer.DeserializeEventSessionCreated(s, serializer) },
        // ...
    };
}
```

**Comparison:**
- ✅ Both strip `.1` suffix
- ✅ Both extract `id`, `seq`, `data`
- ⚠️ Visual Studio rebuilds NSwag-compatible structure (different approach)
- ⚠️ Visual Studio uses strongly-typed event classes (better type safety)

**Gap:** Visual Studio does not have an explicit `unwrapSyncEvent` equivalent, but the deserialization achieves the same result through typed event classes.

---

# Session ID Resolution Comparison

## VS Code: resolveEventSessionId()

```typescript
export function resolveEventSessionId(
  event: SSEPayload,
  lookupMessageSessionId: (messageId: string) => string | undefined,
  onMessageUpdated?: (messageId: string, sessionId: string) => void,
): string | undefined {
  if (event.type === "sync") {
    return resolveSyncSessionId(event, onMessageUpdated)
  }

  if (event.type === "sandbox.status.changed") return event.properties.sessionID
  return resolveTransientSessionId(event)
}

function resolveSyncSessionId(event: SyncPayload, onMessageUpdated?: ...): string | undefined {
  if (event.name === "message.updated.1") {
    onMessageUpdated?.(event.data.info.id, event.data.sessionID)
  }
  return event.data.sessionID
}

function resolveTransientSessionId(event: TransientPayload): string | undefined {
  switch (event.type) {
    case "session.status":
    case "session.turn.open":
    case "session.turn.close":
    case "session.idle":
    case "session.error":
    case "todo.updated":
    case "message.part.delta":
    case "permission.asked":
    case "permission.replied":
    case "question.asked":
    case "question.replied":
    case "question.rejected":
    case "suggestion.shown":
    case "suggestion.accepted":
    case "suggestion.dismissed":
    case "session.network.asked":
    case "session.network.replied":
    case "session.network.rejected":
    case "session.network.restored":
      return event.properties.sessionID
    default:
      return undefined
  }
}
```

**Key behaviors:**
- Sync events: extract from `event.data.sessionID`
- `message.updated.1`: also records `messageID → sessionID` mapping for future lookups
- Transient events: explicit whitelist of event types that carry `sessionID`
- Unknown events: return `undefined`

## Visual Studio: Session ID Handling

Visual Studio extracts session IDs directly from event properties in each handler:

```csharp
// HandleMessageUpdatedSync
var sessionID = infoObj["sessionID"]?.Value<string>();

// HandlePartDelta
var sid = properties["sessionID"]?.Value<string>();
if (!string.IsNullOrEmpty(sid) && !_trackedSessionIds.Contains(sid)) {
    return; // Skip if session not tracked
}

// HandleSessionStatus
var sid = properties["sessionID"]?.Value<string>() ?? "";
```

**Comparison:**
- ✅ Both extract session IDs from event properties
- ⚠️ Visual Studio does NOT have `messageID → sessionID` mapping for fallback resolution
- ⚠️ Visual Studio does NOT have centralized session ID resolution logic
- ⚠️ Visual Studio handlers individually check for session tracking

**Gap:** Visual Studio lacks the `resolveEventSessionId` abstraction and the `messageID → sessionID` fallback mechanism.
---

# Event Filtering Comparison

## VS Code: Event Filtering

VS Code uses `onEventFiltered` in `KiloConnectionService`:

```typescript
onEventFiltered(filter: SSEEventFilter, listener: SSEEventListener): () => void {
  const wrapped: SSEEventListener = (event, directory) => {
    if (!filter(event, directory)) {
      return
    }
    listener(event, directory)
  }
  return this.onEvent(wrapped)
}
```

Filtering logic in `KiloProvider`:

```typescript
// Filter events from foreign projects
const isEventFromForeignProject = (event: StreamEvent, expectedProjectID: string | undefined): boolean => {
  if (!expectedProjectID || event.type !== "sync") return false
  if (event.name === "session.created.1" || event.name === "session.deleted.1") {
    return event.data.info.projectID !== expectedProjectID
  }
  if (event.name !== "session.updated.1") return false
  const project = event.data.info.projectID
  return project !== undefined && project !== expectedProjectID
}

// Filter part events for unknown sessions
const SESSION_SCOPED_PART_EVENTS = new Set(["message.part.updated", "message.part.delta", "message.part.removed"])
const isSessionScopedPartEvent = (type: string) => SESSION_SCOPED_PART_EVENTS.has(type)

// In handleEvent:
if (isSessionScopedPartEvent(event.type) && !sessionID) {
  return // Drop part events without session ID
}
```

## Visual Studio: Event Filtering

Visual Studio performs filtering inside individual handlers:

```csharp
// HandlePartDelta
if (!string.IsNullOrEmpty(sid) && !_trackedSessionIds.Contains(sid)) {
    System.Diagnostics.Debug.WriteLine($"[Kilo] SSEHelper: Skipping part update - session not tracked");
    return;
}

// HandleMemoryEvent
var local = string.IsNullOrEmpty(eventSessionID) || eventSessionID == active || _trackedSessionIds.Contains(eventSessionID);
if (!local) return;
```

**Comparison:**
- ✅ Both filter events for unknown/untracked sessions
- ⚠️ Visual Studio does NOT filter by project ID
- ⚠️ Visual Studio filtering is scattered across handlers (not centralized)
- ⚠️ Visual Studio does NOT have explicit `isEventFromForeignProject` logic

**Gap:** Visual Studio lacks project-based filtering and centralized event filtering.

---

# Revision Handling Comparison

## VS Code: Revision Tracking

VS Code tracks revisions in `KiloProvider`:

```typescript
private readonly revisions = new Map<string, { id: string; seq: number }>();

// In handleEvent for session.updated:
if (event.type === "sync" && event.name === "session.updated.1") {
  const prev = this.revisions.get(sessionID);
  if (prev && (BigInt(event.id) <= prev.id || event.seq <= prev.seq)) {
    return; // Drop stale event
  }
  this.revisions.set(sessionID, { id: event.id, seq: event.seq });
}
```

## Visual Studio: Revision Tracking

Visual Studio tracks revisions in `SSEHelper`:

```csharp
private readonly Dictionary<string, SessionRevision> _revisions = new Dictionary<string, SessionRevision>();

public class SessionRevision
{
    public long Id { get; set; }
    public int Seq { get; set; }
}

// In HandleSessionUpdatedSync:
if (!string.IsNullOrEmpty(evt.Id))
{
    var revision = new SessionRevision { Id = long.Parse(evt.Id), Seq = evt.Seq };
    _revisions[sessionID] = revision;
}
```

**Comparison:**
- ✅ Both track revision `(id, seq)` per session
- ⚠️ Visual Studio does NOT check for stale events before processing
- ⚠️ Visual Studio only updates revision on `session.updated.1` sync events

**Gap:** Visual Studio lacks stale event detection logic.

---

# Child-Session Adoption Comparison

## VS Code: Child Session Discovery

VS Code discovers child sessions through task/tool part metadata:

```typescript
// In handleEvent for message.part.updated:
if (part.metadata?.sessionId) {
  const childId = part.metadata.sessionId;
  if (!this.trackedSessionIds.has(childId)) {
    this.trackedSessionIds.add(childId);
    // Auto-adopt child session
  }
}
```

## Visual Studio: Child Session Adoption

Visual Studio has equivalent logic:

```csharp
// In HandlePartUpdatedSync:
var metadata = partObj["metadata"];
if (metadata != null && metadata is JObject metadataObj && metadataObj["sessionId"] != null)
{
    var childId = metadataObj["sessionId"].Value<string>();
    if (!string.IsNullOrEmpty(childId) && !_trackedSessionIds.Contains(childId))
    {
        System.Diagnostics.Debug.WriteLine($"[Kilo] SSEHelper: Auto-adopting child session: {childId}");
        _trackedSessionIds.Add(childId);
    }
}

// In HandlePartUpdatedStream:
if (part["metadata"]?["sessionId"] != null)
{
    var childId = part["metadata"]["sessionId"].Value<string>();
    if (!string.IsNullOrEmpty(childId) && !_trackedSessionIds.Contains(childId))
    {
        _trackedSessionIds.Add(childId);
    }
}
```

**Comparison:**
- ✅ Both discover child sessions from part metadata
- ✅ Both auto-adopt child sessions when discovered
- ✅ Both handle sync and stream events

**Status:** Visual Studio has equivalent child-session adoption behavior.
---

# Event-by-Event Behavioral Parity Matrix

| Event Type | VS Code Handles | VS2026 Handles | Behavior Equivalent | Revision Tracking | Child Adoption | WebView Message |
|------------|-----------------|----------------|---------------------|-------------------|----------------|-----------------|
| `message.updated` | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ `messageCreated` |
| `message.removed` | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ `messageRemoved` |
| `message.part.updated` | ✅ | ✅ | ✅ | ❌ | ✅ | ✅ `partUpdated` |
| `message.part.delta` | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ `partUpdated` |
| `message.part.removed` | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ `partRemoved` |
| `session.created` | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ `sessionCreated` |
| `session.updated` | ✅ | ✅ | ⚠️ (no stale check) | ✅ | ❌ | ✅ `sessionUpdated` |
| `session.deleted` | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ `sessionDeleted` |
| `session.status` | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ `sessionStatus` |
| `session.error` | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ `sessionError` |
| `session.turn.open` | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| `session.turn.close` | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ `sessionTurnClosed` |
| `permission.asked` | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ `permissionRequest` |
| `permission.replied` | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ `permissionResolved` |
| `question.asked` | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ `questionRequest` |
| `question.replied` | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ `questionResolved` |
| `question.rejected` | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ `questionResolved` |
| `suggestion.shown` | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ `suggestionRequest` |
| `suggestion.accepted` | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ `suggestionResolved` |
| `suggestion.dismissed` | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ `suggestionResolved` |
| `todo.updated` | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ `todoUpdated` |
| `memory.status` | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ `memoryEvent` |
| `memory.updated` | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ `memoryEvent` |
| `memory.error` | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ `memoryEvent` |
| `sandbox.status.changed` | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ `sandboxStatus` |
| `indexing.status` | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ `indexingStatusLoaded` |
| `session.network.*` | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| `global.disposed` | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ `global.disposed` |
| `server.instance.disposed` | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ `server.instance.disposed` |
| `global.config.updated` | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ `globalConfigUpdated` |
| `kilo-sessions.remote-status-changed` | ✅ | ✅ (ignored) | ✅ | ❌ | ❌ | ❌ |

---

# WebView Compatibility Analysis

## Current Visual Studio Approach

Visual Studio currently uses a mix of:
- Strongly-typed DTOs from `WebView.Generated` namespace (some handlers)
- Anonymous objects (many handlers)
- `JObject`/`JToken` for dynamic payloads

**Examples using anonymous objects:**

```csharp
// HandleSessionStatus
PostMessage(new
{
    type = "sessionStatus",
    sessionID = sid,
    status = statusType,
    extra = new { ... }
});

// HandlePartDelta
PostMessage(new
{
    type = "partUpdated",
    sessionID = sid,
    messageID,
    part = new { id = partID, type = "text", messageID, text = delta },
    delta = new { type = "text-delta", textDelta = delta }
});
```

**Examples using typed DTOs:**

```csharp
// HandleMessageUpdatedSync
PostMessage(new MessageCreatedMessage { Message = message });

// HandleSessionCreatedSync
PostMessage(new SessionCreatedMessage { Session = new SessionInfo { ... } });
```

## VS Code WebView Protocol

VS Code defines explicit TypeScript types for all WebView messages:
- `WebviewMessage` union (140+ types)
- `ExtensionMessage` union (130+ types)
- Generated C# DTOs available from PORT-WEBVIEW-001

**Gap:** Visual Studio should use generated DTOs consistently instead of anonymous objects.

---

# Test Coverage Comparison

## VS Code Tests

| Test File | Purpose | Coverage |
|-----------|---------|----------|
| `tests/unit/abort.test.ts` | Abort message handling | ✅ |
| `tests/unit/abort-state.test.ts` | Abort state management | ✅ |
| `test/kilocode/legacy-sse-event.test.ts` | Legacy SSE event format | ✅ |
| `tests/unit/agent-manager-arch.test.ts` | WebView message structure | ✅ |
| `tests/unit/connection-service.test.ts` | Backend communication | ✅ |

## Visual Studio Tests

| Test File | Purpose | Coverage |
|-----------|---------|----------|
| `AbortTests.cs` | Abort message handling | ✅ |
| `AbortStateTests.cs` | Abort state management | ✅ |
| ❌ Missing | Legacy SSE event format | ❌ |
| `AgentManagerArchTests.cs` | WebView message structure | ✅ |
| ❌ Missing | Connection service SSE handling | ❌ |

**Missing Test Coverage:**
1. Legacy SSE event format (`legacy-sse-event.test.ts` equivalent)
2. Connection service SSE event filtering
3. Session ID resolution logic
4. Revision tracking behavior
5. Child session adoption
---

# Findings

## Critical Gaps

1. **No `unwrapSyncEvent` equivalent**: Visual Studio uses typed deserialization instead, which achieves the same result but through a different mechanism.

2. **No centralized session ID resolution**: VS Code has `resolveEventSessionId()` with fallback `messageID → sessionID` mapping. Visual Studio extracts session IDs individually in each handler.

3. **No `messageID → sessionID` mapping**: VS Code records this mapping for `message.updated.1` events to resolve session IDs for related events. Visual Studio lacks this.

4. **No stale event detection**: VS Code checks revision `(id, seq)` before processing `session.updated.1` events. Visual Studio only updates revision but doesn't check for stale events.

5. **No project-based filtering**: VS Code filters events from foreign projects based on `projectID`. Visual Studio lacks this.

6. **Inconsistent WebView message construction**: Visual Studio uses anonymous objects in many handlers instead of generated DTOs.

7. **Missing event handlers**:
   - `session.turn.open` (VS Code only)
   - `session.network.*` family (VS Code only)

## Strengths

1. **Typed deserialization**: Visual Studio uses `SseEventDeserializer` with strongly-typed event classes.

2. **Child session adoption**: Visual Studio correctly discovers and adopts child sessions from part metadata.

3. **Newtonsoft.Json**: Both use Newtonsoft.Json exclusively for SSE pipeline (no System.Text.Json).

4. **Polymorphic deserialization**: Visual Studio has `PolymorphicDeserializer` for Part, ToolState, Message types.

---

# Minimal Proposed Changes

## Priority 1: Core Event Processing

| File | Responsibility | Current State | Proposed Change |
|------|---------------|---------------|-----------------|
| `SSEHelper.cs` | Session ID resolution | Individual extraction in each handler | Add `ResolveSessionId()` helper method matching VS Code's `resolveEventSessionId()` |
| `SSEHelper.cs` | `messageID → sessionID` mapping | Not present | Add `Dictionary<string, string> _messageSessionIds` and record on `message.updated.1` |
| `SSEHelper.cs` | Stale event detection | Not present | Check revision before processing `session.updated.1` |
| `SSEHelper.cs` | Project filtering | Not present | Add project ID tracking and filter foreign project events |

## Priority 2: WebView Message Typing

| File | Responsibility | Current State | Proposed Change |
|------|---------------|---------------|-----------------|
| `SSEHelper.cs` | `HandleSessionStatus` | Anonymous object | Use generated DTO or create typed `SessionStatusMessage` |
| `SSEHelper.cs` | `HandlePartDelta` | Anonymous object | Use generated `PartUpdate` DTO |
| `SSEHelper.cs` | `HandlePermissionAsked` | Anonymous object | Use generated `PermissionRequestMessage` |
| `SSEHelper.cs` | All remaining anonymous `PostMessage` calls | Anonymous objects | Replace with generated DTOs |

## Priority 3: Missing Event Handlers

| File | Event | Current State | Proposed Change |
|------|-------|---------------|-----------------|
| `SSEHelper.cs` | `session.turn.open` | Not handled | Add handler (may be no-op if no WebView message needed) |
| `SSEHelper.cs` | `session.network.*` | Not handled | Add handlers (may be no-op if no WebView message needed) |

---

# Implementation Order

1. **Add session ID resolution helper** (`ResolveSessionId()`)
   - Centralize session ID extraction logic
   - Add `messageID → sessionID` mapping
   - Update all handlers to use the helper

2. **Add stale event detection**
   - Check revision before processing `session.updated.1`
   - Compare `(id, seq)` pairs

3. **Add project-based filtering**
   - Track expected project ID
   - Filter `session.created.1`, `session.updated.1`, `session.deleted.1` by project ID

4. **Replace anonymous objects with generated DTOs**
   - Start with high-frequency events (`sessionStatus`, `partUpdated`)
   - Progress to less frequent events

5. **Add missing event handlers**
   - `session.turn.open`
   - `session.network.*` family

---

# Files to Modify (Next Task)

## Production Code

1. `packages/kilo-visualstudio/KiloVisualStudioExtension/SSEHelper.cs`
   - Add `ResolveSessionId()` method
   - Add `_messageSessionIds` dictionary
   - Add revision checking logic
   - Add project filtering logic
   - Replace anonymous objects with generated DTOs
   - Add missing event handlers

## Tests (Future - Not This Task)

1. `packages/kilo-visualstudio/KiloVisualStudioExtension.Tests/SseEventResolutionTests.cs` (new)
2. `packages/kilo-visualstudio/KiloVisualStudioExtension.Tests/RevisionTrackingTests.cs` (new)
3. `packages/kilo-visualstudio/KiloVisualStudioExtension.Tests/ProjectFilteringTests.cs` (new)

---

# Files That Must NOT Be Modified

- `packages/kilo-vscode/*` (VS Code source of truth)
- `packages/kilo-visualstudio/KiloVisualStudioExtension/ApiClient/Sse/*` (deserialization infrastructure is correct)
- `packages/kilo-visualstudio/KiloVisualStudioExtension/ApiClient/Json/*` (serializer configuration is correct)
- `packages/kilo-visualstudio/KiloVisualStudioExtension/SseClient.cs` (transport layer is correct)
- Generated WebView DTOs (`WebView/Generated/*`)
- NSwag-generated API client files

---

# Validation Strategy

1. **Unit tests for session ID resolution**
   - Test sync events
   - Test transient events
   - Test `messageID → sessionID` fallback

2. **Unit tests for revision tracking**
   - Test stale event rejection
   - Test revision update on valid events

3. **Unit tests for project filtering**
   - Test foreign project event rejection
   - Test local project event acceptance

4. **Integration tests for WebView messages**
   - Verify generated DTOs serialize correctly
   - Verify wire format matches VS Code protocol

5. **Manual validation**
   - Compare SSE event handling with VS Code logs
   - Verify child session adoption works
   - Verify no regressions in existing functionality

---

# Final Recommendation

**Proceed with implementation** of the Priority 1 changes in a new task (e.g., `PORT-SSE-001`).

The Visual Studio SSE infrastructure is fundamentally sound:
- Typed deserialization is correct
- Newtonsoft.Json usage is correct
- Child session adoption works
- Most event handlers are present

The gaps are primarily in:
- Session ID resolution abstraction
- Stale event detection
- Project-based filtering
- Consistent use of generated DTOs

These are incremental improvements that build on the existing architecture rather than requiring a redesign.

---

# Final Status

**PORT-INFRA-005 → REVIEW**

The audit is complete. The resulting implementation plan is ready for review and approval before proceeding to the next task.
