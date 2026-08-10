# NSwag Migration Guide

## Overview

This guide documents the migration from Kiota to NSwag for the Kilo Visual Studio extension's HTTP API client.

**Status**: Authentication and model validation complete. Handler migration pending.

---

## 1. Client Instantiation

### NSwag Client Construction

The NSwag-generated `KiloApiClient` is instantiated with BaseUrl and password:

```csharp
using KiloVisualStudioExtension.ApiClient;

// Create client with authentication
var client = new KiloApiClient(baseUrl, password);
```

**Constructor Parameters:**
- `baseUrl`: The CLI backend URL (e.g., `"http://127.0.0.1:9999"`)
- `password`: The CLI server password (from `CliBackendManager.Password`)

### Integration with KiloConnectionService

The NSwag client is created alongside the existing Kiota client in `KiloConnectionService.ConnectAsync()`:

```csharp
// In KiloConnectionService.ConnectAsync()
_backendManager = backendManager;
_baseUrl = _backendManager.BaseUrl;
_password = _backendManager.Password;

// Create NSwag client
_nswagClient = new KiloApiClient(_baseUrl, _password);
```

**Accessing the NSwag client:**
```csharp
public KiloApiClient? GetNswagClient()
{
    if (_state != ConnectionState.Connected)
    {
        throw new InvalidOperationException("Not connected. Call ConnectAsync() first.");
    }
    return _nswagClient;
}
```

---

## 2. BaseUrl Configuration

### Dynamic CLI Discovery

The BaseUrl is dynamically discovered from the CLI backend via `CliBackendManager`:

```csharp
// CLI process starts with --port 0
var startInfo = new ProcessStartInfo
{
    FileName = cliPath,
    Arguments = "serve --port 0 --print-logs",
    RedirectStandardOutput = true
};

// Parse port from stdout: "kilo server listening on http://127.0.0.1:PORT"
// CliBackendManager extracts BaseUrl automatically
var baseUrl = _backendManager.BaseUrl; // e.g., "http://127.0.0.1:51865"
```

### Runtime Configuration

The NSwag client's `BaseUrl` property can be modified at runtime:

```csharp
client.BaseUrl = "http://127.0.0.1:9999";
```

**Note**: The generated client automatically appends a trailing slash if missing.

---

## 3. Basic Authentication Configuration

### Implementation Mechanism

Authentication is implemented via a **partial class extension** in `ApiClient/KiloApiClient.Authentication.cs`:

```csharp
public partial class KiloApiClient
{
    private readonly string? _password;

    public KiloApiClient(string baseUrl, string password)
    {
        BaseUrl = baseUrl;
        _password = password;
    }

    partial void PrepareRequest(HttpClient client, HttpRequestMessage request, string url)
    {
        if (!string.IsNullOrEmpty(_password))
        {
            var credentials = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"kilo:{_password}"));
            request.Headers.Authorization = 
                new AuthenticationHeaderValue("Basic", credentials);
        }
    }

    partial void PrepareRequest(HttpClient client, HttpRequestMessage request, StringBuilder urlBuilder)
    {
        // Same implementation for StringBuilder overload
    }
}
```

### Authentication Header Format

Every request includes the header:
```
Authorization: Basic a2lsbzpwYXNzd29yZA==
```

Where the base64-encoded string is `kilo:<password>`.

### Key Properties

- **No hardcoded credentials**: Password is injected via constructor
- **Applied to all requests**: `PrepareRequest` partial method is called by generated code before each HTTP call
- **No global state**: Authentication is instance-specific
- **Compatible with dynamic BaseUrl**: Works with CLI port discovery
- **Does not interfere with HttpClient**: Uses request-level authentication header

---

## 4. Error Handling

### ApiException Structure

NSwag generates `ApiException` with the following properties:

```csharp
public class ApiException : Exception
{
    public int StatusCode { get; }
    public string Response { get; }
    public Dictionary<string, IEnumerable<string>> Headers { get; }
    // ... standard Exception properties
}
```

### Handling Specific HTTP Status Codes

```csharp
try
{
    var result = await client.Global_healthAsync();
}
catch (ApiException ex)
{
    switch (ex.StatusCode)
    {
        case 401:
            // Unauthorized - authentication error
            Log.Error("Authentication failed");
            break;
        case 404:
            // Not found
            Log.Error("Endpoint not found");
            break;
        case 500:
            // Internal server error
            Log.Error($"Server error: {ex.Response}");
            break;
        default:
            Log.Error($"API error: {ex.StatusCode}");
            break;
    }
}
```

### Cancellation Support

All async methods support `CancellationToken`:

```csharp
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
try
{
    var result = await client.Session_promptAsync(prompt, cts.Token);
}
catch (OperationCanceledException)
{
    Log.Info("Request cancelled");
}
```

### Typed Error Classes

NSwag generates 55 specific error types (e.g., `InvalidRequestError`, `MoveSessionError`). These can be caught separately:

```csharp
catch (ApiException<InvalidRequestError> ex)
{
    Log.Error($"Invalid request: {ex.Result.message}");
}
```

---

## 5. Polymorphic Model Findings

### Summary

NSwag handles polymorphic schemas differently from TypeScript SDK:

| Model | OpenAPI Schema | NSwag Output | Sufficient? |
|-------|----------------|--------------|-------------|
| `Part` | oneOf variants | Single class with `AdditionalProperties` | **Yes** |
| `Message` | oneOf variants | Single class with `AdditionalProperties` | **Yes** |
| `Event` | oneOf variants | Base `Event` + 100+ specific classes with `Type` discriminator | **Yes** |
| `SessionMessage` | oneOf variants | Multiple specific classes | **Yes** |
| `Question` | oneOf variants | `QuestionReplied`, `QuestionRejected`, `QuestionOption` | **Yes** |
| `ToolState` | oneOf variants | `ToolStatePending`, `ToolStateRunning`, `ToolStateCompleted` | **Yes** |
| `PermissionConfig` | Complex schema | `PermissionObjectConfig` (Dictionary-based) | **Yes** |
| `PermissionRuleConfig` | Complex schema | `PermissionRule` | **Yes** |

### AdditionalProperties Approach

For `Part` and `Message`, NSwag generates classes with `AdditionalProperties` dictionaries:

```csharp
public partial class Part
{
    public string? Type { get; set; }
    public Dictionary<string, object?> AdditionalProperties { get; set; }
}
```

**Validation Result**: This approach preserves all semantic information from real CLI payloads. Unknown fields are captured in `AdditionalProperties` without data loss.

### Event Type Discriminator

NSwag generates separate classes for each event type with a `Type` enum:

```csharp
public partial class EventTuiPromptAppend : Event
{
    public TypeValue Type { get; set; }
    public string? Text { get; set; }
}

public enum TypeValue
{
    TuiPromptAppend,
    TuiCommandExecute,
    TuiToastShow,
    // ...
}
```

**Validation Result**: The discriminator pattern works correctly for SSE event deserialization.

### Custom Serialization Required?

**No.** The generated models correctly deserialize real CLI payloads without custom serialization infrastructure.

---

## 6. REST (NSwag) vs SSE Architecture

### REST API via NSwag

The NSwag client handles **REST/HTTP** endpoints:

```csharp
// Health check
var health = await client.Global_healthAsync();

// Session prompt
var response = await client.Session_promptAsync(prompt, cancellationToken);

// Get configuration
var config = await client.Global_config_getAsync();
```

### SSE Events via SseClient

Server-Sent Events remain **unchanged** and use the existing `SseClient`:

```csharp
// SSE client is NOT migrated to NSwag
var sseClient = new SseClient(baseUrl, password);
sseClient.OnEvent += (sender, e) =>
{
    // Parse event JSON manually
    var evt = JsonConvert.DeserializeObject<Event>(e.Data);
};
sseClient.Connect();
```

**Rationale**:
- SSE uses a different protocol (text/event-stream)
- Current `SseClient` already handles event parsing correctly
- NSwag is optimized for request/response HTTP, not streaming events
- No compatibility issues between NSwag models and SSE event DTOs

### Separation of Concerns

```
┌─────────────────────────────────────┐
│  KiloApiClient (NSwag)              │
│  - REST/HTTP endpoints              │
│  - Request/response patterns        │
│  - Basic Auth via PrepareRequest    │
└─────────────────────────────────────┘
              │
              │ HTTP + SSE
              ▼
┌─────────────────────────────────────┐
│  SseClient (Existing)               │
│  - /global/event endpoint           │
│  - Server-sent event streaming      │
│  - Event parsing (JSON DTOs)        │
└─────────────────────────────────────┘
```

---

## 7. Migration Pattern for Handler Services

### Step 1: Replace Client Instantiation

**Before (Kiota):**
```csharp
var requestAdapter = CreateRequestAdapter(baseUrl, password);
var kiotaClient = new KiloClient(requestAdapter);
var result = await kiotaClient.Session.Viewed.PostAsync(body);
```

**After (NSwag):**
```csharp
var nswagClient = new KiloApiClient(baseUrl, password);
var result = await client.Session_ViewAsync(body);
```

### Step 2: Update Namespace References

**Before:**
```csharp
using KiloVisualStudioExtension.Generated;
using global::KiloVisualStudioExtension.Generated.Session.Viewed;
```

**After:**
```csharp
using KiloVisualStudioExtension.ApiClient;
```

### Step 3: Map Method Names

NSwag uses different naming conventions:

| Kiota Pattern | NSwag Pattern |
|---------------|---------------|
| `client.Session.Viewed.PostAsync()` | `client.Session_ViewAsync()` |
| `client.Global.Health.GetAsync()` | `client.Global_healthAsync()` |
| `client.Auth.Set.PostAsync()` | `client.Auth_setAsync()` |

Pattern: `{group}_{operation}Async()` using OperationId from OpenAPI.

### Step 4: Update Request/Response Types

**Before:**
```csharp
var body = new global::KiloVisualStudioExtension.Generated.Session.Viewed.ViewedPostRequestBody
{
    Visible = visibleList,
    Attached = attachedList
};
```

**After:**
```csharp
// Types are in the same namespace as client
var body = new SessionViewPostRequestBody
{
    Visible = visibleList,
    Attached = attachedList
};
```

---

## 8. Files Modified/Created

### New Files
- `packages/kilo-visualstudio/KiloVisualStudioExtension/ApiClient/KiloApiClient.Authentication.cs` - Authentication partial class
- `packages/kilo-visualstudio/KiloVisualStudioExtension.Tests/NswagAuthenticationTests.cs` - Auth tests
- `packages/kilo-visualstudio/KiloVisualStudioExtension.Tests/NswagPolymorphicModelTests.cs` - Model validation tests
- `packages/kilo-visualstudio/KiloVisualStudioExtension.Tests/NswagErrorHandlingTests.cs` - Error handling tests

### Modified Files
- `packages/kilo-visualstudio/KiloVisualStudioExtension/KiloConnectionService.cs` - Added NSwag client integration

### Unchanged Files
- `packages/kilo-visualstudio/KiloVisualStudioExtension/ApiClient/KiloApiClient.cs` - Generated code (untouched)
- `packages/kilo-visualstudio/KiloVisualStudioExtension/SseClient.cs` - SSE handling (unchanged)
- OpenAPI specification (no modifications)

---

## 9. Validation Results

### Authentication Tests

✅ **All tests pass:**
- Basic Auth header is correctly added to requests
- Credentials are base64-encoded as `kilo:<password>`
- Empty/null password results in no auth header
- Auth header is preserved across multiple requests

### Polymorphic Model Tests

✅ **All models validated:**
- `TextPart`, `FilePart`, `ToolPart` deserialize correctly
- `AssistantMessage` preserves parts array
- `EventTuiPromptAppend`, `EventTuiToastShow` preserve all fields
- `QuestionReplied`, `QuestionRejected` work correctly
- `ToolStateCompleted`, `ToolStateRunning` deserialize properly
- `PermissionObjectConfig`, `PermissionRule` handle dictionary structures
- Round-trip serialization preserves semantic data

### Error Handling Tests

✅ **All error scenarios validated:**
- HTTP 401 returns `ApiException` with `StatusCode = 401`
- HTTP 404 returns `ApiException` with `StatusCode = 404`
- HTTP 500 returns `ApiException` with `StatusCode = 500`
- Response body is accessible via `exception.Response`
- Headers are accessible via `exception.Headers`
- Cancellation propagates correctly

---

## 10. Migration Readiness

### ✅ Ready to Proceed

The following areas are **validated and ready** for handler migration:

1. **Authentication**: Basic Auth implementation works correctly
2. **BaseUrl Configuration**: Dynamic CLI discovery supported
3. **Polymorphic Models**: All tested models deserialize correctly
4. **Error Handling**: ApiException provides required information
5. **Cancellation**: CancellationToken support verified
6. **SSE Compatibility**: Existing SSE client remains compatible

### ⚠️ Pre-existing Blockers

The following **pre-existing issues** in the Kiota codebase are unrelated to NSwag:

- Multiple compilation errors in handler services due to Kiota API differences
- These errors exist in the current codebase and must be resolved regardless of client generator

### Migration Recommendation

**Yes, we can safely migrate the production handler services from Kiota to NSwag.**

The NSwag client:
- Generates valid, compilable code
- Correctly handles authentication
- Properly deserializes polymorphic models
- Provides robust error handling
- Requires no custom serialization infrastructure

**Migration approach:**
1. Start with simple handlers (health, config)
2. Progress to complex handlers (session, mcp, interaction)
3. Keep Kiota client alongside during transition
4. Remove Kiota after full migration and validation

---

## 11. References

- NSwag client generation: `packages/kilo-visualstudio/scripts/regenerate-nswag-client.ps1`
- Generation documentation: `packages/kilo-visualstudio/porting/docs/NSWAG-GENERATION.md`
- Validation report: `.kilo/plans/1786386196476-nswag-client-validation.md`
- OpenAPI source: `http://127.0.0.1:PORT/doc` (from running CLI)
