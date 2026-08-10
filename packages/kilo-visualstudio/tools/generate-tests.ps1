# Generate tests.json for PORT-INFRA-001
# This script inventories all xUnit tests and cross-references with symbols.json

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$symbolsPath = Join-Path $scriptDir "manifest\symbols.json"
$testProjectPath = Join-Path $scriptDir "..\KiloVisualStudioExtension.Tests"
$outputPath = Join-Path $scriptDir "manifest\tests.json"

# Load symbols
$symbols = Get-Content $symbolsPath | ConvertFrom-Json

# Create a lookup for symbols by fullyQualifiedName
$symbolLookup = @{}
foreach ($symbol in $symbols) {
    $symbolLookup[$symbol.fullyQualifiedName] = $symbol
}

# Get all test files
$testFiles = Get-ChildItem -Recurse -File -Path $testProjectPath -Filter "*.cs" | Sort-Object FullName -CaseSensitive

$tests = @()

foreach ($file in $testFiles) {
    $relativePath = $file.FullName.Substring((Get-Item (Join-Path $scriptDir "..")).FullName.Length + 1).Replace('\', '/')
    $content = Get-Content -Path $file.FullName -Raw
    
    # Find all [Fact] and [Theory] attributes
    $factPattern = '\[\s*Fact\s*\]'
    $theoryPattern = '\[\s*Theory\s*\]'
    
    # Match all test attributes with their following method declarations
    $matches = [regex]::Matches($content, '(?s)(\[\s*(Fact|Theory)\s*\]\s*public\s+\w+\s+(\w+)\s*\([^)]*\)\s*\{[^}]*\})')
    
    foreach ($match in $matches) {
        $methodBlock = $match.Value
        $methodName = [regex]::Match($methodBlock, 'public\s+\w+\s+(\w+)\s*\(').Groups[1].Value
        
        # Extract namespace and class from file path
        $namespace = "KiloVisualStudioExtension.Tests"
        $className = $file.BaseName
        
        # Build fully qualified name
        $fqn = "$namespace.$className.$methodName"
        
        # Look up symbolId
        $symbolId = $null
        foreach ($key in $symbolLookup.Keys) {
            if ($key -like "*.$methodName") {
                $symbolId = $symbolLookup[$key].symbolId
                break
            }
        }
        
        # Generate fileId
        $pathParts = $relativePath.Split('/')
        $project = $pathParts[0]
        $fileName = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
        $ext = $file.Extension.ToLowerInvariant().TrimStart('.')
        if ($pathParts.Length -eq 2) {
            $fileId = "file-{0}-{1}-{2}" -f $project, $fileName, $ext
        } else {
            $pathComponents = $pathParts[1..($pathParts.Length-2)] -join '-'
            $fileId = "file-{0}-{1}-{2}-{3}" -f $project, $pathComponents, $fileName, $ext
        }
        
        # Generate testId
        $testId = "test-{0}-{1}-{2}" -f $fileId, $className, $methodName
        
        $test = [PSCustomObject]@{
            testId = $testId
            fileId = $fileId
            symbolId = $symbolId
            fullyQualifiedName = $fqn
            methodName = $methodName
            className = $className
            namespace = $namespace
            testType = if ($methodBlock -match 'Theory') { '[Theory]' } else { '[Fact]' }
            status = "EXISTING_TEST"
            sourceSpan = "$relativePath:1"
        }
        
        $tests += $test
    }
}

# Sort by testId
$sortedTests = $tests | Sort-Object { $_.testId } -CaseSensitive

# Output JSON
$json = $sortedTests | ConvertTo-Json -Depth 10
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($outputPath, $json, $utf8NoBom)

Write-Host "Generated tests.json with $($tests.Count) entries"
