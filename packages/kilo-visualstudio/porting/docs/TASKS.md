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
| `PORT-INFRA-001`   | Establish a deterministic baseline inventory of the existing Visual Studio implementation   | `IN_PROGRESS` | —                                |
| `PORT-INFRA-002`   | Inventory the VS Code extension and establish source-to-target mappings                     | `NOT_STARTED` | `PORT-INFRA-001`                 |
| `PORT-CLI-001`     | Port the VS Code CLI/HTTP client and its relevant tests to Visual Studio                    | `NOT_STARTED` | `PORT-INFRA-002`                 |
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

`IN_PROGRESS`

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
PORT-INFRA-001   → IN_PROGRESS
PORT-INFRA-002   → NOT_STARTED
PORT-CLI-001     → NOT_STARTED
PORT-WEBVIEW-001 → NOT_STARTED
PORT-CORE-001    → NOT_STARTED
PORT-TEST-001    → NOT_STARTED
PORT-SYNC-001    → NOT_STARTED
```

The project must proceed sequentially where practical.

Do not implement future tasks before their scope has been reviewed and approved.
