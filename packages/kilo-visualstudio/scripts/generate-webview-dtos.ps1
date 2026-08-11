# PORT-WEBVIEW-001: WebView Protocol Contract Extraction and C# DTO Generation
# This script runs the complete pipeline:
# 1. TypeScript contract extractor (uses TypeScript Compiler API)
# 2. Generates WebViewContract.json
# 3. C# DTO generator (consumes WebViewContract.json)
# 4. Generates strongly-typed C# DTOs

param(
    [switch]$Clean,
    [switch]$SkipExtraction,
    [switch]$SkipGeneration
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$ExtractorDir = Join-Path $PSScriptRoot "webview-contract-extractor"
$ContractPath = Join-Path $ProjectRoot "porting\contract\WebViewContract.json"
$OutputPath = Join-Path $ProjectRoot "KiloVisualStudioExtension\WebView\Generated"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "WebView Contract Extraction Pipeline" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Clean step
if ($Clean) {
    Write-Host "Cleaning generated artifacts..." -ForegroundColor Yellow
    if (Test-Path $ContractPath) {
        Remove-Item $ContractPath -Force
        Write-Host "  Removed: $ContractPath" -ForegroundColor Gray
    }
    if (Test-Path $OutputPath) {
        Remove-Item $OutputPath -Recurse -Force
        Write-Host "  Removed: $OutputPath" -ForegroundColor Gray
    }
    Write-Host ""
}

# Step 1: Run TypeScript extractor
if (-not $SkipExtraction) {
    Write-Host "Step 1: Running TypeScript contract extractor..." -ForegroundColor Cyan
    Write-Host ""
    
    Set-Location $ExtractorDir
    
    # Install dependencies if needed
    if (-not (Test-Path "node_modules")) {
        Write-Host "Installing dependencies..." -ForegroundColor Yellow
        bun install
    }
    
    # Run extractor
    Write-Host "Extracting WebView protocol from VS Code TypeScript source..." -ForegroundColor Yellow
    bun run src/extractor.ts
    
    if (-not (Test-Path $ContractPath)) {
        throw "Error: Contract file was not generated at $ContractPath"
    }
    
    Write-Host ""
    Write-Host "Contract generated successfully: $ContractPath" -ForegroundColor Green
    Write-Host ""
    
    Set-Location $ProjectRoot
} else {
    Write-Host "Step 1: Skipping extraction (already exists)" -ForegroundColor Yellow
    Write-Host ""
}

# Step 2: Run C# DTO generator
if (-not $SkipGeneration) {
    Write-Host "Step 2: Running C# DTO generator..." -ForegroundColor Cyan
    Write-Host ""
    
    if (-not (Test-Path $ContractPath)) {
        throw "Error: Contract file not found at $ContractPath. Run extraction first."
    }
    
    # Create generator project if it doesn't exist
    $GeneratorDir = Join-Path $PSScriptRoot "generator"
    if (-not (Test-Path $GeneratorDir)) {
        New-Item -ItemType Directory -Path $GeneratorDir | Out-Null
    }
    
    # Create generator project file
    $ProjContent = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
'@
    
    $ProjPath = Join-Path $GeneratorDir "Generator.csproj"
    if (-not (Test-Path $ProjPath)) {
        Set-Content -Path $ProjPath -Value $ProjContent
    }
    
    # Copy generator source files
    Copy-Item (Join-Path $PSScriptRoot "src\Generator.cs") $GeneratorDir -Force
    Copy-Item (Join-Path $PSScriptRoot "src\ContractModels.cs") $GeneratorDir -Force
    
    # Build and run generator
    Set-Location $GeneratorDir
    
    Write-Host "Building generator..." -ForegroundColor Yellow
    dotnet build -c Release --verbosity quiet
    
    Write-Host "Generating C# DTOs..." -ForegroundColor Yellow
    dotnet run -c Release -- --contract "$ContractPath" --output "$OutputPath" --namespace "KiloVisualStudioExtension.WebView.Generated"
    
    if (-not (Test-Path $OutputPath)) {
        throw "Error: DTOs were not generated at $OutputPath"
    }
    
    $GeneratedFiles = (Get-ChildItem $OutputPath -Recurse -Filter "*.cs").Count
    Write-Host ""
    Write-Host "DTOs generated successfully: $GeneratedFiles files in $OutputPath" -ForegroundColor Green
    Write-Host ""
    
    Set-Location $ProjectRoot
} else {
    Write-Host "Step 2: Skipping generation" -ForegroundColor Yellow
    Write-Host ""
}

# Step 3: Verify generated code builds
Write-Host "Step 3: Verifying generated code compiles..." -ForegroundColor Cyan
Write-Host ""

$VsProj = Join-Path $ProjectRoot "KiloVisualStudioExtension\KiloVisualStudioExtension.csproj"
if (Test-Path $VsProj) {
    Write-Host "Building Visual Studio extension with generated DTOs..." -ForegroundColor Yellow
    dotnet build $VsProj --verbosity minimal
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host ""
        Write-Host "Build successful! Generated DTOs compile without errors." -ForegroundColor Green
    } else {
        Write-Host ""
        Write-Host "Warning: Build failed. Please check the generated code." -ForegroundColor Red
    }
} else {
    Write-Host "Warning: Visual Studio project not found at $VsProj" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Pipeline Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Generated artifacts:" -ForegroundColor Cyan
Write-Host "  Contract: $ContractPath" -ForegroundColor Gray
Write-Host "  DTOs:     $OutputPath" -ForegroundColor Gray
Write-Host ""
Write-Host "To regenerate, run:" -ForegroundColor Cyan
Write-Host "  .\scripts\generate-webview-dtos.ps1 -Clean" -ForegroundColor White
Write-Host ""
