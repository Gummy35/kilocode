# Kilo Visual Studio Porting Specification

**Status:** Active
**Source of truth:** Kilo Code VS Code extension
**Target:** Kilo Code Visual Studio extension
**Language:** English

---

## 1. Purpose

This document defines the permanent rules for porting and maintaining the Kilo Code Visual Studio extension.

The Visual Studio extension is a port of the existing Kilo Code VS Code extension.

The objective is to provide equivalent functionality while respecting the conventions and constraints of the Visual Studio extension platform.

This specification defines **what must remain true** throughout the project.

It does not prescribe a specific implementation technology unless explicitly stated.

---

## 2. Source of Truth

The **Kilo Code VS Code extension is the authoritative source of truth** for the port.

When implementing or updating Visual Studio functionality:

1. Inspect the corresponding VS Code implementation.
2. Understand its behavior and responsibilities.
3. Port that behavior to Visual Studio.
4. Preserve the architectural intent of the VS Code implementation whenever the Visual Studio platform permits it.

The existing Visual Studio implementation is **not** the source of truth when it differs from the VS Code implementation.

The Visual Studio implementation may contain incomplete, outdated or incorrect code.

---

## 3. Upstream Independence

The Visual Studio implementation must be suitable for integration into the upstream Kilo Code repository.

No implementation or generated artifact may depend on a developer's personal fork.

Do not encode:

* personal GitHub usernames;
* personal fork names;
* fork-specific URLs;
* local repository paths;
* machine-specific paths;
* developer-specific configuration.

Repository state must be identified using immutable Git information such as commit SHA when a source revision must be recorded.

---

## 4. Structural Fidelity

The Visual Studio implementation should mirror the VS Code extension as closely as reasonably possible.

Where the VS Code implementation contains a distinct:

* file;
* class;
* interface;
* enum;
* service;
* handler;
* method;
* property;
* event;
* test;

the Visual Studio port should have a corresponding element whenever the concept is applicable to the Visual Studio platform.

The preferred mapping is therefore:

```text
VS Code file
    ↓
Visual Studio file

VS Code class
    ↓
Visual Studio class

VS Code method
    ↓
Visual Studio method

VS Code behavior
    ↓
Equivalent Visual Studio behavior
```

Platform-specific differences are permitted when required by the Visual Studio API or runtime model.

Such differences must be documented.

---

## 5. Behavioral Fidelity

A port must preserve the observable behavior of the VS Code implementation unless a platform-specific constraint makes this impossible.

When behavior differs:

1. Verify the VS Code implementation.
2. Verify the Visual Studio implementation.
3. Determine whether the difference is required by the Visual Studio platform.
4. Document the difference if it is intentional.

Do not introduce behavioral changes merely because an alternative implementation appears simpler.

---

## 6. WebView

The Visual Studio extension should use the Kilo Code WebView implementation where practical.

The WebView is considered part of the Kilo Code user interface and should remain aligned with the VS Code implementation.

When the existing WebView can be reused directly, reuse is preferred over independently recreating equivalent UI logic.

Platform-specific host integration belongs in the Visual Studio extension layer.

---

## 7. CLI / HTTP Client

The Visual Studio extension must provide the equivalent communication mechanism used by the VS Code extension to communicate with the Kilo Code CLI.

The VS Code implementation of the CLI/HTTP client is the reference implementation.

The Visual Studio client must preserve:

* request semantics;
* response handling;
* error handling;
* session behavior;
* streaming behavior where applicable;
* cancellation behavior where applicable;
* authentication/configuration behavior where applicable.

Platform-specific networking APIs may differ, but observable behavior should remain equivalent.

---

## 8. Tests

Tests from the VS Code extension are part of the source-of-truth behavior specification.

Where a VS Code unit test has a meaningful Visual Studio equivalent, it must be ported faithfully.

The target is a **1:1 semantic port**.

A ported test should preserve, as applicable:

* test intent;
* setup;
* inputs;
* expected outputs;
* assertions;
* edge cases;
* error cases.

The Visual Studio test may use platform-appropriate test infrastructure where necessary, but its semantic coverage must remain equivalent.

---

## 9. Test Integrity

**Tests must never be modified merely to make the Visual Studio implementation pass.**

When a ported test fails:

1. Compare the Visual Studio implementation with the corresponding VS Code implementation.
2. Determine whether the Visual Studio implementation is incorrect.
3. Correct the implementation if it is inconsistent with the source implementation.
4. Re-run the test.

If the Visual Studio implementation is demonstrably equivalent to the VS Code implementation and the test still fails:

* do not weaken the test;
* do not alter expected results merely to obtain a passing test;
* document the discrepancy;
* stop and allow human review when necessary.

Existing tests must not be silently rewritten to hide implementation differences.

---

## 10. Versioning and Source Tracking

Every ported element must be traceable to the VS Code source from which it was derived.

The project must be able to determine whether the source element has changed.

Source tracking may use:

* Git commit SHA;
* file hash;
* symbol/source hash;
* another deterministic source identifier.

The mechanism used must be deterministic and documented.

The source revision recorded for a port must refer to the actual VS Code source used to generate or verify the port.

---

## 11. Reproducibility

Port generation and maintenance should be reproducible.

Given:

* the same VS Code source revision;
* the same porting specification;
* the same task;
* the same relevant configuration;

the generated result should be stable.

AI-assisted generation must be performed with settings favoring deterministic output.

When possible:

* use low temperature;
* use deterministic generation settings;
* provide explicit source files;
* provide explicit task scope;
* avoid relying on conversational context.

AI output must always be validated against the VS Code source.

AI-generated code is not authoritative merely because it was generated from an approved prompt.

---

## 12. Documentation

All new Visual Studio implementation code must be clearly documented in **English**.

Documentation should explain:

* the purpose of the component;
* important platform-specific decisions;
* deviations from the VS Code implementation;
* non-obvious behavior;
* synchronization/source-tracking information where appropriate.

Do not add comments that merely restate obvious code.

Documentation must remain useful to a developer who has no access to the original development conversation.

---

## 13. Existing Visual Studio Code

The existing Visual Studio implementation must be treated as an existing codebase, not assumed to be correct.

During the port:

* preserve correct existing functionality;
* reuse existing implementation where it matches the source;
* correct existing code when it demonstrably deviates from the VS Code source;
* avoid unrelated refactoring.

Do not rewrite working code solely to make it look different from the VS Code implementation.

Structural fidelity is a goal, not a justification for unnecessary churn.

---

## 14. Scope Control

Each development task must have a clearly defined scope.

An agent must not:

* implement future tasks speculatively;
* introduce unrelated refactoring;
* redesign the architecture without an explicit requirement;
* create infrastructure that is not required by the current task;
* modify unrelated components merely because improvements are possible.

If work outside the current task is discovered, document it and stop or defer it.

**Do not expand the task automatically.**

---

## 15. Human Review

Human review remains the final authority for:

* architectural decisions;
* intentional deviations from VS Code;
* changes to this specification;
* acceptance of platform-specific behavioral differences;
* ambiguous source mappings;
* test discrepancies that cannot be resolved by source comparison.

Agents must not silently resolve ambiguous architectural decisions.

When the correct behavior cannot be established from the source and existing specifications, the issue must be reported for human review.

---

## 16. Source Synchronization Principle

Future synchronization must always follow this direction:

```text
Kilo Code VS Code source
          │
          │ inspect / compare
          ▼
Visual Studio port
```

Never use the Visual Studio implementation as the source for updating the VS Code implementation.

When the VS Code source changes:

1. Identify affected files and symbols.
2. Determine which Visual Studio elements depend on them.
3. Update only the affected Visual Studio elements.
4. Port and update the corresponding tests.
5. Validate the result against the new VS Code source revision.

---

## 17. Change Detection

The project must retain enough deterministic metadata to detect changes in the VS Code source.

At minimum, change detection must be able to identify:

* added source files;
* removed source files;
* modified source files;
* modified symbols;
* modified tests.

A detected source change does not automatically imply that the corresponding Visual Studio code must be changed.

The affected element must be reviewed to determine whether the change is relevant to the port.

---

## 18. Baseline

A baseline represents a precisely identified state of the Visual Studio implementation and its associated metadata.

A baseline must identify its Git revision using an immutable commit SHA.

Generated inventory data must not contain:

* personal fork identifiers;
* local paths;
* machine-specific information.

The initial Visual Studio baseline is established by `PORT-INFRA-001`.

---

## 19. Agent Workflow

AI agents must follow this general workflow:

```text
Read specification
       ↓
Read current task
       ↓
Inspect existing implementation
       ↓
Inspect VS Code source when required
       ↓
Plan
       ↓
Implement
       ↓
Run tests
       ↓
Compare failures against VS Code source
       ↓
Correct implementation if necessary
       ↓
Validate
       ↓
Report
```

Agents must not assume that previous conversational context is available.

The repository documentation and versioned task files must contain the information required to continue the work.

---

## 20. Task Documentation

Each significant task must have a versioned task identifier.

Examples:

```text
PORT-INFRA-001
PORT-CLI-001
PORT-WEBVIEW-001
PORT-TEST-001
```

The task documentation should contain:

* objective;
* scope;
* constraints;
* acceptance criteria;
* current status;
* relevant source revision;
* final result.

Prompts used to execute significant tasks may be stored alongside the task documentation for reproducibility.

---

## 21. Minimalism

The porting process must remain intentionally lightweight.

Do not introduce:

* unnecessary frameworks;
* unnecessary abstraction layers;
* unnecessary synchronization infrastructure;
* speculative automation;
* duplicate sources of truth.

Every additional tool or artifact should have a clear purpose related to the current porting objective.

The goal is to maintain a **small, understandable and controllable process** that can be operated by a human developer and resumed by an AI coding agent.

---

## 22. Priority of Rules

When requirements conflict, apply the following priority:

1. This specification;
2. The current task specification;
3. The actual VS Code source implementation;
4. Existing Visual Studio implementation;
5. Agent-generated assumptions.

When the VS Code implementation contradicts an agent assumption, the agent assumption must be discarded.

When the VS Code implementation and this specification appear to conflict, stop and request human review rather than silently choosing one.
