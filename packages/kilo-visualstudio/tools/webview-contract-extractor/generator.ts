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

interface EnumDefinition {
  name: string
  members: string[]
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

let generatedEnums: Map<string, EnumDefinition>

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
let typesDir: string
let ns: string

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

function parseUnionMembers(elementType: string): string[] {
  return elementType.split('|').map(p => p.trim())
}

function isStringLiteral(type: string): boolean {
  if (typeof type !== 'string') return false
  return type.startsWith('"') || type.startsWith("'")
}

function getStringLiteralValue(type: string): string {
  const trimmed = type.trim()
  return trimmed.slice(1, -1)
}

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

function generateEnumName(literalValues: string[]): string {
  const baseName = literalValues.map(v => {
    const value = isStringLiteral(v) ? getStringLiteralValue(v) : v
    return pascalCase(value)
  }).join('')
  const sanitized = baseName.replace(/[^a-zA-Z0-9]/g, '')
  return sanitized || 'EnumValue'
}

function enumExistsWithSameMembers(enumName: string, members: string[]): boolean {
  const enumDef = generatedEnums.get(enumName)
  if (!enumDef) return false
  if (enumDef.members.length !== members.length) return false
  return enumDef.members.every((m, i) => m === members[i])
}

function tryGenerateUnionEnum(elementType: string, prop: PropertyDefinition): { type: string, isNullable: boolean } | null {
  const unionParts = parseUnionMembers(elementType)
  const nonNullParts = unionParts.filter(p => p !== 'undefined' && p !== 'null')
  
  if (nonNullParts.length === 0) return null
  
  const allLiterals = nonNullParts.every(p => isStringLiteral(p))
  if (!allLiterals) return null
  
  const literalValues = nonNullParts.map(p => getStringLiteralValue(p))
  const enumName = generateEnumName(literalValues)
  
  if (enumExistsWithSameMembers(enumName, literalValues)) {
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
/// Generated from union type
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum ${enumName}
{
${enumDef.members.map((m, i) => `    [JsonProperty("${m}")]\n    ${pascalCase(m)}${i < enumDef.members.length - 1 ? ',' : ''}`).join('\n')}
}
`
  const enumFilePath = path.join(typesDir, `${enumName}.cs`)
  fs.writeFileSync(enumFilePath, enumCode)
  console.log(`  Generated enum: ${enumName}`)
  
  return { type: enumName, isNullable: unionParts.some(p => p === 'undefined' || p === 'null') }
}

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
  
  if (!isPrimitiveType(baseType) && !isInternalType(baseType) && typeDefinitions.has(baseType)) {
    const typeDef = typeDefinitions.get(baseType)!
    const isNodeModules = typeDef.sourceFile.includes('node_modules')
    if (isNodeModules) {
      return { type: 'object', isNullable: prop.optional || prop.nullable, originalType: baseType }
    }
    if (typeDef.kind === 'typeAlias') {
      return { type: pascalCase(baseType), isNullable: prop.optional || prop.nullable, originalType: baseType }
    }
    if (typeDef.kind === 'union') {
      return { type: 'object', isNullable: prop.optional || prop.nullable, originalType: baseType }
    }
    return { type: pascalCase(baseType), isNullable: prop.optional || prop.nullable, originalType: baseType }
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
          const enumResult = tryGenerateUnionEnum(prop.elementType, prop)
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
        for (const p of typeDef.properties) {
          collectNeededTypes(p)
        }
      } else if (typeDef.kind === 'typeAlias') {
        // Type aliases are added to neededTypes and will be generated as placeholder classes
        neededTypes.add(prop.typeRef.name)
      } else if (typeDef.kind === 'union' && typeDef.unionMembers) {
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

function generateTypeClass(typeDef: TypeDefinition, folder: string): string {
  const sb: string[] = []
  
  // Collect referenced types from different namespaces
  const referencedNamespaces = new Set<string>()
  if (typeDef.properties) {
    for (const prop of typeDef.properties) {
      if (prop.typeRef?.name && typeDefinitions.has(prop.typeRef.name)) {
        const refTypeDef = typeDefinitions.get(prop.typeRef.name)!
        const refFolder = getSourceFileFolder(refTypeDef.sourceFile)
        if (refFolder !== folder && refFolder !== 'extensionMessages' && refFolder !== 'webviewMessages') {
          referencedNamespaces.add(ns + "." + refFolder)
        }
      }
      if (prop.elementType && typeDefinitions.has(prop.elementType)) {
        const elemTypeDef = typeDefinitions.get(prop.elementType)!
        const elemFolder = getSourceFileFolder(elemTypeDef.sourceFile)
        if (elemFolder !== folder && elemFolder !== 'extensionMessages' && elemFolder !== 'webviewMessages') {
          referencedNamespaces.add(ns + "." + elemFolder)
        }
      }
    }
  }
  
  sb.push("// <auto-generated>")
  sb.push("//     This code was generated by WebViewContractGenerator.")
  sb.push("//     Do not modify this file directly as changes will be lost on regeneration.")
  sb.push("// </auto-generated>")
  sb.push("")
  sb.push("#nullable enable")
  sb.push("")
  // Use base namespace for Types folder, folder namespace for others
  const namespace = folder === 'Types' ? ns : (ns + "." + folder)
  sb.push("namespace " + namespace + ";")
  sb.push("")
  sb.push("using System;")
  sb.push("using System.Collections.Generic;")
  sb.push("using System.Threading.Tasks;")
  sb.push("using Newtonsoft.Json;")
  for (const refNs of referencedNamespaces) {
    sb.push("using " + refNs + ";")
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
    sb.push("/// </summary>")
    if (sourceComment) sb.push(sourceComment)
  } else {
    sb.push("/// <summary>")
    sb.push(`/// Type: ${typeDef.name}`)
    sb.push(`/// Source: ${typeDef.sourceFile}`)
    sb.push("/// </summary>")
    if (sourceComment) sb.push(sourceComment)
  }
  
  // Handle inheritance for Partial<T> & Pick<T, ...> pattern
  if (typeDef.baseType) {
    sb.push(`public class ${typeDef.name} : ${pascalCase(typeDef.baseType)}`)
  } else {
    sb.push(`public class ${typeDef.name}`)
  }
  sb.push("{")

  if (typeDef.properties) {
    for (const prop of typeDef.properties) {
      // Skip properties that are in the base type - they're inherited
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

  sb.push("}")
  sb.push("")

  return sb.join("\n")
}

function getSourceFileFolder(sourceFile: string): string {
  const normalizedSource = sourceFile.replace(/\\/g, '/')
  if (normalizedSource.includes('connection.ts')) return 'connection'
  if (normalizedSource.includes('parts.ts')) return 'parts'
  if (normalizedSource.includes('sessions.ts')) return 'sessions'
  if (normalizedSource.includes('permissions.ts')) return 'permissions'
  if (normalizedSource.includes('questions.ts')) return 'questions'
  if (normalizedSource.includes('providers.ts')) return 'providers'
  if (normalizedSource.includes('agents.ts')) return 'agents'
  if (normalizedSource.includes('config.ts')) return 'config'
  if (normalizedSource.includes('profile.ts')) return 'profile'
  if (normalizedSource.includes('agent-manager.ts')) return 'agentManager'
  if (normalizedSource.includes('migration.ts')) return 'migration'
  if (normalizedSource.includes('memory.ts')) return 'memory'
  if (normalizedSource.includes('extension-messages.ts')) return 'extensionMessages'
  if (normalizedSource.includes('webview-messages.ts')) return 'webviewMessages'
  // Types from src/shared or marketplace go in the base Types folder
  if (normalizedSource.includes('src/shared')) return 'Types'
  if (normalizedSource.includes('marketplace.ts')) return 'Types'
  return 'Types'
}

function generateMessageClass(message: MessageType, ns: string, folder: string): string {
  const sb: string[] = []
  
  // Collect referenced types from different namespaces
  const referencedNamespaces = new Set<string>()
  for (const prop of message.properties) {
    // Check typeRef first
    if (prop.typeRef?.name && typeDefinitions.has(prop.typeRef.name)) {
      const refTypeDef = typeDefinitions.get(prop.typeRef.name)!
      const refFolder = getSourceFileFolder(refTypeDef.sourceFile)
      if (refFolder !== folder && refFolder !== 'Types') {
        referencedNamespaces.add(ns + "." + refFolder)
      }
    }
    // Also check elementType for union types like "undefined | ServerInfo"
    if (prop.elementType) {
      // Parse union types to find actual type references
      const typeParts = prop.elementType.split('|').map(p => p.trim())
      for (const part of typeParts) {
        if (part && part !== 'undefined' && part !== 'null' && typeDefinitions.has(part)) {
          const refTypeDef = typeDefinitions.get(part)!
          const refFolder = getSourceFileFolder(refTypeDef.sourceFile)
          if (refFolder !== folder && refFolder !== 'Types') {
            referencedNamespaces.add(ns + "." + refFolder)
          }
        }
      }
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
  for (const refNs of referencedNamespaces) {
    sb.push("using " + refNs + ";")
  }
  sb.push("")
  sb.push("/// <summary>")
  sb.push("/// WebView message: " + message.name)
  sb.push("/// Discriminator: " + message.discriminator.field + " = \"" + message.discriminator.value + "\"")
  sb.push("/// Source: " + message.sourceFile)
  sb.push("/// </summary>")
  const sanitizedName = pascalCase(message.name)
  sb.push("public class " + sanitizedName)
  sb.push("{")

  for (const prop of message.properties) {
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
    // Skip messages from node_modules
    if (message.sourceFile.includes('node_modules')) {
      continue
    }
    const discValue = message.discriminator.value
    const sanitizedName = pascalCase(message.name)
    if (!seenDiscriminators.has(discValue)) {
      cases.push("            \"" + discValue + "\" => typeof(T) == typeof(" + sanitizedName + ") ? (T)(object)token.ToObject<" + sanitizedName + ">(Serializer)! : throw new JsonSerializationException(\"Type mismatch\"),")
      seenDiscriminators.add(discValue)
    }
  }

  for (const message of extToWebview) {
    // Skip messages from node_modules
    if (message.sourceFile.includes('node_modules')) {
      continue
    }
    const discValue = message.discriminator.value
    const sanitizedName = pascalCase(message.name)
    if (!seenDiscriminators.has(discValue)) {
      cases.push("            \"" + discValue + "\" => typeof(T) == typeof(" + sanitizedName + ") ? (T)(object)token.ToObject<" + sanitizedName + ">(Serializer)! : throw new JsonSerializationException(\"Type mismatch\"),")
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
generatedEnums = new Map()

console.log(`Contract version: ${contract.schemaVersion}`)
console.log(`Total types in contract: ${contract.types.length}`)
console.log(`WebView→Extension messages: ${contract.messages.webviewToExtension.length}`)
console.log(`Extension→WebView messages: ${contract.messages.extensionToWebview.length}`)
console.log()

// Create namespace-based folder structure
const connectionDir = path.join(OUTPUT_PATH, "Messages", "connection")
const partsDir = path.join(OUTPUT_PATH, "Messages", "parts")
const sessionsDir = path.join(OUTPUT_PATH, "Messages", "sessions")
const permissionsDir = path.join(OUTPUT_PATH, "Messages", "permissions")
const questionsDir = path.join(OUTPUT_PATH, "Messages", "questions")
const providersDir = path.join(OUTPUT_PATH, "Messages", "providers")
const agentsDir = path.join(OUTPUT_PATH, "Messages", "agents")
const configDir = path.join(OUTPUT_PATH, "Messages", "config")
const profileDir = path.join(OUTPUT_PATH, "Messages", "profile")
const agentManagerDir = path.join(OUTPUT_PATH, "Messages", "agentManager")
const migrationDir = path.join(OUTPUT_PATH, "Messages", "migration")
const memoryDir = path.join(OUTPUT_PATH, "Messages", "memory")
const extensionMessagesDir = path.join(OUTPUT_PATH, "Messages", "extensionMessages")
const webviewMessagesDir = path.join(OUTPUT_PATH, "Messages", "webviewMessages")
typesDir = path.join(OUTPUT_PATH, "Types")

ns = "KiloVisualStudioExtension.WebView.Generated"

// Create all directories
fs.mkdirSync(connectionDir, { recursive: true })
fs.mkdirSync(partsDir, { recursive: true })
fs.mkdirSync(sessionsDir, { recursive: true })
fs.mkdirSync(permissionsDir, { recursive: true })
fs.mkdirSync(questionsDir, { recursive: true })
fs.mkdirSync(providersDir, { recursive: true })
fs.mkdirSync(agentsDir, { recursive: true })
fs.mkdirSync(configDir, { recursive: true })
fs.mkdirSync(profileDir, { recursive: true })
fs.mkdirSync(agentManagerDir, { recursive: true })
fs.mkdirSync(migrationDir, { recursive: true })
fs.mkdirSync(memoryDir, { recursive: true })
fs.mkdirSync(extensionMessagesDir, { recursive: true })
fs.mkdirSync(webviewMessagesDir, { recursive: true })
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

// Write enum files first - place them based on source file
for (const [enumName, enumDef] of generatedEnums) {
  // Find the source file for this enum by checking which message/type uses it
  let enumFolder = 'Types' // default
  for (const message of [...contract.messages.webviewToExtension, ...contract.messages.extensionToWebview]) {
    for (const prop of message.properties) {
      if (prop.elementType === enumName || prop.typeRef?.name === enumName) {
        enumFolder = getSourceFileFolder(message.sourceFile)
        break
      }
    }
    if (enumFolder !== 'Types') break
  }
  
  const enumCode = `// <auto-generated>
//     This code was generated by WebViewContractGenerator.
//     Do not modify this file directly as changes will be lost on regeneration.
//     Source: WebViewContract.json schema version ${contract.schemaVersion}
// </auto-generated>

#nullable enable

namespace ${ns}.${enumFolder};

/// <summary>
/// Enum: ${enumName}
/// Generated from union type
/// </summary>
public enum ${enumName}
{
${enumDef.members.map((m, i) => `    ${pascalCase(m)}${i < enumDef.members.length - 1 ? ',' : ''}`).join('\n')}
}
`
  let enumTargetDir: string
  switch (enumFolder) {
    case 'connection': enumTargetDir = connectionDir; break
    case 'parts': enumTargetDir = partsDir; break
    case 'sessions': enumTargetDir = sessionsDir; break
    case 'permissions': enumTargetDir = permissionsDir; break
    case 'questions': enumTargetDir = questionsDir; break
    case 'providers': enumTargetDir = providersDir; break
    case 'agents': enumTargetDir = agentsDir; break
    case 'config': enumTargetDir = configDir; break
    case 'profile': enumTargetDir = profileDir; break
    case 'agentManager': enumTargetDir = agentManagerDir; break
    case 'migration': enumTargetDir = migrationDir; break
    case 'memory': enumTargetDir = memoryDir; break
    case 'extensionMessages': enumTargetDir = extensionMessagesDir; break
    case 'webviewMessages': enumTargetDir = webviewMessagesDir; break
    default: enumTargetDir = typesDir; break
  }
  const enumFilePath = path.join(enumTargetDir, `${enumName}.cs`)
  fs.writeFileSync(enumFilePath, enumCode)
  console.log(`  Generated enum: ${enumName} (${enumFolder})`)
}

// Generate types in dependency order
const generatedOrder: string[] = []
const visited = new Set<string>()

function generateTypeWithDeps(typeName: string) {
  if (visited.has(typeName)) return
  visited.add(typeName)
  
  const typeDef = typeDefinitions.get(typeName)
  if (!typeDef) return
  
  // For interfaces, generate all dependencies first
  if (typeDef.kind === 'interface' && typeDef.properties) {
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

// Now generate in order
for (const typeName of generatedOrder) {
  // Get the type definition
  const typeDef = typeDefinitions.get(typeName)
  if (!typeDef) continue
  
  // Check if this type is also a message
  const isMessage = messageNames.has(typeName)
  
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
  const normalizedSource = typeDef.sourceFile.replace(/\\/g, '/')
  const isNodeModules = normalizedSource.includes('node_modules')
  if (isNodeModules) {
    continue
  }
  
  // For type aliases and unions that are needed, generate placeholder classes
  if (typeDef.kind === 'typeAlias' || typeDef.kind === 'union') {
    // Use base namespace for Types folder
    const namespace = typeFolder === 'Types' ? ns : (ns + "." + typeFolder)
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
public class ${pascalCase(typeName)} { }
`
    let typeTargetDir: string
    switch (typeFolder) {
      case 'connection': typeTargetDir = connectionDir; break
      case 'parts': typeTargetDir = partsDir; break
      case 'sessions': typeTargetDir = sessionsDir; break
      case 'permissions': typeTargetDir = permissionsDir; break
      case 'questions': typeTargetDir = questionsDir; break
      case 'providers': typeTargetDir = providersDir; break
      case 'agents': typeTargetDir = agentsDir; break
      case 'config': typeTargetDir = configDir; break
      case 'profile': typeTargetDir = profileDir; break
      case 'agentManager': typeTargetDir = agentManagerDir; break
      case 'migration': typeTargetDir = migrationDir; break
      case 'memory': typeTargetDir = memoryDir; break
      case 'extensionMessages': typeTargetDir = extensionMessagesDir; break
      case 'webviewMessages': typeTargetDir = webviewMessagesDir; break
      case 'Types': typeTargetDir = typesDir; break
      default: typeTargetDir = typesDir; break
    }
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
  
  const code = generateTypeClass(typeDef, typeFolder)
  let typeTargetDir: string
  switch (typeFolder) {
    case 'connection': typeTargetDir = connectionDir; break
    case 'parts': typeTargetDir = partsDir; break
    case 'sessions': typeTargetDir = sessionsDir; break
    case 'permissions': typeTargetDir = permissionsDir; break
    case 'questions': typeTargetDir = questionsDir; break
    case 'providers': typeTargetDir = providersDir; break
    case 'agents': typeTargetDir = agentsDir; break
    case 'config': typeTargetDir = configDir; break
    case 'profile': typeTargetDir = profileDir; break
    case 'agentManager': typeTargetDir = agentManagerDir; break
    case 'migration': typeTargetDir = migrationDir; break
    case 'memory': typeTargetDir = memoryDir; break
    case 'extensionMessages': typeTargetDir = extensionMessagesDir; break
    case 'webviewMessages': typeTargetDir = webviewMessagesDir; break
    case 'Types': typeTargetDir = typesDir; break
    default: typeTargetDir = typesDir; break
  }
  const filePath = path.join(typeTargetDir, `${typeDef.name}.cs`)
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
  let targetDir: string
  switch (folder) {
    case 'connection': targetDir = connectionDir; break
    case 'parts': targetDir = partsDir; break
    case 'sessions': targetDir = sessionsDir; break
    case 'permissions': targetDir = permissionsDir; break
    case 'questions': targetDir = questionsDir; break
    case 'providers': targetDir = providersDir; break
    case 'agents': targetDir = agentsDir; break
    case 'config': targetDir = configDir; break
    case 'profile': targetDir = profileDir; break
    case 'agentManager': targetDir = agentManagerDir; break
    case 'migration': targetDir = migrationDir; break
    case 'memory': targetDir = memoryDir; break
    case 'extensionMessages': targetDir = extensionMessagesDir; break
    case 'webviewMessages': targetDir = webviewMessagesDir; break
    default: targetDir = extensionMessagesDir; break
  }
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
  let targetDir: string
  switch (folder) {
    case 'connection': targetDir = connectionDir; break
    case 'parts': targetDir = partsDir; break
    case 'sessions': targetDir = sessionsDir; break
    case 'permissions': targetDir = permissionsDir; break
    case 'questions': targetDir = questionsDir; break
    case 'providers': targetDir = providersDir; break
    case 'agents': targetDir = agentsDir; break
    case 'config': targetDir = configDir; break
    case 'profile': targetDir = profileDir; break
    case 'agentManager': targetDir = agentManagerDir; break
    case 'migration': targetDir = migrationDir; break
    case 'memory': targetDir = memoryDir; break
    case 'extensionMessages': targetDir = extensionMessagesDir; break
    case 'webviewMessages': targetDir = webviewMessagesDir; break
    default: targetDir = extensionMessagesDir; break
  }
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

console.log()
console.log("Generation complete!")
console.log(`Output directory: ${OUTPUT_PATH}`)
console.log(`Total files generated: ${generatedCount}`)
console.log(`  Types: ${generatedTypes.size}`)
console.log(`  WebView→Extension: ${contract.messages.webviewToExtension.length} message classes`)
console.log(`  Extension→WebView: ${contract.messages.extensionToWebview.length} message classes`)
console.log(`  Discriminator factory: WebViewMessageFactory.cs`)
