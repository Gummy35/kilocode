# NSwag Client Validation Report

## Executive Summary

The NSwag client has been generated successfully and is structurally viable for migration. However, several critical validation areas require attention before production migration can proceed.

---

## 1. Framework Compatibility

### Target Framework
- **Project**: `.NET Framework 4.8.1` (net481)
- **LangVersion**: 14 (C# 14)
- **Nullable**: Enabled

### Generated Code Analysis
- **NSwag Version**: 14.7.1.0
- **NJsonSchema Version**: 11.6.1.0
- **Newtonsoft.Json Version**: 13.0.0.0 (project has 13.0.3 - compatible)

### Compatibility Concerns
1. **C# 14 Features**: The project uses LangVersion 14, which is compatible with .NET Framework 4.8.1 when using appropriate NuGet packages
2. **Nullable Reference Types**: Generated code includes nullable annotations with appropriate pragma warnings disabled
3. **System.Net.Http**: Already referenced in project (version 4.3.4)

### Build Status
- Project builds with pre-existing warnings (nullable-related)
- **No NSwag-specific compilation errors detected**
- Kiota-related warnings exist but are pre-existing

---

## 2. Generated API Surface

### Statistics
| Metric | Count |
|--------|-------|
| Total Lines | ~86,500 |
| Total Types | ~1,498 |
| API Operations | ~236 (confirmed via method count) |
| Interface Count | 1 (IKiloApiClient) |
| Class Count | 1,498 (including models) |
| Error Types | 55 |
| Exception Types | 2 (ApiException, ApiException<TResult>) |

### Key Generated Types
- **Client**: `IKiloApiClient` / `KiloApiClient`
- **Exception**: `ApiException` with StatusCode, Response, Headers properties
- **Models**: All request/response DTOs with `ToJson()`/`FromJson()` methods

### Verified Endpoints
The following previously problematic endpoints are now present:
- ✅ `/auth/logout` → `Auth_removeAsync()`
- ✅ `/notification/*` → Notification-related methods (32 references found)
- ✅ `/mcp/connect` → `Mcp_connectAsync()`
- ✅ `/mcp/disconnect` → `Mcp_disconnectAsync()`
- ✅ `/indexing` → Present in generated client
- ✅ `/session/{id}/prompt` → `Session_promptAsync()`

---

## 3. OpenAPI Coverage Verification

### Critical Endpoints Confirmed
| Endpoint | NSwag Method | Status |
|----------|--------------|--------|
| `/auth/{providerID}` (PUT/DELETE) | `Auth_setAsync`, `Auth_removeAsync` | ✅ |
| `/global/config` (GET/PUT) | `Global_config_getAsync`, `Global_config_updateAsync` | ✅ |
| `/session/list` | `Session_listAsync` | ✅ |
| `/session/{id}/prompt` | `Session_promptAsync` | ✅ |
| `/mcp/connect` | `Mcp_connectAsync` | ✅ |
| `/mcp/disconnect` | `Mcp_disconnectAsync` | ✅ |
| `/notification/toast` | Toast notification methods | ✅ |
| `/model` | Model-related methods | ✅ |
| `/workstyle` | Workstyle methods | ✅ |

### Comparison with TypeScript SDK
Both generators produce equivalent API coverage from the same OpenAPI source. NSwag generates:
- More verbose output (~86,500 vs ~26,600 lines)
- Explicit interface generation
- Built-in exception classes
- JSON serialization methods on all models

---

## 4. Polymorphic Model Analysis

### Critical Finding: NSwag Does NOT Handle oneOf/anyOf
**Search Result**: 0 occurrences of "oneOf" or "anyOf" in generated code

This is a **critical limitation** that requires investigation:

### Previously Problematic Models
| Model | OpenAPI Schema | NSwag Output |
|-------|----------------|--------------|
| `Part` | oneOf variants | Single class with `AdditionalProperties` |
| `Message` | oneOf variants | Single class with `AdditionalProperties` |
| `Event` | oneOf variants | Base `Event` class + 100+ specific event classes with `Type` discriminator |
| `SessionMessage` | oneOf variants | Multiple specific classes (e.g., `SessionMessageAgentSwitched`) |
| `Question` | oneOf variants | `QuestionReplied`, `QuestionRejected`, `QuestionOption` |
| `ToolState` | oneOf variants | `ToolStatePending`, `ToolStateRunning`, `ToolStateCompleted` |
| `Provider` | Complex schema | Single `Provider` class |
| `PermissionConfig` | Complex schema | `PermissionObjectConfig` (Dictionary-based) |
| `PermissionRuleConfig` | Complex schema | `PermissionRule` |

### Event Type Handling
NSwag generates **separate classes for each event type** with a `Type` property discriminator:
- `EventTuiPromptAppend` with `Type` enum
- `EventTuiCommandExecute` with `Type` enum
- `EventTuiToastShow` with `Type` enum
- `EventTuiSessionSelect` with `Type` enum

This is **different from TypeScript SDK** which uses union types.

### Serialization Concern
The generated models use `AdditionalProperties` dictionary for unknown fields, which may not correctly represent polymorphic JSON payloads. **This requires runtime testing with actual CLI responses.**

---

## 5. Authentication Mechanism

### Current Kiota Implementation
```csharp
var authenticationProvider = new AnonymousAuthenticationProvider();
// Sets Basic Auth header: "kilo:{password}"
```

### NSwag Authentication Approach
NSwag does **not** generate built-in authentication providers. Authentication must be configured via:

**Option 1**: Override `PrepareRequestAsync` in partial class
**Option 2**: Use HttpClient interceptors
**Option 3**: Set Authorization header per request

### Recommended Implementation
```csharp
public partial class KiloApiClient
{
    protected override async Task PrepareRequestAsync(
        HttpClient client, 
        HttpRequestMessage request, 
        CancellationToken cancellationToken)
    {
        var auth = Convert.ToBase64String(
            System.Text.Encoding.ASCII.GetBytes($"kilo:{Password}"));
        request.Headers.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);
    }
}
```

### Dynamic BaseUrl Support
✅ **Confirmed**: `BaseUrl` property is configurable at runtime:
```csharp
public KiloApiClient(string baseUrl) { BaseUrl = baseUrl; }
public string BaseUrl { get; set; }
```

This supports dynamic CLI discovery via `CliBackendManager.BaseUrl`.

---

## 6. Cancellation and Error Handling

### CancellationToken Support
✅ **Confirmed**: All async methods have two overloads:
```csharp
Task<T> MethodAsync(params);
Task<T> MethodAsync(params, CancellationToken cancellationToken);
```

### Exception Handling
- **ApiException**: Contains `StatusCode`, `Response`, `Headers`
- **Generic ApiException<T>**: For typed responses
- **55 specific error types** generated (e.g., `InvalidRequestError`, `MoveSessionError`)

### Comparison with HttpClientWrapper
NSwag provides:
- ✅ Typed exceptions with HTTP status codes
- ✅ Response body access
- ✅ Header access
- ✅ Built-in error parsing

---

## 7. Generation Reproducibility

### Script Validation
`regenerate-nswag-client.ps1` correctly:
1. Fetches OpenAPI spec from `http://127.0.0.1:56631/doc`
2. Runs NSwag with `operationGenerationMode: SingleClientFromOperationId`
3. Verifies single interface/class output

### Documentation Accuracy
`NSWAG-GENERATION.md` accurately reflects:
- ✅ OpenAPI source location
- ✅ NSwag version (14.7.1)
- ✅ Generation parameters
- ✅ Output structure
- ⚠️ Git SHA should be updated if API changes

### Determinism
NSwag generation is deterministic for the same OpenAPI input. The `operationGenerationMode` setting prevents the duplication bug present in NSwag v14.7.1.

---

## 8. Kiota → NSwag Migration Mapping

### Key Type Mappings

| Kiota Type | NSwag Type | Notes |
|------------|------------|-------|
| `KiloClient` | `KiloApiClient` | Main client class |
| `IRequestAdapter` | N/A | NSwag uses HttpClient directly |
| `AnonymousAuthenticationProvider` | Partial class override | Manual Basic Auth implementation |
| Kiota error types | 55 generated error classes | Similar structure |
| Kiota models | NSwag models | Same names, different structure |

### Production Files Requiring Migration

**Primary Files:**
1. `KiloConnectionService.cs` - Client lifecycle, authentication
2. `CliBackendManager.cs` - BaseUrl discovery
3. All handler services using `KiloVisualStudioExtension.Generated`

**Estimated Scope:**
- ~15-20 handler/service files
- `KiloConnectionService` requires authentication reimplementation
- All API calls need namespace change: `Generated` → `ApiClient`

---

## 9. Risks and Blockers

### Critical Risks

1. **Polymorphic Serialization**
   - NSwag may not correctly deserialize oneOf/anyOf payloads
   - **Action Required**: Runtime testing with actual CLI responses
   - **Risk Level**: HIGH

2. **Authentication Implementation**
   - No built-in authentication provider like Kiota
   - **Action Required**: Implement Basic Auth via partial class
   - **Risk Level**: MEDIUM

3. **Event Type Handling**
   - NSwag uses discriminator classes vs TypeScript union types
   - **Action Required**: Verify SSE event parsing compatibility
   - **Risk Level**: MEDIUM

### Non-Blocking Issues

1. **Code Size**: NSwag generates ~3x more code than TypeScript SDK
2. **File Structure**: Single large file vs modular TypeScript
3. **Warnings**: Pre-existing nullable warnings unrelated to NSwag

---

## 10. Migration Readiness Assessment

### ✅ Ready to Proceed
- API endpoint coverage is complete
- Framework compatibility confirmed
- BaseUrl dynamic configuration supported
- CancellationToken support present
- Exception handling is robust
- Generation process is reproducible

### ⚠️ Requires Validation Before Migration
- Polymorphic model serialization/deserialization
- SSE event type handling
- Authentication implementation testing

### 📋 Required Pre-Migration Steps
1. Create test harness for polymorphic model validation
2. Implement and test Basic Auth via partial class
3. Verify SSE event parsing with actual CLI
4. Document authentication pattern for migration

---

## Next Task Specification

**Task**: Implement NSwag authentication and validate polymorphic serialization

**Scope:**
1. Create partial class extension for `KiloApiClient` with Basic Auth
2. Build test harness to validate polymorphic model deserialization
3. Test SSE event parsing with discriminator pattern
4. Document migration pattern for handler services

**Deliverable:**
- Working authentication implementation
- Validation report on polymorphic models
- Migration guide for handler services

**Files to Create:**
- `ApiClient/KiloApiClient.Authentication.cs` (partial class with auth)
- `Tests/ApiClientPolymorphismTests.cs`
- `porting/docs/NSWAG-MIGRATION-GUIDE.md`

---

## Decisions Required

1. **Authentication Pattern**: Use partial class override (recommended) vs HttpClient interceptor
2. **Event Handling**: Accept discriminator class pattern vs custom deserialization
3. **Migration Order**: Start with `KiloConnectionService` then handlers, or vice versa

**Recommended**: Partial class authentication, discriminator events, KiloConnectionService first.
