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

// Project root is 5 levels up from this script
const ROOT_DIR = path.resolve(__dirname, "..", "..", "..", "..", "..")
const VS_CODE_TYPES_PATH = path.join(ROOT_DIR, "packages/kilo-vscode/webview-ui/src/types/messages")
const OUTPUT_PATH = path.join(ROOT_DIR, "packages/kilo-visualstudio/porting/contract/WebViewContract.json")
const TSCONFIG_PATH = path.join(ROOT_DIR, "packages/kilo-vscode/webview-ui/tsconfig.json")

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
      const memberName = getTypeName(memberType, typeChecker)
      members.push(memberName)
    }

    return {
      name,
      kind: "union",
      unionMembers: members,
      sourceFile: path.relative(VS_CODE_TYPES_PATH, node.getSourceFile().fileName),
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
  
  const visit = (node: ts.Node): void => {
    let typeDef: TypeDefinition | null = null
    
    if (ts.isInterfaceDeclaration(node)) {
      typeDef = extractInterfaceDeclaration(node, context)
    } else if (ts.isTypeAliasDeclaration(node)) {
      typeDef = extractTypeAliasDeclaration(node, context)
    }

    if (typeDef) {
      context.types.set(typeDef.name, typeDef)
      
      if (typeDef.discriminator && typeDef.properties) {
        const messageType: MessageType = {
          name: typeDef.name,
          type: "interface",
          discriminator: typeDef.discriminator,
          properties: typeDef.properties,
          sourceFile: typeDef.sourceFile,
        }
        
        // Categorize based on discriminator value
        const discValue = typeDef.discriminator.value
        if (discValue.includes("agentManager") || 
            discValue.includes("sendMessage") ||
            discValue.includes("abort") ||
            discValue.includes("createSession") ||
            discValue.includes("request") ||
            discValue.includes("Reply") ||
            discValue.includes("Response") ||
            discValue.includes("Accept") ||
            discValue.includes("Dismiss") ||
            discValue.includes("Delete") ||
            discValue.includes("Update") ||
            discValue.includes("Open") ||
            discValue.includes("Close") ||
            discValue.includes("Login") ||
            discValue.includes("Logout") ||
            discValue.includes("Select") ||
            discValue.includes("Set") ||
            discValue.includes("Validate") ||
            discValue.includes("Compact") ||
            discValue.includes("Export") ||
            discValue.includes("Rename") ||
            discValue.includes("Clear") ||
            discValue.includes("Load") ||
            discValue.includes("Import") ||
            discValue.includes("Refresh") ||
            discValue.includes("Telemetry") ||
            discValue.includes("Copy") ||
            discValue.includes("Preview") ||
            discValue.includes("Save") ||
            discValue.includes("Continue") ||
            discValue.includes("Persist") ||
            discValue.includes("Forget") ||
            discValue.includes("Promote") ||
            discValue.includes("Fork") ||
            discValue.includes("Remove") ||
            discValue.includes("Filter") ||
            discValue.includes("Install") ||
            discValue.includes("Connect") ||
            discValue.includes("Disconnect") ||
            discValue.includes("Authorize") ||
            discValue.includes("Fetch") ||
            discValue.includes("Toggle") ||
            discValue.includes("Reset") ||
            discValue.includes("Retry") ||
            discValue.includes("Reload") ||
            discValue.includes("Enhance") ||
            discValue.includes("Apply") ||
            discValue.includes("Revert") ||
            discValue.includes("Move") ||
            discValue.includes("Configure") ||
            discValue.includes("Run") ||
            discValue.includes("Stop") ||
            discValue.includes("Show") ||
            discValue.includes("Hide") ||
            discValue.includes("Ready") ||
            discValue.includes("Focus") ||
            discValue.includes("Visible") ||
            discValue.includes("FocusChanged") ||
            discValue.includes("Focus")) {
          context.messages.webviewToExtension.push(messageType)
        } else {
          context.messages.extensionToWebview.push(messageType)
        }
      }
    }

    ts.forEachChild(node, visit)
  }

  ts.forEachChild(sourceFile, visit)
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
      totalTypes: context.types.size,
      webviewToExtensionMessages: context.messages.webviewToExtension.length,
      extensionToWebviewMessages: context.messages.extensionToWebview.length,
    } as Statistics,
  }
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

  const program = ts.createProgram({
    rootNames: parsedConfig.fileNames.filter(f => f.includes("webview-ui/src/types/messages")),
    options: parsedConfig.options,
  })

  const context = createContext(program)
  
  const sourceFiles = program.getSourceFiles()

  console.log(`Found ${sourceFiles.length} source files to process`)
  console.log()

  for (const sourceFile of sourceFiles) {
    const relativePath = path.relative(VS_CODE_TYPES_PATH, sourceFile.fileName)
    console.log(`Processing: ${relativePath}`)
    extractMessageTypes(sourceFile, context)
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
