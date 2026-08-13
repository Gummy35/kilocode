#!/usr/bin/env bun
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
const TSCONFIG_PATH = path.join(PKGS_DIR, "kilo-vscode/webview-ui/tsconfig.json")
const OUTPUT_PATH = path.join(PKGS_DIR, "kilo-visualstudio/porting/contract/WebViewContract.json")

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

interface ExtractionContext {
  program: ts.Program
  typeChecker: ts.TypeChecker
  types: Map<string, TypeDefinition>
  messages: {
    webviewToExtension: MessageType[]
    extensionToWebview: MessageType[]
  }
  errors: string[]
  warnings: string[]
  extractedMessages: Map<string, MessageType> // Track extracted inline messages by discriminator value
}

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

function getTypeName(type: ts.Type, typeChecker: ts.TypeChecker): string {
  const symbol = type.getSymbol()
  if (symbol) {
    return symbol.getName()
  }
  const stringified = typeChecker.typeToString(type)
  return stringified.split(".").pop() || stringified
}

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

function getTypeKind(type: ts.Type): string {
  if (type.flags & ts.TypeFlags.Interface) return "interface"
  if (type.flags & ts.TypeFlags.TypeLiteral) return "typeLiteral"
  if (type.flags & ts.TypeFlags.Union) return "union"
  if (type.flags & ts.TypeFlags.String) return "string"
  if (type.flags & ts.TypeFlags.Number) return "number"
  if (type.flags & ts.TypeFlags.Boolean) return "boolean"
  if (type.flags & ts.TypeFlags.Any) return "any"
  if (type.flags & ts.TypeFlags.Unknown) return "unknown"
  if (type.flags & ts.TypeFlags.Object) return "object"
  return "unknown"
}

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
  
  if (!typeRef) {
    if (type.flags & ts.TypeFlags.Union) {
      const unionType = type as ts.UnionType
      const literalTypes = unionType.types.filter((t) => 
        t.flags & (ts.TypeFlags.StringLiteral | ts.TypeFlags.NumberLiteral | ts.TypeFlags.BooleanLiteral)
      )
      
      if (literalTypes.length > 0 && literalTypes.length === unionType.types.length) {
        isLiteral = true
        propertyType = "literal"
        literalValue = extractLiteralValue(literalTypes[0]) ?? ""
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
      isLiteral = true
      propertyType = "literal"
      literalValue = (type as ts.BooleanLiteralType).value
    } else if (type.flags & ts.TypeFlags.Array || type.symbol?.name === "Array") {
      propertyType = "array"
      const typeArgs = (type as ts.TypeReference).typeArguments
      if (typeArgs && typeArgs.length > 0) {
        elementType = getTypeName(typeArgs[0], typeChecker)
        typeRef = { name: getTypeName(typeArgs[0], typeChecker), kind: getTypeKind(typeArgs[0]) }
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
  } else {
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

  return {
    name,
    kind: "interface",
    properties,
    discriminator: discriminator || undefined,
    sourceFile: path.relative(VS_CODE_TYPES_PATH, node.getSourceFile().fileName),
  }
}

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
      
      // If we got __type, try to parse from source file
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
      return {
        name,
        kind: "interface",
        properties,
        discriminator,
        sourceFile: path.relative(VS_CODE_TYPES_PATH, node.getSourceFile().fileName),
      }
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
          return {
            name,
            kind: "interface",
            properties: referencedType.properties,
            discriminator: referencedType.discriminator,
            sourceFile: path.relative(VS_CODE_TYPES_PATH, node.getSourceFile().fileName),
          }
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
              
              return {
                name,
                kind: "interface",
                properties: mergedProperties,
                discriminator: baseType.discriminator,
                sourceFile: path.relative(VS_CODE_TYPES_PATH, node.getSourceFile().fileName),
                baseType: partialType,
                requiredFields: Array.from(requiredFields),
              }
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
          return {
            name,
            kind: resolvedTypeDef.kind,
            properties: resolvedTypeDef.properties,
            discriminator: resolvedTypeDef.discriminator,
            sourceFile: path.relative(VS_CODE_TYPES_PATH, resolvedSourceFile.fileName),
          }
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
    types: Array.from(context.types.values()),
    diagnostics: {
      errors: context.errors,
      warnings: context.warnings,
    } as Diagnostics,
    statistics: {
      totalTypes: context.types.size + context.extractedMessages.size,
      webviewToExtensionMessages: context.messages.webviewToExtension.length,
      extensionToWebviewMessages: context.messages.extensionToWebview.length,
      inlineMessagesExtracted: context.extractedMessages.size,
    } as Statistics,
  }
}

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

function generateMessageNameFromDiscriminator(discValue: string): string {
  return discValue
    .split(".")
    .map(p => p.charAt(0).toUpperCase() + p.slice(1))
    .join("") + "Message"
}

interface PostMessageExtractResult {
  properties: PropertyDefinition[]
  discriminator: DiscriminatorInfo
}

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
    rootNames: parsedConfig.fileNames.filter(f => f.includes("webview-ui/src/types") || f.includes("src/shared")),
    options: parsedConfig.options,
  })

  const context = createContext(program)
  
  const sourceFiles = program.getSourceFiles()

  console.log(`Found ${sourceFiles.length} source files to process`)
  console.log()

  for (const sourceFile of sourceFiles) {
    // Only process files from the webview-ui/src/types directory or src/shared
    if (!sourceFile.fileName.includes("webview-ui/src/types") && 
        !sourceFile.fileName.includes("src/shared")) {
      continue
    }
    
    const relativePath = path.relative(VS_CODE_TYPES_PATH, sourceFile.fileName)
    console.log(`Processing: ${relativePath}`)
    extractMessageTypes(sourceFile, context)
  }

  console.log()
  console.log("Scanning VS Code source files for postMessage calls...")
  
  const allTsFiles = findTsFiles(VS_CODE_SRC_PATH).filter(f => 
    !f.includes("node_modules") && 
    !f.includes("webview-ui") &&
    !f.includes("test")
  )
  
  console.log(`Found ${allTsFiles.length} VS Code source files to scan`)
  
  for (const filePath of allTsFiles) {
    const relativePath = path.relative(VS_CODE_SRC_PATH, filePath)
    const sourceText = fs.readFileSync(filePath, "utf-8")
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
