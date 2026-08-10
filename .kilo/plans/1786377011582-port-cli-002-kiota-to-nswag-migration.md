# PORT-CLI-002: Kiota to NSwag Migration Plan

## Task Identifier

PORT-CLI-002 — Kiota to NSwag Migration

## Executive Summary

The current Kiota-based C# client generation is producing an API structure that differs significantly from the TypeScript SDK used by the VS Code extension. This has resulted in **52+ compilation errors** due to:

1. Different property naming conventions (e.g., `MessageID` vs `messageID`)
2. Different polymorphic model handling
3. Missing or incorrectly generated request builders
4. Different response model structures

**Solution**: Replace Kiota with **NSwag** to generate the C# client from the same OpenAPI specification used by the TypeScript SDK (`@hey-api/openapi-ts`).

## Current State

- **Kiota version**: 1.0.0 (generated code markers)
- **TypeScript SDK generator**: `@hey-api/openapi-ts` v0.90.10
- **Compilation errors**: 52+ across handler services
- **Generated code location**: `packages/kilo-visualstudio/KiloVisualStudioExtension/Generated/`

## Migration Strategy

### Phase 1: OpenAPI Spec Discovery

1. **Generate the OpenAPI specification** from the CLI backend
   ```bash
   cd packages/opencode
   bun dev generate > ../sdk/js/openapi.json
   ```

2. **Verify the OpenAPI spec** matches what the TypeScript SDK uses
   - Check `packages/sdk/js/openapi.json` exists
   - Compare key endpoints with TypeScript SDK types

### Phase 2: NSwag Setup

1. **Install NSwag CLI**
   ```bash
   dotnet tool install -g NSwag.ConsoleCore
   ```

2. **Create NSwag configuration file** (`nswag.json`)
   - Configure output namespace: `KiloVisualStudioExtension.Generated`
   - Configure class name: `KiloClient`
   - Enable nullable reference types
   - Configure date/time handling
   - Match TypeScript SDK naming conventions where possible

3. **Generate C# client**
   ```bash
   nswag run nswag.json
   ```

### Phase 3: Code Migration

1. **Replace generated code**
   - Delete `packages/kilo-visualstudio/KiloVisualStudioExtension/Generated/`
   - Generate new code to same location
   - Update project references

2. **Update handler services**
   - Fix property name mismatches (e.g., `MessageID` → `MessageId`)
   - Fix request builder paths
   - Fix response model access

3. **Remove obsolete endpoints**
   - `/auth/logout` - Not in OpenAPI
   - `/workstyle` - Not in OpenAPI
   - `/model/embedding` - Not in OpenAPI
   - `/model/image` - Use `/kilo/models/images` instead
   - `/mcp/{name}/remove` - Not in OpenAPI

### Phase 4: Validation

1. **Build verification**
   - Zero compilation errors
   - Zero warnings related to generated code

2. **Functional verification**
   - Each endpoint call works correctly
   - Compare behavior with VS Code implementation

3. **Legacy infrastructure cleanup**
   - Remove `HttpClientWrapper` references
   - Remove `CachedHttpClient` references
   - Confirm `SseClient` remains independent

## NSwag Configuration Template

```json
{
  "runtime": "Net80",
  "defaultVariables": null,
  "documentGenerator": {
    "fromDocument": {
      "url": "./openapi.json",
      "output": null,
      "newLineBehavior": "Auto"
    }
  },
  "codeGenerators": {
    "openApiToCSharpClient": {
      "clientBaseClass": null,
      "configurationClass": null,
      "generateClientClasses": true,
      "generateClientInterfaces": false,
      "generateExceptionClasses": true,
      "exceptionClass": "ApiException",
      "wrapDtoExceptions": true,
      "useHttpClientCreationMethod": false,
      "httpClientType": "System.Net.Http.HttpClient",
      "useHttpRequestMessageCreationMethod": false,
      "useBaseUrl": true,
      "generateBaseUrlProperty": true,
      "generateSyncMethods": false,
      "exposeJsonSerializerSettings": false,
      "clientClassAccessModifier": "public",
      "typeAccessModifier": "public",
      "generateContractsOutput": false,
      "generateNativeRecords": false,
      "generateDataAnnotations": true,
      "generateImmutableArrayProperties": false,
      "generateImmutableDictionaryProperties": false,
      "jsonLibrary": "SystemTextJson",
      "output": "./KiloVisualStudioExtension/Generated/KiloClient.cs",
      "namespace": "KiloVisualStudioExtension.Generated",
      "className": "{controller}Client",
      "generateDefaultValues": true,
      "generateDataTypes": true,
      "generateDtoTypes": true,
      "generateOperationMethods": true,
      "generateExceptionTypes": true,
      "generateResponseClasses": true,
      "useCancellationToken": true,
      "injectableHttpClient": false,
      "generateNullableReferenceTypes": true,
      "dateType": "System.DateTimeOffset",
      "dateTimeType": "System.DateTimeOffset",
      "timeType": "System.TimeSpan",
      "timeSpanType": "System.TimeSpan",
      "arrayType": "ObservableCollection",
      "arrayInstanceType": "List",
      "dictionaryType": "Dictionary",
      "arrayBaseType": "Collection",
      "dictionaryBaseType": "Dictionary",
      "classStyle": "Poco",
      "jsonLibrary": "SystemTextJson",
      "generateDefaultValues": true,
      "generateDataAnnotations": true,
      "requiredPropertiesMustBeDefined": true,
      "generateJsonMethods": false,
      "enforceFlagEnums": false,
      "parameterArrayType": "System.Collections.Generic.IEnumerable",
      "parameterDictionaryType": "System.Collections.Generic.IDictionary",
      "responseArrayType": "System.Collections.Generic.List",
      "responseDictionaryType": "System.Collections.Generic.Dictionary",
      "inlineNamedArrays": false,
      "inlineNamedDictionaries": false,
      "inlineNamedTuples": true,
      "inlineNamedAny": false,
      "generateOptionalPropertiesAsNullable": false,
      "generateNullableReferenceTypesForTemplateArguments": false,
      "targetFramework": "Net481"
    }
  }
}
```

## Files to Modify

### Generated Code (Regenerated)
- `packages/kilo-visualstudio/KiloVisualStudioExtension/Generated/` (entire directory)

### Handler Services (Updated to use new API)
- `AuthHandlerService.cs`
- `ConfigHandlerService.cs`
- `InteractionHandlerService.cs`
- `ModelHandlerService.cs`
- `McpHandlerService.cs`
- `NotificationHandlerService.cs`
- `SessionHandlerService.cs`
- `SessionControlHandlerService.cs`
- `SettingsHandlerService.cs`
- `ProviderRequestService.cs`
- `MiscRequestHandlerService.cs`
- `AgentRequestService.cs`

### Project File
- `KiloVisualStudioExtension.csproj` (update generated code references if needed)

## Expected Outcomes

### Success Criteria
1. ✅ Build succeeds with zero compilation errors
2. ✅ Zero warnings related to generated code
3. ✅ All endpoint calls use correct property names
4. ✅ Polymorphic models handled correctly
5. ✅ Legacy HTTP client infrastructure removed
6. ✅ SSE client remains independent

### Deliverables
1. NSwag configuration file (`nswag.json`)
2. Generated C# client code
3. Updated handler services
4. Migration report documenting:
   - All property name changes
   - All endpoint path corrections
   - All removed obsolete functionality
   - Any deviations from the original plan

## Risks and Mitigations

### Risk 1: NSwag generates different API structure
**Mitigation**: Compare NSwag output with TypeScript SDK types and adjust configuration

### Risk 2: Polymorphic models not handled correctly
**Mitigation**: Use NSwag's discriminator support or implement manual mapping

### Risk 3: Breaking changes in OpenAPI spec
**Mitigation**: Pin OpenAPI spec version and regenerate when spec changes

### Risk 4: Time/culture-specific types
**Mitigation**: Configure NSwag to use `DateTimeOffset` and handle culture-specific formatting

## Timeline Estimate

| Phase | Estimated Time |
|-------|---------------|
| OpenAPI spec discovery | 30 minutes |
| NSwag setup and configuration | 1 hour |
| Code generation and initial migration | 2 hours |
| Handler service updates | 4-6 hours |
| Build and validation | 2 hours |
| **Total** | **8-11 hours** |

## Rollback Plan

If NSwag migration fails:
1. Revert generated code directory to Kiota version
2. Continue with Kiota-based fixes (slower but more predictable)
3. Document NSwag configuration issues for future attempts

## Notes

- The TypeScript SDK uses `@hey-api/openapi-ts` which produces flat, simple types
- NSwag produces more verbose, traditional C# client code
- Property naming in C# will use PascalCase (standard C# convention)
- The TypeScript SDK uses camelCase - this is a semantic difference, not a functional one
- Focus on functional parity, not exact code structure matching

---

**Plan Status**: Ready for Execution

**Source Revision**: `fc77d23f3be3c17acbfa9decdf3cfa49f7d8fd03`

**Next Step**: Execute Phase 1 - Generate OpenAPI specification and verify it matches the TypeScript SDK source.
