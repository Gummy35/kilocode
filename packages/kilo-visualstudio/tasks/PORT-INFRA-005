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

## Context

The Visual Studio extension already has:

- a strongly-typed SSE event model;
- Newtonsoft.Json as the SSE JSON implementation;
- shared serializer configuration;
- explicit discriminator-based polymorphic deserialization;
- strongly-typed WebView DTOs generated from the VS Code TypeScript contract;
- WebView protocol mapping infrastructure.

However, the current `SSEHelper` event-processing logic is still substantially simpler than the corresponding VS Code `KiloProvider` implementation.

The objective is therefore no longer primarily JSON typing.

The objective is behavioral parity with the VS Code event-processing pipeline.

The VS Code implementation must remain the authoritative behavioral reference.

---

# Scope

Analyze the complete event-processing path surrounding `KiloProvider` in VS Code and the corresponding implementation in Visual Studio.

At minimum, analyze:

## VS Code

- `KiloProvider.ts`
- `connection-utils.ts`
- `ConnectionService`
- `unwrapSyncEvent()`
- `resolveEventSessionId()`
- `resolveSyncSessionId()`
- `resolveTransientSessionId()`
- `onEventFiltered()`
- the filtering predicate used by `doInitializedConnection()`
- `handleEvent()`
- all directly relevant helper functions called by `handleEvent()`
- session tracking
- revision tracking
- child-session adoption
- WebView message mapping
- relevant tests

## Visual Studio

Analyze the current implementation of:

- `SseClient`
- `SseEventDeserializer`
- `SSEHelper`
- session tracking services/state
- revision/session state
- WebView DTO mapping
- WebView message dispatch
- relevant generated DTOs
- relevant tests

Do not assume that the existing implementation or previous plans are still accurate.

The repository has evolved significantly since the earlier SSE architecture work.

---

# Required Analysis

## 1. SSE normalization

Determine whether Visual Studio currently has an exact equivalent of the VS Code:

```text
unwrapSyncEvent()
````

Analyze:

* sync event wrappers;
* `.1` event names;
* `payload`;
* `syncEvent`;
* legacy/direct sync formats;
* transient/stream events;
* event IDs;
* sequence numbers.

Determine whether normalization is:

* complete;
* partial;
* unnecessary because an equivalent abstraction already exists.

Do not introduce a duplicate abstraction if an existing one already provides the required behavior.

---

## 2. Session ID resolution

Compare the VS Code:

```text
resolveEventSessionId()
resolveSyncSessionId()
resolveTransientSessionId()
```

with the Visual Studio implementation.

Document:

* every supported event category;
* where `sessionID` is obtained;
* special cases;
* message-to-session mapping;
* events without a session ID;
* child-session handling.

Produce a behavioral comparison table.

---

## 3. Event filtering

Analyze the exact filtering behavior performed by VS Code before `handleEvent()`.

Document all special cases, including but not limited to:

* globally scoped events;
* memory events;
* remote status events;
* session-scoped part events;
* unknown sessions;
* `session.created`;
* pending follow-up sessions;
* `session.status`;
* `session.deleted`;
* tracked sessions.

Compare this with the Visual Studio implementation.

Do not simplify the VS Code behavior unless the analysis proves that a simplification is semantically equivalent.

---

## 4. Revision / sequence handling

Analyze how VS Code prevents stale or duplicate event processing.

Determine:

* what constitutes a revision;
* how `id` is used;
* how `seq` is used;
* whether both legacy and versioned events exist;
* where revision state is stored;
* when revision state is updated;
* which events participate in revision tracking.

Compare with Visual Studio.

Explicitly identify any behavior that is missing or incorrectly implemented.

---

## 5. Event handling

Perform a behavioral comparison of every relevant event handled by VS Code and Visual Studio.

At minimum include:

* `message.updated`
* `message.part.updated`
* `message.part.delta`
* `message.part.removed`
* `session.created`
* `session.updated`
* `session.deleted`
* `session.status`
* `session.error`
* `permission.asked`
* `permission.replied`
* `question.asked`
* `question.replied`
* `suggestion.shown`
* `suggestion.accepted`
* `suggestion.dismissed`
* `todo.updated`
* memory events
* remote status events
* other events currently handled by the VS Code implementation

For each event, determine:

1. Whether VS Code handles it.
2. Whether Visual Studio handles it.
3. Whether the behavior is equivalent.
4. Whether the event affects session state.
5. Whether revision tracking applies.
6. Whether it produces a WebView message.
7. Whether it modifies session tracking.
8. Whether it triggers child-session adoption.
9. Whether there are side effects missing in Visual Studio.

---

## 6. Child-session adoption

Analyze the VS Code logic responsible for discovering and adopting child sessions.

Pay particular attention to task/tool parts and their metadata.

Determine exactly:

* how the child session ID is discovered;
* which part/state structures are inspected;
* when the session becomes tracked;
* what additional actions are triggered;
* whether the Visual Studio implementation already performs any equivalent behavior.

Do not invent a new child-session mechanism.

Port the existing VS Code semantics only if required.

---

## 7. WebView interaction

The WebView protocol is defined by the VS Code extension.

The Visual Studio implementation must remain compatible with that protocol.

Analyze whether current SSE event handling produces the same semantic WebView messages as VS Code.

Use the strongly-typed WebView DTO infrastructure created by `PORT-WEBVIEW-001` and `PORT-WEBVIEW-002` as the target architecture.

Identify where Visual Studio still uses:

* raw `JToken`;
* `JObject`;
* anonymous structures;
* manually constructed JSON;
* weakly typed objects.

For each occurrence, determine whether it can safely be replaced by an existing generated DTO.

Do not create new DTOs unless the audit proves that the VS Code protocol contains a contract that is currently missing.

---

# Test Audit

Analyze the VS Code tests related to:

* SSE event processing;
* event filtering;
* session tracking;
* session lifecycle;
* revision handling;
* child-session adoption;
* WebView messages;
* permissions;
* questions;
* todos;
* memory;
* session status.

Determine which tests have already been ported to Visual Studio.

For every missing test:

* identify the VS Code test;
* describe the behavior it validates;
* identify the corresponding Visual Studio test location;
* determine whether it should be ported;
* identify any necessary adaptation caused by C# or Visual Studio architecture.

Do not implement the tests during this task.

---

# Architecture Constraints

The following constraints are mandatory.

## Source of truth

VS Code is the behavioral source of truth.

Do not redesign behavior independently.

## JSON

Continue using:

```text
Newtonsoft.Json
```

for the SSE/backend pipeline.

Do not introduce `System.Text.Json` into the SSE pipeline.

## WebView

The WebView protocol is external to the Visual Studio implementation.

Do not modify the protocol.

The VS Code extension defines the protocol.

Visual Studio must consume and produce compatible messages.

## Strong typing

Prefer existing strongly-typed models and generated WebView DTOs.

Do not replace strongly typed models with `JObject`/`JToken` merely for convenience.

Conversely, do not force weak typing out of places where the protocol genuinely requires forward-compatible arbitrary JSON.

## Generated code

Do not modify NSwag-generated files.

Do not modify generated WebView DTOs manually.

If a generated type is insufficient, document the required generator/source change instead.

## Minimal changes

The goal is:

> smallest possible set of changes required for behavioral parity.

Do not:

* introduce a new event framework;
* introduce a generic event bus;
* introduce unnecessary abstractions;
* redesign SSE transport;
* redesign WebView communication;
* redesign session management;
* duplicate existing infrastructure;
* port unrelated VS Code functionality.

---

# Required Deliverables

Create/update:

```text
packages/kilo-visualstudio/porting/docs/prompts/PORT-INFRA-005.md
```

Use the previous `PORT-INFRA-*` documentation as the format and history model.

The document must contain:

1. Audit scope.
2. VS Code source files analyzed.
3. Visual Studio source files analyzed.
4. Current architecture diagrams.
5. Event normalization comparison.
6. Session ID resolution comparison.
7. Event filtering comparison.
8. Revision handling comparison.
9. Child-session adoption comparison.
10. Event-by-event behavioral parity matrix.
11. WebView compatibility analysis.
12. Test coverage comparison.
13. Missing behavior.
14. Redundant or obsolete Visual Studio behavior.
15. Minimal proposed changes.
16. Files that would need modification.
17. Files that must NOT be modified.
18. Risks.
19. Validation strategy.
20. Final recommendation.

Also update:

```text
packages/kilo-visualstudio/porting/docs/TASKS.md
```

to reflect the actual status of PORT-INFRA-005.

Do not mark the task `DONE` unless every required audit section is complete.

---

# Required Plan

At the end of the audit, produce a concrete implementation plan for the next implementation task.

The plan must be ordered by dependency.

The preferred conceptual order is:

```text
1. SSE normalization
        ↓
2. Session ID resolution
        ↓
3. Event filtering
        ↓
4. Revision handling
        ↓
5. Child-session adoption
        ↓
6. Event handling parity
        ↓
7. WebView mapping parity
        ↓
8. Missing tests
```

However, do not assume this order is correct.

Change it if the actual code analysis demonstrates a better dependency order.

For each proposed change specify:

* file;
* exact responsibility;
* reason;
* VS Code source reference;
* current Visual Studio state;
* proposed minimal change;
* tests required.

---

# Important: No Implementation

This is an audit and planning task only.

Do NOT modify:

* Visual Studio production code;
* Visual Studio tests;
* VS Code production code;
* VS Code tests;
* generated NSwag files;
* generated WebView DTOs.

The only files that may be modified are the explicitly requested documentation/task files:

```text
packages/kilo-visualstudio/porting/docs/prompts/PORT-INFRA-005.md
packages/kilo-visualstudio/porting/docs/TASKS.md
```

Do not create implementation files.

---

# Validation

Before finishing:

* verify that no production source code was modified;
* verify that no tests were modified;
* verify that no generated code was modified;
* verify that VS Code remains the behavioral reference;
* verify that all proposed changes are grounded in actual VS Code code;
* verify that all referenced helper functions and tests actually exist;
* verify that no behavior is proposed solely from assumptions;
* verify that the proposed implementation plan does not duplicate existing infrastructure;
* verify that the WebView protocol remains unchanged.

---

# Final Status

Use one of:

* `PASS`
* `PASS WITH FINDINGS`
* `BLOCKED`

Expected status:

```text
PORT-INFRA-005 → REVIEW
```

The task must remain `REVIEW` until the resulting implementation plan has been reviewed and approved.

