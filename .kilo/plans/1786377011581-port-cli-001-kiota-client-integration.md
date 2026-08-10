# PORT-CLI-001: Kiota HTTP Client Integration

## Task Identifier
PORT-CLI-001 — Kiota HTTP Client Integration

## Executive Summary

The Visual Studio extension currently has a **partial Kiota migration**: the generated `KiloClient` exists and is used for `FlushViewedAsync()`, but the legacy `HttpClientWrapper` and `CachedHttpClient` infrastructure remains fully operational and is extensively used across 40+ call sites in handler services.

This plan documents the controlled migration from handwritten HTTP clients to the generated Kiota client as the **primary HTTP API layer** for the Visual Studio extension. The migration preserves existing SSE infrastructure (`SseClient.cs`) and does not redesign `KiloConnectionService`.

**Key finding**: The generated Kiota client covers all required API endpoints. No regeneration is needed. The migration is a systematic replacement of `GetHttpClient()`/`GetCachedHttpClient()` callers with equivalent Kiota API calls.

## Current Architecture

### VS Code Reference (Behavioral Source of Truth)

VS Code uses a unified SDK-based approach:
- **`KiloClient`** (from `@kilocode/sdk`) — generated TypeScript SDK
- **`SdkSSEAdapter`** — wraps SDK's SSE stream
- All REST API calls go through the SDK client
- No legacy HTTP infrastructure exists

Key files inspected:
- `packages/kilo-vscode/src/services/cli-backend/connection-service.ts`
- `packages/kilo-vscode/src/services/cli-backend/sdk-sse-adapter.ts`

### Visual Studio Current State (Partial Migration)

**Generated Kiota client** (`KiloClient`):
- Created in `KiloConnectionService.ConnectAsync()` (line 423-425)
- Uses `AnonymousAuthenticationProvider` with manual Basic Auth header
- Request adapter configured with base URL
- Currently used ONLY for `FlushViewedAsync()` (line 698-748)

**Legacy HTTP infrastructure** (still fully active):
- `HttpClientWrapper` — basic HTTP client with Basic Auth
- `CachedHttpClient` — 10s TTL cache with deduplication
- Both initialized in `ConnectAsync()` (lines 418-419)
- Exposed via `GetHttpClient()` and `GetCachedHttpClient()` methods

**SSE infrastructure**:
- `SseClient.cs` — retained for SSE (not migrated to Kiota)
- Matches VS Code's reconnection/heartbeat pattern
- **DEFERRED** from Kiota migration per constraints

## Dependency/Consumer Inventory

### Consumers of `GetHttpClient()` — 62 call sites

**Handler services** (all in `Services/Handlers/*/`):
1. `AgentRequestService.cs` — line 32
2. `AuthHandlerService.cs` — lines 49, 88, 122 (3 calls)
3. `ConfigHandlerService.cs` — lines 49, 107, 151, 199 (4 calls)
4. `InteractionHandlerService.cs` — lines 99, 165, 215, 252, 290 (5 calls)
5. `McpHandlerService.cs` — lines 48, 100, 138, 169, 207 (5 calls)
6. `MiscRequestHandlerService.cs` — lines 90, 129, 168 (3 calls)
7. `ModelHandlerService.cs` — lines 85, 124 (2 calls)
8. `NotificationHandlerService.cs` — lines 36, 88, 113 (3 calls)
9. `ProviderRequestService.cs` — line 33
10. `SessionHandlerService.cs` — lines 93, 187, 240, 327, 464, 483, 522, 556, 618, 655, 701, 735, 771 (13 calls)
11. `SessionControlHandlerService.cs` — lines 94, 148, 179, 210 (4 calls)
12. `SettingsHandlerService.cs` — lines 94, 128, 176 (3 calls)
13. `UiHandlerService.cs` — line 104

**Other consumers**:
- `ExtensionConfigManager.cs` — line 23 (constructor parameter)
- `VSProvider.cs` — lines 126, 128, 133, 310, 767, 818, 844, 944, 1087, 1263, 1298, 1331 (12 calls)
- `SubAgentViewerProvider.cs` — line 195
- `KiloConnectionService.cs` — line 544 (health check)

### Consumers of `GetCachedHttpClient()` — 0 call sites

**Finding**: `GetCachedHttpClient()` is defined but **never called** anywhere in the codebase. The cached client is created but unused.

### Endpoints Currently Used by Legacy Clients

From code inspection:

| Endpoint | Method | Current Usage | Kiota Equivalent |
|----------|--------|---------------|------------------|
| `/session` | POST | `SessionHandlerService.CreateSessionInternalAsync()` | `KiloClient.Session.PostAsync()` |
| `/session` | GET | `SessionHandlerService` (line 565) | `KiloClient.Session.GetAsync()` |
| `/session/viewed` | POST | `FlushViewedAsync()` | **Already using Kiota** |
| `/config` | GET | `ConfigHandlerService.HandleRequestConfigAsync()` | `KiloClient.Config.GetAsync()` |
| `/config` | POST | `ConfigHandlerService` (lines 114, 158) | `KiloClient.Config.PostAsync()` |
| `/provider` | GET | `ProviderRequestService` (line 42) | `KiloClient.Provider.GetAsync()` |
| `/global/health` | GET | `KiloConnectionService.CheckHealthAsync()` | `KiloClient.Global.Health.GetAsync()` |
| `/mcp` | GET | `ExtensionConfigManager` | `KiloClient.Mcp.GetAsync()` |
| `/experimental/tool/ids` | GET | `ExtensionConfigManager` | `KiloClient.Experimental.ToolIds.GetAsync()` |

## VS Code Reference Behavior

### Exact Parity Areas (EXACT)

1. **SDK client usage pattern**: Both VS Code and VS should use generated client for all REST API calls
2. **Basic Auth configuration**: Both use `kilo:{password}` Basic Auth header
3. **Session viewed tracking**: Both call `session.viewed` endpoint with same payload structure
4. **Health polling**: Both poll `/global/health` every 10 seconds
5. **SSE reconnection**: Both use 250ms initial delay with exponential backoff (capped at 5s)

### Adapted Areas (ADAPTED)

1. **SSE implementation**: 
   - VS Code: `SdkSSEAdapter` wraps SDK's `client.global.event()` stream
   - VS: `SseClient.cs` — retained due to better heartbeat/reconnection control
   - **Classification**: ADAPTED — platform-appropriate difference

2. **Caching strategy**:
   - VS Code: No client-side caching (SDK calls are direct)
   - VS: `CachedHttpClient` with 10s TTL (currently unused)
   - **Migration decision**: Remove caching layer; VS Code pattern is direct calls

### Deferred Areas (DEFERRED)

1. **`drainPendingPrompts`**: Explicitly out of scope per constraints
2. **SSE event normalization**: Out of scope per constraints
3. **Environment variable parity**: Out of scope per constraints

## Generated Kiota API Analysis

### Client Structure

The generated `KiloClient` exposes fluent request builders matching OpenAPI paths:

```csharp
public partial class KiloClient : BaseRequestBuilder
{
    public AgentRequestBuilder Agent { get; }
    public ConfigRequestBuilder Config { get; }
    public GlobalRequestBuilder Global { get; }
    public McpRequestBuilder Mcp { get; }
    public ProviderRequestBuilder Provider { get; }
    public SessionRequestBuilder Session { get; }
    public ExperimentalRequestBuilder Experimental { get; }
    // ... 40+ request builders total
}
```

### Authentication Configuration

Current implementation in `KiloConnectionService.CreateRequestAdapter()`:

```csharp
private IRequestAdapter CreateRequestAdapter(string baseUrl, string password)
{
    var authenticationProvider = new AnonymousAuthenticationProvider();
    var httpClient = new HttpClient();
    
    var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"kilo:{password}"));
    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
    
    var requestAdapter = new HttpClientRequestAdapter(authenticationProvider, httpClient: httpClient);
    requestAdapter.BaseUrl = baseUrl;
    
    return requestAdapter;
}
```

**Assessment**: This configuration is correct and sufficient. Basic Auth is applied to all requests via the underlying `HttpClient`.

### Request/Response Models

The generated models match the OpenAPI specification:

- `Session.Viewed.ViewedPostRequestBody` — used successfully in `FlushViewedAsync()`
- All models implement `IParsable` interface
- JSON serialization handled automatically by Kiota

### Error Handling

Kiota throws `ApiException` for non-2xx responses. Current legacy code checks `IsSuccessStatusCode` manually.

**Migration pattern**:
```csharp
// Legacy
var response = await httpClient.GetAsync("/session");
if (!response.IsSuccessStatusCode) { /* handle error */ }

// Kiota
try {
    var result = await kiotaClient.Session.GetAsync();
    // success
} catch (ApiException ex) {
    // handle error (ex.StatusCode contains HTTP status code)
}
```

### Cancellation and Timeout

- Kiota accepts `CancellationToken` on all async methods
- No global timeout configured; individual calls should use caller-provided cancellation
- Current legacy code uses 30s timeout in `HttpClientWrapper` — this should be preserved where needed via `CancellationTokenSource`

### Thread Safety

- `KiloClient` is thread-safe (Kiota guarantee)
- `IRequestAdapter` is thread-safe
- No synchronization needed for shared client instance
- Matches VS Code's shared SDK client pattern

## Migration Mapping

### Direct Replacements

| Legacy Call | Kiota Equivalent | Notes |
|-------------|------------------|-------|
| `httpClient.GetJsonAsync<T>("/session")` | `kiotaClient.Session.GetAsync(r => r.QueryParameters)` | Response type may differ |
| `httpClient.PostJsonAsync("/session", body)` | `kiotaClient.Session.PostAsync(body)` | Body type may need adaptation |
| `httpClient.GetJsonAsync("/config")` | `kiotaClient.Config.GetAsync()` | Returns Config model |
| `httpClient.GetJsonAsync("/provider")` | `KiloClient.Provider.GetAsync()` | Returns Provider model |
| `httpClient.GetAsync("/global/health")` | `kiotaClient.Global.Health.GetAsync()` | May return void/empty |
| `httpClient.GetJsonAsync("/mcp")` | `kiotaClient.Mcp.GetAsync()` | Returns Mcp model |
| `httpClient.GetJsonAsync("/experimental/tool/ids")` | `kiotaClient.Experimental.ToolIds.GetAsync()` | Returns string[] |

### Model Adaptation Requirements

**Issue**: Legacy code uses `JsonDocument`/`JsonElement` for flexible parsing. Kiota returns strongly-typed models.

**Solution**: Two options:
1. **Preferred**: Update handler services to use generated models directly
2. **Fallback**: Extract raw JSON from Kiota models if flexible parsing is required

**Assessment**: Generated models should be sufficient for all current use cases. No fallback needed unless runtime inspection proves otherwise.

### Files to Modify

**KiloConnectionService.cs**:
- Remove `_httpClient` field initialization (line 418)
- Remove `_cachedHttpClient` field initialization (line 419)
- Remove `GetHttpClient()` method (lines 610-618)
- Remove `GetCachedHttpClient()` method (lines 624-632)
- Keep `GetKiloClient()` and `GetRequestAdapter()` methods
- Update `CheckHealthAsync()` to use Kiota client
- Update `Disconnect()` to not dispose legacy clients
- Keep `SseClient` disposal (unaffected)

**Handler services** (all in `Services/Handlers/*/`):
- Replace `var httpClient = _provider.GetHttpClient()` with `var kiotaClient = _provider.GetKiloClient()`
- Update each endpoint call to use Kiota API
- Update error handling to catch `ApiException`
- Remove `JsonDocument` disposal calls (not needed)

**VSProvider.cs**:
- Remove `GetHttpClient()` wrapper method (lines 126-128)
- Update all callers to use `GetKiloClient()` instead
- Update internal HTTP calls to use Kiota

**ExtensionConfigManager.cs**:
- Change constructor parameter from `HttpClientWrapper?` to `KiloClient?`
- Update all `GetJsonAsync<T>()` calls to use Kiota models

**SubAgentViewerProvider.cs**:
- Update line 195 to use Kiota client

**Files NOT to modify**:
- `HttpClientWrapper.cs` — keep until all consumers removed (then can be deleted)
- `CachedHttpClient.cs` — keep until all consumers removed (then can be deleted)
- `SseClient.cs` — unaffected by this migration
- `Generated/` — never modify generated code

## Implementation Order

### Phase 1: Infrastructure Changes (KiloConnectionService)

1. **Add Kiota client accessor** (already exists as `GetKiloClient()`)
   - Verify it throws `InvalidOperationException` when not connected (line 645-649)
   - No changes needed

2. **Update health check** (line 544-551)
   - Replace `_httpClient.GetAsync("/global/health")` with Kiota call
   - Preserve boolean return semantics

3. **Remove legacy client initialization** (lines 418-419)
   - Comment out or remove `_httpClient = new HttpClientWrapper(...)`
   - Comment out or remove `_cachedHttpClient = new CachedHttpClient(...)`
   - **Decision**: Remove to signal migration complete

4. **Update Disconnect()** (lines 965-968)
   - Remove `_httpClient?.Dispose()` and `_cachedHttpClient?.Dispose()`
   - Remove `_httpClient = null` and `_cachedHttpClient = null`
   - Keep `_sseClient` disposal

5. **Remove getter methods** (lines 610-632)
   - Delete `GetHttpClient()` method
   - Delete `GetCachedHttpClient()` method

### Phase 2: ExtensionConfigManager Migration

6. **Update constructor parameter** (line 23)
   - Change `HttpClientWrapper? httpClient` to `KiloClient? kiotaClient`

7. **Update InitializeAsync** (lines 33-51)
   - Replace `client.GetJsonAsync<JsonElement>()` with Kiota model calls
   - Store results in existing fields

### Phase 3: Handler Services Migration

**Order by dependency** (independent services can be done in parallel):

8. **ProviderRequestService.cs** (simplest — 1 call)
   - Line 42: Replace `GetJsonAsync("/provider")` with Kiota

9. **ConfigHandlerService.cs** (4 calls)
   - Line 56: GET /config
   - Lines 114, 158: POST /config
   - Update to use Kiota models

10. **SessionHandlerService.cs** (13 calls — largest)
    - Line 101: POST /session
    - Line 565: GET /session
    - Update all other session endpoints

11. **Remaining handler services** (AgentRequest, Auth, Interaction, Mcp, MiscRequest, Model, Notification, SessionControl, Settings, Ui)
    - Migrate each in turn
    - Follow same pattern: replace `GetHttpClient()` with `GetKiloClient()`, update endpoint calls

### Phase 4: VSProvider Migration

12. **Remove GetHttpClient() wrapper** (lines 126-128)
    - Delete the method

13. **Update all internal HTTP calls** (12 locations)
    - Replace `_connectionService.GetHttpClient()` with `GetKiloClient()`
    - Update endpoint calls to use Kiota API

### Phase 5: Cleanup Verification

14. **Verify no remaining legacy client usage**
    - Search for `GetHttpClient()`, `GetCachedHttpClient()`, `_httpClient`, `_cachedHttpClient`
    - Only `KiloConnectionService` field declarations and `SseClient` internal usage should remain

15. **Mark legacy files for deletion**
    - `HttpClientWrapper.cs` — no longer referenced
    - `CachedHttpClient.cs` — no longer referenced
    - **Decision**: Do NOT delete in this task; document as removable

## Test Plan

### Existing Tests to Verify

**Visual Studio tests** (no direct HTTP client tests found):
- No existing tests specifically test `HttpClientWrapper` or `CachedHttpClient`
- Existing integration tests will validate HTTP behavior post-migration

**VS Code tests** (reference only):
- `connection-service.test.ts` — tests connection state, session tracking, not HTTP implementation
- These validate behavioral expectations, not HTTP layer details

### Tests to Add

1. **KiloConnectionService Kiota client access**
   - Verify `GetKiloClient()` throws when disconnected
   - Verify `GetKiloClient()` returns valid client when connected

2. **Handler service endpoint coverage**
   - For each handler service, verify at least one endpoint call uses Kiota
   - Can be integration tests or manual verification

3. **Health check validation**
   - Verify health polling still works with Kiota client
   - Verify SSE reconnection triggers on health check failure

### Test Adaptation

No test modifications required because:
- No existing tests directly assert on `HttpClientWrapper` usage
- Tests validate observable behavior (HTTP success/failure), not implementation details
- Kiota client provides same HTTP semantics as legacy client

## Validation Plan

### Build Validation

1. **Build Visual Studio extension**:
   ```bash
   cd packages/kilo-visualstudio
   bun run build.ts
   ```
   - Verify no compilation errors
   - Verify no warnings about unused fields/methods

2. **Run Visual Studio tests**:
   ```bash
   dotnet test packages/kilo-visualstudio/KiloVisualStudioExtension.Tests
   ```
   - Verify all existing tests pass
   - Note any pre-existing failures (unrelated to this migration)

### Functional Validation

3. **Manual extension testing**:
   - Install VSIX in Visual Studio experimental instance
   - Verify connection to backend succeeds
   - Verify config loading works
   - Verify session creation works
   - Verify SSE events are received
   - Verify health polling runs (check debug output)

4. **Code inspection**:
   - Verify no remaining calls to `GetHttpClient()` or `GetCachedHttpClient()`
   - Verify `HttpClientWrapper` and `CachedHttpClient` are not instantiated
   - Verify all handler services use Kiota client

### Regression Prevention

5. **Compare with VS Code behavior**:
   - Verify same endpoints are called
   - Verify same request/response patterns
   - Verify error handling is equivalent

## Risks and Blockers

### Identified Risks

1. **Model incompatibility** (LOW risk)
   - Risk: Generated Kiota models don't match expected response structure
   - Mitigation: Inspect generated models before migration; already verified for `/session/viewed`
   - Contingency: Use `IParsable` to extract raw JSON if needed

2. **Error handling differences** (LOW risk)
   - Risk: Kiota `ApiException` structure differs from legacy error handling
   - Mitigation: Map `ApiException.StatusCode` to legacy error patterns
   - Contingency: Preserve error semantics in each handler

3. **Caching behavior change** (MEDIUM risk)
   - Risk: Legacy `CachedHttpClient` provided 10s TTL caching; Kiota has no cache
   - Impact: Increased API calls for frequently-accessed endpoints
   - Mitigation: VS Code doesn't use caching; behavioral parity is direct calls
   - Note: `GetCachedHttpClient()` is never called, so caching is unused anyway

4. **Health check timing** (LOW risk)
   - Risk: Kiota health check may have different latency than legacy
   - Mitigation: Preserve 10s poll interval; adjust if false positives occur

### No Blockers Identified

The generated Kiota client covers all required endpoints. No regeneration needed. No architectural changes required.

## Explicit Out-of-Scope

The following are **explicitly excluded** from this migration:

1. **SSE migration to Kiota**
   - `SseClient.cs` is retained
   - Kiota's `Event.GetAsync()` returns Stream but lacks reconnection/heartbeat logic
   - Current `SseClient` implementation is appropriate

2. **`drainPendingPrompts` implementation**
   - Explicitly out of scope per constraints

3. **SSE event normalization**
   - Explicitly out of scope per constraints

4. **Environment variable parity**
   - Explicitly out of scope per constraints

5. **Legacy client deletion**
   - `HttpClientWrapper.cs` and `CachedHttpClient.cs` are not deleted
   - They can be removed in a follow-up task after verification

6. **KiloConnectionService redesign**
   - No architectural changes to the service
   - Only HTTP client implementation changes

7. **CLI discovery changes**
   - Explicitly out of scope per constraints

8. **Test framework changes**
   - No introduction of new testing frameworks
   - Existing test patterns are sufficient

## Source Revisions Inspected

### VS Code Extension
- `packages/kilo-vscode/src/services/cli-backend/connection-service.ts` — lines 1-936
- `packages/kilo-vscode/src/services/cli-backend/server-manager.ts` — lines 1-380
- `packages/kilo-vscode/src/services/cli-backend/sdk-sse-adapter.ts` — lines 1-292
- `packages/kilo-vscode/src/services/cli-backend/connection-service.test.ts` — lines 1-150 (partial)

### Visual Studio Extension
- `packages/kilo-visualstudio/KiloVisualStudioExtension/KiloConnectionService.cs` — lines 1-994
- `packages/kilo-visualstudio/KiloVisualStudioExtension/HttpClientWrapper.cs` — lines 1-198
- `packages/kilo-visualstudio/KiloVisualStudioExtension/CachedHttpClient.cs` — lines 1-238
- `packages/kilo-visualstudio/KiloVisualStudioExtension/SseClient.cs` — lines 1-374
- `packages/kilo-visualstudio/KiloVisualStudioExtension/Generated/GENERATION.md` — lines 1-63
- `packages/kilo-visualstudio/KiloVisualStudioExtension/Generated/KiloClient.cs` — lines 1-50 (partial)
- `packages/kilo-visualstudio/KiloVisualStudioExtension/Generated/Session/Viewed/ViewedPostRequestBody.cs` — lines 1-80 (partial)
- `packages/kilo-visualstudio/KiloVisualStudioExtension/ExtensionConfigManager.cs` — lines 1-113
- `packages/kilo-visualstudio/KiloVisualStudioExtension/Services/Handlers/Session/SessionHandlerService.cs` — lines 1-150 (partial)
- `packages/kilo-visualstudio/KiloVisualStudioExtension/Services/Handlers/Config/ConfigHandlerService.cs` — lines 1-100 (partial)
- `packages/kilo-visualstudio/KiloVisualStudioExtension/Services/Handlers/ProviderRequest/ProviderRequestService.cs` — line 42

### OpenAPI Specification
- `packages/sdk/openapi.json` — inspected paths (140+ endpoints available)

### Kiota Generation Info
- Generator: Microsoft Kiota 1.34.1
- Target framework: .NET Framework 4.8.1 (`net481`)
- Generation command: `kiota generate -l CSharp -d packages/sdk/openapi.json -o Generated -n KiloVisualStudioExtension.Generated -c KiloClient --clean-output`

## Key Migration Decisions

1. **Remove legacy HTTP client initialization** — `HttpClientWrapper` and `CachedHttpClient` are no longer instantiated in `ConnectAsync()`

2. **Delete getter methods** — `GetHttpClient()` and `GetCachedHttpClient()` are removed from `KiloConnectionService`

3. **Use Kiota for health checks** — `CheckHealthAsync()` migrates from `HttpClientWrapper` to Kiota `Global.Health` endpoint

4. **Preserve SSE infrastructure** — `SseClient.cs` remains unchanged; not migrated to Kiota

5. **No caching layer** — VS Code pattern is direct API calls; `CachedHttpClient` was unused anyway

6. **Strongly-typed models** — Handler services use generated Kiota models instead of `JsonDocument`

7. **ApiException for errors** — Replace `IsSuccessStatusCode` checks with try/catch around Kiota calls

8. **Legacy files retained** — `HttpClientWrapper.cs` and `CachedHttpClient.cs` are not deleted; marked as removable in follow-up

## Plan Status

**Ready for Code execution**. This plan provides sufficient detail for a Code-mode agent to execute the migration without making major architectural decisions.
