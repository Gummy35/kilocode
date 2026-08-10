# Visual Studio Kilo Porting Rules

## 1. Source of truth

The Kilo VS Code extension is the behavioral source of truth for the Visual Studio extension.

The current `vs2026` implementation is the implementation baseline.

The current upstream Kilo repository is NOT used to judge the correctness of the initial baseline audit.

Upstream synchronization will be handled in a later phase.

## 2. Existing code

Existing Visual Studio code must be analyzed before being modified, replaced, or removed.

Existing functionality must not be discarded merely because another implementation appears cleaner.

Prefer incremental correction over unnecessary rewrites.

## 3. Porting fidelity

When a Visual Studio component represents a port of a VS Code component, the intended relationship is:

VS Code source
→ Visual Studio port
→ traceable mapping

A port must not be considered 1:1 unless its source file/symbol/test can be identified.

## 4. Tests

VS Code tests are the source of truth for ported tests.

A Visual Studio test must not be considered a 1:1 port until its relationship to the corresponding VS Code test has been verified.

NEVER modify a test merely to make a failing implementation pass.

If a ported test fails:

1. Verify the Visual Studio implementation against the VS Code implementation.
2. Correct the implementation if it is incorrect.
3. If the Visual Studio implementation is demonstrably correct, STOP and report the discrepancy for human review.

Do not weaken assertions.

Do not remove test cases.

Do not change expected values merely to make tests pass.

## 5. Documentation

Documentation is not authoritative unless explicitly designated as a specification.

Existing Markdown files may contain outdated or incorrect information.

The actual source code and Git history take precedence over descriptive Markdown.

## 6. Determinism

Generated inventories, manifests, mappings and other machine-readable artifacts must be deterministic.

The same source Git commit must produce the same generated content, except for explicitly documented timestamps.

Do not use nondeterministic ordering.

Do not include machine-specific absolute paths.

## 7. Traceability

Every ported file, symbol and test must eventually have identifiable provenance.

The provenance must include, where applicable:

- source repository
- source commit
- source file
- source symbol
- source hash

## 8. Generated code

Generated code must never be manually edited if the generator is available.

The generator and its inputs must instead be corrected.

## 9. Human decisions

When an implementation requires a decision that cannot be established from the source code, stop and request a human decision rather than silently inventing behavior.

## 10. Initial baseline

The first phase inventories the existing `vs2026` implementation.

This phase must NOT:

- compare the implementation with current upstream Kilo;
- implement missing functionality;
- refactor production code;
- modify existing tests;
- claim semantic equivalence with VS Code.

The purpose of the initial baseline is to record what actually exists today.