# Generate baseline.json for PORT-INFRA-001
# This script generates the baseline metadata

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputPath = Join-Path $scriptDir "baseline\baseline.json"

# Get repository state
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

$json = $baseline | ConvertTo-Json -Depth 10
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($outputPath, $json, $utf8NoBom)

Write-Host "Generated baseline.json"
Write-Host "  Commit: $commitSha"
Write-Host "  Branch: $branch"
Write-Host "  Timestamp: $timestamp"
