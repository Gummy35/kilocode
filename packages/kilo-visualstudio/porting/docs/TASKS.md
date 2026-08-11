# Kilo Visual Studio Porting Tasks

**Status:** Active
**Last updated:** 2026-08-10

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
| `PORT-WEBVIEW-001` | Port the VS Code extension host/WebView integration to Visual Studio                        | `NOT_STARTED` | `PORT-INFRA-002`, `PORT-CLI-001` |
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

### `PORT-WEBVIEW-001`

Port the VS Code extension host/WebView integration required by the Visual Studio extension.

The existing Kilo WebView implementation should be reused where practical.

The task must preserve communication and behavior between the WebView and extension host.

The exact scope will be defined after the source-to-target mapping is established.

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
PORT-INFRA-002   → NOT_STARTED
PORT-CLI-001     → REVIEW
CLEANUP-CLI-001  → DONE
PORT-WEBVIEW-001 → NOT_STARTED
PORT-CORE-001    → NOT_STARTED
PORT-TEST-001    → NOT_STARTED
PORT-SYNC-001    → NOT_STARTED
```

The project must proceed sequentially where practical.

Do not implement future tasks before their scope has been reviewed and approved.
