# WebView Contract Extractor

This tool extracts TypeScript type definitions and message schemas from the VS Code extension's webview code and generates a structured JSON contract file (`WebViewContract.json`) that describes the communication protocol between the webview and the extension host.

## Overview

The WebView Contract Extractor is a TypeScript AST-based code analyzer that:

1. **Parses TypeScript source files** from the VS Code extension's webview types directory
2. **Extracts type definitions** (interfaces, type aliases, unions)
3. **Identifies message types** (interfaces with discriminator fields like `type`, `status`, `role`)
4. **Scans for inline messages** in `postMessage()` calls throughout the codebase
5. **Generates a JSON contract** that describes all types and messages

The generated contract is then used by the [Generator](./README.md) to produce C# DTO classes for the Visual Studio extension.

## Architecture

### Input: TypeScript Source Files

The extractor processes TypeScript files from:
- `packages/kilo-vscode/webview-ui/src/types/messages/` - Message type definitions
- `packages/kilo-vscode/src/shared/` - Shared type definitions

### Processing: TypeScript Compiler API

The extractor uses the TypeScript compiler API to:
- Parse source files into Abstract Syntax Trees (ASTs)
- Perform type checking and symbol resolution
- Extract type information including:
  - Interface properties and their types
  - Type alias definitions (including union types)
  - Literal types (string, number, boolean literals)
  - Generic types (arrays, records)
  - Union and intersection types

### Output: WebViewContract.json

The generated contract contains:
- **types**: All extracted type definitions
- **messages**: Message types with discriminator information
- **diagnostics**: Errors and warnings encountered during extraction
- **statistics**: Counts of extracted types and messages
- **metadata**: Version information and source paths

## Type Extraction

### Interface Extraction

For each interface declaration:
1. Extract all property signatures
2. Determine property types (primitive, reference, union, array, etc.)
3. Identify optional properties
4. Extract JSDoc comments as descriptions
5. Detect discriminator fields (`type`, `status`, `role` with literal values)

### Type Alias Extraction

For type aliases:
- **Union types**: Extract all union member type names
- **String literal unions**: Preserve literal values (e.g., `"a" | "b" | "c"`)
- **Type references**: Resolve to referenced type
- **Type literals**: Extract inline object structure

### Special Type Patterns

#### Partial<T> & Pick<T, "field1" | "field2">

The extractor handles this common pattern for creating partial versions of types with specific required fields:

```typescript
export type PartUpdatedMessage = Partial<Part> & Pick<Part, "id" | "status">
```

The extractor:
1. Identifies the `Partial<T>` base type
2. Extracts required fields from `Pick<T, ...>`
3. Merges properties, marking non-picked fields as optional
4. Sets `baseType` and `requiredFields` metadata

#### Re-export Type Aliases

For re-exports like `export type { X as Y } from "..."`:
- The type checker resolves the alias to the actual type
- Source file information is preserved from the original definition

## Message Extraction

### Typed Messages

Messages defined as interfaces with discriminator fields:

```typescript
export interface ConfigLoadedMessage {
  type: "configLoaded"  // Discriminator
  config: Config
  // ... other properties
}
```

The extractor:
1. Identifies interfaces with literal `type`, `status`, or `role` properties
2. Uses the literal value as the discriminator value
3. Categorizes messages by direction (webviewToExtension or extensionToWebview)

### Inline Messages from postMessage() Calls

The extractor scans all TypeScript source files for `postMessage()` calls:

```typescript
window.postMessage({
  type: "partUpdated",
  part: { id: "123", status: "active" }
}, "*")
```

For each call:
1. Extract object literal properties
2. Identify discriminator field
3. Generate message name from discriminator value
4. Track source file location

### Message Direction Detection

- **webviewToExtension**: Messages from `webview-messages.ts`
- **extensionToWebview**: Messages from `extension-messages.ts` or inline messages from extension source files

## Type Resolution

### Two-Pass Extraction

The extractor uses multiple passes to handle type dependencies:

1. **First pass**: Extract all interfaces
2. **Second pass**: Extract all type aliases (now interfaces are available for reference)
3. **Third pass**: Re-extract interfaces (to resolve type alias references)
4. **Fourth pass**: Process messages (interfaces with discriminators)

### Type Name Resolution

For union types where the type checker returns `__type`:
- Parse the source file directly to extract union members
- Look for the `export type Name = ...` pattern
- Split by `|` and clean up member names

## Folder Organization

The `getSourceFileFolder()` function determines output folder based on source file:

| Source File | Folder |
|------------|--------|
| `extension-messages.ts` | ExtensionMessages |
| `webview-messages.ts` | WebviewMessages |
| `marketplace.ts` | Shared |
| `*-manager.ts` | PascalCase (e.g., AgentManager) |
| Other `.ts` files | Derived from filename |

## Design Decisions

### Why TypeScript Compiler API?

Using the TypeScript compiler API provides:
- **Accurate type information**: Full type checking and symbol resolution
- **Source code awareness**: Access to original source files and positions
- **Type inference**: Automatic resolution of complex types
- **Error detection**: Compiler diagnostics for invalid code

### Why Multiple Extraction Passes?

TypeScript types can reference each other in complex ways. Multiple passes ensure:
- Interfaces are extracted before type aliases that reference them
- Type aliases are extracted before interfaces that reference them
- Circular references are handled correctly

### Why Scan postMessage() Calls?

Not all messages are defined as named interfaces. Some are:
- Inline object literals in `postMessage()` calls
- Dynamically constructed messages
- One-off messages without dedicated type definitions

Scanning ensures these messages are also captured in the contract.

## Running the Extractor

```bash
cd packages/kilo-visualstudio/tools/webview-contract-extractor
bun src/extractor.ts
```

The extractor:
1. Reads the TypeScript project from `tsconfig.json`
2. Processes all source files in the types directory
3. Scans VS Code source files for `postMessage()` calls
4. Generates `WebViewContract.json` in the porting/contract directory

## Key Functions

### `extractInterfaceDeclaration()`

Extracts interface definitions including:
- Property signatures with types
- Optional modifiers
- Discriminator detection
- Source file information

### `extractTypeAliasDeclaration()`

Handles type aliases including:
- Union type member extraction
- String literal preservation
- Type reference resolution
- Partial/Pick pattern handling

### `extractPropertyDefinition()`

Extracts individual property definitions:
- Type detection (primitive, reference, union, array, record)
- Literal value extraction
- Optional flag detection
- Type reference resolution

### `extractPostMessageProperties()`

Extracts message structure from `postMessage()` calls:
- Object literal property extraction
- Type inference from literals
- Discriminator identification

### `scanPostMessageCalls()`

Scans source files for `postMessage()` invocations:
- AST traversal
- Call expression matching
- Message extraction and deduplication

## Troubleshooting

### Missing Types

If a type is not being extracted:
1. Check if it's in the processed directories (`webview-ui/src/types/` or `src/shared/`)
2. Verify the type is exported (not just declared)
3. Check for circular dependencies
4. Look for extraction errors in the console output

### Incorrect Union Members

If union type members are incorrect:
1. Check if the type checker returns `__type` (indicates resolution failure)
2. Verify the source file parsing logic
3. Check for complex union patterns (nested unions, intersections)

### Duplicate Messages

If messages appear multiple times:
1. Check the discriminator value tracking (`extractedMessages` map)
2. Verify message direction detection logic
3. Ensure deduplication is working in both extraction paths

## Integration with Generator

The extracted contract (`WebViewContract.json`) is the input to the [Generator](./README.md), which:
1. Reads the contract
2. Maps TypeScript types to C# equivalents
3. Generates C# DTO classes
4. Creates a discriminator-based deserialization factory

The contract serves as the **single source of truth** for the communication protocol between the webview and both the VS Code and Visual Studio extensions.
