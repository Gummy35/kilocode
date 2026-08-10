# Validate PORT-INFRA-001 inventory output
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
# Output is in ../porting directory relative to tools
$outputDir = Join-Path $scriptDir "..\porting"
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
    $fullPath = Join-Path $outputDir $file.Path
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

$files = Get-Content (Join-Path $outputDir "manifest\files.json") | ConvertFrom-Json
$allIds += $files.fileId | ForEach-Object { "file:$_" }

$symbols = Get-Content (Join-Path $outputDir "manifest\symbols.json") | ConvertFrom-Json
$allIds += $symbols.symbolId | ForEach-Object { "symbol:$_" }

$tests = Get-Content (Join-Path $outputDir "manifest\tests.json") | ConvertFrom-Json
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

# 2b. Check for null/missing symbolId in tests
Write-Host "2b. Checking test symbolId references..." -ForegroundColor Yellow
$nullSymbolTests = $tests | Where-Object { $_.symbolId -eq $null -or $_.symbolId -eq "" }
if ($nullSymbolTests) {
    foreach ($test in $nullSymbolTests) {
        $errorMsg = "Test $($test.testId) has null or empty symbolId"
        $errors += $errorMsg
        Write-Host "   FAIL: $errorMsg" -ForegroundColor Red
    }
} else {
    Write-Host "   OK: All tests have valid symbolId" -ForegroundColor Green
}
Write-Host ""

# 2c. Check for empty sourceSpan in tests
Write-Host "2c. Checking test sourceSpan..." -ForegroundColor Yellow
$emptySpanTests = $tests | Where-Object { [string]::IsNullOrWhiteSpace($_.sourceSpan) }
if ($emptySpanTests) {
    foreach ($test in $emptySpanTests) {
        $errorMsg = "Test $($test.testId) has empty or whitespace sourceSpan"
        $errors += $errorMsg
        Write-Host "   FAIL: $errorMsg" -ForegroundColor Red
    }
} else {
    Write-Host "   OK: All tests have non-empty sourceSpan" -ForegroundColor Green
}
Write-Host ""

# 2d. Check for porting/ files in files.json
Write-Host "2d. Checking for porting/ files in files.json..." -ForegroundColor Yellow
$portingFiles = $files | Where-Object { $_.relativePath -match '^porting/' }
if ($portingFiles) {
    foreach ($file in $portingFiles) {
        $errorMsg = "files.json contains porting/ file: $($file.relativePath)"
        $errors += $errorMsg
        Write-Host "   FAIL: $errorMsg" -ForegroundColor Red
    }
} else {
    Write-Host "   OK: No porting/ files in files.json" -ForegroundColor Green
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

# File counts by language
Write-Host "   Files by language:" -ForegroundColor Gray
$filesByLang = $files | Group-Object language | Sort-Object Count -Descending
foreach ($lang in $filesByLang) {
    Write-Host "     - $($lang.Name): $($lang.Count)" -ForegroundColor Gray
}

# File counts by classification
Write-Host "   Files by classification:" -ForegroundColor Gray
$filesByClass = $files | Group-Object classification | Sort-Object Count -Descending
foreach ($class in $filesByClass) {
    Write-Host "     - $($class.Name): $($class.Count)" -ForegroundColor Gray
}

$symbolKinds = $symbols | Group-Object symbolKind | Sort-Object Count -Descending
foreach ($kind in $symbolKinds) {
    Write-Host "     - $($kind.Name): $($kind.Count)" -ForegroundColor Gray
}

# Test counts
Write-Host "   Tests by type:" -ForegroundColor Gray
$factCount = ($tests | Where-Object { $_.testType -eq '[Fact]' }).Count
$theoryCount = ($tests | Where-Object { $_.testType -eq '[Theory]' }).Count
Write-Host "     - [Fact]: $factCount" -ForegroundColor Gray
Write-Host "     - [Theory]: $theoryCount" -ForegroundColor Gray

# Unresolved tests
$unresolvedTests = ($tests | Where-Object { $_.symbolId -eq $null }).Count
Write-Host "   Unresolved test symbols: $unresolvedTests" -ForegroundColor $(if ($unresolvedTests -eq 0) { "Green" } else { "Red" })
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

# 6. Verify baseline commit
Write-Host ""
Write-Host "6. Checking baseline commit..." -ForegroundColor Yellow
$baseline = Get-Content (Join-Path $outputDir "baseline\baseline.json") | ConvertFrom-Json
$expectedCommit = "e46bcd79cb0ddbef72c6a884b128b16c5ab47cd3"
if ($baseline.commitSha -eq $expectedCommit) {
    Write-Host "   OK: Baseline commit matches expected ($expectedCommit)" -ForegroundColor Green
} else {
    $errorMsg = "Baseline commit $($baseline.commitSha) does not match expected $expectedCommit"
    $errors += $errorMsg
    Write-Host "   FAIL: $errorMsg" -ForegroundColor Red
}

# 7. Verify repository identity
Write-Host "7. Checking repository identity..." -ForegroundColor Yellow
if ($baseline.repository -eq "kilocode") {
    Write-Host "   OK: Repository identity is neutral (kilocode)" -ForegroundColor Green
} else {
    $errorMsg = "Repository identity is $($baseline.repository), expected 'kilocode'"
    $errors += $errorMsg
    Write-Host "   FAIL: $errorMsg" -ForegroundColor Red
}
