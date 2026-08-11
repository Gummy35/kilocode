# Task: Migrate SSE Handling to Strongly-Typed Newtonsoft.Json Models

## Objective

Refactor the Visual Studio extension SSE pipeline so that SSE messages are handled using **strongly-typed C# models** and **Newtonsoft.Json exclusively**.

The goal is to eliminate the current mixture of:

* `System.Text.Json`
* `JsonElement`
* manual JSON property extraction
* Newtonsoft.Json in the NSwag API client

and establish **one JSON implementation based on Newtonsoft.Json** across the extension.

The existing WebView protocol must remain compatible.

---

## Important Constraints

### 1. Newtonsoft.Json is mandatory

Use Newtonsoft.Json for:

* SSE deserialization
* SSE event models
* polymorphic model deserialization
* NSwag API client serialization/deserialization

Do NOT introduce `System.Text.Json` for JSON processing.

Do NOT migrate the project to another serializer.

Do NOT upgrade Newtonsoft.Json unless the repository's current dependency situation proves that this is absolutely necessary.

---

### 2. Do NOT use JsonConverter inheritance unless it actually compiles

Previous attempts to implement custom `JsonConverter` classes resulted in compilation failures involving:

* `JsonConverter.CanConvert`
* `JsonConverter.ReadJson`
* nullable annotations
* C# 14
* Newtonsoft.Json 13.0.3

The following files have intentionally been removed from the working tree:

* `PartConverter.cs`
* `ToolStateConverter.cs`
* `MessageConverter.cs`

Do NOT recreate those files or repeat the same approach without first proving with a minimal compilation test that the exact Newtonsoft.Json version and project configuration support it.

Prefer an explicit discriminator-based factory/deserializer if custom converters are not viable.

---

### 3. Inspect the CURRENT repository before implementing

Do not rely on previous investigation reports as authoritative.

Inspect the actual current source code.

In particular, examine:

* `SSEHelper.cs`
* `SseClient.cs`
* `KiloConnectionService.cs`
* `VSProvider.cs`
* all files currently under `ApiClient/`
* generated NSwag models
* `KiloApiClient.cs`
* partial extensions around `KiloApiClient`
* `NswagPolymorphicModelTests.cs`
* all existing serialization-related tests
* the project `.csproj`
* Newtonsoft.Json package/version configuration

Also inspect the recent serialization/SSE implementation to understand what has already been changed.

The current code is authoritative.

---

# Target Architecture

The desired architecture is:

```text
SSE HTTP stream
       |
       v
SseClient
       |
       | event type + JSON string
       v
SSE deserializer
       |
       | Newtonsoft.Json
       v
Strongly typed SSE event
       |
       +--> strongly typed polymorphic Part
       |
       +--> strongly typed Message
       |
       +--> strongly typed ToolState
       |
       v
SSEHelper / business logic
       |
       v
existing WebView protocol
```

There should be **one JSON stack**:

```text
Newtonsoft.Json
       |
       +-- NSwag API client
       |
       +-- SSE deserialization
       |
       +-- polymorphic deserialization
       |
       +-- serialization to WebView
```

---

# Polymorphic Models

The current NSwag-generated models contain several OpenAPI unions.

At minimum, investigate and support these polymorphic structures where they occur in SSE:

## Part

Discriminator:

```text
type
```

Expected variants include:

```text
text
reasoning
file
tool
step-start
step-finish
snapshot
patch
agent
retry
compaction
subtask
```

Map each discriminator to the actual generated class present in the repository.

Do NOT assume class names from previous reports.

Inspect the generated code and use the actual types.

### Important: subtask

The OpenAPI definition may generate `subtask` as an inline/anonymous structure rather than a dedicated `SubtaskPart`.

Inspect the generated code.

If no dedicated type exists, choose the cleanest strongly-typed solution that does not modify generated NSwag code.

Do not simply deserialize it to `object` unless there is a concrete technical reason to do so.

---

## ToolState

Discriminator:

```text
status
```

Variants:

```text
pending
running
completed
error
```

Again, inspect the actual generated classes before implementing.

Nested polymorphism must work:

```text
ToolPart
   |
   +-- State
         |
         +-- ToolStatePending
         +-- ToolStateRunning
         +-- ToolStateCompleted
         +-- ToolStateError
```

---

## Message

Discriminator:

```text
role
```

Variants:

```text
user
assistant
```

Use the actual generated NSwag classes.

---

## Other polymorphic structures

Search the repository and OpenAPI-generated models for other unions that occur inside SSE payloads.

Examples may include:

* `FilePartSource`
* other nested discriminated unions

Do not limit the implementation to the three types above if the actual SSE payloads require additional polymorphic handling.

---

# Polymorphic Deserialization

If Newtonsoft.Json converters cannot safely be used with the current compiler/project configuration, implement centralized explicit discriminator-based deserialization.

For example:

```csharp
public static class PolymorphicDeserializer
{
    public static Part DeserializePart(
        JToken token,
        JsonSerializer serializer)
    {
        // inspect discriminator
        // select concrete generated type
        // deserialize using Newtonsoft.Json
    }

    public static ToolState DeserializeToolState(
        JToken token,
        JsonSerializer serializer)
    {
        // inspect status
        // select concrete generated type
    }

    public static Message DeserializeMessage(
        JToken token,
        JsonSerializer serializer)
    {
        // inspect role
        // select concrete generated type
    }
}
```

Use `JToken` / `JObject` as the intermediate representation where appropriate.

Do not parse JSON multiple times unnecessarily.

The SSE input should ideally be parsed once into a Newtonsoft representation and then passed through the deserialization pipeline.

---

# Shared Newtonsoft.Json Configuration

This is important.

There must not be two unrelated serializer configurations.

Inspect how `KiloApiClient` currently creates/configures its `JsonSerializerSettings`.

There is already an NSwag extension point:

```csharp
UpdateJsonSerializerSettings(JsonSerializerSettings settings)
```

Determine the cleanest way to make the serializer configuration reusable by both:

1. `KiloApiClient`
2. SSE deserialization

The final architecture should have a **single source of truth** for Newtonsoft.Json settings.

Do not duplicate serializer settings in `SSEHelper`.

Do not create subtly different Newtonsoft configurations for SSE and HTTP.

If necessary, introduce a shared serializer-settings factory or equivalent abstraction.

However:

* do not modify generated NSwag code unnecessarily
* do not break NSwag regeneration
* keep generated code untouched whenever possible

---

# SSE Event Models

The SSE pipeline currently contains different event structures.

Inspect the real implementation and model them appropriately.

For example, there may be:

```text
sync events
stream events
```

with structures similar to:

```json
{
  "name": "...",
  "id": "...",
  "data": { ... }
}
```

and:

```json
{
  "type": "...",
  "properties": { ... }
}
```

Do not blindly use a single inheritance hierarchy if the actual protocol does not justify it.

Design typed event envelopes that accurately represent the actual SSE protocol.

---

# Strong Typing Requirements

The objective is not merely to replace:

```csharp
JsonElement
```

with:

```csharp
JObject
```

That would not accomplish the goal.

Where the API contract provides a known type, use the corresponding C# model.

For example, instead of:

```csharp
var part = info.GetProperty("part");
var type = part.GetProperty("type").GetString();
```

the desired direction is:

```csharp
var part = typedEvent.Part;

switch (part)
{
    case TextPart text:
        ...
        break;

    case ToolPart tool:
        ...
        break;
}
```

The same principle applies to:

* Message
* ToolState
* Session
* Permission
* Question
* Suggestion
* status objects
* other SSE payloads where generated models are appropriate

Do not introduce strong typing merely for the sake of typing.

Where an SSE payload is genuinely dynamic or intentionally forwarded unchanged, retaining `JToken` is acceptable.

---

# WebView Compatibility

This refactor must NOT change the externally visible WebView protocol unless there is an explicit reason to do so.

The existing messages such as:

```text
messageCreated
partUpdated
sessionUpdated
...
```

must continue to be generated with the same semantic JSON structure.

The migration is primarily an internal refactor:

```text
BEFORE:

SSE JSON
 -> JsonElement
 -> manual extraction
 -> anonymous objects
 -> WebView

AFTER:

SSE JSON
 -> Newtonsoft.Json
 -> typed models
 -> existing business logic
 -> existing WebView protocol
```

Do not redesign the WebView protocol as part of this task.

---

# SSEHelper Refactoring

Refactor `SSEHelper` so that it no longer performs low-level JSON parsing with `System.Text.Json`.

Move deserialization responsibilities into dedicated classes where appropriate.

`SSEHelper` should primarily be responsible for:

* receiving typed SSE events
* business logic
* state tracking
* transforming typed data into the existing WebView protocol

It should not contain a large amount of discriminator parsing logic.

Avoid creating a giant switch statement in `SSEHelper`.

---

# Error Handling

Unknown or malformed discriminators must fail explicitly.

For example:

```text
Unknown Part type 'xxx'
Unknown ToolState status 'xxx'
Unknown Message role 'xxx'
```

Use appropriate Newtonsoft exceptions such as:

```csharp
JsonSerializationException
```

Include enough information to diagnose malformed SSE messages.

Do not silently deserialize an unknown polymorphic variant as an unrelated type.

However, consider carefully whether unknown **SSE event types** themselves should remain forward-compatible.

Unknown event types may reasonably be ignored or represented as generic events depending on the existing behavior.

Do not accidentally make the extension crash when the server introduces a new unrelated SSE event.

---

# Tests

Update and extend the existing tests.

Inspect:

```text
NswagPolymorphicModelTests.cs
```

and all current SSE tests.

Tests must cover at least:

## Part

* text
* reasoning
* file
* tool
* step-start
* step-finish
* snapshot
* patch
* agent
* retry
* compaction
* subtask if supported

## ToolState

* pending
* running
* completed
* error

Including nested:

```text
ToolPart -> ToolStateRunning
ToolPart -> ToolStateCompleted
```

etc.

## Message

* user
* assistant

## Unknown discriminators

Verify that malformed/unknown values produce useful `JsonSerializationException`s.

---

# SSE Integration Tests

Add tests using realistic SSE payloads.

At minimum test:

```text
message.updated.1
message.part.updated.1
session.created.1
session.status
message.part.delta
session.error
```

and any other event types currently handled by `SSEHelper`.

The tests should verify that:

1. the SSE JSON is deserialized using Newtonsoft.Json
2. the resulting object is strongly typed
3. polymorphic members resolve to the correct generated concrete class
4. existing business logic receives the expected values
5. the WebView output remains semantically equivalent

---

# Serialization Round Trip

Where useful, test:

```text
SSE JSON
 -> typed model
 -> Newtonsoft.Json serialization
```

and verify semantic equivalence of the relevant payload.

Do not require byte-for-byte JSON equality.

Property ordering and insignificant formatting differences are irrelevant.

---

# Generated Code

Do NOT modify generated NSwag classes unless absolutely unavoidable.

In particular:

* do not edit generated model definitions
* do not add custom logic directly to generated classes
* do not make the solution dependent on manual modifications that NSwag regeneration would overwrite

Use partial classes or external helper/factory classes where appropriate.

---

# Important Architectural Requirement

The final implementation should make this possible:

```text
                    Newtonsoft.Json
                           |
             +-------------+-------------+
             |                           |
       KiloApiClient                 SSE pipeline
             |                           |
       NSwag models                 typed SSE events
             |                           |
             +-------------+-------------+
                           |
                  shared configuration
```

There must be no `System.Text.Json` dependency for JSON processing in the SSE path.

Search the extension after the migration and identify remaining JSON-processing usages.

If `System.Text.Json` remains for a non-JSON purpose, that is fine, but JSON serialization/deserialization should use Newtonsoft.Json.

---

# Implementation Strategy

Before modifying production code:

1. Inspect the complete current implementation.
2. Identify all SSE event types actually handled.
3. Identify the generated model corresponding to each payload.
4. Identify all polymorphic fields.
5. Inspect current serializer settings.
6. Inspect existing tests.
7. Determine whether `JsonConverter` is genuinely viable with the current project.
8. If not, implement explicit discriminator factories.
9. Implement the shared Newtonsoft serializer configuration.
10. Refactor the SSE pipeline incrementally.
11. Run the complete relevant test suite.
12. Fix compilation and behavioral regressions.
13. Search for remaining `System.Text.Json` JSON processing in the extension.
14. Review the final diff for unnecessary complexity.

Do not implement a speculative architecture before inspecting the actual current code.

---

# Definition of Done

The task is complete only when all of the following are true:

* SSE JSON processing uses Newtonsoft.Json.
* No `JsonElement` is used by the SSE pipeline.
* SSE messages are represented by strongly-typed C# objects wherever the API contract provides a corresponding model.
* `Part` polymorphism works.
* `ToolState` nested polymorphism works.
* `Message` polymorphism works.
* Other relevant nested unions are handled.
* A single shared Newtonsoft.Json configuration is used.
* Generated NSwag files remain regeneration-safe.
* Existing WebView behavior remains compatible.
* Unknown discriminators fail clearly.
* Existing SSE behavior is covered by tests.
* Existing NSwag/API serialization tests still pass.
* The extension builds successfully with the current C#/.NET configuration.
* No unnecessary `JsonConverter` workaround is introduced.
* No second JSON serialization framework is introduced for SSE.

---

# Final Instruction

Do not ask for confirmation after the investigation.

First inspect the complete current codebase and produce a concise implementation plan identifying:

1. the actual SSE event model currently used,
2. the actual generated NSwag types,
3. all polymorphic/nested polymorphic fields,
4. the current Newtonsoft.Json configuration,
5. the files that need modification,
6. the tests that need modification or creation.

Then implement the migration.

If an assumption in this task conflicts with the actual repository code, **the repository code and OpenAPI-generated models are authoritative**. Adapt the implementation accordingly while preserving the core requirements:

**strongly-typed SSE + Newtonsoft.Json only + shared serializer configuration + WebView compatibility.**
