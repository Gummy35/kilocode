# PORT-INFRA-002 — VS Code to Visual Studio Port Mapping

**Mode:** Plan  
**Task:** PORT-INFRA-002  
**Model:** Qwen3.5-122B

## Objective

Establish the current, evidence-based correspondence between the Kilo VS Code extension and the existing Kilo Visual Studio extension.

The VS Code extension is the **source of truth**.

The existing Visual Studio implementation must be treated as the current target/baseline, not as the source of architectural decisions.

This task is ANALYSIS ONLY.

Do not modify production code.
Do not modify existing tests.
Do not implement missing functionality.
Do not refactor the Visual Studio extension.
Do not modify the VS Code extension.

---

## Required reading

Read:

- `packages/kilo-visualstudio/porting/docs/SPEC.md`
- `packages/kilo-visualstudio/porting/docs/TASKS.md`
- `packages/kilo-visualstudio/porting/docs/prompts/PORT-INFRA-001.md`
- `packages/kilo-visualstudio/porting/baseline/baseline.json`
- `packages/kilo-visualstudio/porting/manifest/files.json`
- `packages/kilo-visualstudio/porting/manifest/symbols.json`
- `packages/kilo-visualstudio/porting/manifest/tests.json`
- `packages/kilo-visualstudio/porting/manifest/relationships.json`

Then inspect the actual VS Code extension source in this repository.

Do not rely on existing Markdown documentation as a source of truth when the source code can be inspected directly.

---

## Source of truth

The authoritative implementation is the current VS Code extension source in this repository.

Do not use:

- my personal fork identity;
- hard-coded `Gummy35/kilocode` references;
- assumptions based on previous generated documents;
- old documentation when source code disagrees with it.

Repository identity must remain neutral.

---

## VS Code scope

Identify the actual VS Code extension package and its relevant source files.

At minimum investigate:

- extension entry point;
- `extension.ts`;
- its direct and indirect dependencies;
- WebView/provider implementation;
- CLI/HTTP communication;
- state/session handling;
- commands;
- configuration;
- event handling;
- authentication/session handling;
- message passing between extension host and WebView;
- tests;
- important utilities used by the extension entry point.

Do not assume filenames from previous discussions. Determine the actual current structure from the repository.

---

## Visual Studio scope

Use the existing PORT-INFRA-001 inventory as the baseline.

Inspect the actual Visual Studio implementation and tests when required to establish correspondence.

Do not modify them.

---

## Mapping requirements

Build a class/method/file-level mapping wherever technically possible.

For each relevant VS Code element record:

- VS Code file;
- VS Code symbol;
- symbol kind;
- source span;
- source hash;
- Visual Studio counterpart, if one exists;
- Visual Studio file;
- Visual Studio symbol;
- mapping status;
- evidence;
- notes.

Mapping status must be one of:

```text
EXACT
PARTIAL
ADAPTED
MISSING
UNRELATED
UNKNOWN
```

Do not claim `EXACT` merely because two classes have similar names.

`EXACT` requires evidence that the responsibility and relevant behavior correspond.

---

## Method-level fidelity

For important classes and services, compare methods individually.

Identify:

- methods present in both;
- methods missing in Visual Studio;
- methods existing only in Visual Studio;
- materially different signatures;
- materially different responsibilities;
- behavior that cannot yet be compared.

Do not attempt to prove semantic equivalence automatically.

Record uncertainty explicitly.

---

## Extension entry point

Pay particular attention to the VS Code `extension.ts` dependency graph.

Produce a dependency-oriented view showing:

```text
extension.ts
    ├── dependency
    ├── dependency
    │     └── dependency
    └── dependency
```

Identify which of these responsibilities already exist in Visual Studio and which do not.

The objective is to determine the actual scope required to faithfully port `extension.ts`.

---

## CLI / HTTP client

Specifically analyze how the current VS Code extension communicates with the Kilo CLI.

Determine:

- where the client is implemented;
- HTTP endpoints;
- request/response models;
- authentication/session handling;
- streaming/event mechanisms;
- lifecycle;
- error handling;
- cancellation;
- reconnect/retry behavior;
- WebView interaction.

Compare this with the current Visual Studio implementation.

Do not implement anything.

The output must tell us precisely what must eventually be ported.

---

## WebView

Determine how the VS Code extension hosts and communicates with its WebView.

Identify:

- provider;
- initialization;
- message protocol;
- message types;
- serialization;
- commands;
- lifecycle;
- state synchronization.

Compare with the existing Visual Studio WebView implementation.

Again: analysis only.

---

## Tests

Compare the VS Code tests relevant to the extension entry point and its dependencies with the existing Visual Studio tests.

For each relevant VS Code test identify:

- source file;
- test symbol;
- behavior being tested;
- corresponding Visual Studio test, if any;
- status:

```text
PORTED_1_TO_1
PORTED_ADAPTED
MISSING
NOT_APPLICABLE
UNKNOWN
```

Do not modify tests.

Do not call an adapted test `PORTED_1_TO_1`.

---

## Versioning / synchronization

The mapping must support future synchronization.

For each mapped VS Code source file record enough information to detect a future change:

- repository-relative path;
- Git commit;
- SHA-256;
- optionally symbol-level hashes where available.

Do not implement automatic synchronization yet.

Do not introduce a dependency on the user's fork.

The design must work if the Visual Studio implementation is eventually merged upstream.

---

## Required output

Produce a concise but complete implementation plan and mapping proposal.

Prefer a small number of machine-readable files rather than a large collection of Markdown documents.

Propose the minimum necessary additions under:

```text
packages/kilo-visualstudio/porting/
```

Do not create unnecessary infrastructure.

At minimum the analysis must provide:

1. VS Code source baseline;
2. VS Code file inventory relevant to the port;
3. VS Code symbol inventory relevant to the port;
4. VS Code → Visual Studio mapping;
5. missing functionality;
6. divergent functionality;
7. test mapping;
8. CLI/HTTP client mapping;
9. WebView mapping;
10. dependency graph of `extension.ts`;
11. recommended implementation order.

---

## Implementation order

The proposed implementation order must respect dependencies.

In particular, determine whether the following order is appropriate rather than assuming it:

1. shared models/protocol;
2. CLI HTTP client;
3. session/state handling;
4. WebView communication;
5. extension/provider orchestration;
6. commands/events;
7. remaining `extension.ts` dependencies;
8. tests.

If another order is technically more correct, explain why.

---

## Important constraints

Do not overengineer.

Do not create a synchronization framework yet.

Do not create code generators yet.

Do not introduce abstractions merely to make the two implementations look similar.

The goal is to understand the existing code accurately before implementation.

The final Visual Studio implementation must ultimately mirror the VS Code structure as closely as the two platforms allow, but platform-specific adaptations are allowed where required by Visual Studio APIs.

The VS Code implementation remains the source of truth.

---

## Acceptance criteria

PORT-INFRA-002 is successful only if:

- the actual VS Code extension entry point has been identified;
- its relevant dependency graph has been inspected;
- the CLI/HTTP implementation has been identified;
- WebView communication has been identified;
- relevant tests have been identified;
- existing Visual Studio counterparts have been mapped;
- missing and divergent functionality is explicitly listed;
- mapping evidence is based on source code;
- no production/test code was modified;
- no upstream/fork dependency was introduced;
- the proposed implementation order is justified;
- the resulting plan is small enough for a human to review and control.

If information cannot be established reliably from source code, mark it `UNKNOWN` instead of guessing.

Do not start implementation.

End with:

```text
PORT-INFRA-002 → REVIEW
```

and list the exact files proposed for creation or modification in the next implementation task.