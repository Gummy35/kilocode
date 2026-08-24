import * as fs from "fs"

const contract = JSON.parse(fs.readFileSync("packages/kilo-visualstudio/porting/contract/WebViewContract.json", "utf-8"))

// Find PartUpdate and PartUpdatedMessage
const partUpdate = contract.types.find(t => t.name === "PartUpdate")
const partUpdatedMessage = contract.types.find(t => t.name === "PartUpdatedMessage")

console.log("PartUpdate:")
console.log("  Properties:", partUpdate?.properties?.map(p => `${p.name}:${p.type}${p.optional ? '?' : ''}`))
console.log("  Discriminator:", partUpdate?.discriminator)

console.log("\nPartUpdatedMessage:")
console.log("  Properties:", partUpdatedMessage?.properties?.map(p => `${p.name}:${p.type}${p.optional ? '?' : ''}`))
console.log("  Discriminator:", partUpdatedMessage?.discriminator)

// Check ExtensionMessage union members
const extMsg = contract.types.find(t => t.name === "ExtensionMessage")
console.log("\nExtensionMessage union members (first 20):", extMsg?.unionMembers?.slice(0, 20))

// Check if IndexingStatusLoadedMessage exists
const indexingStatusLoadedMsg = contract.types.find(t => t.name === "IndexingStatusLoadedMessage")
console.log("\nIndexingStatusLoadedMessage:", indexingStatusLoadedMsg ? "FOUND" : "NOT FOUND")
console.log("  Properties:", indexingStatusLoadedMsg?.properties?.map(p => `${p.name}:${p.type}${p.optional ? '?' : ''}`))
console.log("  Discriminator:", indexingStatusLoadedMsg?.discriminator)

// Check for inline types with type= indexngStatusLoaded
const allTypes = contract.types.filter(t => t.discriminator?.value === "indexingStatusLoaded")
console.log("\nAll types with discriminator 'indexingStatusLoaded':", allTypes.map(t => t.name))
