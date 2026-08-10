# CLEANUP-CLI-001 — Finalize NSwag Migration and Remove Kiota

**Task:** CLEANUP-CLI-001  
**Mode:** Code  
**Status:** NOT_STARTED  
**Parent:** PORT-CLI-001  
**Date:** 2026-08-10

---

## 1. Objective

Finalize the migration of the Kilo Visual Studio extension from the Kiota-generated REST client to the NSwag-generated REST client.

The NSwag migration has already been completed for the handler services and the extension builds successfully with zero compilation errors.

The remaining objective is to:

1. migrate any remaining production Kiota usages;
2. make NSwag the **only generated REST client** used by the Visual Studio extension;
3. remove the obsolete Kiota client and its generation infrastructure;
4. remove obsolete legacy HTTP client infrastructure if it is no longer referenced;
5. synchronize the task and porting documentation with the final architecture;
6. verify the result with a clean build and tests.

This task must be based on the **current repository state**, not on previous reports.

---

## 2. Source of Truth

The current repository is authoritative.

Before making changes, inspect:

- `packages/kilo-visualstudio/`
- `packages/kilo-visualstudio/porting/`
- `packages/kilo-visualstudio/tasks/`
- the current NSwag generation configuration and documentation;
- the current `packages/sdk/openapi.json`.

Do not assume that previous migration reports accurately describe the current state.

---

## 3. Current Architecture

The intended final architecture is:

```text
Visual Studio Extension
        │
        ├── KiloConnectionService
        │        │
        │        └── KiloApiClient (NSwag)
        │                  │
        │                  └── HTTP / REST
        │
        └── SseClient
                 │
                 └── GET /global/event
                         
                    Kilo CLI
```

NSwag is the sole generated REST client.

The existing `SseClient` remains responsible for SSE communication.

Do not replace the SSE implementation with NSwag unless the current code or API requires it.

---

## 4. Phase 1 — Repository Audit

Before modifying anything, perform an exhaustive repository-wide search for Kiota and legacy HTTP client usage.

Search for at least:

```text
KiloVisualStudioExtension.Generated
GetKiloClient(
KiloClient
Kiota
Microsoft.Kiota
Generated/
HttpClientWrapper
CachedHttpClient
```

Also inspect:

- `.csproj` files
- `.props` / `.targets`
- NuGet package references
- generation scripts
- build scripts
- documentation
- tests
- generated source directories

Classify every reference as:

| Classification | Meaning |
|---|---|
| Production | Runtime dependency |
| Test | Test-only dependency |
| Generated | Generated source |
| Build | Package/generation/build dependency |
| Documentation | Historical or current documentation |
| Dead | No longer required |

Do not delete anything during this audit phase.

Produce a short audit summary before proceeding with destructive cleanup.

---

## 5. Phase 2 — Complete Remaining Production Migration

Migrate every remaining **production** Kiota usage to the existing NSwag client.

Pay particular attention to:

- `VSProvider.cs`
- `SubAgentViewerProvider.cs`
- `KiloConnectionService.cs`
- any additional files discovered during the audit

Use the existing:

```text
GetNswagClient()
```

integration.

Use the actual generated NSwag API surface.

Do not invent operations or model properties.

Do not recreate the old Kiota abstractions.

Do not introduce another REST client.

### Important

If a Kiota operation cannot be mapped directly to NSwag:

1. inspect `packages/sdk/openapi.json`;
2. inspect the generated NSwag client;
3. determine whether the endpoint exists under a different operation name;
4. determine whether the HTTP method or parameters differ;
5. only then decide how the functionality should be handled.

If the endpoint genuinely does not exist in OpenAPI, document it explicitly.

Do not silently remove functionality.

---

## 6. Known OpenAPI Limitations

The previous NSwag migration identified these limitations:

### MCP remove

The generated NSwag client does not expose:

```text
Mcp_removeAsync
```

because the backend/OpenAPI specification does not expose the corresponding DELETE operation.

Do not invent an operation.

Verify the current OpenAPI specification before finalizing this limitation.

### Permission response

The generated NSwag client does not expose:

```text
Permission_PostAsync
```

Verify whether the current OpenAPI specification still lacks this operation.

If it does, preserve the current documented behavior/workaround and document the limitation clearly.

These limitations are API/OpenAPI issues, not reasons to retain Kiota.

---

## 7. Phase 3 — KiloConnectionService

Verify that `KiloConnectionService` has a single REST client implementation.

The final implementation must:

- create/use the NSwag `KiloApiClient`;
- use the CLI BaseUrl discovered by `CliBackendManager`;
- use the existing CLI password;
- authenticate through the existing NSwag authentication implementation;
- expose `GetNswagClient()` where required;
- not instantiate or retain a Kiota REST client;
- not instantiate or retain `HttpClientWrapper`;
- not instantiate or retain `CachedHttpClient`.

The SSE connection remains separate and continues to use `SseClient`.

Do not change CLI discovery behavior as part of this task.

In particular:

- do not implement automatic CLI download;
- do not change the current hard-coded CLI path;
- do not add local/remote CLI discovery.

Those are separate concerns.

---

## 8. Phase 4 — Remove Kiota

Only after all production and test references have been migrated or explicitly classified, remove the obsolete Kiota infrastructure.

Remove, where confirmed unused:

- Kiota-generated source files;
- Kiota generation output;
- Kiota generation scripts;
- Kiota configuration;
- Kiota-specific project references;
- Kiota NuGet packages;
- obsolete `using` statements;
- Kiota-specific tests that are no longer relevant.

Before deleting generated files, verify that the generated directory contains only obsolete Kiota output.

Do not remove unrelated generated code.

### Required final condition

There must be no runtime dependency on Kiota.

Preferably there should also be no Kiota package or generation dependency anywhere in the Visual Studio extension project.

If a reference must remain for a legitimate reason, document it explicitly instead of hiding it.

---

## 9. Phase 5 — Remove Legacy HTTP Clients

Check:

```text
HttpClientWrapper.cs
CachedHttpClient.cs
```

If exhaustive reference searches show that they are no longer used:

- remove them;
- remove related project references/usings;
- verify the build.

Do not remove them if a legitimate reference remains.

Do not replace them with another generic HTTP wrapper.

The NSwag client is the REST abstraction.

---

## 10. Phase 6 — Preserve NSwag Generation

Do not manually modify generated NSwag source.

The NSwag client must remain reproducible from the OpenAPI specification.

Preserve the existing:

- NSwag version;
- generation command;
- generation configuration;
- regeneration script;
- generation documentation.

Verify that the documented generation process still produces a usable client.

If documentation is inaccurate, fix the documentation rather than changing the architecture.

The source of the API remains:

```text
packages/sdk/openapi.json
```

---

## 11. Phase 7 — Tests

After migration and cleanup:

### Build

Build the complete Visual Studio extension.

Expected result:

```text
0 compilation errors
```

Warnings must be classified as:

- existing;
- introduced by this task.

Do not dismiss newly introduced warnings as pre-existing without verification.

### Tests

Run the available Visual Studio test suite.

At minimum, verify:

- NSwag authentication tests;
- NSwag polymorphic model tests;
- NSwag error handling tests;
- existing Visual Studio tests;
- tests affected by the migrated services.

The previously known `CloudSessionHandlerTests` compilation problem has already been corrected in the repository.

Treat the current repository state as authoritative.

---

## 12. Phase 8 — Repository-Wide Verification

After all modifications, repeat the exhaustive search.

The following should return zero production references:

```text
KiloVisualStudioExtension.Generated
GetKiloClient(
Kiota
Microsoft.Kiota
```

Also verify:

```text
HttpClientWrapper
CachedHttpClient
```

If these still occur, determine whether each occurrence is:

- legitimate documentation/history;
- generated code;
- test;
- dead code;
- an actual remaining dependency.

The goal is that the Visual Studio extension has one generated REST client:

```text
KiloApiClient (NSwag)
```

---

## 13. Documentation Synchronization

Update the documentation under:

```text
packages/kilo-visualstudio/porting/
```

and:

```text
packages/kilo-visualstudio/tasks/
```

as necessary.

### PORT-CLI-001

Update the PORT-CLI-001 documentation so that it accurately states:

- NSwag is the final REST client;
- Kiota was evaluated and superseded;
- all required production migrations are complete;
- Kiota has been removed;
- known OpenAPI limitations are documented;
- validation results are current.

Do not create a contradictory second completion report.

Update the existing completion report when appropriate.

### TASKS.md

Update task state consistently.

If `PORT-CLI-001` is currently `REVIEW`, only move it to `COMPLETE` after:

- migration is complete;
- Kiota cleanup is complete;
- build succeeds;
- relevant tests pass;
- documentation is synchronized.

If a dedicated Kiota cleanup task already exists, update its state rather than creating a duplicate task.

---

## 14. Scope Restrictions

This task is intentionally limited to completing the REST client migration and removing Kiota.

Do NOT:

- redesign `KiloConnectionService`;
- redesign `SseClient`;
- change CLI executable discovery;
- implement automatic CLI downloading;
- implement WebView integration;
- start `PORT-WEBVIEW-001`;
- introduce a new HTTP client;
- replace NSwag with another generator;
- manually modify generated NSwag source;
- perform unrelated refactoring;
- change unrelated UI behavior.

### Deferred functionality

Do not automatically implement the following unless required to complete the migration:

- SSE event normalization;
- `drainPendingPrompts`;
- VS Code test porting.

These remain separate concerns unless the current implementation proves that they are required for the NSwag migration itself.

---

## 15. Definition of Done

CLEANUP-CLI-001 is complete only when all of the following are true:

- [ ] Every production Kiota usage has been migrated or explicitly justified.
- [ ] Every test dependency on Kiota has been migrated or explicitly justified.
- [ ] `KiloConnectionService` uses NSwag for REST.
- [ ] All relevant providers/services use NSwag.
- [ ] `HttpClientWrapper` is removed if unused.
- [ ] `CachedHttpClient` is removed if unused.
- [ ] Kiota-generated code is removed.
- [ ] Kiota packages are removed.
- [ ] Kiota generation infrastructure is removed.
- [ ] NSwag remains reproducible from `packages/sdk/openapi.json`.
- [ ] NSwag authentication remains functional.
- [ ] NSwag polymorphic model handling remains functional.
- [ ] NSwag error handling remains functional.
- [ ] Extension builds with 0 errors.
- [ ] Relevant tests pass.
- [ ] Repository-wide searches confirm no accidental Kiota dependency remains.
- [ ] PORT-CLI-001 documentation is synchronized.
- [ ] TASKS.md is synchronized.
- [ ] Known OpenAPI limitations are documented.
- [ ] No unrelated architecture or behavior changes were introduced.

---

## 16. Final Report

At completion, provide a concise report containing:

### Migration

- Kiota references before migration;
- Kiota references after migration;
- production files migrated;
- tests migrated.

### Cleanup

- Kiota generated files removed;
- Kiota packages removed;
- Kiota generation infrastructure removed;
- legacy HTTP clients removed;
- remaining generated code.

### Validation

- build result;
- test result;
- NSwag authentication test result;
- NSwag polymorphic model test result;
- NSwag error handling test result;
- final repository-wide search results.

### API limitations

List any operations that remain unavailable because they are absent from the current OpenAPI specification.

### Documentation

List every task/plan/report/documentation file modified.

### Final architecture

Confirm explicitly:

```text
REST client: NSwag KiloApiClient
SSE client: SseClient
Legacy REST clients: removed
Kiota: removed
```

Do not report the task as complete solely because the project compiles. The final repository-wide dependency audit must confirm that Kiota has actually been removed.