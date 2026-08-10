# Visual Studio Port Baseline Inventory Tools

This directory contains the tools and scripts used to generate the PORT-INFRA-001 baseline inventory for the Kilo Visual Studio extension.

## Overview

The PORT-INFRA-001 task created a deterministic, machine-readable inventory of the Visual Studio extension implementation at commit `e46bcd79cb0ddbef72c6a884b128b16c5ab47cd3` on branch `vs2026`.

## Directory Structure

```
tools/
├── symbol-inventory/       # Roslyn-based C# symbol extraction tool
│   ├── Program.cs          # Main implementation
│   └── SymbolInventory.csproj  # .NET project file
├── generate-files.ps1      # Generates files.json manifest
├── generate-tests.ps1      # Generates tests.json with xUnit test detection
├── generate-relationships.ps1  # Builds relationship mappings
├── generate-baseline.ps1   # Creates baseline.json metadata
├── validate.ps1            # Validates all generated JSON files
└── README.md               # This file
```

## Important Notes

**Symbol Deduplication**: The `deduplicate-symbols.ps1` script has been removed. Silent deduplication of distinct source symbols is NOT acceptable. If duplicate symbol IDs are detected, it indicates a problem with the ID generation algorithm that must be fixed, not worked around.

## Generated Output

The inventory output is stored in `porting/`:

```
porting/
├── baseline/
│   ├── baseline.json       # Repository metadata and scope
│   └── report.txt          # Final inventory report
└── manifest/
    ├── files.json          # 234 files with SHA-256 hashes
    ├── symbols.json        # 2,306 C# symbols (all distinct symbols preserved)
    ├── tests.json          # 196 xUnit tests
    └── relationships.json  # File/symbol/test relationships
```

## Usage

### Running the Inventory

To regenerate the inventory (e.g., after code changes):

```powershell
# 1. Generate files.json
cd packages\kilo-visualstudio\porting
.\generate-files.ps1

# 2. Build and run the Roslyn symbol tool
cd ..\..\tools\symbol-inventory
dotnet build --configuration Release
dotnet run -- --sourcePath "C:\prog\kilocode\kilocode\packages\kilo-visualstudio\KiloVisualStudioExtension" --output "C:\prog\kilocode\kilocode\packages\kilo-visualstudio\porting\manifest\symbols.json"

# 3. Generate tests.json
cd ..\..\porting
.\generate-tests.ps1

# 4. Generate relationships.json
.\generate-relationships.ps1

# 5. Generate baseline.json
.\generate-baseline.ps1

# 6. Validate all outputs
.\validate.ps1
```

### Roslyn Symbol Tool

The `symbol-inventory` tool uses Microsoft.CodeAnalysis.CSharp to extract C# symbols:

- **Dependencies**: .NET 9.0, Microsoft.CodeAnalysis.CSharp 4.11.0
- **Output**: JSON array of symbols with deterministic IDs
- **Symbol types**: namespaces, classes, interfaces, methods, properties, fields, constructors, events

### File ID Format

```
file-<project>-<path-components>-<ext>
```

Examples:
- `file-KiloVisualStudioExtension-Services-CloudSessionService-cs`
- `file-KiloVisualStudioExtension.Tests-CloudSessionTests-cs`

### Symbol ID Format

```
symbol-<sha256-hash>
```

The hash is computed from: `fileId + fullyQualifiedName + symbolKind + sourceSpan`

This ensures that each distinct symbol gets a unique, deterministic ID based on its stable source information.

## Validation

The `validate.ps1` script checks:
1. JSON syntax validity
2. No duplicate IDs (fileId, symbolId, testId) - **FAIL if duplicates found**
3. All references resolve to existing IDs
4. Inventory summary counts

If duplicate symbol IDs are detected, the ID generation algorithm must be corrected rather than silently removing duplicates.

## Notes

- **No production code modifications**: This inventory is read-only
- **Deterministic output**: Same input produces identical output
- **UTF-8 without BOM**: All JSON files use UTF-8 encoding
- **Sorted arrays**: All JSON arrays are sorted for consistency

## Cleanup

The original temp directory `$env:TEMP\symbol-inventory\` can be safely deleted after copying the tool source to this directory.
