# WebView Contract Extractor

This tool generates C# DTO (Data Transfer Object) classes from the WebViewContract.json file, which describes the message types used for communication between the Visual Studio extension's webview and the extension host.

## Overview

The WebView Contract Extractor is a code generator that:

1. Reads a JSON contract file (`WebViewContract.json`) that describes TypeScript types and messages
2. Generates corresponding C# classes in the `KiloVisualStudioExtension/WebViewDto` folder
3. Handles type mapping from TypeScript to C#
4. Manages namespace organization based on source file location
5. Resolves type conflicts with existing ApiClient types

## Architecture

### Input: WebViewContract.json

The contract file contains:
- **types**: Type definitions (interfaces, type aliases, unions, enums) extracted from TypeScript source files
- **messages**: Message definitions for webview-to-extension and extension-to-webview communication

Each type/message includes:
- `name`: Type/message name
- `kind`: Type kind (interface, typeAlias, union, enum)
- `properties`: For interfaces, the property definitions
- `sourceFile`: Original TypeScript source file path
- `discriminator`: For messages, the type discriminator field and value

### Output: C# DTO Classes

Generated files are organized into folders under `KiloVisualStudioExtension/WebViewDto/Messages/`:

```
Messages/
├── Shared/           # Types from src/shared/ and SDK types
├── Connection/       # Types from connection.ts
├── Parts/            # Types from parts.ts
├── Sessions/         # Types from sessions.ts
├── Permissions/      # Types from permissions.ts
├── Questions/        # Types from questions.ts
├── Providers/        # Types from providers.ts
├── Agents/           # Types from agents.ts
├── KiloConfig/       # Types from config.ts (special handling)
├── Profile/          # Types from profile.ts
├── AgentManager/     # Types from agent-manager.ts
├── Migration/        # Types from migration.ts
├── Memory/           # Types from memory.ts
├── ExtensionMessages/ # Messages from extension-messages.ts
├── WebviewMessages/  # Messages from webview-messages.ts
└── WebViewMessageFactory.cs  # Discriminator-based deserialization factory
```

## Type Mapping Rules

### Primitive Types

| TypeScript | C# |
|------------|-----|
| `string` | `string` |
| `number` / `integer` | `double` |
| `boolean` | `bool` |
| `any` / `unknown` | `object` |
| `array` | `List<object>` |
| `readonlyarray` | `IReadOnlyList<object>` |
| `map` | `Dictionary<object, object>` |
| `Date` | `DateTime` |
| `RegExp` | `Regex` |
| `Error` | `Exception` |

### Complex Types

- **Interfaces**: Generated as C# classes with properties
- **Type Aliases**: 
  - If referencing an SDK type that exists in ApiClient: `public class X : ApiClient.Y { }`
  - Otherwise: placeholder class `public class X { }`
- **Union Types**:
  - String literal unions (e.g., `"a" | "b" | "c"`): Generated as enums with `[JsonConverter(typeof(StringEnumConverter))]`
  - Mixed type unions: Generated as `object` or common base class if available
- **Enums**: Generated with `[JsonConverter(typeof(StringEnumConverter))]` and `[JsonProperty]` on members

## ApiClient Integration

The generator handles types that exist in both the TypeScript source and the ApiClient:

1. **SDK Types** (from `sdk/js/src/v2/gen/types.gen.ts`):
   - If the type exists in ApiClient: Use `ApiClient.TypeName` with `using KiloVisualStudioExtension.ApiClient;`
   - If not: Generate locally

2. **Local Types** (from VS Code extension):
   - **config.ts types** (e.g., `Config`): Always generated locally, even if ApiClient has a type with the same name
   - **Other local types**: Use `ApiClient.TypeName` if the type exists in ApiClient

3. **Type Aliases** referencing SDK types:
   - Generated as classes inheriting from `ApiClient.TypeName`

## Namespace Organization

- **Shared folder**: Uses base namespace `KiloVisualStudioExtension.WebView.Generated`
- **Other folders**: Use `KiloVisualStudioExtension.WebView.Generated.FolderName`

Example:
- `Shared/WorkStyle.cs` → `namespace KiloVisualStudioExtension.WebView.Generated;`
- `Agents/AgentConfig.cs` → `namespace KiloVisualStudioExtension.WebView.Generated.Agents;`

## Discriminator-Based Deserialization

The `WebViewMessageFactory.cs` provides discriminator-based deserialization for polymorphic message types:

```csharp
public static class WebViewMessageFactory
{
    public static T Deserialize<T>(JToken token) where T : class
    {
        var type = token["type"]?.Value<string>();
        return type switch
        {
            "configLoaded" => typeof(T) == typeof(ConfigLoadedMessage) 
                ? (T)(object)token.ToObject<ConfigLoadedMessage>(Serializer)! 
                : throw new JsonSerializationException("Type mismatch"),
            // ... more cases
            _ => throw new JsonSerializationException("Unknown message type: " + type)
        };
    }
}
```

This pattern:
- Uses explicit discriminator checking instead of JsonConverter inheritance
- Reuses `KiloJsonSerializer` configuration from the ApiClient
- Supports both `JToken` and JSON string input

## Running the Generator

```bash
cd packages/kilo-visualstudio
bun tools/webview-contract-extractor/generator.ts
```

The generator:
1. Reads `porting/contract/WebViewContract.json`
2. Scans `KiloVisualStudioExtension/ApiClient/` for existing types
3. Generates C# files in `KiloVisualStudioExtension/WebViewDto/`
4. Outputs generation statistics to console

## Key Functions

### `mapToCSharpType(prop: PropertyDefinition)`

Maps a TypeScript property to its C# equivalent, handling:
- Primitive type conversion
- Nullable type handling
- ApiClient type resolution
- Union type analysis

### `getSourceFileFolder(sourceFile: string)`

Determines the output folder based on the TypeScript source file path:
- `src/shared/*` → `Shared`
- `config.ts` → `KiloConfig`
- `extension-messages.ts` → `ExtensionMessages`
- `webview-messages.ts` → `WebviewMessages`
- `*-manager.ts` → PascalCase folder name

### `generateMessageClass(message: MessageType, ns: string, folder: string)`

Generates a C# class for a message type, including:
- Namespace declaration
- Using statements for dependencies
- JsonProperty attributes
- Discriminator property with default value

### `generateDiscriminatorFactory(...)`

Generates the `WebViewMessageFactory` class with switch-based deserialization for all message types.

## Design Decisions

### Why `ApiClient.TypeName` for SDK types?

SDK types that exist in ApiClient should use the ApiClient version to:
- Avoid duplicate type definitions
- Ensure consistency with the API contract
- Reduce maintenance burden

### Why generate `Config` locally?

Types from `config.ts` (like `Config`) are generated locally even if ApiClient has a type with the same name because:
- They represent VS Code extension configuration, not API types
- They may have different structure than ApiClient types
- Avoiding namespace conflicts

### Why discriminator-based deserialization?

The `WebViewMessageFactory` uses explicit discriminator checking instead of JsonConverter inheritance because:
- More explicit and debuggable
- Avoids issues with nullable annotations and generic converters
- Matches the pattern used in the rest of the codebase (PORT-INFRA-003)

## Maintenance

### Adding New Types

1. Update the TypeScript source files in `packages/kilo-vscode/webview-ui/src/types/messages/`
2. Regenerate the contract: (contract generation process TBD)
3. Run the generator: `bun tools/webview-contract-extractor/generator.ts`
4. Verify generated files compile correctly

### Modifying Type Mappings

Edit `generator.ts`:
- `csharpTypeMap` for primitive type mappings
- `mapToCSharpType()` for complex type handling
- `getSourceFileFolder()` for folder organization

### Troubleshooting

**Build errors about missing types:**
- Check if the type exists in ApiClient and should use `ApiClient.TypeName`
- Verify the type is being generated (check console output)
- Ensure the namespace is correct

**Type conflicts:**
- Types from `config.ts` are always generated locally
- SDK types use `ApiClient.TypeName` if they exist in ApiClient
- Other local types use `ApiClient.TypeName` if they exist in ApiClient
