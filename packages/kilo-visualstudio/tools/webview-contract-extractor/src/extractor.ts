#!/usr/bin/env bun
/**
 * WebView Contract Extractor
 * 
 * This script extracts TypeScript type definitions and message schemas from the VS Code
 * extension's webview code and generates a structured JSON contract file (WebViewContract.json)
 * that describes the communication protocol between the webview and the extension host.
 * 
 * ## Input
 * - TypeScript source files from packages/kilo-vscode/webview-ui/src/types/messages/
 * - Shared types from packages/kilo-vscode/src/shared/
 * 
 * ## Processing
 * - Uses TypeScript Compiler API to parse and analyze source files
 * - Extracts interfaces, type aliases, and union types
 * - Identifies message types via discriminator fields (type, status, role)
 * - Scans for inline messages in postMessage() calls
 * 
 * ## Output
 * - WebViewContract.json: Structured contract describing all types and messages
 * 
 * ## Usage
 *   bun src/extractor.ts
 */

import * as ts from "typescript"
import * as path from "path"
import * as fs from "fs"
import type {
  WebViewContract,
  MessageType,
  TypeDefinition,
  PropertyDefinition,
  DiscriminatorInfo,
  TypeReference,
  Diagnostics,
  Statistics,
  SourceInfo,
  Metadata,
  MessageCollections,
} from "./types.js"

// __dirname is the src directory; go up 4 levels to packages directory
const PKGS_DIR = path.resolve(__dirname, "..", "..", "..", "..")
const VS_CODE_TYPES_PATH = path.join(PKGS_DIR, "kilo-vscode/webview-ui/src/types/messages")
const VS_CODE_SHARED_PATH = path.join(PKGS_DIR, "kilo-vscode/src/shared")
const VS_CODE_SRC_PATH = path.join(PKGS_DIR, "kilo-vscode/src")
const VS_CODE_PROVIDER_UTILS_PATH = path.join(VS_CODE_SRC_PATH, "kilo-provider-utils.ts")
const TSCONFIG_PATH = path.join(PKGS_DIR, "kilo-vscode/webview-ui/tsconfig.json")
const OUTPUT_PATH = path.join(PKGS_DIR, "kilo-visualstudio/porting/contract/WebViewContract.json")

/**
 * Recursively find all TypeScript files in a directory
 * Excludes hidden directories (starting with .) and node_modules
 */
function findTsFiles(dir: string, files: string[] = []): string[] {
  const entries = fs.readdirSync(dir, { withFileTypes: true })
  for (const entry of entries) {
    const fullPath = path.join(dir, entry.name)
    if (entry.isDirectory() && !entry.name.startsWith(".") && entry.name !== "node_modules") {
      findTsFiles(fullPath, files)
    } else if (entry.isFile() && entry.name.endsWith(".ts")) {
      files.push(fullPath)
    }
  }
  return files
}

/**
 * Extraction context: holds state during the extraction process
 */
interface ExtractionContext {
  program: ts.Program  // TypeScript program for type checking
  typeChecker: ts.TypeChecker  // Type checker for resolving types
  types: Map<string, TypeDefinition>  // Extracted type definitions by name
  messages: {
    webviewToExtension: MessageType[]  // Messages from webview to extension
    extensionToWebview: MessageType[]  // Messages from extension to webview
  }
  errors: string[]  // Extraction errors
  warnings: string[]  // Extraction warnings
  extractedMessages: Map<string, MessageType>  // Track extracted inline messages by discriminator value
}

/**
 * Extract properties from an inline type definition string.
 * 
 * Parses inline object types like `{ type: "sessionStatus"; sessionID: string; status: string }`
 * and extracts property definitions.
 * 
 * @param inlineTypeBody - The body of the inline type (without braces)
 * @param discriminator - Discriminator info if found
 * @returns Array of property definitions
 */
function extractPropertiesFromInlineType(inlineTypeBody: string): { properties: PropertyDefinition[], discriminator: DiscriminatorInfo | undefined } {
  const properties: PropertyDefinition[] = []
  let discriminator: DiscriminatorInfo | undefined
  
  // Remove outer braces if present
  const body = inlineTypeBody.trim()
  const inner = (body.startsWith('{') && body.endsWith('}')) ? body.slice(1, -1) : body
  
  // Split by semicolons or newlines
  const propStrings = inner.split(/[;\n]/).map(s => s.trim()).filter(s => s)
  
  for (const propStr of propStrings) {
    // Match property pattern: name?: type or name: type
    const match = propStr.match(/^(\w+)(\?)?:\s*(.+?)$/)
    if (!match) continue
    
    const propName = match[1]!
    const isOptional = match[2] === '?'
    const propTypeStr = match[3]!.trim()
    
    let propType: string
    let literalValue: string | number | boolean | null = null
    let isLiteral = false
    let typeRef: TypeReference | null = null
    
    // Check for string literal: "value"
    const stringLiteralMatch = propTypeStr.match(/^"([^"]+)"$/)
    if (stringLiteralMatch) {
      propType = "stringLiteral"
      literalValue = stringLiteralMatch[1]!
      isLiteral = true
      typeRef = { name: propType, kind: "stringLiteral" }
    }
    // Check for basic types
    else if (["string", "number", "boolean", "array", "object", "union"].includes(propTypeStr)) {
      propType = propTypeStr
      typeRef = { name: propType, kind: propType as any }
    }
    // Check for Record<...>
    else if (propTypeStr.startsWith("Record<")) {
      propType = "record"
      typeRef = { name: "Record", kind: "object" }
    }
    // Named type reference
    else {
      propType = propTypeStr
      typeRef = { name: propType, kind: "object" }
    }
    
    const propDef: PropertyDefinition = {
      name: propName,
      type: propType,
      optional: isOptional,
      nullable: false,
      elementType: null,
      typeRef,
      literalValue,
      isLiteral,
    }
    
    properties.push(propDef)
    
    // Check for discriminator
    if (propName === "type" && isLiteral && typeof literalValue === "string") {
      discriminator = {
        field: "type",
        value: literalValue,
      }
    }
  }
  
  return { properties, discriminator }
}

/**
 * Extract the WebviewMessage union from kilo-provider-utils.ts
 * This file is outside the webview-ui tsconfig, so we parse it manually
 * 
 * ## Processing Steps
 * 1. Read the source file and find the WebviewMessage type alias
 * 2. Use splitUnionMembers() to properly split union members while tracking nested braces
 * 3. For inline types (starting with `{`), extract properties and create temporary interfaces
 * 4. For named types, add as type references
 * 5. Compute signature hashes for all created types
 * 6. Create the WebviewMessage union with all member names
 */
function extractWebviewMessageFromProviderUtils(context: ExtractionContext): void {
  if (!fs.existsSync(VS_CODE_PROVIDER_UTILS_PATH)) {
    context.warnings.push(`kilo-provider-utils.ts not found at ${VS_CODE_PROVIDER_UTILS_PATH}`)
    return
  }
  
  const sourceText = fs.readFileSync(VS_CODE_PROVIDER_UTILS_PATH, "utf-8")
  
  // First regex: extract the full WebviewMessage union definition
  const unionRegex = /export\s+?type\s+?WebviewMessage\s*?=\n?((((\s*?\w+\s*?)\|)+(\s*?\w+\s*?))|(\s*?\|(\s*?\{)+[\s\S]*?(\s*?\})+)|(\s*?\|[\s\S]*?$))+/m
  const unionMatch = sourceText.match(unionRegex)
  if (!unionMatch) {
    context.warnings.push("WebviewMessage type alias not found in kilo-provider-utils.ts")
    return
  }
  
  // Use the regex-based splitter to get individual members
  const unionMembersRaw = splitUnionMembers(unionMatch[0])
  const unionMembers: string[] = []
  
  for (const member of unionMembersRaw) {
    // Skip null
    if (member === 'null') continue
    
    // Check if it's an inline object type (starts with {)
    if (member.startsWith('{')) {
      // Extract properties from the inline type
      const { properties, discriminator } = extractPropertiesFromInlineType(member)
      
      if (discriminator && properties.length > 0) {
        const discriminatorValue = discriminator.value
        const inlineTypeName = `${toPascalCase(discriminatorValue)}Message`
        
        // Create a type definition with all extracted properties
        const inlineTypeDef: TypeDefinition = {
          name: inlineTypeName,
          kind: "interface",
          properties,
          discriminator,
          sourceFile: "kilo-provider-utils.ts",
        }
        
        inlineTypeDef.signatureHash = computeSignatureHash(inlineTypeDef)
        
        if (!context.types.has(inlineTypeName)) {
          context.types.set(inlineTypeName, inlineTypeDef)
        }
        
        unionMembers.push(inlineTypeName)
      }
    } else {
      // Named type reference - remove generic parameters for the name
      const typeName = member.split('<')[0].trim()
      if (typeName && !unionMembers.includes(typeName)) {
        unionMembers.push(typeName)
      }
    }
  }
  
  // Add the WebviewMessage union to context.types
  // WebviewMessage is a discriminated union by the "type" field
  const webviewMessageTypeDef: TypeDefinition = {
    name: "WebviewMessage",
    kind: "union",
    unionMembers,
    discriminator: {
      field: "type",
      value: "WebviewMessage",  // Generic discriminator value for the union itself
    },
    sourceFile: "kilo-provider-utils.ts",
  }
  
  context.types.set("WebviewMessage", webviewMessageTypeDef)
  console.log(`  Extracted WebviewMessage from kilo-provider-utils.ts with ${unionMembers.length} members`)
  console.log(`  Members: ${unionMembers.join(', ')}`)
}

/**
 * Generate a signature hash for a type definition
 * The signature is based on: discriminator value + sorted property names and C# types
 * This allows detecting types with identical structure across different files
 * 
 * Uses SHA-256 for collision resistance.
 * 
 * ## Normalization for C# Output
 * Type values are normalized to match what the C# generator will produce:
 * - Type parameters (e.g., "P", "T") → "object" (C# generator uses object for generics)
 * - Union types → "object" (C# generator uses object for unions)
 * - Literal types → "string" or "bool" or "double" based on literal value
 * - Primitive types → C# equivalent (string, bool, double)
 * - Arrays → "List<object>"
 * - Records/maps → "Dictionary<object, object>"
 * 
 * @param typeDef - Type definition to hash
 * @returns Hex string hash of the signature (64 chars), or undefined if no discriminator
 */
function computeSignatureHash(typeDef: TypeDefinition): string | undefined {
  if (!typeDef.discriminator || !typeDef.properties) {
    return undefined
  }
  
  // Normalize to C# output types (matches generator.ts mapToCSharpType logic)
  const normalizeToCSharpType = (prop: PropertyDefinition): string => {
    // Type parameters → object (C# generator uses object for generics)
    if (/^[A-Z]$/.test(prop.type) || /^(T|P|K|V|E|R)$/.test(prop.type)) {
      return "object"
    }
    // Union types → object
    if (prop.type === "union") {
      return "object"
    }
    // Literal types → based on value
    if (prop.isLiteral && prop.literalValue !== null) {
      if (typeof prop.literalValue === "string") return "string"
      if (typeof prop.literalValue === "boolean") return "bool"
      if (typeof prop.literalValue === "number") return "double"
    }
    // Primitive type mapping to C#
    switch (prop.type) {
      case "stringLiteral":
      case "string":
        return "string"
      case "boolean":
      case "booleanLiteral":
        return "bool"
      case "number":
      case "numberLiteral":
      case "integer":
        return "double"
      case "array":
      case "readonlyarray":
        return "List<object>"
      case "map":
      case "record":
        return "Dictionary<object, object>"
      case "typeParameter":
        return "object"
      case "object":
      case "any":
      case "unknown":
        return "object"
      case "Date":
        return "DateTime"
      default:
        // For unknown types, use the type name as-is
        return prop.type
    }
  }
  
  // Build signature string: discriminator_value|prop1:CSharpType1|prop2:CSharpType2|...
  const propsSig = typeDef.properties
    .filter(p => p.name !== 'type') // Exclude discriminator field itself
    .map(p => {
      const optionalMarker = p.optional ? '?' : ''
      const csharpType = normalizeToCSharpType(p)
      return `${p.name}:${csharpType}${optionalMarker}`
    })
    .sort() // Sort to ensure consistent ordering
    .join('|')
  
  const signature = `${typeDef.discriminator.value}|${propsSig}`
  
  // Use SHA-256 for collision resistance
  const crypto = require('crypto')
  return crypto.createHash('sha256').update(signature).digest('hex')
}

/**
 * Split a union type definition into individual members using regex.
 * 
 * ## Algorithm
 * 1. Use regex to match each union member pattern:
 *    - Named types: `PartUpdate | PartBatch`
 *    - Inline types: `| { type: "x"; ... }`
 *    - Trailing members: `| null`
 * 2. Strip leading `|` and trim each member
 * 
 * ## Example
 * Input: `export type WebviewMessage =\n  | PartUpdate\n  | { type: "x" }\n  | null`
 * Output: `["PartUpdate", "{ type: \"x\" }", "null"]`
 * 
 * @param unionText - The full union definition text (including `export type Name =`)
 * @returns Array of union member strings
 */
function splitUnionMembers(unionText: string): string[] {
  // Regex to match individual union members
  // Matches: named types with |, inline types with braces, or trailing members
  const memberRegex = /(((\s*?\w+\s*?)\|)+(\s*?\w+\s*?))|(\s*?\|(\s*?\{)+[\s\S]*?(\s*?\})+)|(\s*?\|[\s\S]*?$)/gm
  
  const members: string[] = []
  let match: RegExpExecArray | null
  
  while ((match = memberRegex.exec(unionText)) !== null) {
    let member = match[0].trim()
    // Strip leading |
    if (member.startsWith('|')) {
      member = member.slice(1).trim()
    }
    // Strip trailing |
    if (member.endsWith('|')) {
      member = member.slice(0, -1).trim()
    }
    if (member) {
      members.push(member)
    }
  }
  
  return members
}

/**
 * Extract all union type definitions from a TypeScript source file.
 * 
 * ## Algorithm
 * 1. Use regex to find all `export type Name = ...` patterns
 * 2. For each match, split into union members
 * 3. For inline types (starting with `{`), extract properties and create temporary interfaces
 * 4. For named types, add as type references
 * 5. Compute signature hashes for all created types
 * 
 * @param sourceText - TypeScript source code
 * @param sourceFile - Source file path for reporting
 * @param context - Extraction context
 */
function extractAllUnionsFromSource(
  sourceText: string,
  sourceFile: string,
  context: ExtractionContext
): void {
  // Regex to match export type Name = ... union definitions
  const unionTypeRegex = /export\s+?type\s+(\w+)\s*?=\n?((((\s*?\w+\s*?)\|)+(\s*?\w+\s*?))|(\s*?\|(\s*?\{)+[\s\S]*?(\s*?\})+)|(\s*?\|[\s\S]*?$))+/gm
  
  let match: RegExpExecArray | null
  
  while ((match = unionTypeRegex.exec(sourceText)) !== null) {
    const typeName = match[1]!
    const unionText = match[0]
    
    // Split into members
    const membersRaw = splitUnionMembers(unionText)
    const unionMembers: string[] = []
    
    for (const member of membersRaw) {
      // Skip null
      if (member === 'null') continue
      
      // Check if it's an inline object type (starts with {)
      if (member.startsWith('{')) {
        // Extract properties from the inline type
        const { properties, discriminator } = extractPropertiesFromInlineType(member)
        
        if (discriminator && properties.length > 0) {
          const discriminatorValue = discriminator.value
          const inlineTypeName = `${toPascalCase(discriminatorValue)}Message`
          
          // Create a type definition with all extracted properties
          const inlineTypeDef: TypeDefinition = {
            name: inlineTypeName,
            kind: "interface",
            properties,
            discriminator,
            sourceFile,
          }
          
          inlineTypeDef.signatureHash = computeSignatureHash(inlineTypeDef)
          
          if (!context.types.has(inlineTypeName)) {
            context.types.set(inlineTypeName, inlineTypeDef)
          }
          
          unionMembers.push(inlineTypeName)
        } else if (properties.length > 0) {
          // Inline type without discriminator - generate a name
          const inlineTypeName = `${typeName}Member${unionMembers.length}`
          const inlineTypeDef: TypeDefinition = {
            name: inlineTypeName,
            kind: "interface",
            properties,
            sourceFile,
          }
          
          if (!context.types.has(inlineTypeName)) {
            context.types.set(inlineTypeName, inlineTypeDef)
          }
          
          unionMembers.push(inlineTypeName)
        }
      } else {
        // Named type reference - remove generic parameters for the name
        const typeNameOnly = member.split('<')[0].trim()
        if (typeNameOnly && !unionMembers.includes(typeNameOnly)) {
          unionMembers.push(typeNameOnly)
        }
      }
    }
    
    // Add the union type to context.types
    const unionTypeDef: TypeDefinition = {
      name: typeName,
      kind: "union",
      unionMembers,
      sourceFile,
    }
    
    context.types.set(typeName, unionTypeDef)
    console.log(`  Extracted ${typeName} from ${sourceFile} with ${unionMembers.length} members`)
  }
}

/**
 * Convert a string to PascalCase
 */
function toPascalCase(str: string): string {
  return str
    .replace(/[^a-zA-Z0-9]/g, ' ')
    .split(' ')
    .filter(p => p.length > 0)
    .map(p => p.charAt(0).toUpperCase() + p.slice(1).toLowerCase())
    .join('')
}

/**
 * Create a new extraction context
 */
function createContext(program: ts.Program): ExtractionContext {
  return {
    program,
    typeChecker: program.getTypeChecker(),
    types: new Map(),
    messages: {
      webviewToExtension: [],
      extensionToWebview: [],
    },
    errors: [],
    warnings: [],
    extractedMessages: new Map(),
  }
}

/**
 * Get the name of a TypeScript type
 * - Uses symbol name if available
 * - Falls back to stringified type name
 * - Extracts short name from qualified names (e.g., "Module.Type" → "Type")
 */
function getTypeName(type: ts.Type, typeChecker: ts.TypeChecker): string {
  const symbol = type.getSymbol()
  if (symbol) {
    return symbol.getName()
  }
  const stringified = typeChecker.typeToString(type)
  return stringified.split(".").pop() || stringified
}

/**
 * Extract literal value from a TypeScript literal type
 * Handles string, number, and boolean literals
 */
function extractLiteralValue(type: ts.Type): string | number | boolean | undefined {
  if (type.flags & ts.TypeFlags.StringLiteral) {
    return (type as ts.StringLiteralType).value
  }
  if (type.flags & ts.TypeFlags.NumberLiteral) {
    return (type as ts.NumberLiteralType).value
  }
  if (type.flags & ts.TypeFlags.BooleanLiteral) {
    return (type as ts.BooleanLiteralType).value
  }
  return undefined
}

/**
 * Determine the kind of a TypeScript type
 * Returns a string identifier for the type category
 */
function getTypeKind(type: ts.Type): string {
  // Check literal types FIRST (before base types)
  if (type.flags & ts.TypeFlags.StringLiteral) return "stringLiteral"
  if (type.flags & ts.TypeFlags.NumberLiteral) return "numberLiteral"
  if (type.flags & ts.TypeFlags.BooleanLiteral) return "booleanLiteral"
  if (type.flags & ts.TypeFlags.BigIntLiteral) return "bigIntLiteral"
  if (type.flags & ts.TypeFlags.EnumLiteral) return "enumLiteral"
  
  // Check base/primitive types
  if (type.flags & ts.TypeFlags.String) return "string"
  if (type.flags & ts.TypeFlags.Number) return "number"
  if (type.flags & ts.TypeFlags.Boolean) return "boolean"
  if (type.flags & ts.TypeFlags.BigInt) return "bigint"
  if (type.flags & ts.TypeFlags.Enum) return "enum"
  if (type.flags & ts.TypeFlags.Void) return "void"
  if (type.flags & ts.TypeFlags.Undefined) return "undefined"
  if (type.flags & ts.TypeFlags.Null) return "null"
  if (type.flags & ts.TypeFlags.Never) return "never"
  if (type.flags & ts.TypeFlags.Any) return "any"
  if (type.flags & ts.TypeFlags.Unknown) return "unknown"
  if (type.flags & ts.TypeFlags.ESSymbol) return "symbol"
  
  // Check compound types
  if (type.flags & ts.TypeFlags.Union) return "union"
  if (type.flags & ts.TypeFlags.Intersection) return "intersection"
  if (type.flags & ts.TypeFlags.Object) return "object"
  if (type.flags & ts.TypeFlags.TypeParameter) return "typeParameter"
  if (type.flags & ts.TypeFlags.Conditional) return "conditional"
  if (type.flags & ts.TypeFlags.TemplateLiteral) return "templateLiteral"
  
  return "unknown"
}

/**
 * Extract a property definition from a TypeScript symbol
 * 
 * ## Extraction Process
 * 1. Get property name and declarations
 * 2. Determine property type using type checker
 * 3. Check for optional modifier
 * 4. Handle special types:
 *    - Union types: Extract union members
 *    - Literal types: Extract literal values
 *    - Arrays: Extract element type
 *    - Records: Extract value type
 *    - Type references: Resolve to referenced type
 * 5. Check if property references a known type alias
 * 
 * @param property - TypeScript symbol for the property
 * @param typeChecker - Type checker for type resolution
 * @param context - Extraction context
 * @returns Property definition or null if extraction fails
 */
function extractPropertyDefinition(
  property: ts.Symbol,
  typeChecker: ts.TypeChecker,
  context: ExtractionContext
): PropertyDefinition | null {
  const name = property.getName()
  const declarations = property.getDeclarations()
  
  if (!declarations || declarations.length === 0) {
    return null
  }

  const declaration = declarations[0]
  const type = typeChecker.getTypeOfSymbolAtLocation(property, declaration)
  
  const isOptional = property.flags & ts.SymbolFlags.Optional
  
  let propertyType: string
  let elementType: string | null = null
  let typeRef: TypeReference | null = null
  let literalValue: string | number | boolean | null = null
  let isLiteral = false

  // Check if the property declaration references a type alias
  // by looking at the type annotation in the source code
  if (ts.isPropertySignature(declaration) && declaration.type && ts.isTypeReferenceNode(declaration.type)) {
    const referencedTypeName = declaration.type.typeName.getText()
    // Check if this is a known type alias (union, etc.)
    if (context.types.has(referencedTypeName)) {
      const referencedType = context.types.get(referencedTypeName)!
      if (referencedType.kind === 'union' && referencedType.unionMembers) {
        // This is a reference to a union type alias - use the type alias name
        typeRef = { name: referencedTypeName, kind: 'union' }
        propertyType = referencedTypeName
      }
    }
  }
  
  // Check for inline object types (type literals) in property declarations
  if (ts.isPropertySignature(declaration) && declaration.type) {
    // Inline object type: { email: string, name?: string, ... }
    if (ts.isTypeLiteralNode(declaration.type)) {
      const typeLiteral = declaration.type
      const inlineProperties: PropertyDefinition[] = []
      
      for (const member of typeLiteral.members) {
        if (ts.isPropertySignature(member) && member.name) {
          const memberSymbol = typeChecker.getSymbolAtLocation(member.name)
          if (memberSymbol) {
            const memberPropDef = extractPropertyDefinition(memberSymbol, typeChecker, context)
            if (memberPropDef) {
              inlineProperties.push(memberPropDef)
            }
          }
        }
      }
      
      if (inlineProperties.length > 0) {
        // Generate a unique name for this inline type
        const inlineTypeName = `${name}Type`
        const inlineTypeDef: TypeDefinition = {
          name: inlineTypeName,
          kind: "interface",
          properties: inlineProperties,
          sourceFile: path.relative(VS_CODE_TYPES_PATH, declaration.getSourceFile().fileName),
        }
        context.types.set(inlineTypeName, inlineTypeDef)
        
        // Update typeRef to point to this inline type
        typeRef = { name: inlineTypeName, kind: "interface" }
        propertyType = inlineTypeName
      }
    }
    // Inline array type: Array<{ id: string, name: string, role: string }>
    else if (ts.isTypeReferenceNode(declaration.type) && declaration.type.typeName.getText() === 'Array') {
      const typeArgs = declaration.type.typeArguments
      if (typeArgs && typeArgs.length > 0 && ts.isTypeLiteralNode(typeArgs[0])) {
        const inlineTypeLiteral = typeArgs[0]
        const inlineProperties: PropertyDefinition[] = []
        
        for (const member of inlineTypeLiteral.members) {
          if (ts.isPropertySignature(member) && member.name) {
            const memberSymbol = typeChecker.getSymbolAtLocation(member.name)
            if (memberSymbol) {
              const memberPropDef = extractPropertyDefinition(memberSymbol, typeChecker, context)
              if (memberPropDef) {
                inlineProperties.push(memberPropDef)
              }
            }
          }
        }
        
        if (inlineProperties.length > 0) {
          const inlineTypeName = `${name}ItemType`
          const inlineTypeDef: TypeDefinition = {
            name: inlineTypeName,
            kind: "interface",
            properties: inlineProperties,
            sourceFile: path.relative(VS_CODE_TYPES_PATH, declaration.getSourceFile().fileName),
          }
          context.types.set(inlineTypeName, inlineTypeDef)
          
          elementType = inlineTypeName
          typeRef = { name: inlineTypeName, kind: "interface" }
          propertyType = "array"
        }
      }
    }
  }
  
  if (!typeRef) {
    if (type.flags & ts.TypeFlags.Union) {
      const unionType = type as ts.UnionType
      const literalTypes = unionType.types.filter((t) => 
        t.flags & (ts.TypeFlags.StringLiteral | ts.TypeFlags.NumberLiteral | ts.TypeFlags.BooleanLiteral)
      )
      
      // Check if this is a boolean union (true | false)
      // If all members are BooleanLiteral and there are exactly 2 members, it's the boolean type
      const allBooleanLiterals = unionType.types.every(t => t.flags & ts.TypeFlags.BooleanLiteral)
      if (allBooleanLiterals && unionType.types.length === 2) {
        // This is boolean (true | false), not a literal
        propertyType = "boolean"
        typeRef = { name: "boolean", kind: "boolean" }
      } else if (literalTypes.length > 0 && literalTypes.length === unionType.types.length) {
        // All members are literals, but this is still a union (not a single literal)
        // Multi-member literal unions like "a" | "b" | "c" should be treated as unions
        propertyType = "union"
        const unionMemberNames = unionType.types.map((t) => {
          const literalValue = extractLiteralValue(t)
          return literalValue !== undefined ? String(literalValue) : getTypeName(t, typeChecker)
        })
        typeRef = { name: unionMemberNames[0], kind: "union" }
        elementType = unionMemberNames.join(" | ")
      } else {
        propertyType = "union"
        // Collect union member type names for better documentation
        const unionMemberNames = unionType.types.map((t) => {
          const typeName = getTypeName(t, typeChecker)
          // For primitive types, use the actual type name
          if (t.flags & ts.TypeFlags.String) return "string"
          if (t.flags & ts.TypeFlags.Number) return "number"
          if (t.flags & ts.TypeFlags.Boolean) return "boolean"
          if (t.flags & ts.TypeFlags.Null) return "null"
          if (t.flags & ts.TypeFlags.Undefined) return "undefined"
          if (t.flags & ts.TypeFlags.Any) return "any"
          return typeName
        })
        typeRef = { name: unionMemberNames[0], kind: "union" }
        // Store all union members in elementType for reference
        elementType = unionMemberNames.join(" | ")
      }
    } else if (type.flags & ts.TypeFlags.StringLiteral) {
      isLiteral = true
      propertyType = "literal"
      literalValue = (type as ts.StringLiteralType).value
    } else if (type.flags & ts.TypeFlags.NumberLiteral) {
      isLiteral = true
      propertyType = "literal"
      literalValue = (type as ts.NumberLiteralType).value
    } else if (type.flags & ts.TypeFlags.BooleanLiteral) {
      // Specific boolean literal (true or false) - check BEFORE base Boolean
      // Debug: log when we hit this case
      if (name === 'ok') {
        console.log(`DEBUG: ok property has BooleanLiteral flag, value: ${(type as ts.BooleanLiteralType).value}`)
      }
      isLiteral = true
      propertyType = "literal"
      literalValue = (type as ts.BooleanLiteralType).value
    } else if (type.flags & ts.TypeFlags.Boolean) {
      // Base boolean type (not a literal)
      if (name === 'ok') {
        console.log(`DEBUG: ok property has Boolean flag (not Literal)`)
      }
      propertyType = "boolean"
      typeRef = { name: "boolean", kind: "boolean" }
    } else if (type.flags & ts.TypeFlags.Array || type.symbol?.name === "Array") {
      propertyType = "array"
      const typeArgs = (type as ts.TypeReference).typeArguments
      if (typeArgs && typeArgs.length > 0) {
        // Check if the array element is an inline type literal
        if (typeArgs[0]!.flags & ts.TypeFlags.Object && ts.isTypeLiteralNode((typeArgs[0] as any).symbol?.declarations?.[0])) {
          const typeLiteral = (typeArgs[0] as any).symbol?.declarations?.[0]
          if (typeLiteral && ts.isTypeLiteralNode(typeLiteral)) {
            const inlineProperties: PropertyDefinition[] = []
            for (const member of typeLiteral.members) {
              if (ts.isPropertySignature(member) && member.name) {
                const memberSymbol = typeChecker.getSymbolAtLocation(member.name)
                if (memberSymbol) {
                  const memberPropDef = extractPropertyDefinition(memberSymbol, typeChecker, context)
                  if (memberPropDef) {
                    inlineProperties.push(memberPropDef)
                  }
                }
              }
            }
            if (inlineProperties.length > 0) {
              const inlineTypeName = `${name}ItemType`
              const inlineTypeDef: TypeDefinition = {
                name: inlineTypeName,
                kind: "interface",
                properties: inlineProperties,
                sourceFile: path.relative(VS_CODE_TYPES_PATH, declaration.getSourceFile().fileName),
              }
              context.types.set(inlineTypeName, inlineTypeDef)
              elementType = inlineTypeName
              typeRef = { name: inlineTypeName, kind: "interface" }
            }
          }
        }
        if (!elementType) {
          elementType = getTypeName(typeArgs[0], typeChecker)
          typeRef = { name: getTypeName(typeArgs[0], typeChecker), kind: getTypeKind(typeArgs[0]) }
        }
      }
    } else if (type.flags & ts.TypeFlags.Object && type.symbol?.name === "Record") {
      propertyType = "record"
      const typeArgs = (type as ts.TypeReference).typeArguments
      if (typeArgs && typeArgs.length >= 2) {
        elementType = getTypeName(typeArgs[1], typeChecker)
      }
    } else {
      propertyType = getTypeName(type, typeChecker)
      typeRef = { name: propertyType, kind: getTypeKind(type) }
    }
  } else if (!propertyType && typeRef) {
    // Only set propertyType from typeRef if it hasn't been set already
    propertyType = typeRef.name
  }

  return {
    name,
    type: propertyType,
    optional: isOptional !== 0,
    nullable: false,
    elementType,
    typeRef,
    literalValue,
    isLiteral,
  }
}

/**
 * Extract discriminator information from properties
 * 
 * Looks for literal properties named "type", "status", or "role"
 * that can serve as discriminators for polymorphic deserialization.
 * 
 * @param properties - Extracted property definitions
 * @returns Discriminator info if found, null otherwise
 */
function extractDiscriminator(properties: PropertyDefinition[]): DiscriminatorInfo | null {
  const discriminatorFields = ["type", "status", "role"]
  
  for (const field of discriminatorFields) {
    const prop = properties.find((p) => p.name === field)
    if (prop && prop.isLiteral && prop.literalValue !== null) {
      return {
        field,
        value: String(prop.literalValue),
      }
    }
  }
  
  return null
}

/**
 * Extract an interface declaration from TypeScript AST
 * 
 * ## Extraction Process
 * 1. Get interface name
 * 2. Extract all property signatures
 * 3. Detect discriminator field
 * 4. Extract base types from extends clause
 * 5. Record source file location
 * 
 * @param node - Interface declaration node
 * @param context - Extraction context
 * @returns Type definition or null if extraction fails
 */
function extractInterfaceDeclaration(
  node: ts.InterfaceDeclaration,
  context: ExtractionContext
): TypeDefinition | null {
  const name = node.name.text
  const typeChecker = context.typeChecker
  
  const properties: PropertyDefinition[] = []
  
  for (const member of node.members) {
    if (ts.isPropertySignature(member)) {
      const propertySymbol = typeChecker.getSymbolAtLocation(member.name)
      if (propertySymbol) {
        const propDef = extractPropertyDefinition(propertySymbol, typeChecker, context)
        if (propDef) {
          properties.push(propDef)
        }
      }
    }
  }

  const discriminator = extractDiscriminator(properties)
  
  // Extract base types from extends clause
  let extendsBase: string | undefined
  if (node.heritageClauses && node.heritageClauses.length > 0) {
    for (const clause of node.heritageClauses) {
      if (clause.token === ts.SyntaxKind.ExtendsKeyword) {
        for (const typeRef of clause.types) {
          const baseTypeName = typeRef.expression.getText()
          // For simple extends (e.g., "extends BasePart"), use the base type name
          // For generic extends (e.g., "extends BasePart<T>"), extract just the type name
          const simpleName = baseTypeName.split('<')[0]
          if (!extendsBase) {
            extendsBase = simpleName
          }
        }
      }
    }
  }

  const typeDef: TypeDefinition = {
    name,
    kind: "interface",
    properties,
    discriminator: discriminator || undefined,
    sourceFile: path.relative(VS_CODE_TYPES_PATH, node.getSourceFile().fileName),
    extendsBase,
  }
  
  // Compute signature hash for types with discriminators
  if (typeDef.discriminator && typeDef.properties) {
    typeDef.signatureHash = computeSignatureHash(typeDef)
  }
  
  return typeDef
}

/**
 * Extract a type alias declaration from TypeScript AST
 * 
 * ## Handles Multiple Patterns
 * - **Union types**: Extract all union member type names
 * - **String literal unions**: Parse source file to preserve literal values
 * - **Type literals**: Extract inline object structure
 * - **Type references**: Resolve to referenced type
 * - **Partial<T> & Pick<T, ...>**: Merge properties with correct optionality
 * - **Re-exports**: Resolve alias to actual type
 * 
 * @param node - Type alias declaration node
 * @param context - Extraction context
 * @returns Type definition or null if extraction fails
 */
function extractTypeAliasDeclaration(
  node: ts.TypeAliasDeclaration,
  context: ExtractionContext
): TypeDefinition | null {
  const name = node.name.text
  const typeChecker = context.typeChecker
  const type = typeChecker.getTypeAtLocation(node)

  if (type.flags & ts.TypeFlags.Union) {
    const unionType = type as ts.UnionType
    const members: string[] = []
    
    for (const memberType of unionType.types) {
      // Try to get the name from the symbol first
      let memberName = getTypeName(memberType, typeChecker)
      
      // Check if this is an inline object type (type literal)
      // Inline types have symbol name '__type' and the node is a TypeLiteralNode
      const memberSymbol = memberType.getSymbol()
      const isInlineObject = memberName === '__type' && 
        (memberType.flags & ts.TypeFlags.Object) &&
        memberSymbol?.declarations &&
        memberSymbol.declarations.length > 0 &&
        ts.isTypeLiteralNode(memberSymbol.declarations[0])
      
      if (isInlineObject) {
        // Extract properties from the inline type literal
        const typeLiteral = memberSymbol.declarations[0] as ts.TypeLiteralNode
        const inlineProperties: PropertyDefinition[] = []
        let discriminator: DiscriminatorInfo | undefined
        
        for (const member of typeLiteral.members) {
          if (ts.isPropertySignature(member) && member.name) {
            const propSymbol = typeChecker.getSymbolAtLocation(member.name)
            if (propSymbol) {
              const propDef = extractPropertyDefinition(propSymbol, typeChecker, context)
              if (propDef) {
                inlineProperties.push(propDef)
                // Check for discriminator
                if (propDef.name === 'type' && propDef.isLiteral && propDef.literalValue !== null) {
                  discriminator = {
                    field: 'type',
                    value: String(propDef.literalValue),
                  }
                }
              }
            }
          }
        }
        
        if (inlineProperties.length > 0 && discriminator) {
          // Generate a unique name for this inline type
          const discriminatorValue = String(discriminator.value)
          const inlineTypeName = `${toPascalCase(discriminatorValue)}Message`
          
          // Create a type definition for this inline type
          const inlineTypeDef: TypeDefinition = {
            name: inlineTypeName,
            kind: "interface",
            properties: inlineProperties,
            discriminator: discriminator,
            sourceFile: path.relative(VS_CODE_TYPES_PATH, node.getSourceFile().fileName),
          }
          
          // Compute signature hash
          inlineTypeDef.signatureHash = computeSignatureHash(inlineTypeDef)
          
          // Add to context.types so it can be referenced
          if (!context.types.has(inlineTypeName)) {
            context.types.set(inlineTypeName, inlineTypeDef)
          }
          
          // Use the generated name as the member name
          memberName = inlineTypeName
        }
      }
      
      // If we still got __type, try to parse from source file
      if (memberName === '__type') {
        const sourceFile = node.getSourceFile()
        const sourceText = sourceFile.getFullText()
        const typeName = node.name.text
        
        // Look for the line containing "export type TypeName = ..."
        const lines = sourceText.split('\n')
        for (const line of lines) {
          const trimmed = line.trim()
          if (trimmed.startsWith(`export type ${typeName} =`)) {
            // Extract everything after the =
            const afterEquals = trimmed.substring(trimmed.indexOf('=') + 1).trim()
            // Remove trailing semicolon if present
            const withoutSemi = afterEquals.replace(/;$/, '').trim()
            // Split by | and clean up
            const parsedMembers = withoutSemi.split('|').map(m => m.trim()).filter(m => m && !m.startsWith('{'))
            if (parsedMembers.length > 0) {
              for (const parsedMember of parsedMembers) {
                if (!members.includes(parsedMember)) {
                  members.push(parsedMember)
                }
              }
              break
            }
          }
        }
      }
      
      if (memberName !== '__type' && !members.includes(memberName)) {
        members.push(memberName)
      }
    }

    return {
      name,
      kind: "union",
      unionMembers: members,
      sourceFile: path.relative(VS_CODE_TYPES_PATH, node.getSourceFile().fileName),
    }
  }

  // Check if the type alias has a type literal (object structure)
  if (node.type && ts.isTypeLiteralNode(node.type)) {
    const properties: PropertyDefinition[] = []
    let discriminator: DiscriminatorInfo | undefined
    
    for (const member of node.type.members) {
      if (ts.isPropertySignature(member) && member.name) {
        const propName = member.name.getText()
        const propType = member.type ? typeChecker.getTypeAtLocation(member.type) : null
        const propKind = propType ? getTypeKind(propType) : "object"
        
        // Check if this is a literal type (e.g., type: "partUpdated")
        let isLiteral = false
        let literalValue: string | number | boolean | null = null
        if (member.type && ts.isLiteralTypeNode(member.type)) {
          isLiteral = true
          const literal = member.type.literal
          if (ts.isStringLiteral(literal)) {
            literalValue = literal.text
          } else if (ts.isNumericLiteral(literal)) {
            literalValue = parseFloat(literal.text)
          }
        }
        
        properties.push({
          name: propName,
          type: propKind,
          optional: member.questionToken !== undefined,
          nullable: false,
          isLiteral,
          elementType: null,
          typeRef: null,
          literalValue,
        })
        
        // Check if this is the discriminator property
        if (propName === 'type' && isLiteral && literalValue) {
          discriminator = {
            field: 'type',
            value: literalValue as string,
          }
        }
      }
    }

    if (properties.length > 0) {
      const typeDef: TypeDefinition = {
        name,
        kind: "interface",
        properties,
        discriminator,
        sourceFile: path.relative(VS_CODE_TYPES_PATH, node.getSourceFile().fileName),
      }
      
      // Compute signature hash for types with discriminators
      if (typeDef.discriminator && typeDef.properties) {
        typeDef.signatureHash = computeSignatureHash(typeDef)
      }
      
      return typeDef
    }
  }

  // For type aliases that reference other types, check if the referenced type has properties
  if (node.type) {
    let referencedTypeName: string | null = null
    
    if (ts.isTypeReferenceNode(node.type)) {
      // Handle both simple references (Type) and generic references (Type<T>)
      referencedTypeName = node.type.typeName.getText()
    } else if (ts.isTypeAliasDeclaration(node.type)) {
      // Nested type alias
      referencedTypeName = node.type.name.text
    }
    
    if (referencedTypeName && context.types.has(referencedTypeName)) {
      const referencedType = context.types.get(referencedTypeName)!
      if (referencedType.properties && referencedType.properties.length > 0) {
        // Check if this type alias has a discriminator in the referenced type
        const typeProp = referencedType.properties.find(p => p.name === 'type')
        if (typeProp) {
          const typeDef: TypeDefinition = {
            name,
            kind: "interface",
            properties: referencedType.properties,
            discriminator: referencedType.discriminator,
            sourceFile: path.relative(VS_CODE_TYPES_PATH, node.getSourceFile().fileName),
          }
          
          // Compute signature hash for types with discriminators
          if (typeDef.discriminator && typeDef.properties) {
            typeDef.signatureHash = computeSignatureHash(typeDef)
          }
          
          return typeDef
        }
      }
    }
    
    // Handle Partial<T> & Pick<T, "field1" | "field2"> pattern
    if (ts.isIntersectionTypeNode(node.type)) {
      const intersectionType = node.type
      const partialTypes: string[] = []
      const pickTypes: { typeName: string, fields: string[] }[] = []
      
      for (const typeNode of intersectionType.types) {
        if (ts.isTypeReferenceNode(typeNode)) {
          const typeName = typeNode.typeName.getText()
          
          // Check if it's Partial<T>
          if (typeName === 'Partial' && typeNode.typeArguments && typeNode.typeArguments.length > 0) {
            const argType = typeNode.typeArguments[0]
            if (ts.isTypeReferenceNode(argType)) {
              partialTypes.push(argType.typeName.getText())
            }
          }
          
          // Check if it's Pick<T, "field1" | "field2">
          if (typeName === 'Pick' && typeNode.typeArguments && typeNode.typeArguments.length >= 2) {
            const baseType = typeNode.typeArguments[0]
            const fieldsType = typeNode.typeArguments[1]
            
            if (ts.isTypeReferenceNode(baseType)) {
              const baseTypeName = baseType.typeName.getText()
              const fields: string[] = []
              
              // Extract fields from union type or single literal
              if (ts.isUnionTypeNode(fieldsType)) {
                for (const field of fieldsType.types) {
                  if (ts.isLiteralTypeNode(field) && ts.isStringLiteral(field.literal)) {
                    fields.push(field.literal.text)
                  }
                }
              } else if (ts.isLiteralTypeNode(fieldsType) && ts.isStringLiteral(fieldsType.literal)) {
                fields.push(fieldsType.literal.text)
              }
              
              if (fields.length > 0) {
                pickTypes.push({ typeName: baseTypeName, fields })
              }
            }
          }
        }
      }
      
      // If we found Partial<T> and Pick<T, ...>, merge them
      if (partialTypes.length > 0 && pickTypes.length > 0) {
        for (const partialType of partialTypes) {
          if (context.types.has(partialType)) {
            const baseType = context.types.get(partialType)!
            if (baseType.properties && baseType.properties.length > 0) {
              // Get required fields from Pick
              const requiredFields = new Set<string>()
              for (const pick of pickTypes) {
                if (pick.typeName === partialType) {
                  for (const field of pick.fields) {
                    requiredFields.add(field)
                  }
                }
              }
              
              // Make all properties optional except the picked ones
              const mergedProperties = baseType.properties.map(prop => ({
                ...prop,
                optional: !requiredFields.has(prop.name),
              }))
              
              const typeDef: TypeDefinition = {
                name,
                kind: "interface",
                properties: mergedProperties,
                discriminator: baseType.discriminator,
                sourceFile: path.relative(VS_CODE_TYPES_PATH, node.getSourceFile().fileName),
                baseType: partialType,
                requiredFields: Array.from(requiredFields),
              }
              
              // Compute signature hash for types with discriminators
              if (typeDef.discriminator && typeDef.properties) {
                typeDef.signatureHash = computeSignatureHash(typeDef)
              }
              
              return typeDef
            }
          }
        }
      }
    }
  }
  
  // For re-export type aliases (export type { X as Y } from "..."), use the type checker to resolve
  // The type checker should resolve the alias to the actual type
  const resolvedType = typeChecker.getTypeAtLocation(node)
  const resolvedSymbol = resolvedType.getSymbol()
  if (resolvedSymbol) {
    const resolvedDeclarations = resolvedSymbol.getDeclarations()
    if (resolvedDeclarations && resolvedDeclarations.length > 0) {
      const resolvedDeclaration = resolvedDeclarations[0]
      const resolvedSourceFile = resolvedDeclaration.getSourceFile()
      const resolvedTypeName = resolvedSymbol.getName()
      
      // If the resolved type is from a different file, check if we have it in context.types
      if (resolvedTypeName && context.types.has(resolvedTypeName)) {
        const resolvedTypeDef = context.types.get(resolvedTypeName)!
        if (resolvedTypeDef.properties && resolvedTypeDef.properties.length > 0) {
          const typeDef: TypeDefinition = {
            name,
            kind: resolvedTypeDef.kind,
            properties: resolvedTypeDef.properties,
            discriminator: resolvedTypeDef.discriminator,
            sourceFile: path.relative(VS_CODE_TYPES_PATH, resolvedSourceFile.fileName),
          }
          
          // Compute signature hash for types with discriminators
          if (typeDef.discriminator && typeDef.properties) {
            typeDef.signatureHash = computeSignatureHash(typeDef)
          }
          
          return typeDef
        }
      }
    }
  }

  return {
    name,
    kind: "typeAlias",
    sourceFile: path.relative(VS_CODE_TYPES_PATH, node.getSourceFile().fileName),
  }
}

/**
 * Extract message types from a TypeScript source file
 * 
 * ## Multi-Pass Extraction
 * Uses four passes to handle type dependencies correctly:
 * 
 * 1. **First pass**: Extract all interfaces
 * 2. **Second pass**: Extract all type aliases (interfaces now available for reference)
 * 3. **Third pass**: Re-extract interfaces (to resolve type alias references)
 * 4. **Fourth pass**: Process messages (interfaces with discriminators)
 * 
 * @param sourceFile - TypeScript source file to process
 * @param context - Extraction context
 */
function extractMessageTypes(
  sourceFile: ts.SourceFile,
  context: ExtractionContext
): void {
  const typeChecker = context.typeChecker
  
  // First pass: Extract all interfaces first
  const visitFirstPass = (node: ts.Node): void => {
    if (ts.isInterfaceDeclaration(node)) {
      const typeDef = extractInterfaceDeclaration(node, context)
      if (typeDef) {
        context.types.set(typeDef.name, typeDef)
      }
    }
    ts.forEachChild(node, visitFirstPass)
  }
  
  visitFirstPass(sourceFile)
  
  // Second pass: Extract all type aliases (now that interfaces are available)
  const visitSecondPass = (node: ts.Node): void => {
    if (ts.isTypeAliasDeclaration(node)) {
      const typeDef = extractTypeAliasDeclaration(node, context)
      if (typeDef) {
        context.types.set(typeDef.name, typeDef)
      }
    }
    ts.forEachChild(node, visitSecondPass)
  }
  
  visitSecondPass(sourceFile)
  
  // Third pass: Extract interfaces again (in case they reference type aliases from pass 2)
  const visitThirdPass = (node: ts.Node): void => {
    if (ts.isInterfaceDeclaration(node)) {
      const typeDef = extractInterfaceDeclaration(node, context)
      if (typeDef) {
        // Update the interface if we got more complete information
        context.types.set(typeDef.name, typeDef)
      }
    }
    ts.forEachChild(node, visitThirdPass)
  }
  
  visitThirdPass(sourceFile)
  
  // Fourth pass: Process messages (interfaces with discriminators)
  const visitFourthPass = (node: ts.Node): void => {
    if (!ts.isInterfaceDeclaration(node)) {
      ts.forEachChild(node, visitFourthPass)
      return
    }
    
    const typeDef = context.types.get(node.name.text)
    if (!typeDef || !typeDef.discriminator || !typeDef.properties) {
      ts.forEachChild(node, visitFourthPass)
      return
    }

    const messageType: MessageType = {
      name: typeDef.name,
      type: "interface",
      discriminator: typeDef.discriminator,
      properties: typeDef.properties,
      sourceFile: typeDef.sourceFile,
    }
    
    // Determine direction based on source file: webview-messages.ts → webviewToExtension, extension-messages.ts → extensionToWebview
    const folder = getSourceFileFolder(typeDef.sourceFile)
    const targetArray = folder === 'WebviewMessages' 
      ? context.messages.webviewToExtension 
      : context.messages.extensionToWebview
    
    const existing = targetArray.find(m => m.name === typeDef.name)
    if (!existing) {
      targetArray.push(messageType)
    }

    ts.forEachChild(node, visitFourthPass)
  }
  
  visitFourthPass(sourceFile)
}

/**
 * Deduplicate types by signature hash.
 * 
 * ## Deduplication Rules (in order)
 * 1. **Shared folder priority**: If multiple types have the same hash and one is in Shared, keep it and replace all others
 * 2. **Prefix matching**: If names differ and one is a prefix of another, keep the shorter name, move to Shared
 * 3. **First occurrence**: If none in Shared, keep first with same name, move to Shared, replace others
 * 
 * ## Step 2: Union Interface Generation
 * For each union type, generate a canonical interface name and add it to all types matching the union's member hashes.
 * 
 * @param types - Map of type definitions
 * @param unions - Array of union type definitions
 * @returns Deduplicated types map and updated unions
 */
function deduplicateTypes(
  types: Map<string, TypeDefinition>,
  unions: TypeDefinition[]
): { types: Map<string, TypeDefinition>; unions: TypeDefinition[]; dedupStats: { removed: number; renamed: number; movedToShared: number } } {
  const stats = { removed: 0, renamed: 0, movedToShared: 0 }
  
  // Build hash -> types map
  const hashToTypes = new Map<string, TypeDefinition[]>()
  for (const typeDef of types.values()) {
    if (typeDef.signatureHash) {
      if (!hashToTypes.has(typeDef.signatureHash)) {
        hashToTypes.set(typeDef.signatureHash, [])
      }
      hashToTypes.get(typeDef.signatureHash)!.push(typeDef)
    }
  }
  
  // Track replacements: old name -> new canonical name
  const replacements = new Map<string, string>()
  
  // Process each hash group
  for (const [hash, typeGroup] of hashToTypes.entries()) {
    if (typeGroup.length <= 1) continue
    
    // Sort by source file path to have deterministic ordering
    typeGroup.sort((a, b) => a.sourceFile.localeCompare(b.sourceFile))
    
    // Rule 1: Check if any is in Shared
    // Check for shared folder in path (case-insensitive)
    const sharedType = typeGroup.find(t => {
      const path = t.sourceFile.toLowerCase()
      return path.includes('shared\\') || path.includes('shared/') || 
             path.includes('\\shared\\') || path.includes('\\shared/') ||
             path.startsWith('shared\\') || path.startsWith('shared/')
    })
    
    let canonicalType: TypeDefinition
    
    if (sharedType) {
      // Use Shared type as canonical
      canonicalType = sharedType
    } else {
      // Rule 2: Check for prefix matching
      const names = typeGroup.map(t => t.name).sort((a, b) => a.length - b.length)
      let prefixFound = false
      
      for (let i = 1; i < names.length; i++) {
        if (names[i]!.startsWith(names[0]!)) {
          // names[0] is prefix of names[i]
          const prefixType = typeGroup.find(t => t.name === names[0])
          if (prefixType) {
            canonicalType = prefixType
            prefixFound = true
            stats.renamed += typeGroup.length - 1
            
            // Create replacement mappings for longer names
            for (let j = 1; j < names.length; j++) {
              replacements.set(names[j]!, names[0]!)
            }
            break
          }
        }
      }
      
      if (!prefixFound) {
        // Rule 3: Use shortest name (first after sorting by length), move to Shared
        canonicalType = typeGroup.find(t => t.name === names[0])!
        const canonicalName = names[0]!
        
        // Move to Shared if not already
        const path = canonicalType.sourceFile
        if (!path.toLowerCase().includes('shared\\') && !path.toLowerCase().includes('shared/')) {
          canonicalType.sourceFile = 'Shared\\' + path.split(/[\\/]/).pop()!
          stats.movedToShared++
        }
        
        stats.renamed += typeGroup.length - 1
        
        // Create replacement mappings for all other names
        for (let i = 1; i < names.length; i++) {
          replacements.set(names[i]!, canonicalName)
        }
      }
    }
    
    // Mark all non-canonical types for removal
    for (const typeDef of typeGroup) {
      if (typeDef !== canonicalType) {
        replacements.set(typeDef.name, canonicalType.name)
        stats.removed++
      }
    }
  }
  
  // Apply replacements: remove duplicate types and update references
  // Remove types that have replacements
  const toRemove: string[] = []
  for (const [name, typeDef] of types.entries()) {
    if (replacements.has(name) && replacements.get(name) !== name) {
      toRemove.push(name)
    }
  }
  
  for (const name of toRemove) {
    types.delete(name)
  }
  
  // Update union member references
  for (const union of unions) {
    if (union.kind === 'union' && union.unionMembers) {
      union.unionMembers = union.unionMembers.map(member => {
        const replacement = replacements.get(member)
        return replacement || member
      })
      
      // Remove duplicates after replacement
      union.unionMembers = [...new Set(union.unionMembers)]
    }
  }
  
  return { types, unions, dedupStats: stats }
}

/**
 * Create the WebViewContract from extraction context
 * 
 * Includes metadata about the extraction:
 * - Git branch and commit
 * - TypeScript version
 * - Extraction statistics
 * - Diagnostics (errors and warnings)
 * 
 * @param context - Extraction context with all extracted data
 * @returns Complete WebViewContract object
 */
function createContract(context: ExtractionContext): WebViewContract {
  let branch = "unknown"
  let commit = "unknown"
  try {
    const { execSync } = require("child_process")
    branch = execSync("git rev-parse --abbrev-ref HEAD", { encoding: "utf-8" }).trim()
    commit = execSync("git rev-parse HEAD", { encoding: "utf-8" }).trim()
  } catch {
    // Git not available
  }
  
  // Convert types to array
  let allTypes = Array.from(context.types.values())
  
  // Separate unions from other types
  const unions = allTypes.filter(t => t.kind === 'union')
  let nonUnionTypes = allTypes.filter(t => t.kind !== 'union')
  
  // Deduplicate types by signature hash (only non-union types)
  const typeMap = new Map(nonUnionTypes.map(t => [t.name, t]))
  const dedupResult = deduplicateTypes(typeMap, unions)
  nonUnionTypes = Array.from(dedupResult.types.values())
  
  console.log()
  console.log("Deduplication statistics:")
  console.log(`  Types removed: ${dedupResult.dedupStats.removed}`)
  console.log(`  Types renamed: ${dedupResult.dedupStats.renamed}`)
  console.log(`  Types moved to Shared: ${dedupResult.dedupStats.movedToShared}`)
  
  // Combine deduplicated non-union types with unions
  const types = [...nonUnionTypes, ...dedupResult.unions]
  
  // Post-process: Compute union member hashes for union types
  // Build a map of type name -> signature hash for quick lookup
  const typeHashMap = new Map<string, string>()
  for (const typeDef of types) {
    if (typeDef.signatureHash) {
      typeHashMap.set(typeDef.name, typeDef.signatureHash)
    }
  }
  
  // For each union type, compute the hashes of its members
  for (const typeDef of types) {
    if (typeDef.kind === 'union' && typeDef.unionMembers) {
      const hashes: string[] = []
      for (const memberName of typeDef.unionMembers) {
        const hash = typeHashMap.get(memberName)
        if (hash) {
          hashes.push(hash)
        } else {
          // Member type not found or has no signature hash (e.g., primitive types)
          // Use the member name as a placeholder
          hashes.push(`__${memberName}`)
        }
      }
      typeDef.unionMemberHashes = hashes
    }
  }
  
  return {
    schemaVersion: "1.0.0",
    generatedFrom: {
      repository: "kilocode",
      branch,
      commit,
      generatedAt: new Date().toISOString(),
    } as SourceInfo,
    metadata: {
      extractorVersion: "1.0.0",
      typescriptVersion: ts.version,
      sourcePath: VS_CODE_TYPES_PATH,
    } as Metadata,
    messages: {
      webviewToExtension: context.messages.webviewToExtension,
      extensionToWebview: context.messages.extensionToWebview,
    } as MessageCollections,
    types,
    diagnostics: {
      errors: context.errors,
      warnings: context.warnings,
    } as Diagnostics,
    statistics: {
      totalTypes: types.length,
      webviewToExtensionMessages: context.messages.webviewToExtension.length,
      extensionToWebviewMessages: context.messages.extensionToWebview.length,
      inlineMessagesExtracted: context.extractedMessages.size,
    } as Statistics,
  }
}

/**
 * Determine the output folder based on source file name
 * 
 * See generator.ts getSourceFileFolder() for detailed documentation
 * 
 * @param sourceFile - Source file path
 * @returns Target folder name
 */
function getSourceFileFolder(sourceFile: string): string {
  const normalizedSource = sourceFile.replace(/\\/g, '/')
  
  // Extract filename from path
  const filename = normalizedSource.split('/').pop() || ''
  
  // Special handling for extension-messages.ts and webview-messages.ts
  if (filename === 'extension-messages.ts') return 'ExtensionMessages'
  if (filename === 'webview-messages.ts') return 'WebviewMessages'
  
  // marketplace.ts goes to Shared folder (not a message file)
  if (filename === 'marketplace.ts') return 'Shared'
  
  // Derive folder name from filename with PascalCase (e.g., agent-manager.ts → AgentManager)
  if (filename.endsWith('.ts')) {
    const baseName = filename.slice(0, -3) // Remove .ts
    const camelCase = baseName.replace(/-([a-z])/g, (match) => match.charAt(1).toUpperCase())
    return camelCase.charAt(0).toUpperCase() + camelCase.slice(1)
  }
  
  return 'Shared'
}

/**
 * Generate a message name from a discriminator value
 * 
 * Example: "configLoaded" → "ConfigLoadedMessage"
 * 
 * @param discValue - Discriminator value
 * @returns Generated message name
 */
function generateMessageNameFromDiscriminator(discValue: string): string {
  return discValue
    .split(".")
    .map(p => p.charAt(0).toUpperCase() + p.slice(1))
    .join("") + "Message"
}

/**
 * Result of extracting properties from a postMessage call
 */
interface PostMessageExtractResult {
  properties: PropertyDefinition[]  // Extracted properties
  discriminator: DiscriminatorInfo  // Discriminator information
}

/**
 * Extract properties from a postMessage() call expression
 * 
 * ## Extraction Process
 * 1. Verify it's a postMessage or sendMessage call
 * 2. Extract object literal argument
 * 3. Parse each property assignment
 * 4. Determine property types from literals
 * 5. Identify discriminator field
 * 
 * @param node - Call expression node
 * @returns Extracted properties and discriminator, or null if not a valid postMessage call
 */
function extractPostMessageProperties(node: ts.CallExpression): PostMessageExtractResult | null {
  const expression = node.expression
  let methodName: string | undefined
  
  if (ts.isPropertyAccessExpression(expression)) {
    methodName = expression.name.getText()
  } else if (ts.isCallExpression(expression)) {
    return null
  }
  
  if (methodName !== "postMessage" && methodName !== "sendMessage") {
    return null
  }
  
  const args = node.arguments
  if (args.length === 0 || !ts.isObjectLiteralExpression(args[0])) {
    return null
  }
  
  const properties: PropertyDefinition[] = []
  let discriminator: DiscriminatorInfo | undefined
  
  for (const prop of args[0].properties) {
    if (!ts.isPropertyAssignment(prop)) {
      continue
    }
    
    const propName = prop.name.getText()
    const propValue = prop.initializer
    
    let propType: string
    let literalValue: string | number | boolean | null = null
    let isLiteral = false
    
    if (ts.isStringLiteral(propValue)) {
      propType = "literal"
      literalValue = propValue.text
      isLiteral = true
    } else if (ts.isNumericLiteral(propValue)) {
      propType = "literal"
      literalValue = parseFloat(propValue.text)
      isLiteral = true
    } else if (propValue.kind === ts.SyntaxKind.TrueKeyword) {
      propType = "boolean"
      literalValue = true
      isLiteral = true
    } else if (propValue.kind === ts.SyntaxKind.FalseKeyword) {
      propType = "boolean"
      literalValue = false
      isLiteral = true
    } else {
      propType = "object"
    }
    
    properties.push({
      name: propName,
      type: propType,
      optional: false,
      nullable: false,
      elementType: null,
      typeRef: null,
      literalValue,
      isLiteral,
    })
    
    if (propName === "type" && isLiteral && typeof literalValue === "string") {
      discriminator = {
        field: "type",
        value: literalValue,
      }
    }
  }
  
  if (properties.length === 0 || !discriminator) {
    return null
  }
  
  return { properties, discriminator }
}

/**
 * Scan a source file for postMessage() calls and extract inline messages
 * 
 * ## Purpose
 * Not all messages are defined as named interfaces. Some are inline object
 * literals passed to postMessage(). This function extracts those messages.
 * 
 * ## Process
 * 1. Skip files from node_modules
 * 2. Traverse AST looking for call expressions
 * 3. Extract postMessage/sendMessage calls
 * 4. Deduplicate by discriminator value
 * 5. Add to extensionToWebview messages (extension sends to webview)
 * 
 * @param sourceFile - Source file to scan
 * @param context - Extraction context
 */
function scanPostMessageCalls(
  sourceFile: ts.SourceFile,
  context: ExtractionContext
): void {
  // Skip files from node_modules
  if (sourceFile.fileName.includes("node_modules")) {
    return
  }
  
  const visit = (node: ts.Node): void => {
    if (ts.isCallExpression(node)) {
      const result = extractPostMessageProperties(node)
      if (result) {
        const { properties, discriminator } = result
        const messageKey = `${discriminator.field}:${discriminator.value}`
        
        if (!context.extractedMessages.has(messageKey)) {
          const messageType: MessageType = {
            name: generateMessageNameFromDiscriminator(discriminator.value),
            type: "inline",
            discriminator,
            properties,
            sourceFile: path.relative(VS_CODE_SRC_PATH, sourceFile.fileName),
          }
          
          context.extractedMessages.set(messageKey, messageType)
          
          // Inline messages from VS Code extension source files are always extensionToWebview
          // since the extension is sending them to the webview
          context.messages.extensionToWebview.push(messageType)
        }
      }
    }
    
    ts.forEachChild(node, visit)
  }
  
  ts.forEachChild(sourceFile, visit)
}

/**
 * Main entry point for the extractor
 * 
 * ## Execution Flow
 * 1. Load TypeScript project from tsconfig
 * 2. Find all TypeScript source files
 * 3. Process type definitions from webview types directory
 * 4. Scan VS Code source files for postMessage calls
 * 5. Generate contract JSON
 * 6. Output statistics and diagnostics
 */
function main(): void {
  console.log("WebView Contract Extractor")
  console.log("==========================")
  console.log()
  
  if (!fs.existsSync(TSCONFIG_PATH)) {
    console.error(`Error: tsconfig.json not found at ${TSCONFIG_PATH}`)
    process.exit(1)
  }

  console.log(`Reading TypeScript project from: ${TSCONFIG_PATH}`)
  
  const config = ts.readConfigFile(TSCONFIG_PATH, ts.sys.readFile)
  if (config.error) {
    console.error("Error reading tsconfig:", config.error)
    process.exit(1)
  }

  const parsedConfig = ts.parseJsonConfigFileContent(
    config.config,
    ts.sys,
    path.dirname(TSCONFIG_PATH)
  )

  const vsCodeSrcFiles = findTsFiles(VS_CODE_SRC_PATH).filter(f => 
    !f.includes("node_modules") && 
    !f.includes("webview-ui") &&
    !f.includes("test")
  )
  
  const program = ts.createProgram({
    rootNames: parsedConfig.fileNames.filter(f => 
      f.includes("webview-ui/src/types") || 
      f.includes("src/shared") ||
      f.includes("src/kilo-provider-utils.ts")
    ),
    options: parsedConfig.options,
  })

  const context = createContext(program)
  
  const sourceFiles = program.getSourceFiles()

  console.log(`Found ${sourceFiles.length} source files to process`)
  console.log()

  for (const sourceFile of sourceFiles) {
    // Only process files from the webview-ui/src/types directory, src/shared, or kilo-provider-utils.ts
    if (!sourceFile.fileName.includes("webview-ui/src/types") && 
        !sourceFile.fileName.includes("src/shared") &&
        !sourceFile.fileName.includes("src/kilo-provider-utils.ts")) {
      continue
    }
    
    const relativePath = path.relative(VS_CODE_TYPES_PATH, sourceFile.fileName)
    console.log(`Processing: ${relativePath}`)
    extractMessageTypes(sourceFile, context)
  }

  console.log()
  console.log("Scanning VS Code source files for postMessage calls and union types...")
  
  const allTsFiles = findTsFiles(VS_CODE_SRC_PATH).filter(f => 
    !f.includes("node_modules") && 
    !f.includes("webview-ui") &&
    !f.includes("test")
  )
  
  console.log(`Found ${allTsFiles.length} VS Code source files to scan`)
  
  for (const filePath of allTsFiles) {
    const relativePath = path.relative(VS_CODE_SRC_PATH, filePath)
    const sourceText = fs.readFileSync(filePath, "utf-8")
    
    // Extract all union types from this file using regex
    extractAllUnionsFromSource(sourceText, relativePath, context)
    
    const sourceFile = ts.createSourceFile(
      filePath,
      sourceText,
      ts.ScriptTarget.Latest,
      true
    )
    
    const visit = (node: ts.Node): void => {
      if (ts.isCallExpression(node)) {
        const result = extractPostMessageProperties(node)
        if (result) {
          const { properties, discriminator } = result
          
          // Skip if this message already exists in either direction
          const existingTypedMessage = context.messages.webviewToExtension.find(
            m => m.discriminator.value === discriminator.value
          )
          const existingTypedMessageExtToWeb = context.messages.extensionToWebview.find(
            m => m.discriminator.value === discriminator.value
          )
          
          if (!existingTypedMessage && !existingTypedMessageExtToWeb) {
            const existingMessage = context.extractedMessages.get(discriminator.value)
            if (!existingMessage) {
              const messageName = generateMessageNameFromDiscriminator(discriminator.value)
              context.extractedMessages.set(discriminator.value, {
                name: messageName,
                type: "interface",
                discriminator,
                properties,
                sourceFile: relativePath,
              })
              context.messages.webviewToExtension.push({
                name: messageName,
                type: "interface",
                discriminator,
                properties,
                sourceFile: relativePath,
              })
            }
          }
        }
      }
      ts.forEachChild(node, visit)
    }
    
    visit(sourceFile)
  }

  console.log()
  console.log("Generating contract...")
  
  const contract = createContract(context)
  
  const outputDir = path.dirname(OUTPUT_PATH)
  if (!fs.existsSync(outputDir)) {
    fs.mkdirSync(outputDir, { recursive: true })
  }
  
  fs.writeFileSync(OUTPUT_PATH, JSON.stringify(contract, null, 2))
  
  console.log()
  console.log("Extraction complete!")
  console.log(`Output: ${OUTPUT_PATH}`)
  console.log()
  console.log("Statistics:")
  console.log(`  Total types extracted: ${contract.statistics.totalTypes}`)
  console.log(`  WebView → Extension messages: ${contract.statistics.webviewToExtensionMessages}`)
  console.log(`  Extension → WebView messages: ${contract.statistics.extensionToWebviewMessages}`)
  
  if (contract.diagnostics.errors.length > 0) {
    console.log()
    console.log("Errors:")
    for (const error of contract.diagnostics.errors) {
      console.log(`  - ${error}`)
    }
  }
  
  if (contract.diagnostics.warnings.length > 0) {
    console.log()
    console.log("Warnings:")
    for (const warning of contract.diagnostics.warnings.slice(0, 10)) {
      console.log(`  - ${warning}`)
    }
    if (contract.diagnostics.warnings.length > 10) {
      console.log(`  ... and ${contract.diagnostics.warnings.length - 10} more`)
    }
  }
}

main()
