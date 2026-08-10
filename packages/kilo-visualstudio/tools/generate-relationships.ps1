# Generate relationships.json for PORT-INFRA-001
# This script builds relationships from symbols.json and tests.json

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$symbolsPath = Join-Path $scriptDir "manifest\symbols.json"
$testsPath = Join-Path $scriptDir "manifest\tests.json"
$filesPath = Join-Path $scriptDir "manifest\files.json"
$outputPath = Join-Path $scriptDir "manifest\relationships.json"

# Load data
$symbols = Get-Content $symbolsPath | ConvertFrom-Json
$tests = Get-Content $testsPath | ConvertFrom-Json
$files = Get-Content $filesPath | ConvertFrom-Json

# fileContainsSymbol: group symbols by fileId
$fileContainsSymbol = @()
foreach ($symbol in $symbols) {
    if ($symbol.fileId) {
        $fileContainsSymbol += [PSCustomObject]@{
            fileId = $symbol.fileId
            symbolId = $symbol.symbolId
        }
    }
}
$fileContainsSymbol = $fileContainsSymbol | Sort-Object fileId, symbolId -CaseSensitive

# symbolContainsSymbol: extract from containingSymbolId
$symbolContainsSymbol = @()
foreach ($symbol in $symbols) {
    if ($symbol.containingSymbolId) {
        $symbolContainsSymbol += [PSCustomObject]@{
            parentSymbolId = $symbol.containingSymbolId
            childSymbolId = $symbol.symbolId
        }
    }
}
$symbolContainsSymbol = $symbolContainsSymbol | Sort-Object parentSymbolId, childSymbolId -CaseSensitive

# testBelongsToClass: match test methods to their classes
$testBelongsToClass = @()
foreach ($test in $tests) {
    # Extract class name from fullyQualifiedName (second component: namespace.classname.methodname)
    $fqnParts = $test.fullyQualifiedName.Split('.')
    if ($fqnParts.Length -ge 3) {
        $className = $fqnParts[1]
        # Look up class symbol - match class name at end of FQN (after last dot)
        $classSymbol = $symbols | Where-Object { 
            $_.symbolKind -eq 'class' -and 
            $_.fullyQualifiedName -match "\\.$className`$"
        } | Select-Object -First 1
        if (-not $classSymbol) {
            # Try matching without namespace prefix
            $classSymbol = $symbols | Where-Object { 
                $_.symbolKind -eq 'class' -and 
                $_.fullyQualifiedName -match [regex]::Escape(".$className`$")
            } | Select-Object -First 1
        }
        if ($classSymbol) {
            $testBelongsToClass += [PSCustomObject]@{
                testId = $test.testId
                classSymbolId = $classSymbol.symbolId
            }
        }
    }
}
$testBelongsToClass = $testBelongsToClass | Sort-Object testId -CaseSensitive

# handlerClassInNamespace: extract namespace from FQN for classes in Handlers folders
$handlerClassInNamespace = @()
foreach ($symbol in $symbols) {
    if ($symbol.symbolKind -eq 'class' -and $symbol.fullyQualifiedName -match 'Handlers\.') {
        # Extract namespace (everything before the class name)
        $fqnParts = $symbol.fullyQualifiedName.Split('.')
        if ($fqnParts.Length -ge 2) {
            $namespace = ($fqnParts[0..($fqnParts.Length-2)] -join '.')
            $namespaceSymbol = $symbols | Where-Object { $_.symbolKind -eq 'namespace' -and $_.fullyQualifiedName -eq $namespace } | Select-Object -First 1
            if ($namespaceSymbol) {
                $handlerClassInNamespace += [PSCustomObject]@{
                    classSymbolId = $symbol.symbolId
                    namespaceSymbolId = $namespaceSymbol.symbolId
                }
            }
        }
    }
}
$handlerClassInNamespace = $handlerClassInNamespace | Sort-Object classSymbolId -CaseSensitive

# handlerClassInFolder: extract folder path from file path for classes
$handlerClassInFolder = @()
foreach ($symbol in $symbols) {
    if ($symbol.symbolKind -eq 'class') {
        # Find the file for this symbol
        $file = $files | Where-Object { $_.fileId -eq $symbol.fileId } | Select-Object -First 1
        if ($file) {
            $folderPath = [System.IO.Path]::GetDirectoryName($file.relativePath)
            if (-not $folderPath) { $folderPath = "." }
            $handlerClassInFolder += [PSCustomObject]@{
                classSymbolId = $symbol.symbolId
                folderPath = $folderPath
            }
        }
    }
}
$handlerClassInFolder = $handlerClassInFolder | Sort-Object classSymbolId -CaseSensitive

# Build relationships object
$relationships = [PSCustomObject]@{
    fileContainsSymbol = $fileContainsSymbol
    symbolContainsSymbol = $symbolContainsSymbol
    testBelongsToClass = $testBelongsToClass
    handlerClassInNamespace = $handlerClassInNamespace
    handlerClassInFolder = $handlerClassInFolder
}

# Output JSON
$json = $relationships | ConvertTo-Json -Depth 10
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($outputPath, $json, $utf8NoBom)

Write-Host "Generated relationships.json"
Write-Host "  fileContainsSymbol: $($fileContainsSymbol.Count)"
Write-Host "  symbolContainsSymbol: $($symbolContainsSymbol.Count)"
Write-Host "  testBelongsToClass: $($testBelongsToClass.Count)"
Write-Host "  handlerClassInNamespace: $($handlerClassInNamespace.Count)"
Write-Host "  handlerClassInFolder: $($handlerClassInFolder.Count)"
