#!/usr/bin/env bun
import * as fs from "fs"
import * as path from "path"

// Contract is 2 levels up (from tools/webview-contract-extractor to packages/kilo-visualstudio)
const VS_DIR = path.resolve(__dirname, "..", "..")
const CONTRACT_PATH = path.join(VS_DIR, "porting/contract/WebViewContract.json")
const OUTPUT_PATH = path.join(VS_DIR, "KiloVisualStudioExtension/WebViewDto")

interface TypeReference {
  name: string
  kind: string
}

interface PropertyDefinition {
  name: string
  type: string
  optional: boolean
  nullable: boolean
  elementType?: string | null
  typeRef?: TypeReference | null
  literalValue?: string | number | boolean | null
  isLiteral: boolean
  description?: string
}

interface DiscriminatorInfo {
  field: string
  value: string
}

interface TypeDefinition {
  name: string
  kind: string
  properties?: PropertyDefinition[]
  unionMembers?: string[]
  discriminator?: DiscriminatorInfo
  sourceFile: string
  description?: string
}

interface MessageType {
  name: string
  type: string
  discriminator: DiscriminatorInfo
  properties: PropertyDefinition[]
  sourceFile: string
}

interface WebViewContract {
  schemaVersion: string
  messages: {
    webviewToExtension: MessageType[]
    extensionToWebview: MessageType[]
  }
  types: TypeDefinition[]
}

function pascalCase(name: string): string {
  if (!name) return name
  const converted = name.replace(/-([a-z])/g, (match) => match.charAt(1).toUpperCase())
  const sanitized = converted.replace(/[^a-zA-Z0-9_]/g, '')
  if (!sanitized) return "Value"
  return sanitized.charAt(0).toUpperCase() + sanitized.slice(1)
}

let contract: WebViewContract
let typeDefinitions: Map<string, TypeDefinition>
let generatedTypes: Set<string>
let processingTypes: Set<string>
let neededTypes: Set<string>
let collectingTypes: Set<string>

function isPrimitiveType(typeName: string): boolean {
  const lower = typeName.toLowerCase()
  return lower === 'string' || lower === 'number' || lower === 'integer' || 
         lower === 'boolean' || lower === 'any' || lower === 'unknown' ||
         lower === 'void' || lower === 'null' || lower === 'undefined'
}

// Common TypeScript types that have C# equivalents
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

function getCSharpTypeForCommonType(typeName: string): string | null {
  const lower = typeName.toLowerCase()
  if (csharpTypeMap.has(lower)) {
    return csharpTypeMap.get(lower)!
  }
  return null
}

function isInternalType(typeName: string): boolean {
  return typeName.includes('@') || typeName.includes(':') || 
         typeName.startsWith('"') || typeName.includes('::') ||
         typeName.includes('&') || typeName.includes('|') ||
         typeName.startsWith('__')
}

function mapToCSharpType(prop: PropertyDefinition): { type: string, originalType?: string, isNullable: boolean } {
  const baseType = prop.type.toLowerCase()
  
  if (baseType === 'string' || baseType === 'literal') return { type: 'string', isNullable: prop.optional || prop.nullable }
  if (baseType === 'number' || baseType === 'integer') return { type: 'double', isNullable: prop.optional || prop.nullable }
  if (baseType === 'boolean') return { type: 'bool', isNullable: prop.optional || prop.nullable }
  if (baseType === 'array') {
    const elemType = mapToCSharpType({ type: prop.elementType || 'object', optional: false, nullable: false, typeRef: prop.typeRef })
    return { type: `List<${elemType.type}>`, isNullable: prop.optional || prop.nullable }
  }
  if (baseType === 'record') return { type: 'Dictionary<string, object>', isNullable: prop.optional || prop.nullable }
  if (baseType === 'union') {
    // Use elementType if it contains the full union string (e.g., "string | undefined")
    if (prop.elementType && !prop.elementType.startsWith('List<')) {
      // Parse union to determine best C# type
      const unionParts = prop.elementType.split('|').map(p => p.trim())
      const nonNullParts = unionParts.filter(p => p !== 'undefined' && p !== 'null')
      
      if (nonNullParts.length === 1) {
        // Union is like "string | undefined" or "Type | null" - map to nullable
        let actualType = nonNullParts[0]
        // Check if it's a string literal (starts with quote)
        if (actualType.startsWith('"') || actualType.startsWith("'")) {
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
        // Check if it's an intersection type
        if (actualType.includes('&')) {
          return { type: 'object', isNullable: true, originalType: prop.elementType }
        }
        // Check if it's an internal TypeScript type
        if (isInternalType(actualType)) {
          return { type: 'object', isNullable: true, originalType: prop.elementType }
        }
        // Check if the type exists in the contract and has no properties (from node_modules)
        if (typeDefinitions.has(actualType)) {
          const typeDef = typeDefinitions.get(actualType)!
          const isNodeModules = typeDef.sourceFile.includes('node_modules')
          const hasNoProperties = !typeDef.properties || typeDef.properties.length === 0
          if (isNodeModules && hasNoProperties) {
            return { type: 'object', isNullable: true, originalType: prop.elementType }
          }
        }
        // Check if the type exists in the contract
        if (!typeDefinitions.has(actualType)) {
          return { type: 'object', isNullable: true, originalType: prop.elementType }
        }
        // For reference types, use Type?
        return { type: actualType, isNullable: true, originalType: prop.elementType }
      }
      // Multiple non-null types - use object
      return { type: 'object', isNullable: true, originalType: prop.elementType }
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
        return { type: 'object', isNullable: prop.optional || prop.nullable, originalType: refName }
      }
      if (typeDef.kind === 'union') {
        if (typeDef.unionMembers && typeDef.unionMembers.length > 0) {
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
      return { type: refName, isNullable: prop.optional || prop.nullable }
    }
  }
  
  return { type: 'object', isNullable: prop.optional || prop.nullable }
}

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
      // Skip null, undefined, string literals, and intersection types
      if (part !== 'undefined' && part !== 'null' && !part.startsWith('"') && !part.startsWith("'") && !part.includes('&')) {
        // Only add if the type actually exists in the contract
        if (typeDefinitions.has(part)) {
          collectingTypes.add(part)
          const typeDef = typeDefinitions.get(part)!
          if (typeDef.kind === 'interface' && typeDef.properties) {
            neededTypes.add(part)
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
    if (collectingTypes.has(prop.typeRef.name)) {
      return
    }
    
    if (typeDefinitions.has(prop.typeRef.name)) {
      collectingTypes.add(prop.typeRef.name)
      const typeDef = typeDefinitions.get(prop.typeRef.name)!
      if (typeDef.kind === 'interface' && typeDef.properties) {
        neededTypes.add(prop.typeRef.name)
        for (const p of typeDef.properties) {
          collectNeededTypes(p)
        }
      } else if (typeDef.kind === 'typeAlias') {
        // Type aliases are not generated - they'll map to object
      } else if (typeDef.kind === 'union' && typeDef.unionMembers) {
        for (const memberName of typeDef.unionMembers) {
          if (typeDefinitions.has(memberName)) {
            const memberDef = typeDefinitions.get(memberName)!
            if (memberDef.kind === 'interface' && memberDef.properties) {
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

function generateTypeClass(typeDef: TypeDefinition): string {
  const sb: string[] = []
  
  sb.push("// <auto-generated>")
  sb.push("//     This code was generated by WebViewContractGenerator.")
  sb.push("//     Do not modify this file directly as changes will be lost on regeneration.")
  sb.push("// </auto-generated>")
  sb.push("")
  sb.push("#nullable enable")
  sb.push("")
  sb.push("namespace KiloVisualStudioExtension.WebView.Generated;")
  sb.push("")
  sb.push("using System;")
  sb.push("using System.Collections.Generic;")
  sb.push("using System.Threading.Tasks;")
  sb.push("using Newtonsoft.Json;")
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
  
  if (typeDef.discriminator) {
    sb.push("/// <summary>")
    sb.push(`/// Part type: ${typeDef.name}`)
    sb.push(`/// Discriminator: ${typeDef.discriminator.field} = "${typeDef.discriminator.value}"`)
    sb.push(`/// Source: ${typeDef.sourceFile}`)
    sb.push("/// </summary>")
    if (sourceComment) sb.push(sourceComment)
  } else {
    sb.push("/// <summary>")
    sb.push(`/// Type: ${typeDef.name}`)
    sb.push(`/// Source: ${typeDef.sourceFile}`)
    sb.push("/// </summary>")
    if (sourceComment) sb.push(sourceComment)
  }
  
  sb.push(`public class ${typeDef.name}`)
  sb.push("{")

  if (typeDef.properties) {
    for (const prop of typeDef.properties) {
      const mapped = mapToCSharpType(prop)
      const nullable = mapped.isNullable ? "?" : ""
      const jsonAttr = prop.name !== typeDef.discriminator?.field
        ? `    [JsonProperty("${prop.name}"${mapped.isNullable ? ", NullValueHandling = NullValueHandling.Ignore" : ""})]\n`
        : ""
      const summary = prop.description ? `    /// <summary>${prop.description}</summary>\n` : ""
      const comment = mapped.originalType ? `    // Original TypeScript type: ${mapped.originalType}\n` : ""
      
      sb.push(jsonAttr + comment + summary + `    public ${mapped.type}${nullable} ${pascalCase(prop.name)} { get; set; }`)
    }
  }

  sb.push("}")
  sb.push("")

  return sb.join("\n")
}

function generateMessageClass(message: MessageType, ns: string): string {
  const sb: string[] = []
  
  sb.push("// <auto-generated>")
  sb.push("//     This code was generated by WebViewContractGenerator.")
  sb.push("//     Do not modify this file directly as changes will be lost on regeneration.")
  sb.push("//     Source: WebViewContract.json schema version " + contract.schemaVersion)
  sb.push("// </auto-generated>")
  sb.push("")
  sb.push("#nullable enable")
  sb.push("")
  sb.push("namespace " + ns + ";")
  sb.push("")
  sb.push("using System;")
  sb.push("using System.Collections.Generic;")
  sb.push("using System.Threading.Tasks;")
  sb.push("using Newtonsoft.Json;")
  sb.push("")
  sb.push("/// <summary>")
  sb.push("/// WebView message: " + message.name)
  sb.push("/// Discriminator: " + message.discriminator.field + " = \"" + message.discriminator.value + "\"")
  sb.push("/// Source: " + message.sourceFile)
  sb.push("/// </summary>")
  sb.push("public class " + message.name)
  sb.push("{")

  for (const prop of message.properties) {
    const mapped = mapToCSharpType(prop)
    const nullable = mapped.isNullable ? "?" : ""
    const jsonAttr = `    [JsonProperty("${prop.name}"${mapped.isNullable ? ", NullValueHandling = NullValueHandling.Ignore" : ""})]\n`
    const comment = mapped.originalType ? "    // Original TypeScript type: " + mapped.originalType + "\n" : ""
    
    sb.push(jsonAttr + comment + "    public " + mapped.type + nullable + " " + pascalCase(prop.name) + " { get; set; }")
  }

  sb.push("}")
  sb.push("")

  return sb.join("\n")
}

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
  sb.push("using KiloVisualStudioExtension.ApiClient.Json;")
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
  sb.push("    /// </summary>")
  sb.push("    public static T Deserialize<T>(JToken token) where T : class")
  sb.push("    {")
  sb.push("        var type = token[\"type\"]?.Value<string>();")
  sb.push("")
  sb.push("        return type switch")
  sb.push("        {")

  const seenDiscriminators = new Set<string>()
  const cases: string[] = []
  
  for (const message of webviewToExt) {
    const discValue = message.discriminator.value
    if (!seenDiscriminators.has(discValue)) {
      cases.push("            \"" + discValue + "\" => typeof(T) == typeof(" + message.name + ") ? (T)(object)token.ToObject<" + message.name + ">(Serializer)! : throw new JsonSerializationException(\"Type mismatch\"),")
      seenDiscriminators.add(discValue)
    }
  }

  for (const message of extToWebview) {
    const discValue = message.discriminator.value
    if (!seenDiscriminators.has(discValue)) {
      cases.push("            \"" + discValue + "\" => typeof(T) == typeof(" + message.name + ") ? (T)(object)token.ToObject<" + message.name + ">(Serializer)! : throw new JsonSerializationException(\"Type mismatch\"),")
      seenDiscriminators.add(discValue)
    }
  }
  
  sb.push(cases.join("\n"))

  sb.push("            _ => throw new JsonSerializationException(\"Unknown message type: \" + type)")
  sb.push("        };")
  sb.push("    }")
  sb.push("")
  sb.push("    /// <summary>")
  sb.push("    /// Deserialize a WebView message from JSON string using discriminator-based routing.")
  sb.push("    /// </summary>")
  sb.push("    public static T Deserialize<T>(string json) where T : class")
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
processingTypes = new Set()
collectingTypes = new Set()

console.log(`Contract version: ${contract.schemaVersion}`)
console.log(`Total types in contract: ${contract.types.length}`)
console.log(`WebView→Extension messages: ${contract.messages.webviewToExtension.length}`)
console.log(`Extension→WebView messages: ${contract.messages.extensionToWebview.length}`)
console.log()

const ns = "KiloVisualStudioExtension.WebView.Generated"
const webviewToExtDir = path.join(OUTPUT_PATH, "Messages", "WebviewToExtension")
const extToWebviewDir = path.join(OUTPUT_PATH, "Messages", "ExtensionToWebview")
const typesDir = path.join(OUTPUT_PATH, "Types")

fs.mkdirSync(webviewToExtDir, { recursive: true })
fs.mkdirSync(extToWebviewDir, { recursive: true })
fs.mkdirSync(typesDir, { recursive: true })

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
  for (const prop of message.properties) {
    collectNeededTypes(prop)
  }
}

for (const message of contract.messages.extensionToWebview) {
  for (const prop of message.properties) {
    collectNeededTypes(prop)
  }
}

console.log(`Types to generate: ${neededTypes.size}`)
console.log()

console.log("Generating type definitions...")

// Generate types in dependency order
const generatedOrder: string[] = []
const visited = new Set<string>()

function generateTypeWithDeps(typeName: string) {
  if (visited.has(typeName)) return
  visited.add(typeName)
  
  const typeDef = typeDefinitions.get(typeName)
  if (!typeDef || typeDef.kind !== 'interface' || !typeDef.properties) return
  
  // First generate all dependencies
  for (const prop of typeDef.properties) {
    if (prop.typeRef?.name && neededTypes.has(prop.typeRef.name)) {
      generateTypeWithDeps(prop.typeRef.name)
    }
    if (prop.elementType && neededTypes.has(prop.elementType)) {
      generateTypeWithDeps(prop.elementType)
    }
  }
  
  if (!generatedOrder.includes(typeName)) {
    generatedOrder.push(typeName)
  }
}

for (const typeName of neededTypes) {
  generateTypeWithDeps(typeName)
}

// Now generate in order
for (const typeName of generatedOrder) {
  // Skip if this type is also a message
  if (messageNames.has(typeName)) {
    continue
  }
  
  const typeDef = typeDefinitions.get(typeName)
  if (!typeDef) continue
  
  // Skip common TypeScript types that have C# equivalents (don't generate classes for them)
  const csharpEquivalent = getCSharpTypeForCommonType(typeName)
  if (csharpEquivalent) {
    continue
  }
  
  // Skip types from node_modules with no properties
  const normalizedSource = typeDef.sourceFile.replace(/\\/g, '/')
  const isNodeModules = normalizedSource.includes('node_modules')
  const hasNoProperties = !typeDef.properties || typeDef.properties.length === 0
  if (isNodeModules && hasNoProperties) {
    continue
  }
  
  const code = generateTypeClass(typeDef)
  const filePath = path.join(typesDir, `${typeDef.name}.cs`)
  fs.writeFileSync(filePath, code)
  generatedTypes.add(typeDef.name)
  console.log(`  Generated: ${typeDef.name}`)
}

console.log()
console.log("Generating message classes...")

let generatedCount = 0

for (const message of contract.messages.webviewToExtension) {
  const code = generateMessageClass(message, ns)
  const filePath = path.join(webviewToExtDir, `${message.name}.cs`)
  fs.writeFileSync(filePath, code)
  generatedCount++
  if (generatedCount <= 10 || generatedCount % 20 === 0) {
    console.log(`  Generated: ${message.name}`)
  }
}

for (const message of contract.messages.extensionToWebview) {
  const code = generateMessageClass(message, ns)
  const filePath = path.join(extToWebviewDir, `${message.name}.cs`)
  fs.writeFileSync(filePath, code)
  generatedCount++
  if (generatedCount <= 10 || generatedCount % 20 === 0) {
    console.log(`  Generated: ${message.name}`)
  }
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

console.log()
console.log("Generation complete!")
console.log(`Output directory: ${OUTPUT_PATH}`)
console.log(`Total files generated: ${generatedCount}`)
console.log(`  Types: ${generatedTypes.size}`)
console.log(`  WebView→Extension: ${contract.messages.webviewToExtension.length} message classes`)
console.log(`  Extension→WebView: ${contract.messages.extensionToWebview.length} message classes`)
console.log(`  Discriminator factory: WebViewMessageFactory.cs`)
