# PORT-INFRA-001 — Create deterministic Visual Studio port baseline inventory

## Status

READY

## Type

Infrastructure / analysis

## Dependencies

None.

## Objective

Create a deterministic, machine-readable inventory of the existing Kilo Visual Studio implementation on branch `vs2026`.

This task establishes the baseline from which all future porting work will be performed.

The inventory describes the current repository state.

It does NOT determine whether the implementation is correct or equivalent to VS Code.

## Scope

Inventory only the existing Visual Studio extension and its tests.

Do NOT use current upstream Kilo as a correctness reference during this task.

Do NOT modify production code.

Do NOT modify existing tests.

Do NOT refactor existing code.

Do NOT implement missing functionality.

Do NOT fix existing bugs.

## Authoritative sources

For this task:

1. Actual source code is authoritative.
2. Git metadata is authoritative for repository state.
3. Existing Markdown documentation is informational only.

Do not trust existing Markdown claims without verifying them against the code.

## Required output

Create:

```text
porting/
├── baseline/
│   └── baseline.json
└── manifest/
    ├── files.json
    ├── symbols.json
    ├── tests.json
    └── relationships.json
```

## baseline.json

Record:

- repository
- branch
- exact Git commit SHA
- generation timestamp
- inventory tool/version
- inventory scope
- Visual Studio project(s)
- test project(s)
- actual target framework(s) from the project files

The exact Git commit must be recorded.

Do not use only the branch name.

## files.json

Inventory the source files belonging to the Visual Studio extension and its tests.

For every file record:

- stable file ID
- relative repository path
- project
- language
- file type
- SHA-256
- file size
- line count
- production/test classification
- generated/non-generated classification

Do not invent hashes.

Do not use absolute machine-specific paths.

Use deterministic ordering.

## symbols.json

Inventory C# symbols using the most reliable parser/compiler-based mechanism available.

At minimum include:

- namespace
- class
- interface
- enum
- struct
- record
- constructor
- method
- property
- field
- event

For each symbol record:

- stable symbol ID
- containing file ID
- fully qualified name
- symbol kind
- accessibility
- signature
- source span
- containing symbol ID where applicable
- normalized source hash

Symbol IDs must not depend solely on line numbers.

## tests.json

Inventory every existing C# test.

For every test:

- stable test ID
- file ID
- symbol ID
- fully qualified name
- test framework
- source span
- normalized source hash
- traits/categories if present

Every existing test must initially have status:

`EXISTING_TEST`

Do NOT classify any test as `PORTED_TEST_1_TO_1`.

No VS Code equivalence is established by this task.

## relationships.json

Record relationships that can be determined mechanically.

At minimum:

- file contains symbol
- symbol contains symbol
- test belongs to class
- local symbol dependencies/calls where reliably detectable
- handler class and handler namespace/folder relationships

Do not infer semantic equivalence with VS Code.

## Determinism

Running the inventory twice on the same Git commit must produce identical machine-readable output except for explicitly documented timestamps.

Use:

- UTF-8
- deterministic property ordering
- deterministic array ordering
- repository-relative paths only

Do not include:

- usernames
- absolute paths
- temporary build paths
- local machine identifiers

## Validation

The task is complete only if:

1. `baseline.json` identifies the exact Git commit.
2. Every file has a valid SHA-256.
3. Every symbol references an existing file ID.
4. Every test references an existing file and symbol.
5. There are no duplicate IDs.
6. All JSON files are valid.
7. The inventory can be regenerated successfully.
8. Production code is unchanged.
9. Existing tests are unchanged.
10. The resulting inventory is deterministic.

If a requirement cannot be satisfied reliably, STOP and report the blocker.

Do not fabricate information.

## Important restriction

Do NOT compare the implementation with current upstream Kilo.

Do NOT create VS Code mappings in this task.

Do NOT claim that any Visual Studio component is equivalent to VS Code.

Those activities belong to later tasks.

## Final report

Report:

- exact baseline commit
- number of projects
- number of files
- number of symbols
- number of tests
- generated files
- validation performed
- tests executed
- files changed
- any blockers

Do not modify this task specification during execution.