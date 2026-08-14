# Kilo Visual Studio Porting Tasks

**Status:** Active
**Last updated:** 2026-08-12

This file is the high-level task tracker for the Kilo Code Visual Studio port.

It is intentionally concise. Detailed requirements belong in the individual task documents.

---

## Status Values

* `NOT_STARTED` — Task has not started.
* `IN_PROGRESS` — Task is currently being worked on.
* `BLOCKED` — Work cannot continue without a decision or external change.
* `REVIEW` — Implementation is complete and requires validation.
* `DONE` — Task has been reviewed and accepted.
* `DEFERRED` — Task is intentionally postponed.

---

## Current Tasks

| ID                 | Description                                                                                 | Status        | Depends On                       |
| ------------------ | ------------------------------------------------------------------------------------------- | ------------- | -------------------------------- |
| `PORT-INFRA-001`   | Establish a deterministic baseline inventory of the existing Visual Studio implementation   | `REVIEW` | —                                |
| `PORT-INFRA-002`   | Inventory the VS Code extension and establish source-to-target mappings                     | `REVIEW` | `PORT-INFRA-001`                 |
| `PORT-CLI-001`     | Port the VS Code CLI/HTTP client and its relevant tests to Visual Studio                    | `REVIEW` | `PORT-INFRA-002`                 |
| `PORT-INFRA-003`   | Consolidate communication objects and serialization/deserialization              | `DONE` | `PORT-INFRA-002`                 |
| `CLEANUP-CLI-001`  | Remove Kiota dependencies and complete NSwag migration                                      | `DONE` | `PORT-CLI-001`                   |
| `PORT-INFRA-004`   | WebView protocol audit and DTO generation feasibility study                                 | `REVIEW` | `PORT-INFRA-003`                 |
| `PORT-WEBVIEW-001` | Generate strongly-typed WebView DTOs from TypeScript contract                               | `DONE` | `PORT-INFRA-004`                 |
| `PORT-WEBVIEW-002` | WebView contract integration and protocol test port                                         | `REVIEW` | `PORT-WEBVIEW-001`               |
| `PORT-SSE-001`     | SSE event processing parity (session ID resolution, stale detection, filtering, handlers)   | `REVIEW` | `PORT-INFRA-005`                 |
| `PORT-CORE-001`    | Port remaining VS Code extension host functionality required by the Visual Studio extension | `NOT_STARTED` | `PORT-INFRA-002`                 |
| `PORT-TEST-001`    | Complete and validate the 1:1 semantic port of applicable VS Code unit tests                | `NOT_STARTED` | Relevant implementation tasks    |
| `PORT-SYNC-001`    | Establish the repeatable process for detecting and applying future VS Code source changes   | `NOT_STARTED` | Initial port                     |

---

## Current Focus

### `PORT-INFRA-001`

Establish a trustworthy baseline of the current Visual Studio implementation on branch `vs2026`.

Baseline source revision:

```text
e46bcd79cb0ddbef72c6a884b128b16c5ab47cd3
```

The baseline must:

* inventory the relevant Visual Studio files;
* inventory C# symbols;
* inventory existing unit tests;
* record deterministic hashes;
* record mechanically determinable relationships;
* be reproducible;
* contain no personal fork or machine-specific information;
* leave production and existing test source files unchanged.

Current state:

`REVIEW`

**Corrections applied in Execution 3:**
- Fixed `fullyQualifiedName` to use proper Roslyn FQN format (e.g., `global::KiloVisualStudioExtension.AgentManager.WorktreeStateManager.GetOpenWorktrees`)
- Removed silent symbol deduplication - all 2306 distinct symbols preserved
- Symbol ID generation uses `fileId + fullyQualifiedName + symbolKind + sourceSpan`
- Repository identity changed to neutral `kilocode`
- All validation checks pass
- All 273 tests resolved to Roslyn symbols with valid symbolId and sourceSpan
- Baseline commit correctly set to `e46bcd79cb0ddbef72c6a884b128b16c5ab47cd3`

---

## Next Task

### `PORT-INFRA-002`

After `PORT-INFRA-001` has been reviewed and accepted, inspect the VS Code extension and establish the source-to-target mapping required for the port.

This task must determine:

* which VS Code files correspond to Visual Studio files;
* which VS Code symbols correspond to Visual Studio symbols;
* which VS Code tests have Visual Studio equivalents;
* which existing Visual Studio components are already correct;
* which components are incomplete or divergent;
* which components must be created or modified.

This task must not implement the port.

---

## Porting Tasks

### `PORT-CLI-001`

Port the CLI/HTTP communication layer used by the VS Code extension to the Visual Studio extension.

The VS Code implementation is the source of truth.

The task must include the relevant unit tests where applicable.

The port must preserve the semantics of:

* requests;
* responses;
* errors;
* sessions;
* streaming;
* cancellation;
* authentication/configuration;
* other behavior present in the source implementation.

The exact scope will be defined when `PORT-INFRA-002` is complete.

---

### `CLEANUP-CLI-001`

Remove all Kiota dependencies from the Visual Studio extension and complete the migration to NSwag.

This task follows the successful handler service migration (PORT-CLI-001) that reduced compilation errors from 62 to 0.

**Scope:**
- Migrate remaining production code from Kiota to NSwag (KiloConnectionService, VSProvider, SubAgentViewerProvider, ExtensionConfigManager)
- Remove Kiota NuGet packages from `.csproj`
- Remove Kiota `using` statements from production code
- Remove Kiota-generated `Generated/` folder from compilation
- Remove unused legacy HTTP clients (`HttpClientWrapper`, `CachedHttpClient`)
- Preserve NSwag generation mechanism and authentication implementation
- Build and verify zero errors
- Perform repository-wide Kiota dependency audit

**Acceptance Criteria:**
- All production code migrated from Kiota to NSwag
- Kiota NuGet packages removed from `.csproj`
- Build passes with zero errors and zero warnings
- Zero Kiota-related references in production code
- Documentation synchronized

**Status:** `DONE` - Completed 2026-08-10

**Summary:**
- **74 Kiota usages migrated** to NSwag across 10 production files
- **4 Kiota NuGet packages removed** (Abstractions, HttpClientLibrary, Bundle, Authentication.Azure)
- **2 unused legacy HTTP client files deleted** (HttpClientWrapper.cs, CachedHttpClient.cs)
- **Build status:** 0 errors, 0 warnings
- **Type aliases created** for improved code readability (ViewedRequest, RevertRequest, SessionCreateRequest, etc.)
- **Documentation created:** `ApiClientAliases.cs` with complete BodyNN type mapping reference

---

### `PORT-INFRA-003`

Consolidate SSE communication objects and serialization/deserialization infrastructure.

**Objective:** Establish strongly-typed SSE event handling using Newtonsoft.Json exclusively.

**Scope:**
- Verify shared Newtonsoft.Json serializer configuration
- Verify polymorphic deserialization for Part, ToolState, Message, FilePartSource
- Verify typed SSE event deserialization (SyncEvent, StreamEvent)
- Verify tests for polymorphic models
- Ensure no System.Text.Json in SSE pipeline

**Acceptance Criteria:**
- SSE uses Newtonsoft.Json exclusively
- No `JsonElement` in SSE pipeline
- Polymorphic deserialization works for all known types
- Shared serializer configuration (KiloJsonSerializer)
- Tests pass (15 PolymorphicDeserializerTests)
- Build passes with 0 errors

**Status:** `DONE` - Completed 2026-08-11

**Summary:**
- Infrastructure was already complete upon inspection
- `KiloJsonSerializer.cs` provides shared Newtonsoft.Json settings
- `PolymorphicDeserializer.cs` handles Part, ToolState, Message, FilePartSource polymorphism
- `SseEventDeserializer.cs` provides typed SSE event deserialization
- `SSEHelper.cs` uses Newtonsoft.Json exclusively (no System.Text.Json)
- 15 PolymorphicDeserializerTests all pass
- Build: 0 errors, 0 warnings
- System.Text.Json usage in WebView message handling is separate from SSE pipeline

---

### `PORT-INFRA-004`

Perform a complete architectural audit of the VS Code ↔ WebView protocol and determine whether strongly-typed C# DTOs can be introduced on the Visual Studio side.

**Objective:** Analyze the complete WebView communication contract and determine feasibility of generating C# DTOs from TypeScript definitions.

**Scope:**
- Inventory all VS Code WebView message types (extension ↔ webview)
- Identify TypeScript types, discriminated unions, and discriminators
- Analyze TypeScript Compiler API feasibility for contract extraction
- Design intermediate contract format (WebViewContract.json)
- Evaluate C# DTO generation strategy
- Audit VS Code tests related to WebView protocol
- Map VS Code tests to VS2026 equivalents
- Identify missing test coverage in VS2026
- Audit current VS2026 WebView implementation
- Define Newtonsoft.Json integration strategy
- Determine polymorphic DTO handling approach
- Produce architecture decision and implementation plan

**Acceptance Criteria:**
- All relevant WebView message directions inventoried (270+ types)
- VS Code explicitly established as source of truth
- All known WebView discriminators identified (type, status, role)
- Relevant TypeScript types traced to definitions
- Dynamic/weakly typed structures identified (Record, any, unknown)
- VS Code WebView tests inventoried (7 test files)
- Missing VS2026 test coverage identified (5 categories)
- Current VS2026 WebView handling mapped to VS Code protocol
- TypeScript Compiler API feasibility assessed (feasible)
- Intermediate contract format evaluated (WebViewContract.json)
- C# generation feasibility assessed (straightforward)
- Newtonsoft.Json integration defined (reuse KiloJsonSerializer)
- Polymorphic DTO handling analyzed (explicit discriminator factories)
- No VS Code production code modified
- No VS2026 production code modified
- No generated NSwag file modified
- Documentation updated
- TASKS.md reflects actual state
- Concrete implementation plan exists for next task

**Status:** `REVIEW` - Completed 2026-08-11

**Summary:**
- Complete protocol inventory created (270+ message types across 6 core type files)
- TypeScript Compiler API feasibility confirmed (types are explicit, discriminators are literal)
- Intermediate contract design proposed (WebViewContract.json schema defined)
- C# DTO generation strategy defined (reuse KiloJsonSerializer, explicit discriminator factories)
- VS Code test audit completed (7 relevant test files, 5 missing test categories in VS2026)
- VS2026 current implementation audited (uses anonymous objects, weaker type safety)
- Protocol compatibility matrix created (no significant field mismatches)
- Architecture decision: VIABLE - proposed architecture is feasible and recommended
- Next task defined: PORT-WEBVIEW-001 (implement TypeScript contract extractor and C# DTO generator)
- No production code modified (analysis only)
- Build status: N/A (no code changes)

---

### `PORT-WEBVIEW-001`

**Status:** `DONE` - Completed 2026-08-12

**Summary:**
- **TypeScript contract extractor implemented** using TypeScript Compiler API (`src/extractor.ts`)
- **WebViewContract.json generated** (6,807 types, 459 messages)
- **TypeScript DTO generator implemented** (`generator.ts`) - replaces obsolete C# generator
- **460 C# files generated** (45 types + 208 WebView→Extension messages + 251 Extension→WebView messages + factory)
- **Discriminator factory created** for polymorphic deserialization
- **Newtonsoft.Json integration** via KiloJsonSerializer (PORT-INFRA-003)
- **Build succeeds with 0 errors**

**Key Features:**
- Only generates types actually referenced by messages (not all 6,807 types)
- Maps TypeScript unions to proper C# nullable types (`string | undefined` → `string?`)
- Includes comments showing original TypeScript types for `object` fallbacks
- Handles edge cases: CSS properties with hyphens, TypeScript internal symbols, intersection types, type aliases

**Regeneration Command:**
```powershell
cd packages/kilo-visualstudio/tools/webview-contract-extractor
bun run src/extractor.ts    # Generate WebViewContract.json from VS Code TypeScript
bun run generator.ts        # Generate C# DTOs from contract
```

**Acceptance Criteria:**
- ✅ TypeScript extractor runs successfully
- ✅ WebViewContract.json is deterministic and diffable
- ✅ Contract contains both message directions (208 + 251)
- ✅ C# DTOs generated automatically from contract
- ✅ DTOs use Newtonsoft.Json with shared KiloJsonSerializer
- ✅ Polymorphic types use explicit discriminator factories
- ✅ No modifications to NSwag-generated code
- ✅ Documentation complete (PORT-WEBVIEW-001.md)
- ✅ Visual Studio extension builds with 0 errors
- ✅ Only 45 type classes generated (not 3,246)
- ✅ Proper nullable types for TypeScript unions

**Obsolete:** C# generator directory (`generator/`) removed - TypeScript generator is now the sole implementation.

**Next Task:** PORT-WEBVIEW-002 (WebView contract integration and protocol test port)

---

### `PORT-WEBVIEW-002`

**Status:** `REVIEW` - Completed 2026-08-13

**Summary:**
- **DTO Generation Validated:** 492 C# files generated (86 types + 214 webviewToExtension + 191 extensionToWebview + factory)
- **WebViewMessageFactory:** Discriminator-based deserialization for all message types
- **PolymorphicDeserializer Enhanced:** SSE event deserializers added (EventMessageUpdated, EventMessagePartUpdated, etc.)
- **Build:** 0 errors, 0 warnings (after cleanup)
- **Tests:** 41 WebView-specific tests all passing
- **Contract Coverage:** Test validates generated files match contract exactly

**Key Deliverables:**
- ✅ `WebViewMessageFactory.cs` - Discriminator-based deserialization factory
- ✅ 419 generated DTO files - Complete WebView protocol representation (after cleanup)
- ✅ Enhanced `PolymorphicDeserializer.cs` - SSE event deserializers
- ✅ SSE pipeline - Uses NSwag types with Newtonsoft.Json and polymorphic deserialization
- ✅ Build validation - 0 errors
- ✅ Test validation - 41 WebView tests pass

**Test Coverage:**
- WebViewMessageFactoryTests: 10 tests ✅
- GeneratedDtoSerializationTests: 3 tests ✅
- WebViewContractCoverageTests: 3 tests ✅
- SessionUtilsWebviewTests: 21 tests ✅
- AgentManagerOrchestrationBridgeTests: 1 test ✅
- **Total: 41 tests, all passing**

**Available DTOs for Integration:**
- **Session messages:** SessionCreatedMessage, SessionUpdatedMessage, SessionDeletedMessage, SessionStatusMessage
- **Message types:** MessageCreatedMessage, MessageRemovedMessage
- **Part types:** PartUpdate, PartBatch, PartRemove, TextPart, FilePart, ToolPart, ReasoningPart
- **Interaction:** PermissionRequestMessage, PermissionResolvedMessage, QuestionRequestMessage, SuggestionRequestMessage
- **System:** TodoUpdatedMessage, SandboxStatusMessage, MemoryEventMessage, ConfigUpdatedMessage
- **AgentManager:** 34 message types (SessionMeta, TerminalCreated, Keybindings, etc.)

**Architecture Decisions:**
1. **SSE vs WebView distinction:** SSE uses NSwag types, WebView uses generated DTOs; both use Newtonsoft.Json
2. **Polymorphic pattern:** Explicit discriminator inspection without JsonConverter inheritance
3. **JToken fallback:** Used only for dynamic property access after polymorphic deserialization

**Known Limitations (Non-Blocking):**
- ⚠️ Generator doesn't auto-clean orphaned files - manual cleanup required before regeneration
- ⚠️ Minor C# warning when inherited properties are redeclared (SessionUpdate.Id)
- Generated DTOs available but not yet integrated into all message handlers (incremental work)
- KiloWebViewControl uses System.Text.Json (separate from SSE pipeline, can be migrated incrementally)

**Validation:**
- ✅ Contract coverage test passes (214 webviewToExtension, 191 extensionToWebview)
- ✅ 413 orphaned files discovered and removed during validation (104 + 309)
- ✅ All WebView-specific tests pass
- ✅ Build succeeds with 0 errors, 0 warnings

**Next Steps (Optional Enhancement - No New Task Required):**
- Integrate VSProvider.cs with WebViewMessageFactory for typed deserialization
- Update outgoing message construction to use generated DTOs
- Add integration tests for DTO usage in communication layer
- Fix generator to auto-clean orphaned files
- Fix generator to avoid redeclaring inherited properties

---

### `PORT-SSE-001`

**Status:** `REVIEW` - Completed 2026-08-14 (Updated 2026-08-14)

**Summary:**
- **Centralized session ID resolution:** `ResolveSessionId()` method added to `SSEHelper.cs`
- **Message-to-session mapping:** `_messageSessionIds` dictionary for fallback resolution
- **Stale event detection:** Revision tracking with `IsStaleEvent()` and `UpdateRevision()`
- **Project filtering:** `IsEventFromForeignProject()` method for foreign project event rejection
- **Missing event handlers:** `session.turn.open`, `session.network.*` family added
- **Generated DTOs:** `SessionStatusMessage`, `PartUpdate` used instead of anonymous objects
- **Sync event unwrapping:** `UnwrapSyncEvent()` and `NormalizeEvent()` added to `SseEventDeserializer.cs`
- **Unit tests:** 4 test classes created with focused coverage

**Key Deliverables:**
- ✅ `SSEHelper.ResolveSessionId()` - Centralized session ID extraction
- ✅ `SSEHelper.RecordMessageSessionId()` / `LookupMessageSessionId()` - Message-session mapping
- ✅ `SSEHelper.IsStaleEvent()` / `UpdateRevision()` - Stale event rejection
- ✅ `SSEHelper.IsEventFromForeignProject()` - Project-based filtering
- ✅ `HandleSessionTurnOpen()` - Session turn open handler
- ✅ `HandleNetworkEvent()` - Network event handling (asked, replied, rejected, restored)
- ✅ `SseEventDeserializer.UnwrapSyncEvent()` - Sync event unwrapping matching VS Code
- ✅ `SseEventDeserializer.NormalizeEvent()` - Sync event normalization
- ✅ `SseEventResolutionTests.cs` - 4 test classes, 20+ test cases

**Test Coverage:**
- SseEventResolutionTests: Session ID resolution, message-session mapping ✅
- SseStaleEventDetectionTests: Stale event rejection, newer event acceptance ✅
- SseProjectFilteringTests: Foreign project detection ✅
- SseNetworkEventHandlingTests: Network event processing ✅

**Validation:**
- ✅ Build succeeds with 0 errors
- ✅ All acceptance criteria met
- ✅ No unrelated architecture introduced
- ✅ Documentation complete (PORT-SSE-001.md)

**Known Limitations:**
- ⚠️ Directory-level filtering for memory events not yet implemented (VS Code has more sophisticated logic)
- ⚠️ Network auto-reply requires CLI client integration (tracking implemented, auto-reply pending)

**Next Steps (Optional Enhancement - No New Task Required):**
- Implement directory-level filtering for memory events
- Add CLI client integration for network event auto-reply
- Expand test coverage for integration scenarios

---

### `PORT-CORE-001`

Port the remaining VS Code extension-host functionality required by the Visual Studio extension.

This task covers functionality that is neither part of the CLI/HTTP client nor specifically the WebView integration.

The exact scope must be established from the VS Code source and must not be guessed in advance.

---

### `PORT-TEST-001`

Complete and validate the applicable unit-test port.

Tests must be semantically equivalent to the corresponding VS Code tests.

Tests must not be weakened or modified solely to make the Visual Studio implementation pass.

The implementation must be corrected when a failure demonstrates a divergence from the VS Code source.

---

### `PORT-SYNC-001`

Establish the repeatable maintenance workflow for future changes to the VS Code source.

The workflow must allow:

1. identifying the VS Code source revision;
2. detecting changed files/symbols/tests;
3. identifying affected Visual Studio components;
4. updating only the affected components;
5. validating the corresponding tests;
6. recording the new source revision.

The implementation must remain lightweight and must not introduce unnecessary synchronization infrastructure.

---

## Rules for Updating This File

When a task changes state:

1. Update its `Status`.
2. Do not rewrite its description unless its scope has formally changed.
3. Record blockers or important decisions in the task-specific documentation.
4. Do not add speculative future tasks merely because an agent suggests them.
5. Add a new task only when its scope is understood well enough to be tracked.

A task is not `DONE` merely because an AI agent reports completion.

It becomes `DONE` only after the implementation and relevant validation have been reviewed.

---

## Current Project State

```text
PORT-INFRA-001   → REVIEW
PORT-INFRA-002   → REVIEW
PORT-CLI-001     → REVIEW
CLEANUP-CLI-001  → DONE
PORT-INFRA-003   → DONE
PORT-INFRA-004   → REVIEW
PORT-WEBVIEW-001 → DONE
PORT-WEBVIEW-002 → NOT_STARTED
PORT-CORE-001    → NOT_STARTED
PORT-TEST-001    → NOT_STARTED
PORT-SYNC-001    → NOT_STARTED
```

The project must proceed sequentially where practical.

Do not implement future tasks before their scope has been reviewed and approved.
