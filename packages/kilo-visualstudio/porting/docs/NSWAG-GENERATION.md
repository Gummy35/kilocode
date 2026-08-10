# NSwag C# Client Generation

## Overview

This document describes the generation of the C# HTTP client for the Kilo Visual Studio Extension using NSwag.

## Source Information

- **OpenAPI Source**: `http://127.0.0.1:56631/doc`
- **OpenAPI Version**: 3.1.0
- **API Version**: 1.0.0
- **Git Commit SHA**: `a48b1f8c9efebab4d05134761e7e7f48f5906d93`
- **Generation Date**: 2026-08-10

## Tooling

- **NSwag Version**: 14.7.1.0
- **NJsonSchema Version**: 11.6.1.0
- **Newtonsoft.Json Version**: 13.0.0.0

## Installation

```powershell
dotnet tool install --global NSwag.ConsoleCore
```

## Generation Command

```powershell
nswag openapi2csclient `
  /input:"C:\Users\RFHP2615\AppData\Local\Temp\kilo\openapi.json" `
  /output:"packages\kilo-visualstudio\KiloVisualStudioExtension\ApiClient\KiloApiClient.cs" `
  /namespace:"KiloVisualStudioExtension.ApiClient" `
  /ClassName:"KiloApiClient" `
  /GenerateClientInterfaces:true `
  /GenerateExceptionClasses:true `
  /ExceptionClass:"ApiException" `
  /UseBaseUrl:true `
  /GenerateBaseUrlProperty:true `
  /InjectHttpClient:false `
  /GenerateNativeRecords:false `
  /GenerateDataAnnotations:false `
  /GenerateJsonMethods:true `
  /EnforceFlagEnums:false `
  /GenerateDefaultValues:true `
  /GenerateImmutableArrayProperties:false `
  /GenerateImmutableDictionaryProperties:false `
  /GenerateResponseClasses:true `
  /ResponseClass:"ApiResponse" `
  /operationGenerationMode:"SingleClientFromOperationId"
```

**Important**: The `/operationGenerationMode:"SingleClientFromOperationId"` option is required to prevent NSwag v14.7.1 from duplicating the client code.

## Generated Artifacts

### File Structure

```
packages/kilo-visualstudio/KiloVisualStudioExtension/ApiClient/
└── KiloApiClient.cs (~36,600 lines after extraction)
```

### Statistics

- **Total Lines**: ~86,500
- **Total Types**: ~1,500
- **API Endpoints**: 236
- **Client Interface**: `IKiloApiClient`
- **Client Implementation**: `KiloApiClient`
- **Exception Class**: `ApiException`

### Key Generated Types

#### Client Interface & Implementation
- `IKiloApiClient` - Client interface with all API methods
- `KiloApiClient` - Concrete implementation

#### Exception Handling
- `ApiException` - Base exception class with status code, response, and headers
- `ApiException<TResult>` - Generic exception with typed result
- Various error classes (e.g., `InvalidRequestError`, `MoveSessionError`)

#### Event Types
All server-sent event types are generated as separate classes:
- `EventSessionCreated`, `EventSessionUpdated`, `EventSessionDeleted`
- `EventMessageUpdated`, `EventMessagePartUpdated`
- `EventPermissionAsked`, `EventPermissionReplied`
- `EventQuestionAsked`, `EventQuestionReplied`
- And 100+ more event types

#### Data Models
- Session, Message, Part types
- Agent, Tool, Permission models
- Configuration types
- Agent Manager types
- Notebook types
- VCS types

## Comparison with TypeScript SDK

### TypeScript SDK (`packages/sdk/js/src/v2/`)

**Generator**: `@hey-api/openapi-ts`

**Structure**:
```
gen/
├── client.gen.ts       # Client factory
├── sdk.gen.ts          # SDK methods (~10,600 lines)
├── types.gen.ts        # Type definitions (~16,000 lines)
└── client/
    ├── index.ts
    ├── types.gen.ts
    └── client.gen.ts
```

**Total TypeScript Lines**: ~26,600 (excluding core utilities)

### C# Client (NSwag)

**Generator**: NSwag v14.7.1

**Structure**:
```
ApiClient/
└── KiloApiClient.cs    # Single file (~36,600 lines after extraction)
```

### Key Differences

| Aspect | TypeScript SDK | C# Client (NSwag) |
|--------|---------------|-------------------|
| **Generator** | @hey-api/openapi-ts | NSwag |
| **Output Files** | Multiple (modular) | Single file |
| **Total Lines** | ~26,600 | ~86,500 |
| **Client Pattern** | Functional (`createClient`) | Class-based (`KiloApiClient`) |
| **Interface** | Implicit (TypeScript) | Explicit (`IKiloApiClient`) |
| **Serialization** | Native JSON | Newtonsoft.Json |
| **HTTP Client** | Fetch API | HttpClient |
| **Event Handling** | Typed unions | Typed classes with discriminator |
| **Null Safety** | TypeScript nullables | C# nullable reference types |

### Similarities

1. **API Coverage**: Both cover all 236 endpoints
2. **Type Safety**: Full type coverage for requests/responses
3. **Error Handling**: Structured error types
4. **Event Support**: All event types generated
5. **Base URL Configuration**: Configurable base URL

### NSwag Advantages

1. **Interface Generation**: Explicit `IKiloApiClient` for dependency injection
2. **Exception Classes**: Typed exceptions with result support
3. **JSON Methods**: Built-in `ToJson()`/`FromJson()` on all models
4. **Partial Classes**: Enable partial method hooks for customization
5. **CancellationToken**: All async methods support cancellation

### TypeScript SDK Advantages

1. **Modularity**: Separate files for client, types, and SDK
2. **Size**: ~27% smaller output
3. **Modern JS**: Uses native fetch, async/await
4. **Tree-shaking**: Better dead code elimination

## Usage Example

```csharp
using KiloVisualStudioExtension.ApiClient;

// Initialize client
var client = new KiloApiClient("http://127.0.0.1:56631");

// Call API
try 
{
    var sessions = await client.Session_listAsync("project-dir", "workspace");
    // Process sessions
}
catch (ApiException ex)
{
    Console.WriteLine($"Error: {ex.StatusCode} - {ex.Response}");
}
```

## Regeneration

To regenerate the client when the API changes:

1. **Fetch latest OpenAPI spec**:
   ```powershell
   Invoke-WebRequest -Uri "http://127.0.0.1:56631/doc" -OutFile "openapi.json"
   ```

2. **Run NSwag with operationGenerationMode**:
   ```powershell
   nswag openapi2csclient /input:"openapi.json" /output:"packages\kilo-visualstudio\KiloVisualStudioExtension\ApiClient\KiloApiClient.cs" [all options] /operationGenerationMode:"SingleClientFromOperationId"
   ```

4. **Record generation metadata**:
   - Update this document with new Git SHA
   - Update generation date

## Notes

- The generated client uses `Newtonsoft.Json` for serialization
- All models implement `ToJson()` and `FromJson()` static methods
- Event types use a discriminator pattern with `Type` property
- The client supports both parameterless and `CancellationToken` overloads
- Base URL automatically ensures trailing slash
- Use `/operationGenerationMode:"SingleClientFromOperationId"` to prevent duplication bug in NSwag v14.7.1

## References

- [NSwag Documentation](https://github.com/RicoSuter/NSwag)
- [NJsonSchema](https://github.com/RicoSuter/NJsonSchema)
- OpenAPI Specification: https://spec.openapis.org/oas/v3.1.0
