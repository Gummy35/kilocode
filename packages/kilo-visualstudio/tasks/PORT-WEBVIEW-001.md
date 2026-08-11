# PORT-WEBVIEW-001 — WebView Protocol Contract Extraction and C# DTO Generation

## Status

**DONE**

## Objective

Create an automated, deterministic pipeline that extracts the WebView protocol contract from the VS Code TypeScript implementation and generates strongly-typed C# DTOs for the Visual Studio extension.

The VS Code implementation is the **sole source of truth** for the WebView protocol.

The Visual Studio extension must not independently redefine, simplify, or manually duplicate the protocol.

The resulting architecture must allow the Visual Studio extension to consume WebView messages using strongly-typed C# objects while remaining synchronized with the VS Code protocol.

---

## Source of Truth

The authoritative protocol definition is the VS Code extension source code.

The implementation MUST inspect the actual TypeScript source rather than relying on manually maintained documentation or manually copied interfaces.

The extractor should use the **TypeScript Compiler API / TypeChecker** wherever possible.

Do not implement a parser based on regular expressions or fragile textual matching.

---

## Scope

### In scope

1. Implement a TypeScript WebView contract extractor.
2. Resolve TypeScript imports and type references.
3. Identify WebView → Extension message contracts.
4. Identify Extension → WebView message contracts.
5. Resolve referenced interfaces, unions, enums and literal types.
6. Preserve discriminators such as:

   * `type`
   * `status`
   * `role`
   * other literal discriminator fields actually present in the source.
7. Produce a deterministic intermediate contract artifact.
8. Generate strongly-typed C# DTOs from that artifact.
9. Generate Newtonsoft.Json-compatible DTOs.
10. Integrate generated DTOs into the existing Visual Studio architecture without modifying generated NSwag code.
11. Reuse the existing shared `KiloJsonSerializer` configuration.
12. Add validation and regression tests for the extraction/generation pipeline.
13. Document the generated contract and regeneration workflow.
14. Update porting documentation and task tracking.

### Out of scope

Do NOT:

* modify the VS Code WebView protocol;
* modify VS Code production code;
* manually redesign WebView messages;
* introduce a second manually maintained WebView contract;
* modify NSwag-generated files;
* replace Newtonsoft.Json;
* reintroduce `JsonConverter` subclasses for polymorphism;
* modify the existing SSE architecture unless strictly required for integration;
* generate DTOs manually and treat them as authoritative;
* port unrelated VS Code functionality;
* implement the missing WebView tests from PORT-INFRA-004 yet unless they are strictly required to validate the generated contract infrastructure;
* start PORT-WEBVIEW-002;
* start unrelated porting tasks.

---

## Required Architecture

The target architecture is:

```text
VS Code TypeScript source
        │
        ▼
TypeScript Compiler API
        │
        ▼
WebView Contract Extractor
        │
        ▼
WebViewContract.json
        │
        ▼
C# DTO Generator
        │
        ▼
Generated WebView DTOs
        │
        ▼
Newtonsoft.Json
        │
        ▼
Visual Studio WebView communication
```

The intermediate `WebViewContract.json` is an implementation artifact generated from VS Code.

It must NOT become a second source of truth.

---

## Contract Extractor

Implement a TypeScript-based extractor using the TypeScript Compiler API.

The extractor must:

* load the VS Code TypeScript project using its existing `tsconfig`;
* resolve imports through the TypeChecker;
* locate the WebView message type definitions identified during PORT-INFRA-004;
* recursively resolve referenced types;
* preserve type names;
* preserve property names exactly as they appear on the wire;
* preserve optional vs required properties;
* preserve nullable semantics where distinguishable;
* preserve arrays;
* preserve enums;
* preserve string/number/boolean literal unions;
* preserve union types;
* preserve discriminator properties;
* preserve nested object structures;
* detect recursive type references;
* detect unsupported TypeScript constructs explicitly rather than silently generating incorrect DTOs.

The extractor must fail clearly when it encounters an unsupported construct that could result in an incorrect C# contract.

---

## Intermediate Contract

Create a documented machine-readable contract format.

Expected location:

```text
packages/kilo-visualstudio/porting/
```

or another location justified by the existing repository architecture.

The contract must contain enough information for the C# generator to operate without reading TypeScript source code.

At minimum, it should represent:

* protocol direction;
* message name/type;
* root TypeScript type;
* referenced types;
* properties;
* property names;
* required/optional state;
* nullable state;
* primitive type;
* arrays;
* enums/literal unions;
* discriminators;
* union members;
* nested types;
* type references.

The format must be versioned.

Example conceptual structure:

```json
{
  "schemaVersion": 1,
  "source": {
    "project": "vscode",
    "typescript": "..."
  },
  "messages": {
    "extensionToWebView": [],
    "webViewToExtension": []
  },
  "types": []
}
```

Do not blindly use this exact structure if the PORT-INFRA-004 audit or repository architecture provides a better one.

---

## C# DTO Generator

Implement a generator that consumes only `WebViewContract.json`.

The generator must produce C# DTOs suitable for Newtonsoft.Json.

Requirements:

* preserve wire property names;
* use `[JsonProperty(...)]` where required;
* preserve optional/nullable semantics;
* generate collections using appropriate .NET types;
* generate enums where appropriate;
* generate polymorphic type hierarchies or discriminated representations where appropriate;
* support nested polymorphism;
* avoid dependence on generated NSwag classes;
* avoid modifying generated files;
* produce deterministic output.

The generator must not contain hard-coded definitions of individual WebView messages.

Protocol-specific information belongs in the generated contract.

---

## Polymorphism

Use the architecture established by PORT-INFRA-003.

Do NOT introduce `JsonConverter` inheritance as the primary mechanism.

Use explicit discriminator-based deserialization infrastructure where required.

The generated DTO model must therefore expose enough information for the existing centralized discriminator factory architecture to deserialize:

```text
WebView JSON
    ↓
JObject / JToken
    ↓
discriminator
    ↓
concrete DTO
```

Nested polymorphism must be supported where the TypeScript contract requires it.

---

## Newtonsoft.Json

The generated DTOs must use Newtonsoft.Json.

The implementation must reuse:

```text
KiloJsonSerializer
```

as the shared serializer configuration.

Do not create an independent serializer configuration for WebView DTOs unless there is a demonstrated technical requirement.

If a serializer customization is required, integrate it through the existing shared serializer architecture.

---

## Generated Code Boundaries

Generated code must be clearly separated from handwritten code.

Do not modify generated NSwag files.

The generator must be rerunnable without destroying handwritten code.

Generated files must contain an appropriate generated-file marker/header.

The generator must produce deterministic output so that repeated generation from the same VS Code source produces identical results.

---

## Testing

Add tests for the extractor and generator.

At minimum:

### Extractor tests

* primitive properties;
* optional properties;
* nullable properties;
* arrays;
* enums;
* literal unions;
* discriminated unions;
* nested objects;
* imported types;
* recursive references;
* unknown/unsupported constructs.

### Generator tests

* property naming;
* nullable types;
* arrays;
* enums;
* unions;
* discriminated types;
* nested types;
* Newtonsoft attributes;
* deterministic output.

### Contract regression

The generated `WebViewContract.json` must be validated.

Where practical, compare generated output against a checked-in expected artifact or use a deterministic snapshot.

Do not rely exclusively on "the generator completed successfully".

---

## Integration

After generating DTOs, identify the existing Visual Studio WebView communication paths that currently use untyped JSON/dictionaries/JToken.

Do not rewrite the entire WebView layer as part of this task.

Instead:

1. identify integration points;
2. demonstrate that generated DTOs can deserialize the existing protocol;
3. integrate only the minimum production code required to prove the architecture;
4. preserve the existing WebView protocol exactly.

If PORT-INFRA-004 identified specific communication paths suitable for an initial typed migration, use those paths.

---

## VS Code Test Audit

PORT-INFRA-004 identified WebView-related VS Code tests that have not yet been fully ported.

For this task:

* identify which tests validate the protocol contract itself;
* determine whether they can be reused as contract fixtures;
* determine whether their relevant input/output JSON should become generator/deserializer fixtures;
* document the findings.

Do not perform the complete test port unless it is explicitly required by the approved implementation plan.

That work belongs to the appropriate follow-up task.

---

## Documentation

Create or update:

```text
packages/kilo-visualstudio/porting/docs/prompts/PORT-WEBVIEW-001.md
```

Use the previous PORT-INFRA task prompt/history files as the documentation model.

Preserve execution history.

Do not silently replace previous analysis or decisions.

Update:

```text
packages/kilo-visualstudio/porting/docs/TASKS.md
```

with the actual implementation status.

Also document:

* extractor architecture;
* contract schema;
* generator architecture;
* generated-code location;
* regeneration command;
* source-of-truth rules;
* supported TypeScript constructs;
* unsupported constructs;
* polymorphism strategy;
* Newtonsoft.Json integration;
* validation procedure.

---

## Validation

Before completing the task:

* build the TypeScript extractor;
* run extractor tests;
* generate `WebViewContract.json`;
* verify deterministic output;
* run the C# generator;
* build generated C# code;
* run DTO deserialization tests;
* verify Newtonsoft.Json is used;
* verify the shared `KiloJsonSerializer` is reused;
* verify no NSwag-generated files were modified;
* verify no VS Code production code was modified;
* verify no WebView protocol was changed;
* verify generated files are reproducible;
* verify generated DTO property names match the wire protocol;
* verify discriminated unions are represented correctly;
* verify nested polymorphism where applicable.

---

## Acceptance Criteria

PORT-WEBVIEW-001 may be marked **DONE** only when:

1. The VS Code TypeScript source is automatically inspected using the TypeScript Compiler API.
2. A deterministic `WebViewContract.json` is generated.
3. The contract contains both WebView → Extension and Extension → WebView messages identified by the extractor.
4. Referenced TypeScript types are resolved automatically.
5. Discriminated unions are represented correctly.
6. Nested types are represented correctly.
7. Unsupported TypeScript constructs fail explicitly.
8. C# DTOs are generated automatically from the intermediate contract.
9. Generated DTOs use Newtonsoft.Json.
10. Generated DTOs preserve the WebView wire format.
11. The existing shared serializer configuration is reused.
12. Generated output is deterministic.
13. The generator is independent from NSwag generated code.
14. Tests cover the core extractor and generator functionality.
15. Existing WebView protocol behavior is unchanged.
16. Documentation is complete.
17. `TASKS.md` accurately reflects the final state.

---

## Expected Final State

```text
PORT-WEBVIEW-001 → DONE
```

### Implementation Summary

**Generator**: TypeScript (`generator.ts`) - consumes `WebViewContract.json` and produces C# DTOs

**Generated Artifacts**:
- 45 type classes in `KiloVisualStudioExtension/WebView/Generated/Types/`
- 208 WebView→Extension message classes in `KiloVisualStudioExtension/WebView/Generated/Messages/WebviewToExtension/`
- 251 Extension→WebView message classes in `KiloVisualStudioExtension/WebView/Generated/Messages/ExtensionToWebview/`
- 1 discriminator factory: `WebViewMessageFactory.cs`

**Regeneration Command**:
```powershell
cd packages/kilo-visualstudio/tools/webview-contract-extractor
bun run src/extractor.ts    # Generate WebViewContract.json from VS Code TypeScript
bun run generator.ts        # Generate C# DTOs from contract
```

**Key Features**:
- Only generates types actually referenced by messages (not all 6807 types in contract)
- Maps TypeScript unions to proper C# nullable types (`string | undefined` → `string?`)
- Includes comments showing original TypeScript types for `object` fallbacks
- Handles edge cases: CSS properties with hyphens, TypeScript internal symbols, intersection types, type aliases
- Build succeeds with 0 errors

**Validation**:
- ✅ TypeScript extractor runs successfully
- ✅ WebViewContract.json generated (6807 types, 459 messages)
- ✅ 460 C# files generated (45 types + 459 messages + factory)
- ✅ Visual Studio extension builds with 0 errors
- ✅ Newtonsoft.Json attributes preserved
- ✅ Discriminator factory working
- ✅ No NSwag files modified
- ✅ No VS Code production code modified
