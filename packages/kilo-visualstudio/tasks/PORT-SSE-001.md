# PORT-SSE-001 — SSE Event Processing Parity

## Status

PLANNED

## Objective

Bring the Visual Studio SSE event-processing pipeline into behavioral parity with the current VS Code implementation, based on the findings of `PORT-INFRA-005`.

The VS Code extension remains the source of truth.

The implementation must be minimal and targeted. Do not redesign the SSE architecture or introduce unnecessary abstractions.

## References

Before implementation, read:

- `packages/kilo-visualstudio/porting/docs/SPEC.md`
- `packages/kilo-visualstudio/porting/docs/TASKS.md`
- `packages/kilo-visualstudio/porting/docs/prompts/PORT-INFRA-005.md`
- `packages/kilo-visualstudio/porting/docs/prompts/PORT-INFRA-004.md`
- the current VS Code implementation under `packages/kilo-vscode`
- the current Visual Studio implementation under `packages/kilo-visualstudio`

The implementation must be based on the current source code, not on assumptions from previous audits.

## Scope

Implement the Priority 1 SSE processing changes identified by PORT-INFRA-005:

1. Centralized session ID resolution
2. Message-to-session fallback mapping
3. Stale event detection
4. Project/directory-aware event filtering
5. Use generated WebView DTOs where they already exist and are applicable
6. Preserve VS Code event semantics
7. Add or update tests for the implemented behavior

## 1. Centralized Session ID Resolution

Implement a single session ID resolution mechanism equivalent to the VS Code `resolveEventSessionId()` / related connection utilities.

It must:

- resolve session IDs from sync event payloads;
- resolve session IDs from transient/stream event properties;
- handle events where the session ID is nested in event-specific data;
- support the `messageID → sessionID` fallback used by VS Code;
- maintain the mapping only where required by the VS Code behavior.

Do not duplicate session ID extraction logic across individual handlers.

The implementation must use the existing event/model infrastructure wherever possible.

## 2. Message-to-Session Mapping

Port the VS Code behavior for events where a message identifies the session indirectly.

In particular:

- when a `message.updated` event provides both message ID and session ID, record the mapping;
- when a subsequent event contains only the message ID, use the mapping to resolve its session;
- ensure the mapping is updated consistently with the VS Code implementation.

Do not introduce a general-purpose cache framework.

Use the smallest suitable in-memory structure.

## 3. Event Filtering

Bring the Visual Studio filtering behavior into parity with the VS Code implementation.

The filtering phase must distinguish:

- global events;
- memory events;
- session-scoped events;
- message-part events;
- events belonging to tracked sessions;
- events which must always pass through.

Pay particular attention to the VS Code behavior for:

- `session.status`
- `session.deleted`
- `session.created`
- `kilo-sessions.remote-status-changed`
- memory events
- message part events
- pending follow-up sessions.

Do not invent new filtering rules.

Port the actual semantics from the current VS Code implementation.

Filtering must happen before business-event processing where that matches the VS Code architecture.

## 4. Project / Directory Filtering

Implement the project/directory filtering identified by PORT-INFRA-005.

The Visual Studio implementation must not process SSE events belonging to another project/workspace when the VS Code implementation would reject them.

Use the existing directory/project information already available in the Visual Studio extension.

Do not introduce a new project management abstraction.

If the existing Visual Studio architecture does not expose the required information in exactly the same place as VS Code, adapt the smallest possible boundary while preserving the VS Code semantics.

## 5. Stale Event Detection

Implement the stale-event protection identified by PORT-INFRA-005.

Port the actual VS Code behavior for revision/sequence handling, particularly for versioned sync events such as:

- `session.updated.1`
- other events for which the VS Code implementation performs stale-event detection.

The implementation must:

- recognize the event sequence/revision information;
- reject stale events;
- accept newer events;
- preserve the existing behavior for event formats that do not contain sequence information.

Do not invent a generic event versioning framework.

Port only the logic actually required by the VS Code implementation.

## 6. Event Coverage

Review the event parity matrix from PORT-INFRA-005 and implement the missing event handling required for behavioral parity.

In particular, verify the handling of:

- `session.turn.open`
- `session.turn.close`
- `session.network.*`
- all other events explicitly identified as missing by the audit.

Only implement events that are actually handled by the current VS Code implementation and are relevant to the Visual Studio extension.

Do not create speculative handlers for events that are not used by the current VS Code implementation.

## 7. Generated WebView DTOs

PORT-WEBVIEW-001 and PORT-WEBVIEW-002 established generated WebView DTO infrastructure.

Where a corresponding generated DTO already exists:

- use it instead of creating anonymous transport objects;
- preserve the exact WebView protocol shape;
- do not modify the protocol;
- do not manually duplicate DTO definitions.

If a required DTO does not exist, determine whether it is genuinely required by the current VS Code protocol before modifying the generator or contract.

Do not expand the DTO generator as part of this task unless strictly required by an actual missing protocol type.

## 8. Child Session Handling

Preserve the existing correct child-session adoption behavior identified by PORT-INFRA-005.

Do not rewrite it unless necessary to integrate the centralized event/session handling.

The behavior must remain compatible with the VS Code implementation.

## 9. WebView Protocol

The VS Code WebView protocol is the source of truth.

Do not change the protocol.

Do not introduce Visual Studio-specific WebView message shapes.

Do not replace generated DTOs with anonymous objects where a generated DTO already represents the required protocol message.

## 10. Tests

Add or update tests covering the behavior implemented by this task.

At minimum, cover:

- sync event unwrapping where relevant;
- session ID resolution;
- message ID → session ID fallback;
- session filtering;
- directory/project filtering;
- stale event rejection;
- newer event acceptance;
- `session.status` pass-through;
- `session.deleted` pass-through;
- global event pass-through;
- missing event handlers identified by the audit;
- WebView DTO mapping for affected events.

Tests should verify behavior, not implementation details.

Prefer focused unit tests over large integration tests unless an integration test is required to validate the actual event-processing pipeline.

## Constraints

Do NOT:

- modify VS Code production code;
- modify the VS Code WebView protocol;
- redesign `SSEHelper`;
- redesign `SseEventDeserializer`;
- introduce a new event bus;
- introduce a synchronization framework;
- introduce a generic caching framework;
- introduce speculative abstractions;
- regenerate unrelated NSwag code;
- modify unrelated WebView DTO infrastructure;
- port unrelated VS Code functionality;
- implement future SSE tasks;
- change the external SSE protocol.

The existing architecture should be extended only where necessary.

## Documentation

Create or update:

`packages/kilo-visualstudio/porting/docs/prompts/PORT-SSE-001.md`

Use the existing PORT-INFRA documentation history as the model.

Preserve execution history.

The document must contain:

1. the original task scope;
2. the implementation plan;
3. the VS Code source references used;
4. the Visual Studio files modified;
5. the behavioral differences addressed;
6. test coverage added;
7. validation results;
8. remaining known differences, if any;
9. final status.

Update:

`packages/kilo-visualstudio/porting/docs/TASKS.md`

to reflect the actual state of PORT-SSE-001.

Do not mark the task `DONE` if an acceptance criterion remains unresolved.

## Validation

Before finishing:

- build the Visual Studio extension;
- run all relevant tests;
- verify no VS Code source was modified;
- verify no WebView protocol changes were introduced;
- verify generated DTO infrastructure remains valid;
- verify stale events are rejected;
- verify newer events are processed;
- verify session filtering matches VS Code behavior;
- verify directory/project filtering;
- verify global events still pass through;
- verify `session.status` and `session.deleted` behavior;
- verify no unrelated production files were changed.

Compare the resulting behavior against the current VS Code implementation.

## Acceptance Criteria

PORT-SSE-001 is complete only when:

- session ID resolution is centralized;
- message-to-session fallback is implemented where required;
- event filtering matches VS Code semantics;
- project/directory filtering is implemented;
- stale event handling matches VS Code semantics;
- identified missing SSE event handlers are implemented;
- existing child-session adoption behavior is preserved;
- affected WebView messages use generated DTOs where available;
- relevant tests exist and pass;
- the Visual Studio build succeeds;
- documentation is updated;
- no unrelated architecture has been introduced.

## Expected Final State

`PORT-SSE-001 → REVIEW`
