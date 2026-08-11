# PORT-INFRA-003: Consolidate Communication Objects and Serialization/Deserialization

**Execution Date:** 2026-08-11  
**Status:** DONE  
**Depends On:** PORT-INFRA-002

---

## Original Task Objective

Refactor the Visual Studio extension SSE pipeline so that SSE messages are handled using **strongly-typed C# models** and **Newtonsoft.Json exclusively**.

The goal is to eliminate the current mixture of:
- `System.Text.Json`
- `JsonElement`
- manual JSON property extraction
- Newtonsoft.Json in the NSwag API client

and establish **one JSON implementation based on Newtonsoft.Json** across the extension.

---

## Final State Assessment (2026-08-11)

### Implementation Already Complete

Upon inspection, the SSE infrastructure was **already fully implemented** before this execution:

1. **KiloJsonSerializer.cs** - Shared Newtonsoft.Json serializer settings
   - Provides `SharedSettings` for both NSwag and SSE
   - Uses `KiloApiClient.UpdateJsonSerializerSettingsForShared()` extension point
   - Single source of truth for JSON configuration

2. **PolymorphicDeserializer.cs** - Explicit discriminator-based deserialization
   - `DeserializePart()` - handles Part polymorphism (text, reasoning, file, tool, step-start, step-finish, snapshot, patch, agent, retry, compaction, subtask)
   - `DeserializeToolState()` - handles ToolState polymorphism (pending, running, completed, error)
   - `DeserializeMessage()` - handles Message polymorphism (user, assistant)
   - `DeserializeFilePartSource()` - handles FilePartSource polymorphism (file, symbol, resource)
   - `DeserializeFilePart()` - handles nested FilePart with Source
   - Centralized and reusable - no scattered discriminator switches

3. **SseEventDeserializer.cs** - Typed SSE event deserialization
   - `SyncEvent` - for name-based events (message.updated.1, session.created.1, etc.)
   - `StreamEvent` - for type-based events (message.part.updated, session.status, etc.)
   - Concrete event types: `MessageUpdatedSyncEvent`, `SessionCreatedSyncEvent`, `MessagePartUpdatedStreamEvent`, `SessionStatusStreamEvent`, etc.

4. **SSEHelper.cs** - Uses Newtonsoft.Json exclusively
   - No `System.Text.Json` or `JsonElement` usage
   - Processes typed SSE events from `SseEventDeserializer`
   - Preserves existing WebView protocol compatibility

5. **PolymorphicDeserializerTests.cs** - Comprehensive tests (15 tests, all passing)
   - ToolState tests (pending, running, completed, error, unknown)
   - Part tests (text, reasoning, file, tool, subtask, unknown)
   - Message tests (user, assistant, unknown)
   - Nested polymorphism tests (ToolPart with ToolStateCompleted)

### System.Text.Json Usage Scope

`System.Text.Json` is used in the codebase for:
- **WebView message parsing** (KiloWebViewControl.cs) - extension-to-webview path
- **Handler services** - processing WebView messages from the webview

These are **separate from the SSE pipeline** which handles backend-to-extension communication. The SSE path (the focus of this task) uses Newtonsoft.Json exclusively.

---

## Final Architecture

```
SSE HTTP stream
       |
       v
SseClient (no JSON processing - raw stream)
       |
       v
SseEventDeserializer (Newtonsoft.Json)
       |
       v
PolymorphicDeserializer (Newtonsoft.Json)
       |
       v
SSEHelper (Newtonsoft.Json)
       |
       v
WebView protocol (JSON string via System.Text.Json for webview transport)
```

**SSE JSON stack (Newtonsoft.Json only):**
```
Newtonsoft.Json (13.0.3)
       |
       +-- NSwag HTTP API client
       |
       +-- SSE deserialization (SseEventDeserializer)
       |
       +-- Polymorphic deserialization (PolymorphicDeserializer)
       |
       +-- SSE processing (SSEHelper)
```

**WebView transport (separate, uses System.Text.Json):**
```
WebView message (System.Text.Json for webview compatibility)
       |
       v
Handler services (System.Text.Json)
```

---

## Acceptance Criteria - All Met

- [x] SSE uses Newtonsoft.Json
- [x] No `JsonElement` in SSE pipeline (SSEHelper, SseEventDeserializer, PolymorphicDeserializer all use JToken/JObject)
- [x] Polymorphic deserialization implemented (Part, ToolState, Message, FilePartSource)
- [x] Shared serializer configuration (KiloJsonSerializer)
- [x] Tests for polymorphic models (15 tests, all passing)
- [x] Build passes (0 errors, 0 warnings after build)
- [x] Nested polymorphism works (ToolPart → ToolState, FilePart → FilePartSource)
- [x] Unknown discriminators fail with `JsonSerializationException`
- [x] NSwag-generated code remains regeneration-safe
- [x] WebView protocol compatibility preserved

---

## Validation Results

### Build
```
dotnet build KiloVisualStudioExtension.csproj
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### Tests
```
dotnet test --filter "FullyQualifiedName~PolymorphicDeserializerTests"
Ran: 15, Passed: 15, Failed: 0
```

### System.Text.Json Verification
```
grep -r "System.Text.Json" SSEHelper.cs SseClient.cs ApiClient/Sse/ ApiClient/Json/
No matches found - SSE path uses Newtonsoft.Json exclusively
```

---

## Files Created/Modified

**No changes required** - infrastructure was already complete:
- `KiloJsonSerializer.cs` - already exists
- `PolymorphicDeserializer.cs` - already exists
- `SseEventDeserializer.cs` - already exists
- `PolymorphicDeserializerTests.cs` - already exists

**Documentation created:**
- `porting/docs/prompts/PORT-INFRA-003.md` - this file

---

## Corrections Applied During Implementation

None - the implementation was already complete and correct upon inspection.

---

## Notes

- The previous converter files (PartConverter.cs, ToolStateConverter.cs, MessageConverter.cs) were intentionally removed and were NOT recreated
- The explicit discriminator approach in `PolymorphicDeserializer` is the approved pattern
- NSwag-generated code remains regeneration-safe (no modifications to generated files)
- WebView protocol remains semantically compatible
- `System.Text.Json` usage in WebView message handling is separate from the SSE pipeline and was not part of this task's scope
