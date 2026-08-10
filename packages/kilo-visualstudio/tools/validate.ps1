# Validate PORT-INFRA-001 inventory output
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$errors = @()
$warnings = @()

Write-Host "=== PORT-INFRA-001 Validation Script ===" -ForegroundColor Cyan
Write-Host ""

# 1. Validate JSON syntax
Write-Host "1. Validating JSON syntax..." -ForegroundColor Yellow
$filesToCheck = @(
    @{Path = "baseline\baseline.json"; Name = "baseline.json"},
    @{Path = "manifest\files.json"; Name = "files.json"},
    @{Path = "manifest\symbols.json"; Name = "symbols.json"},
    @{Path = "manifest\tests.json"; Name = "tests.json"},
    @{Path = "manifest\relationships.json"; Name = "relationships.json"}
)

foreach ($file in $filesToCheck) {
    $fullPath = Join-Path $scriptDir $file.Path
    try {
        $content = Get-Content $fullPath -Raw -ErrorAction Stop
        $json = $content | ConvertFrom-Json -ErrorAction Stop
        Write-Host "   OK: $($file.Name)" -ForegroundColor Green
    } catch {
        $errorMsg = "JSON syntax error in $($file.Name): $($_.Exception.Message)"
        $errors += $errorMsg
        Write-Host "   FAIL: $($file.Name) - $($_.Exception.Message)" -ForegroundColor Red
    }
}
Write-Host ""

# 2. Check for duplicate IDs
Write-Host "2. Checking for duplicate IDs..." -ForegroundColor Yellow
$allIds = @()

$files = Get-Content (Join-Path $scriptDir "manifest\files.json") | ConvertFrom-Json
$allIds += $files.fileId | ForEach-Object { "file:$_" }

$symbols = Get-Content (Join-Path $scriptDir "manifest\symbols.json") | ConvertFrom-Json
$allIds += $symbols.symbolId | ForEach-Object { "symbol:$_" }

$tests = Get-Content (Join-Path $scriptDir "manifest\tests.json") | ConvertFrom-Json
$allIds += $tests.testId | ForEach-Object { "test:$_" }

$duplicates = $allIds | Group-Object | Where-Object { $_.Count -gt 1 }
if ($duplicates) {
    foreach ($dup in $duplicates) {
        $errorMsg = "Duplicate ID found: $($dup.Name) (count: $($dup.Count))"
        $errors += $errorMsg
        Write-Host "   FAIL: $errorMsg" -ForegroundColor Red
    }
} else {
    Write-Host "   OK: No duplicate IDs found" -ForegroundColor Green
}
Write-Host ""

# 3. Verify all references resolve
Write-Host "3. Verifying references resolve..." -ForegroundColor Yellow
$validFileIds = $files.fileId | Sort-Object -Unique
$validSymbolIds = $symbols.symbolId | Sort-Object -Unique
$validTestIds = $tests.testId | Sort-Object -Unique

$refErrors = 0

# Check symbol fileId references
foreach ($symbol in $symbols) {
    if ($symbol.fileId -and $symbol.fileId -notin $validFileIds) {
        $errorMsg = "Symbol $($symbol.symbolId) references non-existent fileId: $($symbol.fileId)"
        $errors += $errorMsg
        $refErrors++
    }
}

# Check test references
foreach ($test in $tests) {
    if ($test.fileId -and $test.fileId -notin $validFileIds) {
        $errorMsg = "Test $($test.testId) references non-existent fileId: $($test.fileId)"
        $errors += $errorMsg
        $refErrors++
    }
    if ($test.symbolId -and $test.symbolId -notin $validSymbolIds) {
        $errorMsg = "Test $($test.testId) references non-existent symbolId: $($test.symbolId)"
        $errors += $errorMsg
        $refErrors++
    }
}

if ($refErrors -eq 0) {
    Write-Host "   OK: All references resolve correctly" -ForegroundColor Green
} else {
    Write-Host "   FAIL: $refErrors reference errors found" -ForegroundColor Red
}
Write-Host ""

# 4. Summary counts
Write-Host "4. Inventory Summary..." -ForegroundColor Yellow
Write-Host "   Files: $($files.Count)" -ForegroundColor White
Write-Host "   Symbols: $($symbols.Count)" -ForegroundColor White
Write-Host "   Tests: $($tests.Count)" -ForegroundColor White

$symbolKinds = $symbols | Group-Object symbolKind | Sort-Object Count -Descending
foreach ($kind in $symbolKinds) {
    Write-Host "     - $($kind.Name): $($kind.Count)" -ForegroundColor Gray
}
Write-Host ""

# 5. Final result
Write-Host "=== Validation Result ===" -ForegroundColor Cyan
if ($errors.Count -eq 0) {
    Write-Host "PASS: All validations passed" -ForegroundColor Green
} else {
    Write-Host "FAIL: $($errors.Count) errors found" -ForegroundColor Red
    foreach ($err in $errors) {
        Write-Host "   - $err" -ForegroundColor Red
    }
}
