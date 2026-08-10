# Deduplicate symbols.json
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$symbolsPath = Join-Path $scriptDir "manifest\symbols.json"
$outputPath = Join-Path $scriptDir "manifest\symbols.json"

$symbols = Get-Content $symbolsPath | ConvertFrom-Json

# Keep only the first occurrence of each symbolId
$seen = @{}
$uniqueSymbols = @()
foreach ($symbol in $symbols) {
    if ($symbol.symbolId -notin $seen.Keys) {
        $seen[$symbol.symbolId] = $true
        $uniqueSymbols += $symbol
    }
}

$json = $uniqueSymbols | ConvertTo-Json -Depth 10
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($outputPath, $json, $utf8NoBom)

Write-Host "Deduplicated symbols: $($symbols.Count) -> $($uniqueSymbols.Count)"
