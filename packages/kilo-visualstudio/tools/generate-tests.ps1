# Generate tests.json for PORT-INFRA-001
# This script inventories all xUnit tests and cross-references with symbols.json

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$symbolsPath = Join-Path $scriptDir "..\porting\manifest\symbols.json"
$outputPath = Join-Path $scriptDir "..\porting\manifest\tests.json"
$rootPath = Join-Path $scriptDir ".."

$symbols = Get-Content $symbolsPath | ConvertFrom-Json

# Create lookup by fileId -> method name -> symbol
$symbolLookup = @{}
foreach ($symbol in $symbols) {
    if ($symbol.symbolKind -eq 'method') {
        if (-not $symbolLookup[$symbol.fileId]) {
            $symbolLookup[$symbol.fileId] = @{}
        }
        # Extract method name from FQN (last part after the last dot)
        $methodName = $symbol.fullyQualifiedName -replace '.*\.', ''
        $symbolLookup[$symbol.fileId][$methodName] = $symbol
    }
}

# Get test files only
$testProjectPath = Join-Path $rootPath "KiloVisualStudioExtension.Tests"
$testFiles = Get-ChildItem -Recurse -File -Path $testProjectPath -Filter "*.cs" | Sort-Object FullName -CaseSensitive

$tests = @()

foreach ($file in $testFiles) {
    # Normalize path separators to forward slashes
    $relativePath = $file.FullName.Substring($rootPath.Length + 1).Replace('\', '/')
    
    # Generate fileId - match the format used in generate-files.ps1
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
    
    $className = $file.BaseName
    
    # Find test methods by looking for [Fact] or [Theory] in the source
    $content = Get-Content -Path $file.FullName -Raw
    $matches = [regex]::Matches($content, '(?m)\[\s*(Fact|Theory)\s*\][\s\S]*?(?:public|private|internal)[\s\S]*?\b(\w+)\s*\(')
    
    foreach ($match in $matches) {
        $testTypeRaw = $match.Groups[1].Value
        $testType = if ($testTypeRaw -eq 'Theory') { '[Theory]' } else { '[Fact]' }
        $methodName = $match.Groups[2].Value
        
        # Find the matching symbol using the lookup
        $symbolId = $null
        $fqn = $null
        if ($symbolLookup[$fileId] -and $symbolLookup[$fileId][$methodName]) {
            $sym = $symbolLookup[$fileId][$methodName]
            $symbolId = $sym.symbolId
            $fqn = $sym.fullyQualifiedName
        }
        
        # Find line number
        $lineNumber = 1
        $lines = $content -split "`n"
        for ($i = 0; $i -lt $lines.Length; $i++) {
            if ($lines[$i] -match "\[\s*${testTypeRaw}\s*\]") {
                $lineNumber = $i + 1
                break
            }
        }
        
        $testId = "test-{0}-{1}-{2}" -f $fileId, $className, $methodName
        $sourceSpan = "${relativePath}:${lineNumber}"
        
        $test = [PSCustomObject]@{
            testId = $testId
            fileId = $fileId
            symbolId = $symbolId
            fullyQualifiedName = if ($fqn) { $fqn } else { "global::KiloVisualStudioExtension.Tests.${className}.${methodName}" }
            methodName = $methodName
            className = $className
            namespace = "KiloVisualStudioExtension.Tests"
            testType = $testType
            status = "EXISTING_TEST"
            sourceSpan = $sourceSpan
        }
        
        $tests += $test
    }
}

$sortedTests = $tests | Sort-Object { $_.testId } -CaseSensitive
$json = $sortedTests | ConvertTo-Json -Depth 10
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($outputPath, $json, $utf8NoBom)

Write-Host "Generated tests.json with $($tests.Count) entries"

$unresolved = $tests | Where-Object { $_.symbolId -eq $null }
if ($unresolved) {
    Write-Host "WARNING: $($unresolved.Count) tests could not be resolved to symbols" -ForegroundColor Yellow
} else {
    Write-Host "All tests resolved to symbols" -ForegroundColor Green
}
