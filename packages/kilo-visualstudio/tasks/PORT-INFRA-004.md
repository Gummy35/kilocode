# PORT-INFRA-004 — WebView Protocol Analysis, Contract Extraction and DTO Generation Design

## Objective

Analyze the WebView communication contract used by the VS Code extension and determine how it can be represented by strongly-typed C# DTOs in the Visual Studio extension while remaining fully compatible with the existing VS Code WebView protocol.

The VS Code implementation is the source of truth.

The objective is **not** to redesign the WebView protocol.

The objective is to:

1. identify the complete existing WebView protocol;
2. identify the corresponding TypeScript types and discriminated unions;
3. identify all extension → WebView and WebView → extension messages;
4. identify all existing VS Code tests covering this protocol;
5. compare them with the Visual Studio implementation and tests;
6. identify missing test coverage in the Visual Studio port;
7. determine whether the TypeScript types can be mechanically extracted;
8. design a TypeScript contract-extraction tool if appropriate;
9. determine whether C# DTOs can be generated reliably from the extracted contract;
10. define a safe architecture for generated WebView DTOs;
11. determine how Newtonsoft.Json can be used consistently for the C# side of the protocol.

No production implementation of the DTO generator is required by this task unless explicitly required by the approved implementation plan.

---

## Source of Truth

The following hierarchy must be respected:

1. VS Code WebView TypeScript types and implementation;
2. VS Code WebView-related tests;
3. existing VS Code extension behavior;
4. existing Visual Studio port;
5. existing Visual Studio tests;
6. documentation.

Do not infer the protocol solely from `SSEHelper.cs` or existing Visual Studio code.

If the Visual Studio implementation differs from VS Code, document the difference and determine whether it is intentional or a porting discrepancy.

---

## Scope

### VS Code analysis

Analyze the relevant VS Code extension and WebView implementation for:

* extension → WebView messages;
* WebView → extension messages;
* message discriminators;
* discriminated unions;
* payload structures;
* nested types;
* optional properties;
* nullable properties;
* enums;
* literal types;
* unions;
* intersections;
* imported types;
* aliases;
* generic types;
* dynamically constructed messages;
* serialization/deserialization;
* `postMessage`;
* WebView message listeners;
* message routing;
* protocol-specific helper functions.

Identify the actual files and symbols involved.

Do not rely only on grep results. Trace the types and their consumers where necessary.

---

## WebView Protocol Inventory

Produce a complete inventory containing at least:

| Direction | Message Type | TypeScript Type | Payload | Discriminator | VS Code Source | VS Code Tests | VS2026 Equivalent | VS2026 Tests |
| --------- | ------------ | --------------- | ------- | ------------- | -------------- | ------------- | ----------------- | ------------ |

Include all known protocol messages, not only SSE-related messages.

Explicitly distinguish:

* backend/SSE events;
* extension-internal events;
* extension → WebView messages;
* WebView → extension messages.

Do not assume these are interchangeable.

---

## TypeScript Contract Analysis

Determine whether the existing TypeScript definitions form a sufficiently structured contract to support automated extraction.

Investigate whether the protocol can be represented using:

* discriminated unions;
* interfaces;
* type aliases;
* literal string discriminators;
* enums;
* nested object types.

Identify structures that cannot be safely extracted automatically.

Pay particular attention to:

* `any`;
* `unknown`;
* `Record<string, ...>`;
* inline object types;
* anonymous union members;
* conditional types;
* mapped types;
* dynamically generated types;
* runtime-only structures;
* types whose JSON representation differs from their TypeScript representation.

For every problematic construct, determine whether:

1. it can still be represented safely;
2. it requires a generator rule;
3. it requires a manual mapping;
4. it requires a source annotation/convention;
5. it should remain untyped.

---

## Contract Extraction Tool Investigation

Determine whether a dedicated TypeScript tool should be introduced to extract the WebView contract.

Investigate the TypeScript Compiler API / AST as the primary approach.

Do not assume runtime JavaScript reflection is sufficient.

Evaluate:

* TypeScript Compiler API;
* AST traversal;
* `TypeChecker`;
* resolution of imported types;
* discriminated-union detection;
* recursive type resolution;
* literal types;
* optional properties;
* nullable properties;
* arrays;
* enums;
* nested objects;
* circular references;
* aliases.

Determine the minimum viable extraction model.

---

## Intermediate Contract

Evaluate the use of an intermediate machine-readable contract, for example:

`WebViewContract.json`

The audit should determine whether the following architecture is appropriate:

```text
VS Code TypeScript
        ↓
TypeScript Contract Extractor
        ↓
WebViewContract.json
        ↓
C# DTO Generator
        ↓
Generated WebView DTOs
```

The intermediate representation should be:

* deterministic;
* diffable;
* reviewable;
* independent of C#;
* independent of TypeScript implementation details;
* sufficient to regenerate equivalent C# DTOs.

Define the proposed schema of this intermediate contract if the approach is viable.

Do not implement the generator during this task unless explicitly required by the approved plan.

---

## C# DTO Generation Analysis

Determine how the extracted WebView contract could map to C#.

Analyze mappings for:

* string;
* boolean;
* numeric types;
* nullable values;
* arrays;
* objects;
* enums;
* discriminated unions;
* nested unions;
* dictionaries;
* unknown/dynamic JSON;
* recursive structures.

Determine how generated C# types should be organized.

Generated types must not be placed inside NSwag-generated files.

The design must remain safe across NSwag regeneration.

Propose an appropriate location under the Visual Studio extension, for example:

```text
ApiClient/
WebView/
    Generated/
```

or another structure justified by the audit.

---

## Newtonsoft.Json

The C# WebView protocol must use Newtonsoft.Json.

The audit must determine how the WebView DTO serialization should reuse the existing shared Newtonsoft configuration introduced by PORT-INFRA-003.

The target architecture is:

```text
SSE
 ↓
Newtonsoft.Json
 ↓
typed C# models
 ↓
typed WebView DTOs
 ↓
Newtonsoft.Json
 ↓
WebView JSON
```

Do not introduce System.Text.Json for the WebView protocol.

Do not create a second independent Newtonsoft serializer configuration if the existing shared configuration can be reused.

Determine whether polymorphic WebView DTOs require:

* explicit discriminator factories;
* generated serializers;
* custom converters;
* manual routing;
* or another approach.

Do not implement converters merely for the purpose of this audit.

---

## VS Code Test Analysis

Identify all VS Code tests related to:

* WebView message serialization;
* WebView message deserialization;
* message routing;
* message construction;
* discriminated unions;
* WebView state updates;
* extension/WebView interaction;
* protocol compatibility;
* error handling.

For every relevant test, determine whether a Visual Studio equivalent exists.

Produce a test-porting matrix:

| VS Code Test | Purpose | Protocol Area | VS2026 Equivalent | Missing | Porting Difficulty |
| ------------ | ------- | ------------- | ----------------- | ------- | ------------------ |

Explicitly identify tests that should be ported in a future task.

Do not automatically port the tests during PORT-INFRA-004 unless the approved plan explicitly requires it.

---

## Existing Visual Studio Analysis

Inspect at minimum:

* `SSEHelper.cs`;
* WebView communication classes;
* WebView message models if any;
* message dispatching;
* `PostMessage` implementations;
* WebView request handling;
* JSON serialization/deserialization;
* current tests;
* existing porting documentation.

Identify all anonymous objects, dictionaries, `JToken`, `JObject`, `object`, and other weakly-typed structures crossing the WebView boundary.

Map each one to the corresponding VS Code protocol type where possible.

---

## Architecture Decision

The audit must conclude whether the following target architecture is viable:

```text
VS Code TypeScript
        │
        │ source of truth
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
Strongly Typed WebView DTOs
        │
        ▼
Newtonsoft.Json
        │
        ▼
Existing VS Code WebView
```

If this architecture is not fully viable, identify precisely which parts require manual mapping.

Do not recommend changing the VS Code WebView protocol merely to simplify the Visual Studio port.

---

## Non-Goals

Do NOT:

* redesign the VS Code WebView protocol;
* modify the VS Code WebView implementation;
* modify VS Code production code;
* modify the Visual Studio WebView production implementation;
* replace Newtonsoft.Json;
* reintroduce System.Text.Json into the SSE/WebView pipeline;
* modify NSwag-generated files;
* create a second JSON serialization infrastructure;
* implement the complete DTO generator;
* automatically generate production DTOs;
* port all missing tests;
* modify unrelated Visual Studio code;
* start unrelated PORT tasks.

The task is analysis, contract extraction design, generator feasibility analysis, and documentation.

---

## Documentation Requirements

Create or update:

`packages/kilo-visualstudio/porting/docs/prompts/PORT-INFRA-004.md`

Use the existing PORT-INFRA prompt documentation as the model.

Preserve the complete execution history:

1. initial task/prompt;
2. investigation;
3. generated plan;
4. review/corrections;
5. final audit;
6. implementation recommendations;
7. validation result.

Do not silently overwrite previous history.

Update:

`packages/kilo-visualstudio/porting/docs/TASKS.md`

to reflect the actual state of PORT-INFRA-004.

Update other porting documentation when necessary to record:

* the WebView protocol source of truth;
* the proposed contract extraction architecture;
* the proposed DTO generation architecture;
* important findings;
* identified VS Code tests that remain to be ported.

Do not create redundant documentation.

---

## Deliverables

The task must produce:

1. complete WebView protocol inventory;
2. TypeScript type/dependency analysis;
3. VS Code → VS2026 protocol mapping;
4. VS Code test → VS2026 test mapping;
5. list of missing tests;
6. analysis of TypeScript Compiler API feasibility;
7. proposed `WebViewContract.json` representation;
8. proposed C# DTO generation strategy;
9. Newtonsoft.Json integration strategy;
10. polymorphism strategy;
11. architecture decision;
12. implementation plan for the subsequent task;
13. documentation updates.

---

## Acceptance Criteria

PORT-INFRA-004 can be considered complete only when:

* [ ] all relevant WebView message directions are inventoried;
* [ ] VS Code is explicitly established as the source of truth;
* [ ] all known WebView discriminators are identified;
* [ ] relevant TypeScript types are traced to their definitions;
* [ ] dynamic/weakly typed structures are identified;
* [ ] VS Code WebView tests have been inventoried;
* [ ] missing Visual Studio test coverage is identified;
* [ ] current Visual Studio WebView handling has been mapped to the VS Code protocol;
* [ ] TypeScript Compiler API feasibility has been assessed;
* [ ] an intermediate contract format has been evaluated;
* [ ] C# generation feasibility has been assessed;
* [ ] Newtonsoft.Json integration has been defined;
* [ ] polymorphic WebView DTO handling has been analyzed;
* [ ] no VS Code production code has been modified;
* [ ] no Visual Studio production implementation has been modified unless explicitly required by the approved plan;
* [ ] no generated NSwag file has been modified;
* [ ] documentation has been updated;
* [ ] `TASKS.md` reflects the actual state;
* [ ] a concrete implementation plan exists for the next task.

## Expected Final Status

`PORT-INFRA-004 → REVIEW`
