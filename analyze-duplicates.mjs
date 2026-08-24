import * as fs from "fs"

const contract = JSON.parse(fs.readFileSync("packages/kilo-visualstudio/porting/contract/WebViewContract.json", "utf-8"))

// Build a map of signature -> type names
const signatureMap = new Map()

for (const typeDef of contract.types) {
  if (!typeDef.discriminator || !typeDef.properties) continue
  
  // Create signature: discriminator_value|prop1:type1|prop2:type2|...
  const propsSig = typeDef.properties
    .filter(p => p.name !== 'type') // Exclude discriminator field itself
    .map(p => `${p.name}:${p.type}${p.optional ? '?' : ''}`)
    .sort()
    .join('|')
  
  const signature = `${typeDef.discriminator.value}|${propsSig}`
  
  if (!signatureMap.has(signature)) {
    signatureMap.set(signature, [])
  }
  signatureMap.get(signature).push(typeDef.name)
}

// Find signatures with multiple types (duplicates)
console.log("Types with duplicate signatures:")
for (const [sig, names] of signatureMap) {
  if (names.length > 1) {
    console.log(`\nSignature: ${sig.substring(0, 80)}...`)
    console.log(`  Types: ${names.join(', ')}`)
  }
}

// Check ExtensionMessage union
const extMsg = contract.types.find(t => t.name === "ExtensionMessage")
console.log("\n\nExtensionMessage union has", extMsg?.unionMembers?.length, "members")

// Check which "duplicate" types are in ExtensionMessage
console.log("\nChecking if duplicate types are in ExtensionMessage:")
for (const [sig, names] of signatureMap) {
  if (names.length > 1) {
    const inUnion = names.filter(n => extMsg?.unionMembers?.includes(n))
    const notInUnion = names.filter(n => !extMsg?.unionMembers?.includes(n))
    if (inUnion.length > 0 && notInUnion.length > 0) {
      console.log(`\n  Signature ${sig.substring(0, 50)}...`)
      console.log(`    In ExtensionMessage: ${inUnion.join(', ')}`)
      console.log(`    NOT in ExtensionMessage: ${notInUnion.join(', ')}`)
    }
  }
}
