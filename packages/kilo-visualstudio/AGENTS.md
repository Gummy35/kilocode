# AGENTS.md

This file provides guidance to agents when working with the Visual Studio extension in this package.

## Product Context

Kilo Code is an open source AI coding agent platform. It ships as a CLI and multiple editor clients that communicate with the same backend.

This package (`packages/kilo-visualstudio/`) contains the Visual Studio extension.

The Visual Studio extension is a **C# / .NET Framework 4.8.1** application. It is a port of selected functionality from the Kilo VS Code extension, but it is not a copy of the VS Code implementation.

The VS Code extension is the primary behavioral reference when porting functionality. However, implementation must follow Visual Studio/.NET conventions and the architecture already present in this package.

## Relationship With the CLI

The Visual Studio extension is a client of the Kilo CLI backend.

The architecture is:

```text
Visual Studio Extension
        |
        | HTTP REST + SSE
        v
kilo serve --port 0
        |
        v
Kilo CLI backend
```

The extension starts a local CLI server process and communicates with it through HTTP and Server-Sent Events.

The CLI contains the actual agent runtime, tool execution, session management, providers, MCP, LSP, and backend services.

Do not duplicate CLI functionality in the Visual Studio extension when the CLI already provides that functionality.

In particular, before implementing behavior that appears to be related to:

- `AGENTS.md`
- project instructions
- configuration discovery
- workspace/project state
- session management
- agent behavior
- provider configuration

first determine whether that behavior belongs to the CLI/backend rather than the editor client.

## Source of Truth for Porting

When porting functionality from VS Code:

1. Inspect the current VS Code implementation.
2. Identify the observable behavior that must be preserved.
3. Identify whether the behavior is editor-specific or backend/CLI functionality.
4. Check whether the Visual Studio extension already provides an equivalent.
5. Implement the smallest Visual Studio-native change required for behavioral parity.

Do not mechanically translate TypeScript into C#.

Do not introduce abstractions merely to make the C# implementation structurally resemble the TypeScript implementation.

## Porting Principles

### Keep Changes Minimal

This project is being ported incrementally.

Prefer:

- small focused changes
- existing classes and services
- existing interfaces
- existing HTTP/SSE infrastructure
- existing generated client models
- simple C# collections and synchronization primitives

Avoid:

- unnecessary architectural refactoring
- introducing new frameworks
- introducing dependency injection containers unless already required
- replacing working infrastructure without a demonstrated need
- speculative abstractions
- implementing future functionality early

Do not expand the scope of a porting task without a concrete parity requirement.

### VS Code Is a Behavioral Reference, Not an Architecture

The VS Code extension uses TypeScript/Node.js APIs and patterns that do not necessarily have direct equivalents in Visual Studio.

For example:

- VS Code APIs must not be emulated unnecessarily.
- Node.js process APIs must not be reproduced as abstractions in C#.
- JavaScript event-loop behavior must not be assumed to exist in .NET.
- TypeScript SDK patterns must not dictate the C# architecture.

Preserve behavior, not implementation structure.

## Target Framework

The extension targets:

```text
.NET Framework 4.8.1
```

All implementation must remain compatible with .NET Framework 4.8.1.

Do not introduce APIs that require .NET 6/7/8/9 unless the project explicitly changes its target framework.

Check NuGet package compatibility with .NET Framework 4.8.1 before adding dependencies.

## Visual Studio Integration

The extension runs inside Visual Studio and must respect Visual Studio's threading and lifecycle requirements.

When interacting with Visual Studio APIs:

- use the appropriate Visual Studio threading mechanisms
- do not block the UI thread unnecessarily
- do not perform long-running network or process operations synchronously on the UI thread
- preserve cancellation where the existing architecture provides it
- avoid introducing background threads when async APIs are sufficient

Do not assume that code running in the CLI/backend has the same threading constraints as code running inside Visual Studio.

## CLI Process

The extension currently starts the CLI using the existing CLI path configured by the project.

For the current porting work:

**Do not change CLI discovery or download behavior.**

Do not:

- add automatic CLI downloads
- add local/system CLI discovery
- introduce platform-specific CLI resolution
- change the configured CLI path

CLI discovery/download can be addressed by a separate future task.

The current responsibility is to make the existing CLI connection work correctly.

## CLI Authentication

The CLI server uses the `KILO_SERVER_PASSWORD` environment variable for authentication.

The Visual Studio extension must generate and manage the password required for its own CLI process and use the same credential when constructing the HTTP client.

Do not hard-code credentials.

Do not persist the server password unnecessarily.

## Generated API Client

The CLI HTTP API is described by the repository OpenAPI specification.

The Visual Studio extension uses a generated C# client for the API.

Generated code must be treated as generated code:

- do not manually modify generated source files
- do not add application logic to generated classes
- do not duplicate generated request/response models
- regenerate the client when the OpenAPI source changes

The OpenAPI source used for generation must come from the repository, not from the user's fork or a separate private source.

Record generator/version/configuration information when required by the porting documentation.

### Kiota

The current generated C# client is generated using Microsoft Kiota.

Kiota warnings about OpenAPI polymorphic schemas must not automatically result in modifications to the OpenAPI specification.

Before changing the OpenAPI source or generated-client configuration:

1. determine whether the warning affects functionality actually used by the Visual Studio extension
2. compare the corresponding VS Code behavior
3. determine whether the issue belongs to the API specification, generator, or client usage
4. make the smallest justified change

Do not modify the OpenAPI specification merely to silence generator warnings.

## HTTP Communication

Use the generated C# API client for API operations where an appropriate generated endpoint/model exists.

Do not reintroduce manual HTTP request construction when the generated client already supports the operation.

Manual HTTP code should only be introduced when:

- the generated client cannot represent the required endpoint correctly
- the endpoint is not present in the OpenAPI specification
- or a documented technical limitation requires it

Such a case must be explicitly documented.

## Server-Sent Events

The CLI exposes global events through SSE.

The Visual Studio implementation uses the existing `SseClient` infrastructure.

When porting SSE behavior from VS Code:

- preserve observable event semantics
- preserve cancellation
- preserve reconnection behavior where required
- avoid unnecessary rewrites of the existing SSE client

Do not replace the existing SSE implementation with a new framework unless required.

## Connection Service

`KiloConnectionService` is the central service responsible for communication with the CLI backend.

Before adding functionality:

- inspect existing state
- inspect all callers
- inspect lifecycle and disposal behavior
- determine whether the state is connection-wide or workspace/directory-specific

Avoid putting unrelated responsibilities into `KiloConnectionService`.

If a feature requires state that has a different lifetime, use the smallest appropriate mechanism rather than expanding the service indiscriminately.

## Directory and Workspace State

Directory-related behavior must be understood in terms of the CLI/backend architecture.

Do not assume that every directory-related function in the VS Code extension needs to be replicated in C#.

For any directory tracking or workspace state feature:

1. inspect all VS Code call sites
2. determine why the directory is tracked
3. determine whether the CLI already performs the underlying operation
4. determine what state must actually exist in the Visual Studio extension
5. implement only the required client-side state

Avoid creating duplicate workspace discovery mechanisms.

## AGENTS.md and Project Instructions

`AGENTS.md` handling requires particular care.

The Visual Studio extension must not independently reimplement CLI instruction discovery without first verifying ownership of that behavior.

When investigating `AGENTS.md`:

- inspect the VS Code extension
- inspect the CLI/backend
- inspect the actual call path used when creating/starting a session
- determine where instructions are discovered
- determine where they are loaded into the agent context

If the CLI already discovers and applies `AGENTS.md`, the Visual Studio extension should rely on the CLI rather than implementing a second discovery mechanism.

If VS Code performs editor-specific discovery or passes additional information to the CLI, port only that editor-specific behavior.

## Process Lifetime

The CLI process is a child process of the Visual Studio extension.

Process management must account for:

- startup timeout
- stdout/stderr handling
- process exit
- cancellation
- disposal
- cleanup on extension shutdown

Do not leave child processes running after the extension has disposed its connection infrastructure.

Avoid blocking waits on the Visual Studio UI thread.

## Thread Safety

The extension may receive events from:

- Visual Studio UI operations
- asynchronous HTTP requests
- SSE callbacks
- timers
- CLI process events

Shared state must therefore be protected appropriately.

Prefer simple synchronization mechanisms already used by the project.

Do not introduce complex concurrency abstractions unless the behavior requires them.

## Tests

Tests for the Visual Studio extension are located under:

`packages/kilo-visualstudio/KiloVisualStudioExtension.Tests/`

When porting behavior from VS Code:

- identify the corresponding VS Code test
- verify that the behavior is actually applicable to Visual Studio
- create an adapted C# test where appropriate

A Visual Studio test without a verified VS Code counterpart must not automatically be described as a one-to-one port.

Tests may legitimately differ between VS Code and Visual Studio because their host APIs and UI lifecycles differ.

Do not modify unrelated failing tests merely to make a porting task pass.

If a failure is pre-existing and unrelated to the current change, document it.

## Build and Validation

After modifying the extension:

1. build the affected Visual Studio project
2. run the relevant tests
3. verify generated-code compilation
4. inspect warnings/errors for regressions
5. avoid treating pre-existing warnings as newly introduced issues

The primary target is .NET Framework 4.8.1.

## Repository Structure

Important directories:

```text
packages/kilo-visualstudio/
├── KiloVisualStudioExtension/
│   └── Main Visual Studio extension
├── KiloVisualStudioExtension.Tests/
│   └── C# tests
├── porting/
│   └── Porting documentation, specifications and task tracking
├── specs/
│   └── Visual Studio-specific specifications
├── tasks/
│   └── Porting task prompts
└── tools/
    └── Development/build tooling
```

The VS Code implementation is located at:

`packages/kilo-vscode/`

The CLI/backend is primarily located at:

`packages/opencode/`

The generated TypeScript SDK is located at:

`packages/sdk/js/`

The OpenAPI source used by the SDK is maintained in the repository.

## Porting Documentation

Porting work is documented under:

`packages/kilo-visualstudio/porting/`

Important documents include:

- `SPEC.md`
- `TASKS.md`
- porting plans
- porting task prompts
- implementation/validation reports

When a porting task requires a plan, follow the existing plan/task workflow.

Do not create ad-hoc architecture documents when an existing porting document is appropriate.

## No Fork Dependencies

The Visual Studio extension must not introduce dependencies on the user's fork.

References to Kilo source code must remain compatible with eventual upstream integration.

Do not:

- reference `Gummy35/kilocode` in application code
- download artifacts from the fork
- hard-code fork-specific URLs
- introduce package dependencies that only exist in the fork
- make runtime behavior depend on the fork

The eventual goal is for the Visual Studio implementation to be mergeable into the upstream Kilo repository.

## Change Markers

The Visual Studio extension is Kilo-specific code.

Do not add upstream `kilocode_change` markers to files that belong exclusively to:

`packages/kilo-visualstudio/`

If a porting task modifies shared upstream code outside this package, follow the repository's existing conventions for marking Kilo-specific changes.

## Scope Discipline

This project is being developed incrementally.

When a task identifies several differences between VS Code and Visual Studio:

- implement only the differences assigned to the current task
- record deferred differences explicitly
- do not silently expand the task
- do not perform unrelated cleanup
- do not refactor working code for stylistic consistency with VS Code

A technically correct smaller change is preferred over a broad redesign.

## Important Agent Rule

Before making a change, answer these questions:

1. What exact VS Code behavior are we trying to reproduce?
2. Is that behavior actually owned by the VS Code extension?
3. Does the Visual Studio extension already implement part of it?
4. Does the CLI already provide the underlying behavior?
5. What is the smallest C# change required?
6. How will the change be tested?

If these questions cannot be answered from the source, inspect the relevant code before implementing.

Do not guess.