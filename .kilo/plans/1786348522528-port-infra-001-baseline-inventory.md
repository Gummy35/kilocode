# PORT-INFRA-001: Deterministic Visual Studio Port Baseline Inventory

## Objective

Create a deterministic, machine-readable inventory of the existing Kilo Visual Studio implementation on branch `vs2026` at commit `e46bcd79cb0ddbef72c6a884b128b16c5ab47cd3`.

## Repository State

- **Branch**: `vs2026`
- **Commit SHA**: `e46bcd79cb0ddbef72c6a884b128b16c5ab47cd3`
- **Target Framework**: `net481` (.NET Framework 4.8.1)
- **Projects**: 
  - `KiloVisualStudioExtension` (production)
  - `KiloVisualStudioExtension.Tests` (test)

## Scope

Inventory only the Visual Studio extension and its tests. Do NOT:
- Compare with upstream Kilo
- Modify production code
- Modify existing tests
- Refactor existing code
- Implement missing functionality
- Fix existing bugs

## Output Structure

```
porting/
├── baseline/
│   └── baseline.json
└── manifest/
    ├── files.json
    ├── symbols.json
    ├── tests.json
    └── relationships.json
```

## Implementation Steps (Ordered)

### Step 1: Create Directory Structure
```powershell
New-Item -ItemType Directory -Force -Path "packages\kilo-visualstudio\porting\baseline"
New-Item -ItemType Directory -Force -Path "packages\kilo-visualstudio\porting\manifest"
```

### Step 2: Create Roslyn Symbol Inventory Tool

Create temp directory using environment variable: `%TEMP%\symbol-inventory\` (PowerShell: `$env:TEMP\symbol-inventory\`)

Create `SymbolInventory.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.11.0" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="4.11.0" />
    <PackageReference Include="System.Text.Json" Version="9.0.0" />
  </ItemGroup>
</Project>
```

Create `Program.cs` with:
- `SymbolWalker` class extending `CSharpSyntaxWalker` with constructor accepting `SemanticModel`
- Visit methods: `VisitNamespaceDeclaration`, `VisitClassDeclaration`, `VisitInterfaceDeclaration`, etc.
- For each symbol: extract accessibility, signature, source span, containing symbol
- Use `SymbolDisplayFormat.FullyQualifiedFormat` for consistent naming
- Generate deterministic IDs using SHA-256 of (name + signature)[:8]
- Output JSON array with sorted symbols

**SymbolWalker key methods:**
```csharp
public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node) {
    // Extract namespace name, create symbol entry
}
public override void VisitClassDeclaration(ClassDeclarationSyntax node) {
    // Extract class name, modifiers, generics, base types
}
// Similar for interface, enum, struct, record
public override void VisitMethodDeclaration(MethodDeclarationSyntax node) {
    // Extract method name, return type, parameters, modifiers
}
// Similar for property, field, constructor, event
```

**FileId generation algorithm (for both PowerShell and Roslyn tool):**
- Pattern: `file-<project>-<path-components>-<ext>`
- Example: `KiloVisualStudioExtension/Services/CloudSessionService.cs` → `file-KiloVisualStudioExtension-Services-CloudSessionService-cs`
- Example: `KiloVisualStudioExtension.Tests/CloudSessionTests.cs` → `file-KiloVisualStudioExtension.Tests-CloudSessionTests-cs`
- Example: `KiloVisualStudioExtension/webview/index.html` → `file-KiloVisualStudioExtension-webview-index-html`
- Replace path separators with hyphens
- Use lowercase for extension

**Symbol extraction requirements:**
- MUST use Roslyn APIs (`Microsoft.CodeAnalysis.CSharp`)
- NO regex-based fallback - if Roslyn cannot be used, report as blocker
- Record `rawSourceHash`: SHA-256 of exact source text for the symbol
- Record `normalizedSourceHash` ONLY if deterministic syntax-based normalization is implemented (not whitespace removal)
- If reliable normalization is not available, omit `normalizedSourceHash` and document this explicitly

### Step 3: Generate files.json (PowerShell)

**FileId generation algorithm:**
- Pattern: `file-<project>-<path-components>-<ext>`
- Example: `KiloVisualStudioExtension/Services/CloudSessionService.cs` → `file-KiloVisualStudioExtension-Services-CloudSessionService-cs`
- Example: `KiloVisualStudioExtension.Tests/CloudSessionTests.cs` → `file-KiloVisualStudioExtension.Tests-CloudSessionTests-cs`
- Example: `KiloVisualStudioExtension/webview/index.html` → `file-KiloVisualStudioExtension-webview-index-html`
- Replace path separators with hyphens
- Use lowercase for extension

**PowerShell implementation:**
```powershell
# Get all files excluding .git, .vs, bin, obj, node_modules, and temporary/build output
$files = Get-ChildItem -Recurse -File -Path "packages\kilo-visualstudio" | Where-Object { 
    $_.FullName -notmatch '\\\\.git\\\\' -and 
    $_.FullName -notmatch '\\\\.vs\\\\' -and 
    $_.FullName -notmatch '\\\\bin\\\\' -and 
    $_.FullName -notmatch '\\\\obj\\\\' -and 
    $_.FullName -notmatch '\\\\node_modules\\\\'
}

# For each file:
# - Compute fileId from relative path
# - Compute SHA-256 using Get-FileHash
# - Count lines for text files
# - Determine fileType, language, classification, isGenerated
# - Build JSON object

# Output sorted JSON array (sorted by relativePath)
```

**File classification rules:**
- `production`: Files in `KiloVisualStudioExtension/` (excluding `webview/`)
- `test`: Files in `KiloVisualStudioExtension.Tests/` (`.cs` files only)
- `asset`: Files in `webview/` directory
- `documentation`: `.md` files in root or docs
- `build`: `.csproj`, `.vsct`, `.resx`, `.vsixmanifest`, `build.ts`

**File type mapping:**
- `.cs` → `csharp`, `language: csharp`
- `.csproj` → `csproj`, `language: xml`
- `.js` in `webview/` → `webview-js`, `language: javascript`
- `.css` in `webview/` → `webview-css`, `language: css`
- `.woff`, `.woff2`, `.ttf` in `webview/` → `webview-font`, `language: font`
- `.html` in `webview/` → `webview-html`, `language: html`
- `.png`, `.svg` in `webview/` → `webview-image`, `language: image`
- `.map` in `webview/` → `webview-map`, `language: javascript`
- `.vsct`, `.resx` → `xml`, `language: xml`
- `.vsixmanifest` → `manifest`, `language: xml`
- `.md` → `markdown`, `language: markdown`
- `.json`, `.ts` (build files) → `config`, `language: json` or `typescript`

### Step 4: Run Roslyn Tool to Generate symbols.json

**Command:**
```powershell
cd $env:TEMP\symbol-inventory
dotnet restore
dotnet run -- --sourcePath "packages\kilo-visualstudio\KiloVisualStudioExtension" --output "packages\kilo-visualstudio\porting\manifest\symbols.json"
```

**Roslyn requirement:**
- MUST use Roslyn APIs for symbol extraction
- If Roslyn tool cannot be built or executed, STOP and report as blocker
- Do NOT generate approximate symbol inventory using regex

### Step 5: Generate tests.json (PowerShell + symbol cross-reference)

**Test detection regex patterns:**
```powershell
# Match [Fact], [Theory] attributes (multi-line aware)
# [InlineData] is metadata for Theory, NOT an independent test detector
$factPattern = '\[\s*Fact\s*\]'
$theoryPattern = '\[\s*Theory\s*\]'
```

**Algorithm:**
1. Find all `.cs` files in `KiloVisualStudioExtension.Tests/`
2. For each file, read content and find all `[Fact]` and `[Theory]` attributes
3. Extract method name from the method declaration following each attribute
4. Build fully qualified name: `namespace.classname.methodname`
5. Look up `symbolId` from `symbols.json` using FQN
6. Build test entry with `status: "EXISTING_TEST"`
7. Output sorted JSON array (sorted by testId)

**TestId generation:**
- Pattern: `test-<fileId>-<className>-<methodName>`
- Example: `test-file-KiloVisualStudioExtension.Tests-CloudSessionTests-GetSession_ReturnsData`

### Step 6: Generate relationships.json (PowerShell)

**Build relationships from symbols.json and tests.json:**

```powershell
# Load symbols.json and tests.json
$symbols = Get-Content "porting\manifest\symbols.json" | ConvertFrom-Json
$tests = Get-Content "porting\manifest\tests.json" | ConvertFrom-Json

# fileContainsSymbol: group symbols by fileId
$fileContainsSymbol = $symbols | Group-Object fileId | ForEach-Object {
    $_.Group | ForEach-Object { [PSCustomObject]@{ fileId = $_.fileId; symbolId = $_.symbolId } }
} | Sort-Object fileId, symbolId

# symbolContainsSymbol: extract from containingSymbolId
$symbolContainsSymbol = $symbols | Where-Object { $_.containingSymbolId } | ForEach-Object {
    [PSCustomObject]@{ parentSymbolId = $_.containingSymbolId; childSymbolId = $_.symbolId }
} | Sort-Object parentSymbolId, childSymbolId

# testBelongsToClass: match test methods to their classes
# Extract class name from test fullyQualifiedName (second component)
# Look up class symbolId from symbols.json
$testBelongsToClass = $tests | ForEach-Object {
    $className = $_.fullyQualifiedName -replace '^[^.]+\.([^.]+)\..*$', '$1'
    $classSymbol = $symbols | Where-Object { $_.fullyQualifiedName -like "*.$className" -and $_.symbolKind -eq 'class' } | Select-Object -First 1
    [PSCustomObject]@{ testId = $_.testId; classSymbolId = $classSymbol.symbolId }
} | Sort-Object testId

# handlerClassInNamespace: extract namespace from FQN
# For classes in Services/Handlers/* folders
$handlerClassInNamespace = $symbols | Where-Object { $_.symbolKind -eq 'class' -and $_.fullyQualifiedName -match 'Handlers\.' } | ForEach-Object {
    $namespace = $_.fullyQualifiedName -replace '\.[^.]+$', ''
    $namespaceSymbol = $symbols | Where-Object { $_.symbolKind -eq 'namespace' -and $_.fullyQualifiedName -eq $namespace } | Select-Object -First 1
    if ($namespaceSymbol) {
        [PSCustomObject]@{ classSymbolId = $_.symbolId; namespaceSymbolId = $namespaceSymbol.symbolId }
    }
} | Sort-Object classSymbolId

# handlerClassInFolder: extract folder path from file path
$handlerClassInFolder = $symbols | Where-Object { $_.symbolKind -eq 'class' } | ForEach-Object {
    $file = $files | Where-Object { $_.fileId -eq $_.fileId } | Select-Object -First 1
    $folderPath = (Get-Item $file.relativePath).Directory.FullName -replace '\\', '/'
    [PSCustomObject]@{ classSymbolId = $_.symbolId; folderPath = $folderPath }
} | Sort-Object classSymbolId
```

**Output structure:**
```json
{
  "fileContainsSymbol": [...],
  "symbolContainsSymbol": [...],
  "testBelongsToClass": [...],
  "handlerClassInNamespace": [...],
  "handlerClassInFolder": [...]
}
```

**Note:** `symbol_calls_symbol` relationship is OUT OF SCOPE for PORT-INFRA-001. Dependency/call graph analysis will be handled by a later task.

### Step 7: Generate baseline.json (PowerShell)

```powershell
$commitSha = git rev-parse HEAD
$branch = git branch --show-current
$timestamp = (Get-Date -Format "o")

$baseline = [PSCustomObject]@{
    repository = "kilocode/kilocode"
    branch = $branch
    commitSha = $commitSha
    generationTimestamp = $timestamp
    inventoryTool = "PORT-INFRA-001 baseline inventory script"
    inventoryScope = "Visual Studio extension and tests only"
    projects = @(
        [PSCustomObject]@{
            name = "KiloVisualStudioExtension"
            type = "production"
            projectFile = "KiloVisualStudioExtension/KiloVisualStudioExtension.csproj"
            targetFramework = "net481"
        },
        [PSCustomObject]@{
            name = "KiloVisualStudioExtension.Tests"
            type = "test"
            projectFile = "KiloVisualStudioExtension.Tests/KiloVisualStudioExtension.Tests.csproj"
            targetFramework = "net481"
        }
    )
}

$baseline | ConvertTo-Json -Depth 10 | Out-File "porting\baseline\baseline.json" -Encoding UTF8
```

### Step 8: Validation Script

```powershell
$errors = @()

# 1. Validate JSON syntax
foreach ($file in @("baseline.json", "files.json", "symbols.json", "tests.json", "relationships.json")) {
    $path = "porting\manifest\$file"
    if ($file -eq "baseline.json") { $path = "porting\baseline\$file" }
    try {
        Get-Content $path | ConvertFrom-Json | Out-Null
        Write-Host "OK: $file"
    } catch {
        $errors += "JSON syntax error in $file: $($_.Exception.Message)"
    }
}

# 2. Check for duplicate IDs
$allIds = @()
$files = Get-Content "porting\manifest\files.json" | ConvertFrom-Json
$allIds += $files.fileId | ForEach-Object { "file:$_" }

$symbols = Get-Content "porting\manifest\symbols.json" | ConvertFrom-Json
$allIds += $symbols.symbolId | ForEach-Object { "symbol:$_" }

$tests = Get-Content "porting\manifest\tests.json" | ConvertFrom-Json
$allIds += $tests.testId | ForEach-Object { "test:$_" }

$duplicates = $allIds | Group-Object | Where-Object { $_.Count -gt 1 }
if ($duplicates) {
    $errors += "Duplicate IDs found: $($duplicates.Name -join ', ')"
}

# 3. Verify all references resolve
$validFileIds = $files.fileId
$validSymbolIds = $symbols.symbolId
$validTestIds = $tests.testId

# Check symbol fileId references
foreach ($symbol in $symbols) {
    if ($symbol.fileId -and $symbol.fileId -notin $validFileIds) {
        $errors += "Symbol $($symbol.symbolId) references non-existent fileId: $($symbol.fileId)"
    }
}

# Check test references
foreach ($test in $tests) {
    if ($test.fileId -notin $validFileIds) {
        $errors += "Test $($test.testId) references non-existent fileId: $($test.fileId)"
    }
    if ($test.symbolId -notin $validSymbolIds) {
        $errors += "Test $($test.testId) references non-existent symbolId: $($test.symbolId)"
    }
}

# 4. Determinism check
Copy-Item "porting\manifest" "porting\manifest-run1" -Recurse -Force
# Run inventory again (re-run steps 3-6)
# Compare files
$run1Hash = Get-FileHash "porting\manifest-run1\symbols.json"
$run2Hash = Get-FileHash "porting\manifest\symbols.json"
if ($run1Hash.Hash -ne $run2Hash.Hash) {
    $errors += "Determinism check failed: symbols.json differs between runs"
}
```

### Step 9: Generate Final Report
Output summary to console and `porting/baseline/report.txt`:
- Commit SHA
- File counts by category
- Symbol counts by kind
- Test counts
- Validation results
- Any blockers encountered

## Technical Approach

### File Inventory
- Use PowerShell `Get-ChildItem` with exclusions for `.git`, `.vs`, `bin`, `obj`, `node_modules`
- Compute SHA-256 using `Get-FileHash -Algorithm SHA256`
- Count lines using `(Get-Content -Path $file -Raw).Split("`n").Length`
- Use `StringComparer.Ordinal` for deterministic path sorting
- Exclude all temporary/build output and generated build artifacts

### Symbol Inventory
**Available tools**: .NET SDK 9.0.316 and 10.0.302 installed; `dotnet-script` not available

**Implementation approach**: Create a standalone C# console application using Roslyn APIs:
1. Create temporary project using `$env:TEMP\symbol-inventory\`
2. Add NuGet packages: `Microsoft.CodeAnalysis.CSharp`, `Microsoft.CodeAnalysis.CSharp.Workspaces`
3. Implement `SymbolWalker` class inheriting `CSharpSyntaxWalker`
4. For each `.cs` file:
   - Parse with `CSharpSyntaxTree.ParseText()`
   - Create `Compilation` with appropriate references
   - Walk syntax tree to extract symbols
   - Output JSON with symbol metadata

**Roslyn requirement**:
- MUST use Roslyn APIs for symbol extraction
- If Roslyn tool cannot be built or executed, STOP and report as blocker
- Do NOT generate approximate symbol inventory using regex

### Test Inventory
- Parse xUnit attributes using regex: `\[Fact\]`, `\[Theory\]`
- [InlineData] is metadata for Theory, NOT an independent test detector
- Extract test method signatures from methods immediately following attributes
- Cross-reference with symbol inventory for symbolId lookup

### Determinism
- Use UTF-8 encoding without BOM: `new StreamWriter(path, false, Encoding.UTF8)`
- Sort all arrays by stable key using `OrderBy(x => x.Id, StringComparer.Ordinal)`
- Use `JsonSerializerOptions` with `WriteIndented = true` and `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping`
- Exclude timestamps from all fields except `generationTimestamp`
- Property order in JSON objects must be consistent (use explicit serialization)

## Validation Plan

Run these checks after generating all files:

```powershell
# 1. Validate JSON syntax
Get-ChildItem "packages\kilo-visualstudio\porting\manifest\*.json" | ForEach-Object {
    try { Get-Content $_.FullName | ConvertFrom-Json | Out-Null; Write-Host "OK: $($_.Name)" }
    catch { Write-Host "FAIL: $($_.Name) - $($_.Exception.Message)" }
}

# 2. Check for duplicate IDs
# (Implement in PowerShell or inline script)

# 3. Verify all references resolve
# (Cross-reference fileId, symbolId, testId across files)

# 4. Determinism check - run inventory twice and compare
Copy-Item "packages\kilo-visualstudio\porting\manifest" "packages\kilo-visualstudio\porting\manifest-run1"
# Run inventory again
# Compare using Compare-Object or Get-FileHash on each file
```

**Validation checklist:**
- [ ] `baseline.json` exists with exact Git commit SHA
- [ ] `files.json` contains all source files with valid SHA-256 hashes
- [ ] `symbols.json` contains all C# symbols with proper structure
- [ ] `tests.json` contains all xUnit tests with `EXISTING_TEST` status
- [ ] `relationships.json` contains mechanically determinable relationships
- [ ] All JSON files are valid and parseable
- [ ] No duplicate IDs in any file
- [ ] All symbol/file/test references resolve to existing IDs
- [ ] Running inventory twice produces identical output (except timestamp)
- [ ] No production code or test files were modified

## Final Report

After validation, output a summary report to `porting/baseline/report.txt`:

```
PORT-INFRA-001 Baseline Inventory Report
=========================================

Repository State:
  Commit: e46bcd79cb0ddbef72c6a884b128b16c5ab47cd3
  Branch: vs2026
  Timestamp: <ISO8601>

Inventory Summary:
  Projects: 2
  Files: <count>
    - C# source: <count>
    - Webview assets: <count>
    - Configuration: <count>
    - Documentation: <count>
  Symbols: <count>
    - Namespaces: <count>
    - Types: <count>
    - Members: <count>
  Tests: <count>
    - [Fact]: <count>
    - [Theory]: <count>

Validation Results:
  JSON syntax: PASS/FAIL
  Duplicate IDs: PASS/FAIL
  Reference resolution: PASS/FAIL
  Determinism: PASS/FAIL

Blockers:
  <none or list any issues>
```

## Notes

- This task does NOT create VS Code mappings
- This task does NOT claim semantic equivalence with VS Code
- This task does NOT classify any test as `PORTED_TEST_1_TO_1`
- All work is inventory-only; no production code or test modifications
- Existing Markdown files are inventory inputs only; do not treat their contents as authoritative
- No machine-specific absolute paths in any generated artifact
