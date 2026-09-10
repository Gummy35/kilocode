#!/usr/bin/env bun
/**
 * WebView Contract Generator
 * 
 * This script generates C# DTO (Data Transfer Object) classes from the WebViewContract.json file,
 * which describes the message types used for communication between the Visual Studio extension's
 * webview and the extension host.
 * 
 * ## Architecture
 * 
 * The generator performs a multi-stage code generation process:
 * 
 * 1. **Contract Loading**: Reads WebViewContract.json produced by extractor.ts
 * 
 * 2. **Enum Generation**: Processes string literal unions (e.g., `"open" | "closed" | "pending"`)
 *    and generates C# enum types with appropriate attributes.
 * 
 * 3. **Interface Generation**: For each discriminated union (like `WebviewMessage`), generates
 *    an empty marker interface (e.g., `IWebviewMessage`) that all union members implement.
 * 
 * 4. **Type Generation**: For each type definition in the contract:
 *    - Computes signature hash to detect duplicates
 *    - Checks if the type implements any union interfaces
 *    - Generates C# class with JsonProperty attributes
 *    - Handles inheritance (Partial<T> & Pick<T, ...> pattern)
 * 
 * 5. **Message Generation**: For each message in webviewToExtension and extensionToWebview:
 *    - Generates C# class with discriminator field
 *    - Tags class with union interface if hash matches
 *    - Adds using statements for cross-namespace references
 * 
 * 6. **Factory Generation**: Creates WebViewMessageFactory.cs with discriminator-based
 *    deserialization logic for polymorphic message handling.
 * 
 * ## Type Mapping Rules
 * 
 * | TypeScript | C# | Notes |
 * |---|---|---|
 * | `string` | `string` | |
 * | `number` | `double` | C# uses double for all numbers |
 * | `boolean` | `bool` | |
 * | `P`, `T` (generics) | `object` | Generics become object |
 * | `union` | `object` | Unions become object |
 * | `Record<string, X>` | `object` | Dynamic objects |
 * | `Array<T>` | `List<T>` | Collections |
 * | `stringLiteral` | `string` with default | Discriminator values |
 * 
 * ## Deduplication
 * 
 * Before generation, the extractor deduplicates types by signature hash:
 * - Types in `Shared/` folder take priority
 * - Shorter names (prefix matches) take priority  
 * - First occurrence wins for same-name duplicates
 * 
 * This ensures each unique structure is generated only once.
 * 
 * ## Interface Implementation Tagging
 * 
 * For discriminated unions like `WebviewMessage`:
 * 1. Extractor computes `unionMemberHashes` for all 22 members
 * 2. Generator builds `hashToUnion` map (memberHash → unionName)
 * 3. For each class being generated, check if its signatureHash is in the map
 * 4. If yes, add `: IUnionName` to the class declaration
 * 5. Add using statement for the interface namespace if needed
 * 
 * ## AI Implementation Notes
 * 
 * ### Signature Hash Matching
 * The key to interface tagging is matching the class's `signatureHash` against
 * the union's `unionMemberHashes`. This works because:
 * - Both use the same hash computation algorithm (SHA-256 of normalized structure)
 * - The hash is stable across naming conventions and file locations
 * - Case-insensitive lookup handles naming discrepancies
 * 
 * ### Using Statement Generation
 * When a class implements an interface from a different namespace:
 * - Determine the interface's folder from its sourceFile
 * - Compute the interface namespace (Shared → base ns, else ns.Folder)
 * - Add `using Namespace;` if different from class's namespace
 * 
 * ### Inheritance Detection
 * The generator detects inheritance patterns:
 * - `extendsBase` field: Explicit interface extends (e.g., `TextPart extends BasePart`)
 * - `baseType` field: Partial<T> & Pick<T, ...> pattern
 * - Heuristic detection: Matches derived/base types by property subset
 * 
 * ## Usage
 * 
 * ```bash
 * # Run the generator (typically via generate-webview-dtos.ps1)
 * bun tools/webview-contract-extractor/generator.ts
 * 
 * # Output: C# classes in KiloExtensionDTOs/src/
 * ```
 * 
 * ## Files
 * 
 * - `generator.ts`: Main code generation logic (this file)
 * - `extractor.ts`: TypeScript type extractor (produces WebViewContract.json)
 * - `types.ts`: TypeScript type definitions for the contract structure
 */

import * as fs from "fs"
import * as path from "path"

// Contract is 2 levels up (from tools/webview-contract-extractor to packages/kilo-visualstudio)
const VS_DIR = path.resolve(__dirname, "..", "..")
const CONTRACT_PATH = path.join(VS_DIR, "porting/contract/WebViewContract.json")
const OUTPUT_PATH = path.join(VS_DIR, "KiloExtensionDTOs/src")

/**
 * Type reference in a property definition
 */
interface TypeReference {
  name: string
  kind: string
}

/**
 * Property definition from TypeScript type
 */
interface PropertyDefinition {
  name: string
  type: string
  optional: boolean
  nullable: boolean
  elementType?: string | null  // For arrays and unions
  typeRef?: TypeReference | null  // Reference to another type
  literalValue?: string | number | boolean | null  // For literal types
  isLiteral: boolean
  description?: string
}

/**
 * Discriminator info for polymorphic types
 */
interface DiscriminatorInfo {
  field: string  // Usually "type"
  value: string  // Discriminator value (e.g., "configLoaded")
}

/**
 * Enum definition for generated enums
 */
interface EnumDefinition {
  name: string
  members: string[]  // Original string literal values
}

/**
 * Inheritance candidate for heuristic detection
 */
interface InheritanceCandidate {
  derivedType: TypeDefinition
  baseType: TypeDefinition
  confidence: number
  reasons: string[]
}

/**
 * Type definition from the contract
 */
interface TypeDefinition {
  name: string
  kind: string  // "interface", "typeAlias", "union", "enum"
  properties?: PropertyDefinition[]  // For interfaces
  unionMembers?: string[]  // For unions
  discriminator?: DiscriminatorInfo  // For message types
  sourceFile: string  // Original TypeScript source file
  description?: string
  extendsBase?: string  // Explicit interface extends relationship
  baseType?: string  // For Partial<T> & Pick<T, ...> pattern
  requiredFields?: string[]  // Required fields from Pick<T, ...>
}

/**
 * Global map of generated enums (to avoid duplicates)
 */
let generatedEnums: Map<string, EnumDefinition>

/**
 * Message type definition
 */
interface MessageType {
  name: string
  type: string
  discriminator: DiscriminatorInfo
  properties: PropertyDefinition[]
  sourceFile: string
}

/**
 * WebView contract structure
 */
interface WebViewContract {
  schemaVersion: string
  messages: {
    webviewToExtension: MessageType[]  // Messages from webview to extension host
    extensionToWebview: MessageType[]  // Messages from extension host to webview
  }
  types: TypeDefinition[]  // All type definitions
}

function pascalCase(name: string): string {
  if (!name) return name
  const converted = name.replace(/-([a-z])/g, (match) => match.charAt(1).toUpperCase())
  const sanitized = converted.replace(/[^a-zA-Z0-9_]/g, '')
  if (!sanitized) return "Value"
  return sanitized.charAt(0).toUpperCase() + sanitized.slice(1)
}

/**
 * Global state for code generation
 */
let contract: WebViewContract  // Parsed contract data
let typeDefinitions: Map<string, TypeDefinition>  // Map of type name to definition
let generatedTypes: Set<string>  // Set of already generated type names
let neededTypes: Set<string>  // Set of types needed by messages
let collectingTypes: Set<string>  // Track types being collected (prevent infinite recursion)
let existingApiTypes: Set<string>  // Set of type names that exist in ApiClient
let inlineEnumRegistry: Map<string, { enumName: string; usageCount: number; usedBy: string[] }>  // Map of enum signature to definition
let generatedInlineEnumSignatures: Set<string>  // Set of already generated inline enum signatures

/**
 * Check if a type name is a primitive TypeScript type
 */
function isPrimitiveType(typeName: string): boolean {
  const lower = typeName.toLowerCase()
  return lower === 'string' || lower === 'number' || lower === 'integer' || 
         lower === 'boolean' || lower === 'any' || lower === 'unknown' ||
         lower === 'void' || lower === 'null' || lower === 'undefined'
}

/**
 * Map of TypeScript types to their C# equivalents
 */
const csharpTypeMap: Map<string, string> = new Map([
  ['array', 'List<object>'],
  ['readonlyarray', 'IReadOnlyList<object>'],
  ['map', 'Dictionary<object, object>'],
  ['readonlymap', 'IReadOnlyDictionary<object, object>'],
  ['set', 'HashSet<object>'],
  ['readonlyset', 'IReadOnlySet<object>'],
  ['promise', 'Task<object>'],
  ['function', 'Delegate'],
  ['object', 'object'],
  ['date', 'DateTime'],
  ['regexp', 'Regex'],
  ['error', 'Exception'],
  ['symbol', 'object'],
])

/**
 * Get C# equivalent for common TypeScript types
 * Returns null if no direct equivalent exists
 */
function getCSharpTypeForCommonType(typeName: string): string | null {
  const lower = typeName.toLowerCase()
  if (csharpTypeMap.has(lower)) {
    return csharpTypeMap.get(lower)!
  }
  return null
}

/**
 * Check if a type name is an internal/complex TypeScript type
 * These include:
 * - Types with @ (namespace)
 * - Types with : (type operators)
 * - String literals (start with ")
 * - Namespaced types (::)
 * - Intersection types (&)
 * - Union types (|)
 * - Internal types (__prefix)
 */
function isInternalType(typeName: string): boolean {
  return typeName.includes('@') || typeName.includes(':') || 
         typeName.startsWith('"') || typeName.includes('::') ||
         typeName.includes('&') || typeName.includes('|') ||
         typeName.startsWith('__')
}

/**
 * Parse a union type string into its members.
 * 
 * ## Example
 * Input: `"string | number | undefined"`
 * Output: `["string", "number", "undefined"]`
 * 
 * AI Note: Simple split on `|` - no brace tracking needed since this is for
 * elementType parsing, not full union extraction.
 */
function parseUnionMembers(elementType: string): string[] {
  return elementType.split('|').map(p => p.trim())
}

/**
 * Check if a type is a string literal (wrapped in quotes).
 * 
 * AI Note: Used to detect inline enum candidates. String literals like `"open"` or `'closed'`
 * are candidates for enum generation.
 */
function isStringLiteral(type: string): boolean {
  if (typeof type !== 'string') return false
  return type.startsWith('"') || type.startsWith("'")
}

/**
 * Extract the value from a string literal type.
 * 
 * ## Example
 * Input: `'"hello"'`
 * Output: `'hello'`
 * 
 * AI Note: Strips the outer quotes from a string literal type.
 */
function getStringLiteralValue(type: string): string {
  const trimmed = type.trim()
  return trimmed.slice(1, -1)
}

/**
 * Find a common base class for a list of types
 * Used for union types with multiple class members
 * Returns null if no common base is found
 */
function findCommonBaseClass(typeNames: string[]): string | null {
  const typeDefs = typeNames.map(name => typeDefinitions.get(name)).filter(t => t !== undefined)
  if (typeDefs.length === 0) return null
  
  const baseClasses = new Set<string>()
  
  for (const typeDef of typeDefs) {
    if (typeDef.kind === 'interface') {
      const normalizedSource = typeDef.sourceFile.replace(/\\/g, '/')
      const isNodeModules = normalizedSource.includes('node_modules')
      if (!isNodeModules) {
        baseClasses.add(typeDef.name)
      }
    }
  }
  
  if (baseClasses.size === 0) return null
  
  for (const baseClass of baseClasses) {
    const allInherit = typeDefs.every(typeDef => {
      if (typeDef.name === baseClass) return true
      if (typeDef.kind !== 'interface') return false
      return false
    })
    
    if (allInherit) return baseClass
  }
  
  const commonBases: string[] = []
  for (const baseClass of baseClasses) {
    let isCommon = true
    for (const typeDef of typeDefs) {
      if (typeDef.name === baseClass) continue
      if (typeDef.kind !== 'interface') {
        isCommon = false
        break
      }
    }
    if (isCommon && commonBases.length === 0) {
      commonBases.push(baseClass)
    }
  }
  
  if (commonBases.length > 0) {
    const baseType = commonBases[0]!
    const typeDef = typeDefinitions.get(baseType)
    if (typeDef && typeDef.kind === 'interface') {
      const normalizedSource = typeDef.sourceFile.replace(/\\/g, '/')
      const isNodeModules = normalizedSource.includes('node_modules')
      if (!isNodeModules) {
        return baseType
      }
    }
  }
  
  return null
}

/**
 * Generate an enum name from literal values
 * Example: ["a", "b", "c"] → "ABCEnum"
 */
function generateEnumName(literalValues: string[]): string {
  const baseName = literalValues.map(v => {
    const value = isStringLiteral(v) ? getStringLiteralValue(v) : v
    return pascalCase(value)
  }).join('')
  const sanitized = baseName.replace(/[^a-zA-Z0-9]/g, '')
  return (sanitized || 'EnumValue') + 'Enum'
}

/**
 * Generate a signature from enum members for deduplication
 * Example: ["error", "ready", "disabled"] → "disabled,error,ready"
 */
function generateInlineEnumSignature(members: string[]): string {
  return members
    .map(m => isStringLiteral(m) ? getStringLiteralValue(m) : m)
    .sort()
    .join(',')
}

/**
 * Check if an enum with the same members already exists
 * Prevents duplicate enum generation
 */
function enumExistsWithSameMembers(enumName: string, members: string[]): boolean {
  const enumDef = generatedEnums.get(enumName)
  if (!enumDef) return false
  if (enumDef.members.length !== members.length) return false
  return enumDef.members.every((m, i) => m === members[i])
}

/**
 * Register an inline enum discovered in a property
 * Returns the enum name to use for this property
 */
function registerInlineEnum(members: string[], usedBy: string): string {
  const signature = generateInlineEnumSignature(members)
  
  if (inlineEnumRegistry.has(signature)) {
    const existing = inlineEnumRegistry.get(signature)!
    existing.usageCount++
    if (!existing.usedBy.includes(usedBy)) {
      existing.usedBy.push(usedBy)
    }
    return existing.enumName
  }
  
  const enumName = generateEnumName(members)
  inlineEnumRegistry.set(signature, {
    enumName,
    usageCount: 1,
    usedBy: [usedBy]
  })
  
  return enumName
}

/**
 * Get or create inline enum from members
 */
function getOrCreateInlineEnum(members: string[], usedBy: string): { enumName: string; isNew: boolean } {
  const signature = generateInlineEnumSignature(members)
  
  if (generatedInlineEnumSignatures.has(signature)) {
    const existing = inlineEnumRegistry.get(signature)!
    existing.usageCount++
    if (!existing.usedBy.includes(usedBy)) {
      existing.usedBy.push(usedBy)
    }
    return { enumName: existing.enumName, isNew: false }
  }
  
  const enumName = generateEnumName(members)
  inlineEnumRegistry.set(signature, {
    enumName,
    usageCount: 1,
    usedBy: [usedBy]
  })
  generatedInlineEnumSignatures.add(signature)
  
  return { enumName, isNew: true }
}

/**
 * Try to generate an enum from a union type with string literal members
 * Creates the enum file immediately and returns the enum type name
 * Returns null if the union cannot be converted to an enum
 */
function tryGenerateUnionEnum(elementType: string, prop: PropertyDefinition, typeName: string): { type: string, isNullable: boolean } | null {
  const unionParts = parseUnionMembers(elementType)
  const nonNullParts = unionParts.filter(p => p !== 'undefined' && p !== 'null')
  
  if (nonNullParts.length === 0) return null
  
  const allLiterals = nonNullParts.every(p => isStringLiteral(p))
  if (!allLiterals) return null
  
  const literalValues = nonNullParts.map(p => getStringLiteralValue(p))
  const { enumName, isNew } = getOrCreateInlineEnum(literalValues, typeName)
  
  if (!isNew) {
    return { type: enumName, isNullable: unionParts.some(p => p === 'undefined' || p === 'null') }
  }
  
  const enumDef: EnumDefinition = { name: enumName, members: literalValues }
  generatedEnums.set(enumName, enumDef)
  
  const enumCode = `// <auto-generated>
//     This code was generated by WebViewContractGenerator.
//     Do not modify this file directly as changes will be lost on regeneration.
//     Source: WebViewContract.json schema version ${contract.schemaVersion}
// </auto-generated>

#nullable enable

namespace ${ns};

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

/// <summary>
/// Enum: ${enumName}
/// Generated from inline string literal union
/// Members: ${literalValues.join(', ')}
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum ${enumName}
{
${enumDef.members.map((m, i) => `    [JsonProperty("${m}")]\n    ${pascalCase(m)}${i < enumDef.members.length - 1 ? ',' : ''}`).join('\n')}
}
`
  const enumTargetDir = getDirectoryForFolder('Shared')
  const enumFilePath = path.join(enumTargetDir, `${enumName}.cs`)
  fs.writeFileSync(enumFilePath, enumCode)
  console.log(`  Generated inline enum: ${enumName} (${literalValues.join(', ')})`)
  
  return { type: enumName, isNullable: unionParts.some(p => p === 'undefined' || p === 'null') }
}

/**
 * Map a TypeScript property definition to its C# type equivalent
 * 
 * ## Type Resolution Logic
 * 1. Handle literal types (string, number, boolean literals)
 * 2. Map primitive types (string, number, boolean)
 * 3. Handle arrays (List<T>)
 * 4. Resolve type references to other types
 * 5. Handle union types:
 *    - Single non-null type: use that type
 *    - String literal unions: generate/use enum
 *    - Boolean literal unions: use bool
 *    - Multiple class types: find common base
 *    - Mixed types: use object
 * 6. Check ApiClient for type conflicts:
 *    - SDK types that exist in ApiClient: use ApiClient.TypeName
 *    - Local types (except config.ts) that exist in ApiClient: use ApiClient.TypeName
 *    - config.ts types: generate locally even if ApiClient has same name
 * 
 * @param prop - TypeScript property definition
 * @returns Object with C# type, nullability, and original TypeScript type
 */
function mapToCSharpType(prop: PropertyDefinition): { type: string, originalType?: string, isNullable: boolean } {
  const baseType = prop.type.toLowerCase()
  
  if (prop.isLiteral && prop.literalValue !== null) {
    if (typeof prop.literalValue === 'string') {
      return { type: 'string', isNullable: prop.optional || prop.nullable, originalType: prop.type }
    }
    if (typeof prop.literalValue === 'number') {
      return { type: 'double', isNullable: prop.optional || prop.nullable, originalType: prop.type }
    }
    if (typeof prop.literalValue === 'boolean') {
      return { type: 'bool', isNullable: prop.optional || prop.nullable, originalType: prop.type }
    }
  }
  
  if (baseType === 'string' || baseType === 'literal') return { type: 'string', isNullable: prop.optional || prop.nullable }
  if (baseType === 'number' || baseType === 'integer') return { type: 'double', isNullable: prop.optional || prop.nullable }
  if (baseType === 'boolean') return { type: 'bool', isNullable: prop.optional || prop.nullable }
  if (baseType === 'array') {
    const elemType = mapToCSharpType({ type: prop.elementType || 'object', optional: false, nullable: false, typeRef: prop.typeRef })
    return { type: `List<${elemType.type}>`, isNullable: prop.optional || prop.nullable }
  }
  if (baseType === 'record') return { type: 'Dictionary<string, object>', isNullable: prop.optional || prop.nullable }
  
  // Use the original prop.type for lookup (not lowercase) to preserve case
  if (!isPrimitiveType(prop.type) && !isInternalType(prop.type) && typeDefinitions.has(prop.type)) {
    const typeDef = typeDefinitions.get(prop.type)!
    const isNodeModules = typeDef.sourceFile.includes('node_modules')
    if (isNodeModules) {
      return { type: 'object', isNullable: prop.optional || prop.nullable, originalType: prop.type }
    }
    if (typeDef.kind === 'typeAlias') {
      return { type: pascalCase(prop.type), isNullable: prop.optional || prop.nullable, originalType: prop.type }
    }
    if (typeDef.kind === 'union') {
      // Skip union types with no members (indicates members couldn't be resolved from external imports)
      const memberCount = Array.isArray(typeDef.unionMembers) ? typeDef.unionMembers.length : (typeDef.unionMembers ? Object.keys(typeDef.unionMembers).length : 0)
      if (memberCount === 0) {
        return { type: 'object', isNullable: prop.optional || prop.nullable, originalType: prop.type }
      }
      
      // Check if all union members are string literals (enum-able)
      if (typeDef.unionMembers && typeDef.unionMembers.every(m => m.startsWith('"') || m.startsWith("'"))) {
        // This is a string literal union - use the type name with "Enum" suffix
        const enumName = pascalCase(prop.type) + (pascalCase(prop.type).endsWith('Enum') ? '' : 'Enum')
        return { type: enumName, isNullable: prop.optional || prop.nullable, originalType: prop.type }
      }
      return { type: 'object', isNullable: prop.optional || prop.nullable, originalType: prop.type }
    }
    return { type: pascalCase(prop.type), isNullable: prop.optional || prop.nullable, originalType: prop.type }
    return { type: pascalCase(prop.type), isNullable: prop.optional || prop.nullable, originalType: prop.type }
  }
  
  if (baseType === 'union') {
    if (prop.elementType && !prop.elementType.startsWith('List<')) {
      const unionParts = parseUnionMembers(prop.elementType)
      const nonNullParts = unionParts.filter(p => p !== 'undefined' && p !== 'null')
      
      if (nonNullParts.length === 1) {
        let actualType = nonNullParts[0]!
        
        if (isStringLiteral(actualType)) {
          return { type: 'string', isNullable: true, originalType: prop.elementType }
        }
        if (actualType === 'string') {
          return { type: 'string', isNullable: true, originalType: prop.elementType }
        }
        if (actualType === 'number' || actualType === 'integer') {
          return { type: 'double', isNullable: true, originalType: prop.elementType }
        }
        if (actualType === 'boolean') {
          return { type: 'bool', isNullable: true, originalType: prop.elementType }
        }
        if (actualType === 'true' || actualType === 'false') {
          return { type: 'bool', isNullable: true, originalType: prop.elementType }
        }
        if (actualType.includes('&')) {
          return { type: 'object', isNullable: true, originalType: prop.elementType }
        }
        if (isInternalType(actualType)) {
          return { type: 'object', isNullable: true, originalType: prop.elementType }
        }
        if (!typeDefinitions.has(actualType)) {
          return { type: 'object', isNullable: true, originalType: prop.elementType }
        }
        const actualTypeDef = typeDefinitions.get(actualType)!
        const isNodeModules = actualTypeDef.sourceFile.includes('node_modules')
        if (isNodeModules) {
          return { type: 'object', isNullable: true, originalType: prop.elementType }
        }
        return { type: pascalCase(actualType), isNullable: true, originalType: prop.elementType }
      }
      
      if (nonNullParts.length > 1) {
        const allLiterals = nonNullParts.every(p => isStringLiteral(p))
        if (allLiterals) {
          const enumResult = tryGenerateUnionEnum(prop.elementType, prop, prop.typeRef?.name || 'unknown')
          if (enumResult) return enumResult
        }
        
        const allBooleans = nonNullParts.every(p => p === 'true' || p === 'false')
        if (allBooleans) {
          return { type: 'bool', isNullable: unionParts.some(p => p === 'undefined' || p === 'null'), originalType: prop.elementType }
        }
        
        const classTypes = nonNullParts.filter(p => !isStringLiteral(p) && !isPrimitiveType(p.toLowerCase()) && !isInternalType(p) && typeDefinitions.has(p))
        if (classTypes.length > 0) {
          const commonBase = findCommonBaseClass(classTypes)
          if (commonBase) {
            return { type: pascalCase(commonBase), isNullable: unionParts.some(p => p === 'undefined' || p === 'null'), originalType: prop.elementType }
          }
        }
        
        return { type: 'object', isNullable: true, originalType: prop.elementType }
      }
    }
    return { type: 'object', isNullable: true, originalType: prop.typeRef?.name }
  }
  if (baseType === 'any' || baseType === 'unknown') return { type: 'object', isNullable: prop.optional || prop.nullable }
  
  if (prop.typeRef?.name) {
    const refName = prop.typeRef.name
    if (!refName || refName.trim() === '' || refName === '[]') {
      return { type: 'object', isNullable: prop.optional || prop.nullable }
    }
    if (isPrimitiveType(refName)) {
      const isNullable = prop.optional || prop.nullable
      return { 
        type: refName.toLowerCase() === 'string' ? 'string' : 
              refName.toLowerCase() === 'number' ? 'double' :
              refName.toLowerCase() === 'boolean' ? 'bool' : 'object',
        isNullable: isNullable
      }
    }
    if (isInternalType(refName)) {
      return { type: 'object', isNullable: prop.optional || prop.nullable, originalType: refName }
    }
    // Check for common TypeScript types with C# equivalents
    const csharpEquivalent = getCSharpTypeForCommonType(refName)
    if (csharpEquivalent) {
      return { type: csharpEquivalent, isNullable: prop.optional || prop.nullable, originalType: refName }
    }
    
    if (typeDefinitions.has(refName)) {
      const typeDef = typeDefinitions.get(refName)!
      
      // Check if the type is from node_modules with no properties
      const isNodeModules = typeDef.sourceFile.includes('node_modules')
      const hasNoProperties = !typeDef.properties || typeDef.properties.length === 0
      if (isNodeModules && hasNoProperties) {
        return { type: 'object', isNullable: prop.optional || prop.nullable, originalType: refName }
      }
      if (typeDef.kind === 'typeAlias') {
        // Type aliases will be generated as placeholder classes if needed
        return { type: pascalCase(refName), isNullable: prop.optional || prop.nullable, originalType: refName }
      }
      if (typeDef.kind === 'union') {
        // Skip union types with no members (indicates members couldn't be resolved from external imports)
        const memberCount = Array.isArray(typeDef.unionMembers) ? typeDef.unionMembers.length : (typeDef.unionMembers ? Object.keys(typeDef.unionMembers).length : 0)
        
        if (memberCount === 0) {
          
          return { type: 'object', isNullable: prop.optional || prop.nullable, originalType: refName }
        }
        
        if (typeDef.unionMembers && memberCount > 0) {
          const allLiterals = typeDef.unionMembers.every(m => 
            ['other', 'zero', 'one', 'two', 'few', 'many'].includes(m)
          )
          if (allLiterals) {
            return { type: 'string', isNullable: prop.optional || prop.nullable }
          }
        }
        // Use elementType if available for better union info
        if (prop.elementType) {
          const unionParts = prop.elementType.split('|').map(p => p.trim())
          const nonNullParts = unionParts.filter(p => p !== 'undefined' && p !== 'null')
          
          if (nonNullParts.length === 1) {
            let actualType = nonNullParts[0]
            // Check if it's a string literal
            if (actualType.startsWith('"') || actualType.startsWith("'")) {
              return { type: 'string', isNullable: true, originalType: prop.elementType }
            }
        // Check if it's an intersection type
        if (actualType.includes('&')) {
          return { type: 'object', isNullable: true, originalType: prop.elementType }
        }
        if (actualType === 'string') {
              return { type: 'string', isNullable: true, originalType: prop.elementType }
            }
            if (actualType === 'number' || actualType === 'integer') {
              return { type: 'double', isNullable: true, originalType: prop.elementType }
            }
            if (actualType === 'boolean') {
              return { type: 'bool', isNullable: true, originalType: prop.elementType }
            }
            return { type: actualType, isNullable: true, originalType: prop.elementType }
          }
        }
        return { type: 'object', isNullable: prop.optional || prop.nullable, originalType: prop.elementType || refName }
      }
      // For types that exist in both local generation and ApiClient, use local type
      // For SDK types that only exist in ApiClient, use ApiClient.TypeName
      // Exception: types from config.ts should be generated locally even if ApiClient has same name
      const isConfigType = typeDef.sourceFile.includes('config.ts')
      const isSdkType = typeDef.sourceFile.includes('sdk/js/src/v2/gen/types.gen.ts')
      
      if (isConfigType) {
        // Always use local type for config.ts types
        return { type: refName, isNullable: prop.optional || prop.nullable, originalType: refName }
      } else if (isSdkType && existingApiTypes.has(refName)) {
        // SDK types that exist in ApiClient: use ApiClient.TypeName
        return { type: 'ApiClient.' + refName, isNullable: prop.optional || prop.nullable, originalType: refName }
      } else if (!isSdkType && existingApiTypes.has(refName)) {
        // Local types (non-SDK) that exist in ApiClient: use local type (priority over ApiClient)
        return { type: refName, isNullable: prop.optional || prop.nullable, originalType: refName }
      }
      return { type: refName, isNullable: prop.optional || prop.nullable }
    }
    // Type not in typeDefinitions but might exist in ApiClient
    if (existingApiTypes.has(refName)) {
      return { type: 'ApiClient.' + refName, isNullable: prop.optional || prop.nullable, originalType: refName }
    }
    return { type: 'object', isNullable: prop.optional || prop.nullable, originalType: refName }
  }
  
  return { type: 'object', isNullable: prop.optional || prop.nullable }
}

/**
 * Recursively collect all types needed by a property
 * 
 * This function traverses the type graph to find all types that need to be generated
 * for a given property. It handles:
 * - Array element types
 * - Union type members
 * - Type references
 * - Type aliases and unions
 * 
 * Uses collectingTypes set to prevent infinite recursion on circular references.
 */
function collectNeededTypes(prop: PropertyDefinition) {
  const baseType = prop.type.toLowerCase()
  
  // Prevent infinite recursion
  if (prop.typeRef?.name && collectingTypes.has(prop.typeRef.name)) {
    return
  }
  
  if (baseType === 'array') {
    if (prop.elementType && !isPrimitiveType(prop.elementType) && !isInternalType(prop.elementType)) {
      if (typeDefinitions.has(prop.elementType)) {
        collectingTypes.add(prop.elementType)
        const elemTypeDef = typeDefinitions.get(prop.elementType)!
        if (elemTypeDef.kind === 'interface' && elemTypeDef.properties) {
          neededTypes.add(prop.elementType)
          for (const p of elemTypeDef.properties) {
            collectNeededTypes(p)
          }
        } else if (elemTypeDef.kind === 'typeAlias' || elemTypeDef.kind === 'union') {
          // Add type aliases and unions to neededTypes
          neededTypes.add(prop.elementType)
        }
        collectingTypes.delete(prop.elementType)
      }
    } else if (prop.typeRef?.name && !isPrimitiveType(prop.typeRef.name) && !isInternalType(prop.typeRef.name)) {
      if (typeDefinitions.has(prop.typeRef.name)) {
        collectingTypes.add(prop.typeRef.name)
        const typeDef = typeDefinitions.get(prop.typeRef.name)!
        if (typeDef.kind === 'interface' && typeDef.properties) {
          neededTypes.add(prop.typeRef.name)
          for (const p of typeDef.properties) {
            collectNeededTypes(p)
          }
        } else if (typeDef.kind === 'typeAlias' || typeDef.kind === 'union') {
          // Add type aliases and unions to neededTypes
          neededTypes.add(prop.typeRef.name)
        }
        collectingTypes.delete(prop.typeRef.name)
      }
    }
    return
  }
  
  // For union types, parse elementType to find referenced types
  if (baseType === 'union' && prop.elementType) {
    const unionParts = prop.elementType.split('|').map(p => p.trim())
    for (const part of unionParts) {
      // Skip null, undefined, string literals, intersection types, and Zod types
      if (part !== 'undefined' && part !== 'null' && !part.startsWith('"') && !part.startsWith("'") && !part.includes('&') && !part.startsWith('$Zod') && !part.startsWith('Zod')) {
        // Only add if the type actually exists in the contract
        if (typeDefinitions.has(part)) {
          collectingTypes.add(part)
          const typeDef = typeDefinitions.get(part)!
          // Add to neededTypes regardless of whether it has properties
          neededTypes.add(part)
          if (typeDef.kind === 'interface' && typeDef.properties) {
            for (const p of typeDef.properties) {
              collectNeededTypes(p)
            }
          }
          collectingTypes.delete(part)
        }
      }
    }
    return
  }
  
  if (prop.typeRef?.name && !isPrimitiveType(prop.typeRef.name) && !isInternalType(prop.typeRef.name)) {
    // Skip Zod internal types
    if (prop.typeRef.name.startsWith('$Zod') || prop.typeRef.name.startsWith('Zod')) {
      return
    }
    
    if (collectingTypes.has(prop.typeRef.name)) {
      return
    }
    
    if (typeDefinitions.has(prop.typeRef.name)) {
      collectingTypes.add(prop.typeRef.name)
      const typeDef = typeDefinitions.get(prop.typeRef.name)!
      if (typeDef.kind === 'interface' && typeDef.properties) {
        neededTypes.add(prop.typeRef.name)
        // Handle extendsBase: collect base type and its properties
        if ((typeDef as any).extendsBase && typeDefinitions.has((typeDef as any).extendsBase)) {
          const baseTypeName = (typeDef as any).extendsBase
          neededTypes.add(baseTypeName)
          const baseTypeDef = typeDefinitions.get(baseTypeName)!
          if (baseTypeDef.kind === 'interface' && baseTypeDef.properties) {
            for (const p of baseTypeDef.properties) {
              collectNeededTypes(p)
            }
          }
        }
        for (const p of typeDef.properties) {
          collectNeededTypes(p)
        }
      } else if (typeDef.kind === 'typeAlias') {
        // Type aliases are added to neededTypes and will be generated as placeholder classes
        neededTypes.add(prop.typeRef.name)
      } else if (typeDef.kind === 'union' && typeDef.unionMembers) {
        // Add the union type itself to neededTypes (for string literal unions like CodeEditDisplay)
        neededTypes.add(prop.typeRef.name)
        for (const memberName of typeDef.unionMembers) {
          if (typeDefinitions.has(memberName)) {
            const memberDef = typeDefinitions.get(memberName)!
            // Generate union members that are interfaces OR type aliases with properties
            if ((memberDef.kind === 'interface' || memberDef.kind === 'typeAlias') && memberDef.properties) {
              neededTypes.add(memberName)
              for (const p of memberDef.properties) {
                collectNeededTypes(p)
              }
            }
          }
        }
      }
      collectingTypes.delete(prop.typeRef.name)
    }
  }
}

/**
 * Generate a C# class for a type definition
 * 
 * ## Generated Code Structure
 * - Auto-generated header comment
 * - #nullable enable directive
 * - Namespace declaration (base namespace for Shared, nested for others)
 * - Using statements (System, Collections, Newtonsoft.Json, ApiClient if needed)
 * - XML documentation comments
 * - Class declaration with inheritance if applicable
 * - Properties with JsonProperty attributes
 * 
 * ## ApiClient Integration
 * - Checks if referenced types exist in ApiClient
 * - Adds using KiloVisualStudioExtension.ApiClient if needed
 * - Uses ApiClient.TypeName for SDK types that exist in ApiClient
 * 
 * @param typeDef - Type definition from contract
 * @param folder - Target folder/namespace for the generated class
 * @returns Generated C# code as string
 */
function generateTypeClass(typeDef: TypeDefinition, folder: string, generatedOrder: string[]): string {
  const sb: string[] = []
  const ns = "KiloExtensionDTOs"
  
  // Collect referenced types from different namespaces
  const referencedNamespaces = new Set<string>()
  let needsApiClientReference = false
  if (typeDef.properties) {
    for (const prop of typeDef.properties) {
      // Get the type name from typeRef or from the type field
      const typeName = prop.typeRef?.name || (!prop.isLiteral && prop.type !== 'string' && prop.type !== 'number' && prop.type !== 'boolean' && prop.type !== 'array' && prop.type !== 'object' ? prop.type : null)
      
      if (typeName) {
        // Only check ApiClient for SDK types (from sdk/js/src/v2/gen/types.gen.ts)
        const refTypeDef = typeDefinitions.get(typeName)
        const isSdkType = refTypeDef?.sourceFile.includes('sdk/js/src/v2/gen/types.gen.ts')
        
        if (isSdkType && existingApiTypes.has(typeName) && !generatedOrder.includes(typeName)) {
          // SDK type exists in ApiClient and is NOT being generated locally - use ApiClient reference
          needsApiClientReference = true
        } else if (refTypeDef && !(isSdkType && existingApiTypes.has(typeName))) {
          // Non-SDK type or SDK type being generated locally - generate reference
          const refFolder = getSourceFileFolder(refTypeDef.sourceFile)
          if (refFolder !== folder) {
            const refNs = refFolder === 'Shared' ? ns : (ns + "." + refFolder)
            referencedNamespaces.add(refNs)
          }
        }
      }
      // Check elementType for union types
      if (prop.elementType) {
        const typeParts = prop.elementType.split('|').map(p => p.trim())
        for (const part of typeParts) {
          if (part && part !== 'undefined' && part !== 'null' && !part.startsWith('"')) {
            const elemTypeDef = typeDefinitions.get(part)
            const isSdkType = elemTypeDef?.sourceFile.includes('sdk/js/src/v2/gen/types.gen.ts')
            
            if (isSdkType && existingApiTypes.has(part) && !generatedOrder.includes(part)) {
              // SDK type exists in ApiClient and is NOT being generated locally - use ApiClient reference
              needsApiClientReference = true
            } else if (elemTypeDef && !(isSdkType && existingApiTypes.has(part))) {
              // Non-SDK type or SDK type being generated locally - generate reference
              const elemFolder = getSourceFileFolder(elemTypeDef.sourceFile)
              if (elemFolder !== folder) {
                const elemNs = elemFolder === 'Shared' ? ns : (ns + "." + elemFolder)
                referencedNamespaces.add(elemNs)
              }
            }
          }
        }
      }
    }
  }
  sb.push("//     This code was generated by WebViewContractGenerator.")
  sb.push("//     Do not modify this file directly as changes will be lost on regeneration.")
  sb.push("// </auto-generated>")
  sb.push("")
  sb.push("#nullable enable")
  sb.push("")
  // Use base namespace for Shared folder, folder namespace for others
  const namespace = folder === 'Shared' ? ns : (ns + "." + folder)
  sb.push("namespace " + namespace + ";")
  sb.push("")
  sb.push("using System;")
  sb.push("using System.Collections.Generic;")
  sb.push("using System.Threading.Tasks;")
  sb.push("using Newtonsoft.Json;")
  if (needsApiClientReference) {
    sb.push("using KiloVisualStudioExtension.ApiClient;")
  }
  for (const refNs of referencedNamespaces) {
    sb.push("using " + refNs + ";")
  }
  
  // Add using statements for interface namespaces
  if (typeDef.signatureHash && hashToUnion.has(typeDef.signatureHash)) {
    const unionName = hashToUnion.get(typeDef.signatureHash)!
    const unionTypeDef = typeDefinitions.get(unionName)
    if (unionTypeDef) {
      const unionFolder = getSourceFileFolder(unionTypeDef.sourceFile)
      if (unionFolder !== folder) {
        const interfaceNs = unionFolder === 'Shared' ? ns : (ns + "." + unionFolder)
        sb.push("using " + interfaceNs + ";")
      }
    }
  }
  
  sb.push("")
  
  // Add source information comment
  const isNodeModules = typeDef.sourceFile.includes('node_modules')
  let sourceComment = ""
  if (isNodeModules) {
    const normalizedSource = typeDef.sourceFile.replace(/\\/g, '/')
    if (normalizedSource.includes('node_modules/typescript')) {
      sourceComment = "/// <remarks>\n/// This type is from TypeScript lib definitions (node_modules/typescript).\n/// It is included because it is referenced by a message property.\n/// </remarks>\n"
    } else if (normalizedSource.includes('node_modules/@types/node')) {
      sourceComment = "/// <remarks>\n/// This type is from Node.js type definitions (node_modules/@types/node).\n/// It is included because it is referenced by a message property.\n/// </remarks>\n"
    } else if (normalizedSource.includes('node_modules/@types')) {
      sourceComment = "/// <remarks>\n/// This type is from @types package (node_modules/@types).\n/// It is included because it is referenced by a message property.\n/// </remarks>\n"
    } else {
      sourceComment = "/// <remarks>\n/// This type is from node_modules.\n/// It is included because it is referenced by a message property.\n/// </remarks>\n"
    }
  }
  
  // Generate enum for enum kinds
  if (typeDef.kind === 'enum' && typeDef.unionMembers) {
    sb.push("/// <summary>")
    sb.push(`/// Enum: ${typeDef.name}`)
    sb.push(`/// Source: ${typeDef.sourceFile}`)
    sb.push("/// </summary>")
    if (sourceComment) sb.push(sourceComment)
    sb.push(`public enum ${typeDef.name}`)
    sb.push("{")
    for (let i = 0; i < typeDef.unionMembers.length; i++) {
      const member = typeDef.unionMembers[i]!
      const enumName = pascalCase(member)
      const comma = i < typeDef.unionMembers.length - 1 ? "," : ""
      sb.push(`    ${enumName}${comma}`)
    }
    sb.push("}")
    sb.push("")
    return sb.join("\n")
  }
  
  if (typeDef.discriminator) {
    sb.push("/// <summary>")
    sb.push(`/// Part type: ${typeDef.name}`)
    sb.push(`/// Discriminator: ${typeDef.discriminator.field} = "${typeDef.discriminator.value}"`)
    sb.push(`/// Source: ${typeDef.sourceFile}`)
    if (typeDef.signatureHash) {
      sb.push(`/// Signature hash: ${typeDef.signatureHash}`)
    }
    sb.push("/// </summary>")
    if (sourceComment) sb.push(sourceComment)
  } else {
    sb.push("/// <summary>")
    sb.push(`/// Type: ${typeDef.name}`)
    sb.push(`/// Source: ${typeDef.sourceFile}`)
    if (typeDef.signatureHash) {
      sb.push(`/// Signature hash: ${typeDef.signatureHash}`)
    }
    sb.push("/// </summary>")
    if (sourceComment) sb.push(sourceComment)
  }
  
  // Handle inheritance for Partial<T> & Pick<T, ...> pattern AND explicit extends
  const baseClassName = typeDef.extendsBase || typeDef.baseType
  const className = pascalCase(typeDef.name)
  
  // Check if this class implements any discriminated union interfaces
  const implementedInterfaces: string[] = []
  if (typeDef.signatureHash && hashToUnion.has(typeDef.signatureHash)) {
    const unionName = hashToUnion.get(typeDef.signatureHash)!
    implementedInterfaces.push(`I${pascalCase(unionName)}`)
  }
  
  // Determine if this is a Request message (based on discriminator value or class name)
  const isRequestMessage = className.toLowerCase().includes("request") || 
    (typeDef.discriminator && typeDef.discriminator.value.toLowerCase().includes("request"))
  
  // Add base interface if not already present
  const hasRequestInterface = implementedInterfaces.some(i => i === "IWebviewMessageRequest")
  const hasMessageInterface = implementedInterfaces.some(i => i === "IWebviewMessage")
  
  if (!hasRequestInterface) {
    if (isRequestMessage && !hasMessageInterface) {
      implementedInterfaces.push("IWebviewMessageRequest")
    } else if (!isRequestMessage && !hasMessageInterface) {
      implementedInterfaces.push("IWebviewMessage")
    }
  }
  
  // Deduplicate interfaces
  const uniqueInterfaces = [...new Set(implementedInterfaces)]
  
  // Build class declaration with inheritance and interfaces
  if (baseClassName) {
    sb.push(`public partial class ${className} : ${pascalCase(baseClassName)}, ${uniqueInterfaces.join(', ')}`)
  } else {
    sb.push(`public partial class ${className} : ${uniqueInterfaces.join(', ')}`)
  }
  sb.push("{")

  if (typeDef.properties) {
    // Get base class property names to skip (for extendsBase inheritance)
    const basePropNames = new Set<string>()
    if (typeDef.extendsBase && typeDefinitions.has(typeDef.extendsBase)) {
      const baseDef = typeDefinitions.get(typeDef.extendsBase)!
      if (baseDef.properties) {
        for (const baseProp of baseDef.properties) {
          basePropNames.add(baseProp.name)
        }
      }
    }
    
    for (const prop of typeDef.properties) {
      // Skip properties that are in the base type - they're inherited
      // For extendsBase, skip if property exists in base class
      // For baseType (Partial<T> & Pick<T, ...>), use requiredFields to determine what to skip
      if (basePropNames.has(prop.name)) {
        continue
      }
      if (typeDef.baseType && typeDef.requiredFields && !typeDef.requiredFields.includes(prop.name)) {
        continue
      }
      const mapped = mapToCSharpType(prop)
      const nullable = mapped.isNullable ? "?" : ""
      const jsonAttr = `    [JsonProperty("${prop.name}"${mapped.isNullable ? ", NullValueHandling = NullValueHandling.Ignore" : ""})]\n`
      const summary = prop.description ? `    /// <summary>${prop.description}</summary>\n` : ""
      const comment = mapped.originalType ? `    // Original TypeScript type: ${mapped.originalType}\n` : ""
      
      // For discriminator properties, add a default value via field initializer
      // Note: We use { get; set; } instead of { get; init; } for .NET Framework 4.8.1 compatibility
      let propertyDecl: string
      if (typeDef.discriminator && prop.name === typeDef.discriminator.field && typeDef.discriminator.value) {
        // Discriminator property with default value
        const literalValue = typeof typeDef.discriminator.value === 'string' 
          ? `"${typeDef.discriminator.value}"` 
          : String(typeDef.discriminator.value)
        propertyDecl = `    public ${mapped.type} ${pascalCase(prop.name)} { get; set; } = ${literalValue};`
      } else {
        propertyDecl = `    public ${mapped.type}${nullable} ${pascalCase(prop.name)} { get; set; }`
      }
      
      sb.push(jsonAttr + comment + summary + propertyDecl)
    }
  }

  // Generate nested types (inline object types) as inner classes
  // Only generate as nested if the type is NOT already generated as a standalone file
  const nestedTypes = Array.from(typeDefinitions.values()).filter(td => 
    td.kind === 'interface' && 
    td.properties &&
    td.name !== typeDef.name &&
    (typeDef.properties?.some(p => p.typeRef?.name === td.name || p.elementType === td.name) || false)
  )

  for (const nestedType of nestedTypes) {
    // Check if this type is already generated as a standalone file
    const nestedFolder = getSourceFileFolder(nestedType.sourceFile)
    const isStandaloneType = neededTypes.has(nestedType.name) || generatedTypes.has(nestedType.name)
    
    // Skip nested generation if this type is already generated standalone
    if (isStandaloneType) {
      continue
    }
    
    const nestedClassName = pascalCase(nestedType.name)
    sb.push("    /// <summary>")
    sb.push(`    /// Nested type: ${nestedType.name}`)
    sb.push("    /// </summary>")
    sb.push("    public partial class " + nestedClassName)
    sb.push("    {")
    
    if (nestedType.properties) {
      for (const prop of nestedType.properties) {
        const mapped = mapToCSharpType(prop)
        const nullable = mapped.isNullable ? "?" : ""
        const jsonAttr = `        [JsonProperty("${prop.name}"${mapped.isNullable ? ", NullValueHandling = NullValueHandling.Ignore" : ""})]\n`
        const comment = mapped.originalType ? `        // Original TypeScript type: ${mapped.originalType}\n` : ""
        sb.push(jsonAttr + comment + `        public ${mapped.type}${nullable} ${pascalCase(prop.name)} { get; set; }`)
      }
    }
    
    sb.push("    }")
    sb.push("")
  }

  sb.push("}")
  sb.push("")

  return sb.join("\n")
}

/**
 * Determine the output folder based on the TypeScript source file path
 * 
 * ## Folder Mapping Rules
 * - src/shared/* → Shared
 * - sdk/js/src/v2/gen/types.gen.ts → Shared (SDK types)
 * - extension-messages.ts → ExtensionMessages
 * - webview-messages.ts → WebviewMessages
 * - marketplace.ts → Shared
 * - config.ts → KiloConfig (special handling to avoid ApiClient conflict)
 * - *.ts → PascalCase folder name (e.g., agent-manager.ts → AgentManager)
 * 
 * @param sourceFile - Original TypeScript source file path
 * @returns Target folder name
 */
function getSourceFileFolder(sourceFile: string): string {
  const normalizedSource = sourceFile.replace(/\\/g, '/')
  
  // Types from src/shared or SDK go in the Shared folder
  if (normalizedSource.includes('src/shared') || 
      normalizedSource.includes('sdk/js/src/v2/gen/types.gen.ts') ||
      normalizedSource === 'Shared/types.gen.ts' ||
      normalizedSource.includes('Shared/types.gen.ts')) {
    return 'Shared'
  }
  
  // Extract filename from path
  const filename = normalizedSource.split('/').pop() || ''
  
  // Special handling for extension-messages.ts and webview-messages.ts
  if (filename === 'extension-messages.ts') return 'ExtensionMessages'
  if (filename === 'webview-messages.ts') return 'WebviewMessages'
  
  // marketplace.ts goes to Shared folder (not a message file)
  if (filename === 'marketplace.ts') return 'Shared'
  
  // config.ts uses KiloConfig to avoid namespace conflict with Config type from ApiClient
  if (filename === 'config.ts') return 'KiloConfig'
  
  // Derive folder name from filename with PascalCase (e.g., agent-manager.ts → AgentManager)
  if (filename.endsWith('.ts')) {
    const baseName = filename.slice(0, -3) // Remove .ts
    const camelCase = baseName.replace(/-([a-z])/g, (match) => match.charAt(1).toUpperCase())
    return camelCase.charAt(0).toUpperCase() + camelCase.slice(1)
  }
  
  return 'Shared'
}

/**
 * Generate a C# class for a WebView message
 * 
 * ## Message Class Structure
 * - Auto-generated header
 * - Discriminator property with default value (e.g., `public string Type { get; set; } = "configLoaded";`)
 * - Other properties with JsonProperty attributes
 * - XML documentation with discriminator information
 * 
 * ## ApiClient Integration
 * - Checks if any property references a type that exists in ApiClient
 * - Adds using KiloVisualStudioExtension.ApiClient if needed
 * - Uses ApiClient.TypeName for SDK types and local types (except config.ts)
 * 
 * @param message - Message definition from contract
 * @param ns - Base namespace
 * @param folder - Target folder for the message
 * @returns Generated C# code as string
 */
function generateMessageClass(message: MessageType, ns: string, folder: string): string {
  const sb: string[] = []
  
  // Collect referenced types from different namespaces
  const referencedNamespaces = new Set<string>()
  let needsApiClientReference = false
  for (const prop of message.properties) {
    // Check typeRef first
    if (prop.typeRef?.name) {
      const refTypeDef = typeDefinitions.get(prop.typeRef.name)
      // Check if type exists in ApiClient
      const existsInApiClient = existingApiTypes.has(prop.typeRef.name)
      const isSdkType = refTypeDef?.sourceFile.includes('sdk/js/src/v2/gen/types.gen.ts')
      const isConfigType = refTypeDef?.sourceFile.includes('config.ts')
      
      // Use ApiClient reference only for SDK types that don't have a local definition
      // Local types (including config.ts) take priority over ApiClient
      if (existsInApiClient && isSdkType && !refTypeDef) {
        // SDK type exists in ApiClient but not locally - use ApiClient reference
        needsApiClientReference = true
      } else if (refTypeDef) {
        // Local type exists - use local reference (even if ApiClient has same name)
        const refFolder = getSourceFileFolder(refTypeDef.sourceFile)
        if (refFolder !== folder) {
          const refNs = refFolder === 'Shared' ? ns : (ns + "." + refFolder)
          referencedNamespaces.add(refNs)
        }
      }
    }
    // Also check elementType for union types like "undefined | ServerInfo"
    if (prop.elementType) {
      const typeParts = prop.elementType.split('|').map(p => p.trim())
      for (const part of typeParts) {
        if (part && part !== 'undefined' && part !== 'null' && !part.startsWith('"')) {
          const refTypeDef = typeDefinitions.get(part)
          const existsInApiClient = existingApiTypes.has(part)
          const isSdkType = refTypeDef?.sourceFile.includes('sdk/js/src/v2/gen/types.gen.ts')
          
          // Use ApiClient reference only for SDK types that don't have a local definition
          if (existsInApiClient && isSdkType && !refTypeDef) {
            needsApiClientReference = true
          } else if (refTypeDef) {
            // Local type exists - use local reference
            const refFolder = getSourceFileFolder(refTypeDef.sourceFile)
            if (refFolder !== folder) {
              const refNs = refFolder === 'Shared' ? ns : (ns + "." + refFolder)
              referencedNamespaces.add(refNs)
            }
          }
        }
      }
    }
  }
  
  // Check for inheritance relationship and add base type namespace if needed
  const messageTypeDef = typeDefinitions.get(message.name)
  if (messageTypeDef?.extendsBase && typeDefinitions.has(messageTypeDef.extendsBase)) {
    const baseTypeDef = typeDefinitions.get(messageTypeDef.extendsBase)!
    const baseFolder = getSourceFileFolder(baseTypeDef.sourceFile)
    if (baseFolder !== folder) {
      const baseNs = baseFolder === 'Shared' ? ns : (ns + "." + baseFolder)
      referencedNamespaces.add(baseNs)
    }
  }
  
  sb.push("// <auto-generated>")
  sb.push("//     This code was generated by WebViewContractGenerator.")
  sb.push("//     Do not modify this file directly as changes will be lost on regeneration.")
  sb.push("//     Source: WebViewContract.json schema version " + contract.schemaVersion)
  sb.push("// </auto-generated>")
  sb.push("")
  sb.push("#nullable enable")
  sb.push("")
  sb.push("namespace " + ns + "." + folder + ";")
  sb.push("")
  sb.push("using System;")
  sb.push("using System.Collections.Generic;")
  sb.push("using System.Threading.Tasks;")
  sb.push("using Newtonsoft.Json;")
  if (needsApiClientReference) {
    sb.push("using KiloVisualStudioExtension.ApiClient;")
  }
  for (const refNs of referencedNamespaces) {
    sb.push("using " + refNs + ";")
  }
  
  // Add using statements for interface namespaces
  if (messageTypeDef?.signatureHash && hashToUnion.has(messageTypeDef.signatureHash)) {
    const unionName = hashToUnion.get(messageTypeDef.signatureHash)!
    const unionTypeDef = typeDefinitions.get(unionName)
    if (unionTypeDef) {
      const unionFolder = getSourceFileFolder(unionTypeDef.sourceFile)
      if (unionFolder !== folder) {
        const interfaceNs = unionFolder === 'Shared' ? ns : (ns + "." + unionFolder)
        sb.push("using " + interfaceNs + ";")
      }
    }
  }
  
  sb.push("")
  sb.push("/// <summary>")
  sb.push("/// WebView message: " + message.name)
  sb.push("/// Discriminator: " + message.discriminator.field + " = \"" + message.discriminator.value + "\"")
  sb.push("/// Source: " + message.sourceFile)
  // Get signature hash from type definition if available
  if (messageTypeDef?.signatureHash) {
    sb.push("/// Signature hash: " + messageTypeDef.signatureHash)
  }
  sb.push("/// </summary>")
  const sanitizedName = pascalCase(message.name)
  
  // Check if this message implements any union interfaces
  const implementedInterfaces: string[] = []
  if (messageTypeDef?.signatureHash && hashToUnion.has(messageTypeDef.signatureHash)) {
    const unionName = hashToUnion.get(messageTypeDef.signatureHash)!
    const interfaceName = `I${pascalCase(unionName)}`
    implementedInterfaces.push(interfaceName)
  }
  
  // Determine if this is a Request message (from webview to extension)
  // Webview-to-extension messages are requests, so they inherit IWebviewMessageRequest
  const isRequestMessage = contract.messages.webviewToExtension.includes(message)
  
  const baseClassName = messageTypeDef?.extendsBase || messageTypeDef?.baseType
  if (baseClassName) {
    if (implementedInterfaces.length > 0) {
      sb.push("public partial class " + sanitizedName + " : " + pascalCase(baseClassName) + ", " + implementedInterfaces.join(', '))
    } else {
      const baseInterface = isRequestMessage ? "IWebviewMessageRequest" : "IWebviewMessage"
      sb.push("public partial class " + sanitizedName + " : " + pascalCase(baseClassName) + ", " + baseInterface)
    }
  } else {
    if (implementedInterfaces.length > 0) {
      // Check if any implemented interface already inherits from IWebviewMessage or IWebviewMessageRequest
      const hasWebviewMessageInterface = implementedInterfaces.some(i => 
        i === "IWebviewMessage" || i === "IWebviewMessageRequest" || 
        i === "IExtensionMessage"  // IExtensionMessage inherits IWebviewMessage
      )
      if (hasWebviewMessageInterface) {
        sb.push("public partial class " + sanitizedName + " : " + implementedInterfaces.join(', '))
      } else {
        const baseInterface = isRequestMessage ? "IWebviewMessageRequest" : "IWebviewMessage"
        sb.push("public partial class " + sanitizedName + " : " + implementedInterfaces.join(', ') + ", " + baseInterface)
      }
    } else {
      const baseInterface = isRequestMessage ? "IWebviewMessageRequest" : "IWebviewMessage"
      sb.push("public partial class " + sanitizedName + " : " + baseInterface)
    }
  }
  sb.push("{")

  // Get base class property names to skip (for extendsBase inheritance)
  const basePropNames = new Set<string>()
  if (messageTypeDef?.extendsBase && typeDefinitions.has(messageTypeDef.extendsBase)) {
    const baseDef = typeDefinitions.get(messageTypeDef.extendsBase)!
    if (baseDef.properties) {
      for (const baseProp of baseDef.properties) {
        basePropNames.add(baseProp.name)
      }
    }
  }

  for (const prop of message.properties) {
    // Skip properties that are in the base type - they're inherited
    if (basePropNames.has(prop.name)) {
      continue
    }
    const mapped = mapToCSharpType(prop)
    const nullable = mapped.isNullable ? "?" : ""
    const jsonAttr = `    [JsonProperty("${prop.name}"${mapped.isNullable ? ", NullValueHandling = NullValueHandling.Ignore" : ""})]\n`
    const comment = mapped.originalType ? "    // Original TypeScript type: " + mapped.originalType + "\n" : ""
    
    // For discriminator properties, add a default value via field initializer
    // Note: We use { get; set; } instead of { get; init; } for .NET Framework 4.8.1 compatibility
    let propertyDecl: string
    if (message.discriminator && prop.name === message.discriminator.field && message.discriminator.value) {
      // Discriminator property with default value
      const literalValue = typeof message.discriminator.value === 'string' 
        ? `"${message.discriminator.value}"` 
        : String(message.discriminator.value)
      propertyDecl = `    public ${mapped.type} ${pascalCase(prop.name)} { get; set; } = ${literalValue};`
    } else {
      propertyDecl = `    public ${mapped.type}${nullable} ${pascalCase(prop.name)} { get; set; }`
    }
    
    sb.push(jsonAttr + comment + propertyDecl)
  }

  sb.push("}")
  sb.push("")

  return sb.join("\n")
}

/**
 * Generate the WebViewMessageFactory class for discriminator-based deserialization
 * 
 * ## Factory Pattern
 * The generated factory provides two methods:
 * - `Deserialize<T>(JToken token)`: Deserialize from JToken using discriminator
 * - `Deserialize<T>(string json)`: Deserialize from JSON string
 * 
 * ## Discriminator-Based Routing
 * Uses C# switch expression to route to the correct type based on the "type" field:
 * ```csharp
 * return type switch
 * {
 *     "configLoaded" => typeof(T) == typeof(ConfigLoadedMessage) 
 *         ? (T)(object)token.ToObject<ConfigLoadedMessage>(Serializer)! 
 *         : throw new JsonSerializationException("Type mismatch"),
 *     // ... more cases
 *     _ => throw new JsonSerializationException("Unknown message type: " + type)
 * };
 * ```
 * 
 * ## Design Decisions
 * - Uses explicit discriminator checking instead of JsonConverter inheritance
 * - Reuses KiloJsonSerializer from ApiClient (PORT-INFRA-003)
 * - Avoids issues with nullable annotations and generic converters
 * 
 * @param webviewToExt - Messages from webview to extension
 * @param extToWebview - Messages from extension to webview
 * @param ns - Base namespace
 * @returns Generated C# code as string
 */
function generateDiscriminatorFactory(
  webviewToExt: MessageType[], 
  extToWebview: MessageType[], 
  ns: string
): string {
  const sb: string[] = []

  sb.push("// <auto-generated>")
  sb.push("//     This code was generated by WebViewContractGenerator.")
  sb.push("//     Do not modify this file directly as changes will be lost on regeneration.")
  sb.push("// </auto-generated>")
  sb.push("")
  sb.push("#nullable enable")
  sb.push("")
  sb.push("namespace " + ns + ";")
  sb.push("")
  sb.push("using System;")
  sb.push("using Newtonsoft.Json;")
  sb.push("using Newtonsoft.Json.Linq;")
  sb.push("")
  sb.push("using Common;")
  
  // Add using statements for all message namespaces
  const namespaces = new Set<string>()
  for (const message of [...webviewToExt, ...extToWebview]) {
    if (!message.sourceFile.includes('node_modules')) {
      const folder = getSourceFileFolder(message.sourceFile)
      namespaces.add(ns + "." + folder)
    }
  }
  for (const namespace of namespaces) {
    sb.push("using " + namespace + ";")
  }
  sb.push("")
  sb.push("using Common;")
  sb.push("")
  sb.push("/// <summary>")
  sb.push("/// Discriminator-based deserialization factory for WebView messages.")
  sb.push("/// Uses explicit discriminator checking instead of JsonConverter inheritance.")
  sb.push("/// Reuses KiloJsonSerializer configuration from PORT-INFRA-003.")
  sb.push("/// </summary>")
  sb.push("public static class WebViewMessageFactory")
  sb.push("{")
  sb.push("    private static readonly JsonSerializer Serializer = KiloJsonSerializer.Create();")
  sb.push("")
  sb.push("    /// <summary>")
  sb.push("    /// Deserialize a WebView message from JSON using discriminator-based routing.")
  sb.push("    /// Returns the deserialized message implementing IWebviewMessage.")
  sb.push("    /// </summary>")
  sb.push("    public static IWebviewMessage Deserialize(JToken token)")
  sb.push("    {")
  sb.push("        var type = token[\"type\"]?.Value<string>();")
  sb.push("")
  sb.push("        return type switch")
  sb.push("        {")

  const seenDiscriminatorsNonGeneric = new Set<string>()
  const casesNonGeneric: string[] = []
  
  for (const message of webviewToExt) {
    if (message.sourceFile.includes('node_modules')) {
      continue
    }
    const discValue = message.discriminator.value
    const sanitizedName = pascalCase(message.name)
    if (!seenDiscriminatorsNonGeneric.has(discValue)) {
      casesNonGeneric.push("            \"" + discValue + "\" => (IWebviewMessage)(object)token.ToObject<" + sanitizedName + ">(Serializer)!,")
      seenDiscriminatorsNonGeneric.add(discValue)
    }
  }
  
  for (const message of extToWebview) {
    if (message.sourceFile.includes('node_modules')) {
      continue
    }
    const discValue = message.discriminator.value
    const sanitizedName = pascalCase(message.name)
    if (!seenDiscriminatorsNonGeneric.has(discValue)) {
      casesNonGeneric.push("            \"" + discValue + "\" => (IWebviewMessage)(object)token.ToObject<" + sanitizedName + ">(Serializer)!,")
      seenDiscriminatorsNonGeneric.add(discValue)
    }
  }
  
  sb.push(casesNonGeneric.join("\n"))
  sb.push("            _ => throw new JsonSerializationException(\"Unknown message type: \" + type)")
  sb.push("        };")
  sb.push("    }")
  sb.push("")
  sb.push("    /// <summary>")
  sb.push("    /// Deserialize a WebView message from JSON using discriminator-based routing.")
  sb.push("    /// </summary>")
  sb.push("    public static T Deserialize<T>(JToken token) where T : IWebviewMessage")
  sb.push("    {")
  sb.push("        var type = token[\"type\"]?.Value<string>();")
  sb.push("")
  sb.push("        return type switch")
  sb.push("        {")

  const seenDiscriminatorsTyped = new Set<string>()
  const casesTyped: string[] = []
  
  for (const message of webviewToExt) {
    if (message.sourceFile.includes('node_modules')) {
      continue
    }
    const discValue = message.discriminator.value
    const sanitizedName = pascalCase(message.name)
    if (!seenDiscriminatorsTyped.has(discValue)) {
      casesTyped.push("            \"" + discValue + "\" => typeof(T) == typeof(" + sanitizedName + ") ? (T)(object)token.ToObject<" + sanitizedName + ">(Serializer)! : throw new JsonSerializationException(\"Type mismatch\"),")
      seenDiscriminatorsTyped.add(discValue)
    }
  }
  
  for (const message of extToWebview) {
    if (message.sourceFile.includes('node_modules')) {
      continue
    }
    const discValue = message.discriminator.value
    const sanitizedName = pascalCase(message.name)
    if (!seenDiscriminatorsTyped.has(discValue)) {
      casesTyped.push("            \"" + discValue + "\" => typeof(T) == typeof(" + sanitizedName + ") ? (T)(object)token.ToObject<" + sanitizedName + ">(Serializer)! : throw new JsonSerializationException(\"Type mismatch\"),")
      seenDiscriminatorsTyped.add(discValue)
    }
  }
  
  sb.push(casesTyped.join("\n"))
  sb.push("            _ => throw new JsonSerializationException(\"Unknown message type: \" + type)")
  sb.push("        };")
  sb.push("    }")
  sb.push("")
  sb.push("    /// <summary>")
  sb.push("    /// Deserialize a WebView message from JSON string using discriminator-based routing.")
  sb.push("    /// </summary>")
  sb.push("    public static IWebviewMessage Deserialize(string json)")
  sb.push("    {")
  sb.push("        var token = JToken.Parse(json);")
  sb.push("        return Deserialize(token);")
  sb.push("    }")
  sb.push("")
  sb.push("    /// <summary>")
  sb.push("    /// Deserialize a WebView message from JSON string using discriminator-based routing.")
  sb.push("    /// </summary>")
  sb.push("    public static T Deserialize<T>(string json) where T : IWebviewMessage")
  sb.push("    {")
  sb.push("        var token = JToken.Parse(json);")
  sb.push("        return Deserialize<T>(token);")
  sb.push("    }")
  sb.push("}")
  sb.push("")

  return sb.join("\n")
}

console.log("WebView DTO Generator (Bun version)")
console.log("===================================")
console.log()

if (!fs.existsSync(CONTRACT_PATH)) {
  console.error(`Error: Contract file not found: ${CONTRACT_PATH}`)
  process.exit(1)
}

console.log(`Reading contract from: ${CONTRACT_PATH}`)
const contractJson = fs.readFileSync(CONTRACT_PATH, "utf-8")
contract = JSON.parse(contractJson)

typeDefinitions = new Map()
for (const typeDef of contract.types) {
  typeDefinitions.set(typeDef.name, typeDef)
}

neededTypes = new Set()
generatedTypes = new Set()
collectingTypes = new Set()
generatedEnums = new Map()
inlineEnumRegistry = new Map()
generatedInlineEnumSignatures = new Set()

console.log(`Contract version: ${contract.schemaVersion}`)
console.log(`Total types in contract: ${contract.types.length}`)
console.log(`WebView→Extension messages: ${contract.messages.webviewToExtension.length}`)
console.log(`Extension→WebView messages: ${contract.messages.extensionToWebview.length}`)
console.log()

// Create namespace-based folder structure with PascalCase folder names
const DIRECTORY_MAP: Record<string, string> = {
  'Shared': path.join(OUTPUT_PATH, "Shared"),
  'Connection': path.join(OUTPUT_PATH, "Connection"),
  'Parts': path.join(OUTPUT_PATH, "Parts"),
  'Sessions': path.join(OUTPUT_PATH, "Sessions"),
  'Permissions': path.join(OUTPUT_PATH, "Permissions"),
  'Questions': path.join(OUTPUT_PATH, "Questions"),
  'Providers': path.join(OUTPUT_PATH, "Providers"),
  'Agents': path.join(OUTPUT_PATH, "Agents"),
  'KiloConfig': path.join(OUTPUT_PATH, "KiloConfig"),
  'Profile': path.join(OUTPUT_PATH, "Profile"),
  'AgentManager': path.join(OUTPUT_PATH, "AgentManager"),
  'Migration': path.join(OUTPUT_PATH, "Migration"),
  'Memory': path.join(OUTPUT_PATH, "Memory"),
  'ExtensionMessages': path.join(OUTPUT_PATH, "ExtensionMessages"),
  'WebviewMessages': path.join(OUTPUT_PATH, "WebviewMessages"),
}

function getDirectoryForFolder(folder: string): string {
  return DIRECTORY_MAP[folder] || DIRECTORY_MAP['Shared']
}

const ns = "KiloExtensionDTOs"

// ============================================================================
// MAIN GENERATION FLOW
// ============================================================================
// 1. Create output directories
// 2. Collect all types needed by messages (transitive dependencies)
// 3. Generate enums for string literal unions
// 4. Generate empty interfaces for discriminated unions
// 5. Generate type classes with interface implementations
// 6. Generate message classes with interface implementations
// 7. Generate discriminator factory
// ============================================================================

// Create all directories
for (const dir of Object.values(DIRECTORY_MAP)) {
  fs.mkdirSync(dir, { recursive: true })
}
const sharedDir = DIRECTORY_MAP['Shared']

// Collect message names to avoid generating them as types
const messageNames = new Set<string>()
for (const message of contract.messages.webviewToExtension) {
  messageNames.add(message.name)
}
for (const message of contract.messages.extensionToWebview) {
  messageNames.add(message.name)
}

console.log("Collecting types needed by messages...")

for (const message of contract.messages.webviewToExtension) {
  // Handle extendsBase for messages (check both message and typeDefinitions)
  const extendsBase = (message as any).extendsBase || typeDefinitions.get(message.name)?.extendsBase
  if (extendsBase && typeDefinitions.has(extendsBase)) {
    const baseTypeName = extendsBase
    neededTypes.add(baseTypeName)
    const baseTypeDef = typeDefinitions.get(baseTypeName)!
    if (baseTypeDef.kind === 'interface' && baseTypeDef.properties) {
      for (const p of baseTypeDef.properties) {
        collectNeededTypes(p)
      }
    }
  }
  for (const prop of message.properties) {
    collectNeededTypes(prop)
  }
}

for (const message of contract.messages.extensionToWebview) {
  // Handle extendsBase for messages (check both message and typeDefinitions)
  const extendsBase = (message as any).extendsBase || typeDefinitions.get(message.name)?.extendsBase
  if (extendsBase && typeDefinitions.has(extendsBase)) {
    const baseTypeName = extendsBase
    neededTypes.add(baseTypeName)
    const baseTypeDef = typeDefinitions.get(baseTypeName)!
    if (baseTypeDef.kind === 'interface' && baseTypeDef.properties) {
      for (const p of baseTypeDef.properties) {
        collectNeededTypes(p)
      }
    }
  }
  for (const prop of message.properties) {
    collectNeededTypes(prop)
  }
}

console.log(`Types to generate: ${neededTypes.size}`)
console.log()

console.log()
console.log("Generating type definitions...")

// Pre-processing pass: detect all inline string literal unions
console.log("Scanning for inline string literal unions...")
for (const [typeName, typeDef] of typeDefinitions) {
  if (typeDef.kind !== 'interface' || !typeDef.properties) continue
  
  for (const prop of typeDef.properties) {
    if (prop.elementType && prop.type.toLowerCase() === 'union') {
      const unionParts = parseUnionMembers(prop.elementType)
      const nonNullParts = unionParts.filter(p => p !== 'undefined' && p !== 'null')
      
      if (nonNullParts.length > 1 && nonNullParts.every(p => isStringLiteral(p))) {
        const literalValues = nonNullParts.map(p => getStringLiteralValue(p))
        registerInlineEnum(literalValues, typeName)
      }
    }
  }
}

if (inlineEnumRegistry.size > 0) {
  console.log(`Found ${inlineEnumRegistry.size} inline enum signatures`)
  for (const [sig, info] of inlineEnumRegistry) {
    const members = sig.split(',')
    const enumName = info.enumName
    console.log(`  ${enumName}: ${members.join(', ')} (used by ${info.usageCount} type(s))`)
  }
}
console.log()

// First, collect all unions with member hashes (discriminated or not)
const unionsWithHashes = Array.from(typeDefinitions.values()).filter(
  t => t.kind === 'union' && t.unionMemberHashes && t.unionMemberHashes.length > 0
)

// Build a map: memberHash -> unionName for quick lookup
const hashToUnion = new Map<string, string>()
for (const union of unionsWithHashes) {
  if (union.unionMemberHashes) {
    for (const hash of union.unionMemberHashes) {
      hashToUnion.set(hash, union.name)
    }
  }
}

// Generate empty interfaces for unions with member hashes
console.log("Generating empty interfaces for unions with member hashes...")
console.log(`  Found ${unionsWithHashes.length} unions with member hashes`)
for (const union of unionsWithHashes) {
  // Skip WebviewMessage union - IWebviewMessage is defined manually in Shared/IWebviewMessage.cs
  if (union.name === "WebviewMessage") {
    console.log(`  Skipping union: ${union.name} (IWebviewMessage defined manually)`)
    continue
  }
  console.log(`  Processing union: ${union.name} with ${union.unionMemberHashes?.length ?? 0} members`)
  const unionName = union.name
  const unionFolder = getSourceFileFolder(union.sourceFile)
  const namespace = unionFolder === 'Shared' ? ns : (ns + "." + unionFolder)
  const interfaceName = `I${unionName}`
  
  // Determine base interface: WebviewMessage unions inherit IWebviewMessage, Request unions inherit IWebviewMessageRequest
  const isRequestUnion = unionName.toLowerCase().includes("request")
  const baseInterface = isRequestUnion ? ": IWebviewMessageRequest" : ": IWebviewMessage"

  const interfaceCode = `// <auto-generated>
//     This code was generated by WebViewContractGenerator.
//     Do not modify this file directly as changes will be lost on regeneration.
//     Source: WebViewContract.json schema version ${contract.schemaVersion}
// </auto-generated>

#nullable enable

namespace ${namespace};

/// <summary>
/// Union Marker: ${unionName}
/// Member count: ${union.unionMemberHashes?.length ?? 0}
/// Source: ${union.sourceFile}
/// </summary>
public interface ${interfaceName}${baseInterface}
{
}
`
  const targetDir = getDirectoryForFolder(unionFolder)
  const filePath = path.join(targetDir, `${interfaceName}.cs`)
  fs.writeFileSync(filePath, interfaceCode)
  console.log(`  Generated interface: ${interfaceName} (${unionFolder})`)
}

console.log()

// First, generate enums for all union type aliases from message source files
// This ensures that types like DeviceAuthStatus are generated even if not directly referenced
console.log("Generating enums from union type aliases...")
for (const [typeName, typeDef] of typeDefinitions) {
  if (typeDef.kind === 'union' && typeDef.unionMembers) {
    // Skip union types with no members (indicates members couldn't be resolved from external imports)
    if (typeDef.unionMembers.length === 0) {
      console.log(`  Skipping ${typeName}: no union members (external types not resolved)`)
      continue
    }
    
    // Check if all members are string literals (enum-able)
    const allLiterals = typeDef.unionMembers.every(m => 
      m.startsWith('"') || m.startsWith("'")
    )
    if (allLiterals) {
      // This is a string literal union - generate as enum
      // Add "Enum" suffix to match inline enum naming convention
      const enumName = typeName.endsWith("Enum") ? typeName : (typeName + "Enum")
      const enumFolder = getSourceFileFolder(typeDef.sourceFile)
      
      const literalValues = typeDef.unionMembers.map(v => {
        if (v.startsWith('"') || v.startsWith("'")) {
          return v.slice(1, -1)
        }
        return v
      })
      
      // For named union type aliases (like TerminalCommandDisplay, CodeEditDisplay),
      // generate them with their own names (with Enum suffix) even if signature matches an inline enum
      // Inline enums are generated from properties, but named type aliases need their own files
      const signature = generateInlineEnumSignature(literalValues)
      const existingInlineEnum = inlineEnumRegistry.get(signature)
      if (existingInlineEnum && existingInlineEnum.enumName === enumName) {
        // This is the inline enum itself, skip (will be generated later with proper attributes)
        continue
      }
      
      // Check if enum already exists
      if (!generatedEnums.has(enumName)) {
        const enumDef: EnumDefinition = { name: enumName, members: literalValues }
        generatedEnums.set(enumName, enumDef)
        
      // Use base namespace for Shared folder, folder namespace for others
      const enumNamespace = enumFolder === 'Shared' ? ns : (ns + "." + enumFolder)
        
        const enumCode = `// <auto-generated>
//     This code was generated by WebViewContractGenerator.
//     Do not modify this file directly as changes will be lost on regeneration.
//     Source: WebViewContract.json schema version ${contract.schemaVersion}
// </auto-generated>

#nullable enable

namespace ${enumNamespace};

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

/// <summary>
/// Enum: ${enumName}
/// Generated from union type
/// Source: ${typeDef.sourceFile}
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum ${enumName}
{
${enumDef.members.map((m, i) => `    [JsonProperty("${m}")]\n    ${pascalCase(m)}${i < enumDef.members.length - 1 ? ',' : ''}`).join('\n')}
}
`
        const enumTargetDir = getDirectoryForFolder(enumFolder)
        const enumFilePath = path.join(enumTargetDir, `${enumName}.cs`)
        fs.writeFileSync(enumFilePath, enumCode)
        console.log(`  Generated enum: ${enumName} (${enumFolder})`)
      }
    }
  }
}

// Generate inline enums discovered in properties
console.log("Generating inline enums from properties...")
for (const [signature, info] of inlineEnumRegistry) {
  if (generatedInlineEnumSignatures.has(signature)) continue
  
  const members = signature.split(',')
  const enumName = info.enumName
  
  const enumDef: EnumDefinition = { name: enumName, members }
  generatedEnums.set(enumName, enumDef)
  generatedInlineEnumSignatures.add(signature)
  
  const enumCode = `// <auto-generated>
//     This code was generated by WebViewContractGenerator.
//     Do not modify this file directly as changes will be lost on regeneration.
//     Source: WebViewContract.json schema version ${contract.schemaVersion}
// </auto-generated>

#nullable enable

namespace ${ns};

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

/// <summary>
/// Enum: ${enumName}
/// Generated from inline string literal union
/// Members: ${members.join(', ')}
/// Used by: ${info.usedBy.join(', ')}
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum ${enumName}
{
${enumDef.members.map((m, i) => `    [JsonProperty("${m}")]\n    ${pascalCase(m)}${i < enumDef.members.length - 1 ? ',' : ''}`).join('\n')}
}
`
  const enumTargetDir = getDirectoryForFolder('Shared')
  const enumFilePath = path.join(enumTargetDir, `${enumName}.cs`)
  fs.writeFileSync(enumFilePath, enumCode)
  console.log(`  Generated inline enum: ${enumName} (${members.join(', ')})`)
}

// Write enum files - place them based on source file
// Skip inline enums that were already generated with proper attributes
for (const [enumName, enumDef] of generatedEnums) {
  // Skip inline enums - they were already generated with StringEnumConverter
  const signature = generateInlineEnumSignature(enumDef.members)
  if (generatedInlineEnumSignatures.has(signature)) {
    continue
  }
  
  // Find the source file for this enum by checking typeDefinitions first
  // The enumName may have "Enum" suffix, so we need to look up the original type name
  let enumFolder = 'Types' // default
  let originalTypeName = enumName.endsWith('Enum') ? enumName.slice(0, -4) : enumName
  const typeDef = typeDefinitions.get(originalTypeName) || typeDefinitions.get(enumName)
  if (typeDef && typeDef.sourceFile) {
    enumFolder = getSourceFileFolder(typeDef.sourceFile)
  } else {
    // Fallback: check which message/type uses it
    for (const message of [...contract.messages.webviewToExtension, ...contract.messages.extensionToWebview]) {
      for (const prop of message.properties) {
        if (prop.elementType === enumName || prop.typeRef?.name === enumName) {
          enumFolder = getSourceFileFolder(message.sourceFile)
          break
        }
      }
      if (enumFolder !== 'Types') break
    }
  }
  
  // Use base namespace for Shared folder, folder namespace for others
  const enumNamespace = enumFolder === 'Shared' ? ns : (ns + "." + enumFolder)
  
  const enumCode = `// <auto-generated>
//     This code was generated by WebViewContractGenerator.
//     Do not modify this file directly as changes will be lost on regeneration.
//     Source: WebViewContract.json schema version ${contract.schemaVersion}
// </auto-generated>

#nullable enable

namespace ${enumNamespace};

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

/// <summary>
/// Enum: ${enumName}
/// Generated from union type
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum ${enumName}
{
${enumDef.members.map((m, i) => `    [JsonProperty("${m}")]\n    ${pascalCase(m)}${i < enumDef.members.length - 1 ? ',' : ''}`).join('\n')}
}
`
  const enumTargetDir = getDirectoryForFolder(enumFolder)
  const enumFilePath = path.join(enumTargetDir, `${enumName}.cs`)
  fs.writeFileSync(enumFilePath, enumCode)
  console.log(`  Generated enum: ${enumName} (${enumFolder})`)
}

// Generate types in dependency order
const generatedOrder: string[] = []
const visited = new Set<string>()

/**
 * Detect inheritance relationships using heuristics
 * 
 * Returns a map of derived type name -> base type name
 */
function detectInheritance(): Map<string, string> {
  const inheritanceMap = new Map<string, string>()
  
  // First, use explicit extendsBase from contract (highest confidence)
  for (const [typeName, typeDef] of typeDefinitions) {
    if (typeDef.extendsBase && typeDefinitions.has(typeDef.extendsBase)) {
      inheritanceMap.set(typeName, typeDef.extendsBase)
    }
  }
  
  // Then, apply heuristic detection for types without explicit extendsBase
  for (const [typeName, typeDef] of typeDefinitions) {
    if (typeDef.kind !== 'interface' || !typeDef.properties || inheritanceMap.has(typeName)) {
      continue
    }
    
    // Heuristic 1: Name prefix matching + same source file
    for (const [baseName, baseDef] of typeDefinitions) {
      if (baseName === typeName || baseDef.kind !== 'interface' || !baseDef.properties) {
        continue
      }
      
      // Check if typeName starts with baseName (e.g., "TextPart" starts with "Part")
      if (!typeName.startsWith(baseName) && !baseName.endsWith(typeName)) {
        continue
      }
      
      // Check if same source file
      if (typeDef.sourceFile !== baseDef.sourceFile) {
        continue
      }
      
      // Check if derived type has ALL properties of base type (property subset)
      const basePropNames = new Set(baseDef.properties.map(p => p.name))
      const derivedPropNames = new Set(typeDef.properties.map(p => p.name))
      
      const hasAllBaseProps = Array.from(basePropNames).every(name => derivedPropNames.has(name))
      
      if (hasAllBaseProps && basePropNames.size > 0) {
        // Confidence: name prefix (+10) + same file (+10) + property subset (+30) = 50
        // Threshold is 50, so this qualifies
        inheritanceMap.set(typeName, baseName)
        console.log(`  Heuristic: ${typeName} extends ${baseName} (confidence: name prefix + property subset)`)
        break
      }
    }
  }
  
  return inheritanceMap
}

// Detect inheritance relationships
console.log("Detecting inheritance relationships...")
const inheritanceMap = detectInheritance()
if (inheritanceMap.size > 0) {
  console.log(`Found ${inheritanceMap.size} inheritance relationships:`)
  for (const [derived, base] of inheritanceMap) {
    console.log(`  ${derived} → ${base}`)
  }
}
console.log()

function generateTypeWithDeps(typeName: string) {
  if (visited.has(typeName)) return
  visited.add(typeName)
  
  const typeDef = typeDefinitions.get(typeName)
  if (!typeDef) return
  
  // For interfaces, generate all dependencies first
  if (typeDef.kind === 'interface' && typeDef.properties) {
    // Generate base type first if there's an inheritance relationship
    const baseType = inheritanceMap.get(typeName)
    if (baseType && neededTypes.has(baseType)) {
      generateTypeWithDeps(baseType)
    }
    for (const prop of typeDef.properties) {
      if (prop.typeRef?.name && neededTypes.has(prop.typeRef.name)) {
        generateTypeWithDeps(prop.typeRef.name)
      }
      if (prop.elementType && neededTypes.has(prop.elementType)) {
        generateTypeWithDeps(prop.elementType)
      }
    }
  }
  
  if (!generatedOrder.includes(typeName)) {
    generatedOrder.push(typeName)
  }
}

for (const typeName of neededTypes) {
  generateTypeWithDeps(typeName)
}

// Also add all types from src/shared that have properties to generatedOrder
// These may be used by SSEHelper or other extension code
for (const [typeName, typeDef] of typeDefinitions) {
  const normalizedSource = typeDef.sourceFile.replace(/\\/g, '/')
  if (normalizedSource.includes('src/shared') && typeDef.properties && typeDef.properties.length > 0) {
    if (!generatedOrder.includes(typeName)) {
      generatedOrder.push(typeName)
    }
  }
}

// Also add all types from SDK that are referenced by type aliases in the message files
// These are external types that belong to the repository and should be generated
for (const [typeName, typeDef] of typeDefinitions) {
  const normalizedSource = typeDef.sourceFile.replace(/\\/g, '/')
  
  // Check if this type is from the SDK
  const isSdkType = normalizedSource.includes('sdk/js/src/v2/gen/types.gen.ts')
  
  if (isSdkType && typeDef.properties && typeDef.properties.length > 0) {
    // Check if all property names are valid C# identifiers
    const hasValidProperties = typeDef.properties.every(p => 
      /^[a-zA-Z_][a-zA-Z0-9_]*$/.test(p.name)
    )
    
    if (hasValidProperties && !generatedOrder.includes(typeName)) {
      generatedOrder.push(typeName)
    }
  }
}

// Add base types that are extended by other types (for inheritance support)
console.log("Adding base types for inheritance...")
for (const [typeName, typeDef] of typeDefinitions) {
  if (typeDef.extendsBase && !generatedOrder.includes(typeDef.extendsBase)) {
    // Check if the base type has properties and should be generated
    const baseDef = typeDefinitions.get(typeDef.extendsBase)
    if (baseDef && baseDef.properties && baseDef.properties.length > 0) {
      generatedOrder.push(typeDef.extendsBase)
      console.log(`  Added base type: ${typeDef.extendsBase} (extended by ${typeName})`)
    }
  }
}

// Generate ALL types from the message source files, regardless of whether they're referenced
// This ensures that types like DeviceAuthState are generated even if not directly referenced by messages
console.log("Adding all types from message source files to generation list...")

// Check if types already exist in the ApiClient folder and skip generating them
const apiClientPath = path.join(VS_DIR, "KiloVisualStudioExtension/ApiClient")
existingApiTypes = new Set<string>()
if (fs.existsSync(apiClientPath)) {
  const apiFiles = fs.readdirSync(apiClientPath).filter(f => f.endsWith('.cs'))
  for (const file of apiFiles) {
    const content = fs.readFileSync(path.join(apiClientPath, file), 'utf-8')
    // Look for class, enum, interface, record declarations
    const matches = content.matchAll(/(?:public\s+(?:partial\s+)?(?:class|enum|interface|record)\s+)(\w+)/g)
    for (const match of matches) {
      existingApiTypes.add(match[1])
    }
  }
}
console.log(`Found ${existingApiTypes.size} existing types in ApiClient folder`)

for (const [typeName, typeDef] of typeDefinitions) {
  const normalizedSource = typeDef.sourceFile.replace(/\\/g, '/')
  
  // Only include types from the message folders
  const isMessageSource = 
    normalizedSource.includes('connection.ts') ||
    normalizedSource.includes('parts.ts') ||
    normalizedSource.includes('sessions.ts') ||
    normalizedSource.includes('permissions.ts') ||
    normalizedSource.includes('questions.ts') ||
    normalizedSource.includes('providers.ts') ||
    normalizedSource.includes('agents.ts') ||
    normalizedSource.includes('config.ts') ||
    normalizedSource.includes('profile.ts') ||
    normalizedSource.includes('agent-manager.ts') ||
    normalizedSource.includes('migration.ts') ||
    normalizedSource.includes('memory.ts') ||
    normalizedSource.includes('extension-messages.ts') ||
    normalizedSource.includes('webview-messages.ts')
  
  // Skip types that already exist in ApiClient ONLY if they are SDK types
  // Local types (from VS Code extension) should be added to generation list even if ApiClient has same name
  const isSdkType = normalizedSource.includes('sdk/js/src/v2/gen/types.gen.ts')
  if (isSdkType && existingApiTypes.has(typeName)) {
    console.log(`  Skipping ${typeName} - SDK type already exists in ApiClient`)
    continue
  }
  
  if (isMessageSource && !generatedOrder.includes(typeName)) {
    generatedOrder.push(typeName)
  }
}

// Now generate in order
for (const typeName of generatedOrder) {
  // Skip if this type is also a message
  const isMessage = messageNames.has(typeName)
  
  // Get the type definition
  const typeDef = typeDefinitions.get(typeName)
  if (!typeDef) continue
  
  // Normalize source file path
  const normalizedSource = typeDef.sourceFile.replace(/\\/g, '/')
  
  // Skip types that already exist in ApiClient ONLY if they are SDK types
  // Local types (from VS Code extension) should be generated locally even if ApiClient has same name
  const isSdkType = normalizedSource.includes('sdk/js/src/v2/gen/types.gen.ts')
  if (isSdkType && existingApiTypes.has(typeName)) {
    console.log(`  Skipping ${typeName} - SDK type already exists in ApiClient`)
    continue
  }
  
  // Determine the folder based on source file
  const typeFolder = getSourceFileFolder(typeDef.sourceFile)
  
  // If this is a message from a non-messages source file (like marketplace.ts),
  // generate it as a type in the Types folder
  if (isMessage && typeFolder !== 'Types') {
    // Skip messages that are in the messages folder structure - they'll be generated later
    continue
  }
  
  // Skip common TypeScript types that have C# equivalents (don't generate classes for them)
  const csharpEquivalent = getCSharpTypeForCommonType(typeName)
  if (csharpEquivalent) {
    continue
  }
  
  // Skip Zod internal types
  if (typeName.startsWith('$Zod') || typeName.startsWith('Zod')) {
    continue
  }
  
  // Skip types from node_modules
  const isNodeModules = normalizedSource.includes('node_modules')
  if (isNodeModules) {
    continue
  }
  
  // For types from src/shared, always generate them if they have properties
  // (they may be used by SSEHelper or other extension code)
  const isSharedType = normalizedSource.includes('src/shared')
  if (isSharedType && typeDef.properties && typeDef.properties.length > 0) {
    // Generate this shared type
  } else if (isSharedType) {
    // Skip shared types with no properties
    continue
  }
  
  // For type aliases and unions that are needed, generate appropriate types
  if (typeDef.kind === 'typeAlias' || typeDef.kind === 'union') {
    // Skip union types with no members (indicates members couldn't be resolved from external imports)
    if (typeDef.kind === 'union' && typeDef.unionMembers && typeDef.unionMembers.length === 0) {
      continue
    }
    
    // Check if this is a string literal union (should be an enum)
    const isStringLiteralUnion = typeDef.kind === 'union' && 
      typeDef.unionMembers && 
      typeDef.unionMembers.every(m => m.startsWith('"') || m.startsWith("'"))
    
    if (isStringLiteralUnion) {
      // Generate the signature to check if this is an inline enum
      const literalValuesForSignature = typeDef.unionMembers.map(v => {
        if (v.startsWith('"') || v.startsWith("'")) {
          return v.slice(1, -1)
        }
        return v
      })
      const signature = generateInlineEnumSignature(literalValuesForSignature)
      
      // Skip if this enum was already generated as an inline enum
      if (generatedInlineEnumSignatures.has(signature)) {
        continue
      }
      
      // Also skip if the enum name matches an already generated inline enum
      const generatedEnumName = generateEnumName(literalValuesForSignature)
      console.log(`  Checking ${typeName}: generatedEnumName=${generatedEnumName}, registry size=${inlineEnumRegistry.size}`)
      const existingInlineEnum = Array.from(inlineEnumRegistry.values()).find(e => e.enumName === generatedEnumName)
      console.log(`  existingInlineEnum=${existingInlineEnum ? existingInlineEnum.enumName : 'none'}`)
      if (existingInlineEnum) {
        console.log(`  Skipping ${typeName} -> ${generatedEnumName} - matches inline enum`)
        continue
      }
      
      // Generate as enum with "Enum" suffix
      const enumName = typeName.endsWith('Enum') ? typeName : (typeName + 'Enum')
      const enumFolder = typeFolder
      const enumNamespace = enumFolder === 'Shared' ? ns : (ns + "." + enumFolder)
      
      const literalValues = literalValuesForSignature
      
      // Skip if this enum (with Enum suffix) was already generated
      if (generatedEnums.has(enumName)) {
        continue
      }
      
      const enumCode = `// <auto-generated>
//     This code was generated by WebViewContractGenerator.
//     Do not modify this file directly as changes will be lost on regeneration.
//     Source: WebViewContract.json schema version ${contract.schemaVersion}
// </auto-generated>

#nullable enable

namespace ${enumNamespace};

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

/// <summary>
/// Enum: ${enumName}
/// Generated from union type
/// Source: ${typeDef.sourceFile}
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum ${enumName}
{
${literalValues.map((m, i) => `    [JsonProperty("${m}")]\n    ${pascalCase(m)}${i < literalValues.length - 1 ? ',' : ''}`).join('\n')}
}
`
      const enumTargetDir = getDirectoryForFolder(enumFolder)
      const enumFilePath = path.join(enumTargetDir, `${enumName}.cs`)
      fs.writeFileSync(enumFilePath, enumCode)
      generatedEnums.set(enumName, { name: enumName, members: literalValues })
      generatedTypes.add(typeName)
      console.log(`  Generated enum: ${enumName} (${enumFolder})`)
      continue
    }
    
    // Check if this type alias references a known type (e.g., SdkIndexingStatus -> IndexingStatus)
    // by checking if there's a type with a similar name (without the "Sdk" prefix)
    let referencedTypeName: string | null = null
    if (typeName.startsWith('Sdk') && typeDefinitions.has(typeName.substring(3))) {
      referencedTypeName = typeName.substring(3)
    }
    
    if (referencedTypeName) {
      // Check if the referenced type is an SDK type that exists in ApiClient
      const refTypeDef = typeDefinitions.get(referencedTypeName)
      const isSdkType = refTypeDef?.sourceFile.includes('sdk/js/src/v2/gen/types.gen.ts')
      // Also check if the referenced type exists in ApiClient (even if not an SDK type)
      const existsInApiClient = existingApiTypes.has(referencedTypeName)
      // Only use ApiClient reference if the type is actually an SDK type OR exists in ApiClient
      // and is actually used (not just defined)
      const needsApiClientReference = isSdkType && existsInApiClient
      
      // Generate as a type alias (using the referenced type)
      const namespace = typeFolder === 'Shared' ? ns : (ns + "." + typeFolder)
      const usingStatements = needsApiClientReference 
        ? 'using KiloVisualStudioExtension.ApiClient;\n' 
        : ''
      const baseTypeName = needsApiClientReference 
        ? `ApiClient.${pascalCase(referencedTypeName)}` 
        : pascalCase(referencedTypeName)
      const code = `// <auto-generated>
//     This code was generated by WebViewContractGenerator.
//     Do not modify this file directly as changes will be lost on regeneration.
//     Source: WebViewContract.json schema version ${contract.schemaVersion}
// </auto-generated>

#nullable enable

namespace ${namespace};

${usingStatements}/// <summary>
/// Type: ${typeName} (alias for ${referencedTypeName})
/// Source: ${typeDef.sourceFile}
/// </summary>
public partial class ${pascalCase(typeName)} : ${baseTypeName} { }
`
      const typeTargetDir = getDirectoryForFolder(typeFolder)
      const filePath = path.join(typeTargetDir, `${pascalCase(typeName)}.cs`)
      fs.writeFileSync(filePath, code)
      generatedTypes.add(typeName)
      console.log(`  Generated: ${typeName} (alias for ${referencedTypeName}, ${typeFolder})`)
      continue
    }
    
    // Use base namespace for Shared folder
    const namespace = typeFolder === 'Shared' ? ns : (ns + "." + typeFolder)
    // Generate a placeholder class for needed type aliases/unions
    const code = `// <auto-generated>
//     This code was generated by WebViewContractGenerator.
//     Do not modify this file directly as changes will be lost on regeneration.
//     Source: WebViewContract.json schema version ${contract.schemaVersion}
// </auto-generated>

#nullable enable

namespace ${namespace};

/// <summary>
/// Type: ${typeName} (placeholder for ${typeDef.kind})
/// Source: ${typeDef.sourceFile}
/// </summary>
public partial class ${pascalCase(typeName)} { }
`
    const typeTargetDir = getDirectoryForFolder(typeFolder)
    const filePath = path.join(typeTargetDir, `${pascalCase(typeName)}.cs`)
    fs.writeFileSync(filePath, code)
    generatedTypes.add(typeName)
    console.log(`  Generated: ${typeName} (placeholder, ${typeFolder})`)
    continue
  }
  
  // Skip interfaces with no properties UNLESS they are needed by messages
  // (needed types are those explicitly referenced by message properties)
  const hasNoProperties = !typeDef.properties || typeDef.properties.length === 0
  if (hasNoProperties && !neededTypes.has(typeName)) {
    continue
  }
  
  const code = generateTypeClass(typeDef, typeFolder, generatedOrder)
  const typeTargetDir = getDirectoryForFolder(typeFolder)
  const filePath = path.join(typeTargetDir, `${pascalCase(typeDef.name)}.cs`)
  fs.writeFileSync(filePath, code)
  generatedTypes.add(typeDef.name)
  console.log(`  Generated: ${typeDef.name} (${typeFolder})`)
}

console.log()
console.log("Generating message classes...")

let generatedCount = 0

// Map to track message counts per folder
const folderCounts = new Map<string, number>()

for (const message of contract.messages.webviewToExtension) {
  // Skip messages from node_modules
  if (message.sourceFile.includes('node_modules')) {
    continue
  }
  const folder = getSourceFileFolder(message.sourceFile)
  const code = generateMessageClass(message, ns, folder)
  const targetDir = getDirectoryForFolder(folder)
  const filePath = path.join(targetDir, `${pascalCase(message.name)}.cs`)
  fs.writeFileSync(filePath, code)
  generatedCount++
  const count = folderCounts.get(folder) || 0
  folderCounts.set(folder, count + 1)
  if (generatedCount <= 10 || generatedCount % 20 === 0) {
    console.log(`  Generated: ${message.name} (${folder})`)
  }
}

for (const message of contract.messages.extensionToWebview) {
  // Skip messages from node_modules
  if (message.sourceFile.includes('node_modules')) {
    continue
  }
  const folder = getSourceFileFolder(message.sourceFile)
  const code = generateMessageClass(message, ns, folder)
  const targetDir = getDirectoryForFolder(folder)
  const filePath = path.join(targetDir, `${pascalCase(message.name)}.cs`)
  fs.writeFileSync(filePath, code)
  generatedCount++
  const count = folderCounts.get(folder) || 0
  folderCounts.set(folder, count + 1)
  if (generatedCount <= 10 || generatedCount % 20 === 0) {
    console.log(`  Generated: ${message.name} (${folder})`)
  }
}

console.log()
console.log("Message generation by folder:")
for (const [folder, count] of folderCounts.entries()) {
  console.log(`  ${folder}: ${count} messages`)
}

console.log()
console.log("Generating discriminator factory...")
const factoryCode = generateDiscriminatorFactory(
  contract.messages.webviewToExtension,
  contract.messages.extensionToWebview,
  ns
)
const factoryPath = path.join(OUTPUT_PATH, "WebViewMessageFactory.cs")
fs.writeFileSync(factoryPath, factoryCode)
generatedCount++

// Generate IWebviewMessage interface in its own file
const iWebviewMessageCode = `// <auto-generated>
//     This code was generated by WebViewContractGenerator.
//     Do not modify this file directly as changes will be lost on regeneration.
// </auto-generated>

#nullable enable

namespace ${ns};

/// <summary>
/// Marker interface for WebView messages that can be sent from extension to webview.
/// All message types implement this interface for unified handling.
/// </summary>
public interface IWebviewMessage
{
}
`
const iWebviewMessagePath = path.join(OUTPUT_PATH, "IWebviewMessage.cs")
fs.writeFileSync(iWebviewMessagePath, iWebviewMessageCode)
generatedCount++
console.log("  Interface: IWebviewMessage.cs")

// Generate IWebviewMessageRequest interface in its own file
const iWebviewMessageRequestCode = `// <auto-generated>
//     This code was generated by WebViewContractGenerator.
//     Do not modify this file directly as changes will be lost on regeneration.
// </auto-generated>

#nullable enable

namespace ${ns};

/// <summary>
/// Marker interface for WebView request messages (sent from webview to extension).
/// Inherits from IWebviewMessage for unified handling.
/// </summary>
public interface IWebviewMessageRequest : IWebviewMessage
{
}
`
const iWebviewMessageRequestPath = path.join(OUTPUT_PATH, "IWebviewMessageRequest.cs")
fs.writeFileSync(iWebviewMessageRequestPath, iWebviewMessageRequestCode)
generatedCount++
console.log("  Interface: IWebviewMessageRequest.cs")

console.log()
console.log("Generation complete!")
console.log(`Output directory: ${OUTPUT_PATH}`)
console.log(`Total files generated: ${generatedCount}`)
console.log(`  Types: ${generatedTypes.size}`)
console.log(`  WebView→Extension: ${contract.messages.webviewToExtension.length} message classes`)
console.log(`  Extension→WebView: ${contract.messages.extensionToWebview.length} message classes`)
console.log(`  Discriminator factory: WebViewMessageFactory.cs`)
console.log(`  Base interfaces: IWebviewMessage.cs, IWebviewMessageRequest.cs`)

