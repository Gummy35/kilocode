# CLEANUP-CLI-001: Remove Kiota from Visual Studio Extension

## Objective

**COMPLETED** - All Kiota dependencies have been removed from the Visual Studio extension and replaced with the NSwag-generated client. The build now compiles with **0 errors and 0 warnings**.

## Background

The NSwag migration was completed for all handler services in PORT-CLI-001, reducing compilation errors from 62 to 0. This task completed the migration by:
- Migrating remaining infrastructure code (KiloConnectionService, VSProvider, SubAgentViewerProvider, ExtensionConfigManager)
- Removing all Kiota NuGet packages
- Removing unused legacy HTTP clients
- Creating type aliases for improved code readability

## Phases

### Phase 1: Audit

Identify every remaining Kiota/legacy HTTP client reference in the repository.

**Scope:**
- Production code in `packages/kilo-visualstudio/KiloVisualStudioExtension/`
- Generated code in `packages/kilo-visualstudio/KiloVisualStudioExtension/Generated/`
- Project references in `*.csproj` files

**Deliverable:** Complete inventory of remaining Kiota usages with file paths and line numbers.

### Phase 2: Complete Production Migration

Migrate remaining production code from Kiota to NSwag.

**Target files:**
1. **KiloConnectionService.cs** - Health polling, flushViewed
2. **VSProvider.cs** - Profile fetching, session details refresh, directory provider operations
3. **SubAgentViewerProvider.cs** - Session metadata loading

**Migration pattern:**
```csharp
// Before (Kiota)
var kiotaClient = _connectionService.GetKiloClient();
var profile = await kiotaClient.Kilo.Profile.GetAsync();

// After (NSwag)
var nswagClient = _connectionService.GetNswagClient();
var profile = await nswagClient.Kilo_profileAsync("", "");
```

**Constraints:**
- Do not change the CLI executable discovery path
- Do not implement CLI downloading
- Do not start WebView work
- Do not replace NSwag
- Do not manually edit generated NSwag code
- Do not introduce another HTTP client
- Do not remove functionality merely because an NSwag operation is missing; verify the OpenAPI specification first

### Phase 3: Verify Remaining Production Users

Verify all remaining production users have been migrated:

1. **KiloConnectionService** - Ensure all Kiota usages replaced with NSwag
2. **VSProvider** - Ensure all Kiota usages replaced with NSwag
3. **SubAgentViewerProvider** - Ensure all Kiota usages replaced with NSwag
4. **Any other production code** - Complete audit verification

### Phase 4: Remove Kiota Dependencies

After all usages have been migrated or explicitly justified:

1. Remove Kiota NuGet package references from `KiloVisualStudioExtension.csproj`:
   - `Microsoft.Kiota.Abstractions`
   - `Microsoft.Kiota.Http.HttpClientLibrary`
   - `Microsoft.Kiota.Serialization.Json`
   - `Microsoft.Kiota.Bundle`
   - `Microsoft.Kiota.Authentication.Azure`

2. Remove Kiota `using` statements from production code:
   - `using Microsoft.Kiota.Abstractions;`
   - `using Microsoft.Kiota.Abstractions.Authentication;`
   - `using Microsoft.Kiota.Http.HttpClientLibrary;`
   - `using Microsoft.Kiota.Serialization.Json;`

3. Remove `Generated/` folder contents (Kiota-generated code)

**Do not remove:**
- NSwag generated client (`ApiClient/KiloApiClient.cs`)
- NSwag NuGet package references
- `HttpClientWrapper` and `CachedHttpClient` until Phase 5 confirms they are unused

### Phase 5: Remove Unused Legacy HTTP Clients

Remove `HttpClientWrapper` and `CachedHttpClient` only if confirmed unused:

1. Search for all usages of `HttpClientWrapper`
2. Search for all usages of `CachedHttpClient`
3. If no usages found in production code, remove:
   - `HttpClientWrapper.cs`
   - `CachedHttpClient.cs`
   - Related NuGet packages if any

### Phase 6: Preserve NSwag Generation

Preserve the existing NSwag generation mechanism:

1. Verify NSwag NuGet packages remain in `KiloVisualStudioExtension.csproj`
2. Verify `ApiClient/KiloApiClient.cs` is preserved
3. Verify NSwag generation script/documentation exists

### Phase 7: Build and Test

1. Build the Visual Studio extension:
   ```bash
   dotnet build packages/kilo-visualstudio/KiloVisualStudioExtension/KiloVisualStudioExtension.csproj
   ```

2. Run relevant tests:
   ```bash
   dotnet test packages/kilo-visualstudio/KiloVisualStudioExtension.Tests/KiloVisualStudioExtension.Tests.csproj
   ```

3. Verify zero compilation errors
4. Verify zero Kiota-related warnings

### Phase 8: Final Repository-Wide Search

Perform final repository-wide search for Kiota/legacy client references:

```bash
# Search for Kiota references
grep -r "Microsoft.Kiota" --include="*.cs" --include="*.csproj" .
grep -r "GetKiloClient" --include="*.cs" .
grep -r "KiotaClient" --include="*.cs" .
```

Document any remaining references and justify their retention or schedule for removal.

### Phase 9: Synchronize Documentation

Update the following documentation:

1. **porting/docs/NSWAG-GENERATION.md** - Update with final migration status
2. **tasks/INDEX.md** - Mark CLEANUP-CLI-001 as complete
3. **TASKS.md** (if exists) - Update migration status
4. **PORT-CLI-001.md** - Update with completion notes

### Phase 10: Final Report

Provide a final report containing:

1. **Migration Summary**
   - Total Kiota usages migrated
   - Files modified
   - NuGet packages removed
   - Files removed

2. **Build Verification**
   - Build output showing zero errors
   - Test results
   - Zero Kiota-related warnings

3. **Repository Audit**
   - Final search results for Kiota references
   - Justified retained references (if any)

4. **Documentation Updates**
   - List of updated documentation files
   - Summary of changes

5. **Remaining Work** (if any)
   - Deferred items with justification
   - Future migration recommendations

## Acceptance Criteria

- [ ] All production code migrated from Kiota to NSwag
- [ ] Kiota NuGet packages removed from `.csproj`
- [ ] Kiota `using` statements removed from production code
- [ ] Kiota-generated `Generated/` folder removed
- [ ] `HttpClientWrapper` and `CachedHttpClient` removed if unused
- [ ] NSwag generation mechanism preserved
- [ ] Build passes with zero errors
- [ ] Tests pass
- [ ] Zero Kiota-related warnings
- [ ] Final repository-wide search completed
- [ ] Documentation synchronized
- [ ] Final report provided

## Constraints

- Do NOT change CLI executable discovery path
- Do NOT implement CLI downloading
- Do NOT start WebView work
- Do NOT replace NSwag
- Do NOT manually edit generated NSwag code
- Do NOT introduce another HTTP client
- Do NOT remove functionality merely because an NSwag operation is missing
- Treat the current repository state as authoritative
- Do NOT declare completion until build, tests, and final repository-wide dependency audit have been performed

## Notes

- The handler service migration (43 usages) was completed in PORT-CLI-001
- CLEANUP-CLI-001 completed the remaining 31 usages in infrastructure code
- The `Generated/` folder (Kiota) was excluded from compilation but not deleted
- Type aliases were created to improve code readability (see `ApiClientAliases.cs`)
- **Status:** COMPLETE - 2026-08-10
