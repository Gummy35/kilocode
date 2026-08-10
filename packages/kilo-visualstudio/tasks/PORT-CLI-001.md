# PORT-CLI-001: Port VS Code CLI/HTTP Client to Visual Studio

## Objective

Port the CLI/HTTP communication layer used by the Kilo VS Code extension to the Visual Studio extension.

The VS Code implementation is the source of truth.

The existing Visual Studio implementation must be inspected first. Reuse, correct, or extend existing code where appropriate rather than creating duplicate implementations.

The resulting Visual Studio implementation must preserve the behavior and semantics of the corresponding VS Code implementation.

## Source of Truth

The authoritative source is the VS Code extension source currently present in the repository.

Do not treat previous plans, Markdown files, generated mappings, or assumptions as authoritative when they conflict with the actual VS Code source code.

For every component being ported:

1. Inspect the actual VS Code source.
2. Identify its dependencies.
3. Inspect the existing Visual Studio implementation.
4. Determine whether the Visual Studio implementation is equivalent, partial, divergent, or missing.
5. Only then determine the required change.

## Scope

The scope includes the CLI/HTTP communication layer required by the VS Code extension, including where applicable:

- CLI process/backend management;
- HTTP client communication;
- API requests and responses;
- session management;
- SSE/event streaming;
- connection/reconnection behavior;
- cancellation;
- authentication;
- configuration;
- error handling;
- notifications;
- permissions/questions;
- MCP-related communication;
- health/status handling;
- any other behavior directly required by the corresponding VS Code implementation.

Do not assume that every item listed above requires implementation.

Determine the actual scope from the VS Code source.

## Existing Visual Studio Code

Before implementing anything, inspect the existing Visual Studio implementation identified by PORT-INFRA-001 and PORT-INFRA-002.

Prefer modifying or completing existing components when they already represent the relevant responsibility.

Do not create duplicate classes or services when an existing component can correctly implement the required behavior.

Do not perform unrelated refactoring.

## Tests

Identify the actual VS Code unit tests covering the functionality being ported.

Only consider a test a port candidate when an actual VS Code test exists.

Do not infer a VS Code test from the name or behavior of an existing Visual Studio test.

For each applicable VS Code test:

- identify the actual source file;
- identify the test class;
- identify the test method;
- determine whether an equivalent Visual Studio test already exists;
- determine whether it is missing or requires adaptation;
- preserve the semantic intent of the VS Code test.

Existing Visual Studio tests must not be weakened or modified merely to make them pass.

If a test fails:

1. determine whether the Visual Studio implementation differs from the VS Code source;
2. if it does, correct the implementation;
3. if the implementation already matches the VS Code behavior, stop and report the discrepancy for human review.

Never modify a test simply to accommodate an incorrect implementation.

## Architecture

The Visual Studio implementation should follow the same logical decomposition as the VS Code implementation wherever the platforms permit it.

Platform-specific adaptations are allowed where required by the Visual Studio/.NET environment.

Do not force literal TypeScript-to-C# translation when the platform architecture requires an equivalent .NET implementation.

However, behavior, responsibilities, sequencing, error handling, cancellation and observable semantics must remain equivalent.

## Dependencies

Every dependency required by the VS Code CLI/HTTP implementation must be identified.

For each dependency, determine whether:

- an equivalent already exists in the Visual Studio project;
- it can be implemented using existing .NET facilities;
- an existing Visual Studio component already provides the required behavior;
- a new dependency is actually necessary.

Avoid adding third-party dependencies unless there is a concrete requirement.

## Generated/Ported Code Documentation

All newly ported or substantially modified implementation elements must be documented in English.

Documentation should explain the responsibility and important platform-specific adaptations.

Do not add comments that merely restate the code.

## Versioning of Ported Source

For every VS Code source component that is actually ported, record enough information to identify the source revision used for the port.

At minimum record:

- VS Code source file path;
- source file SHA-256;
- repository commit SHA;
- relevant symbol/method where applicable.

Use the existing porting documentation/manifests where practical.

Do not introduce a new synchronization framework as part of this task.

## Reproducibility

The porting process must be deterministic and reproducible.

When an AI agent generates or transforms code from the VS Code implementation:

- the VS Code source revision must be explicit;
- the source files/symbols being used must be explicit;
- instructions must avoid nondeterministic choices;
- generated code must be reproducible from the same source and instructions.

The goal is not to reproduce TypeScript syntax in C#, but to reproduce the behavior and architecture faithfully.

## No Upstream Dependency

The Visual Studio implementation must not introduce a dependency on the user's fork.

Do not reference:

- `Gummy35/kilocode`;
- fork-specific URLs;
- fork-specific package feeds;
- fork-specific namespaces;
- fork-specific APIs.

The resulting implementation must be suitable for eventual contribution upstream to the Kilo repository.

The repository containing the Visual Studio work is only the development environment.

## Plan Requirements

The Plan phase must produce a concrete implementation plan before any production code is modified.

The plan must identify:

1. actual VS Code source files inspected;
2. relevant VS Code classes/functions;
3. relevant VS Code tests;
4. existing Visual Studio counterparts;
5. required changes;
6. files expected to be modified or created;
7. tests to be ported or adapted;
8. validation steps;
9. any unresolved questions or blockers.

Do not invent implementation files before inspecting the existing Visual Studio architecture.

## Constraints

Do NOT:

- modify production code during Plan mode;
- modify existing tests during Plan mode;
- implement the port during Plan mode;
- perform unrelated refactoring;
- redesign the architecture without evidence from the VS Code source;
- create synchronization infrastructure;
- compare against unrelated upstream changes;
- add speculative functionality;
- add dependencies without justification.

## Acceptance Criteria

The task is ready for Code execution when:

- the relevant VS Code implementation has been inspected;
- the actual CLI/HTTP responsibilities are identified;
- the existing Visual Studio implementation has been inspected;
- the required deltas are identified;
- actual applicable VS Code tests are identified;
- existing Visual Studio tests are distinguished from genuine VS Code test ports;
- required implementation files are identified;
- required test changes are identified;
- source revisions/hashes are recorded;
- no unresolved architectural ambiguity remains that requires implementation-time guessing.

The Plan must remain focused on the CLI/HTTP layer.

WebView integration and unrelated extension-host functionality belong to subsequent tasks.