export interface WebViewContract {
  schemaVersion: string
  generatedFrom: SourceInfo
  metadata: Metadata
  messages: MessageCollections
  types: TypeDefinition[]
  diagnostics: Diagnostics
  statistics: Statistics
}

export interface SourceInfo {
  repository: string
  branch: string
  commit: string
  generatedAt: string
}

export interface Metadata {
  extractorVersion: string
  typescriptVersion: string
  sourcePath: string
}

export interface MessageCollections {
  webviewToExtension: MessageType[]
  extensionToWebview: MessageType[]
}

export interface MessageType {
  name: string
  type: string
  discriminator: DiscriminatorInfo
  properties: PropertyDefinition[]
  sourceFile: string
}

export interface TypeDefinition {
  name: string
  kind: string
  properties?: PropertyDefinition[]
  unionMembers?: string[]
  discriminator?: DiscriminatorInfo
  sourceFile: string
  description?: string
  typeRef?: TypeReference
}

export interface PropertyDefinition {
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

export interface TypeReference {
  name: string
  kind: string
}

export interface DiscriminatorInfo {
  field: string
  value: string
}

export interface Diagnostics {
  errors: string[]
  warnings: string[]
}

export interface Statistics {
  totalTypes: number
  webviewToExtensionMessages: number
  extensionToWebviewMessages: number
}
