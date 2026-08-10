# NSwag C# Client Regeneration Script
# Usage: .\regenerate-nswag-client.ps1

$ErrorActionPreference = "Stop"

# Configuration
$OpenApiUrl = "http://127.0.0.1:56631/doc"
$TempOpenApiFile = "C:\Users\RFHP2615\AppData\Local\Temp\kilo\openapi.json"
$TempRawClientFile = "C:\Users\RFHP2615\AppData\Local\Temp\kilo\KiloApiClient_raw.cs"
$OutputFile = "packages\kilo-visualstudio\KiloVisualStudioExtension\ApiClient\KiloApiClient.cs"
$Namespace = "KiloVisualStudioExtension.ApiClient"
$ClassName = "KiloApiClient"

Write-Host "=== NSwag C# Client Regeneration ===" -ForegroundColor Cyan
Write-Host ""

# Step 1: Fetch OpenAPI spec (or use existing if server not running)
Write-Host "Step 1: Fetching OpenAPI spec from $OpenApiUrl" -ForegroundColor Yellow
try {
    Invoke-WebRequest -Uri $OpenApiUrl -OutFile $TempOpenApiFile -UseBasicParsing
    Write-Host "  OpenAPI spec fetched from server" -ForegroundColor Green
} catch {
    if (Test-Path $TempOpenApiFile) {
        Write-Host "  Server not running, using existing OpenAPI spec" -ForegroundColor Yellow
    } else {
        Write-Host "  ERROR: No OpenAPI spec available and server not running: $_" -ForegroundColor Red
        exit 1
    }
}

# Step 2: Run NSwag
Write-Host ""
Write-Host "Step 2: Running NSwag code generator" -ForegroundColor Yellow
$nswagArgs = @(
    "openapi2csclient",
    "/input:`"$TempOpenApiFile`"",
    "/output:`"$OutputFile`"",
    "/namespace:`"$Namespace`"",
    "/ClassName:`"$ClassName`"",
    "/GenerateClientInterfaces:true",
    "/GenerateExceptionClasses:true",
    "/ExceptionClass:`"ApiException`"",
    "/UseBaseUrl:true",
    "/GenerateBaseUrlProperty:true",
    "/InjectHttpClient:false",
    "/GenerateNativeRecords:false",
    "/GenerateDataAnnotations:false",
    "/GenerateJsonMethods:true",
    "/EnforceFlagEnums:false",
    "/GenerateDefaultValues:true",
    "/GenerateImmutableArrayProperties:false",
    "/GenerateImmutableDictionaryProperties:false",
    "/GenerateResponseClasses:true",
    "/ResponseClass:`"ApiResponse`"",
    "/operationGenerationMode:`"SingleClientFromOperationId`""
)

Write-Host "  Command: nswag $($nswagArgs -join ' ')" -ForegroundColor Gray
try {
    & nswag $nswagArgs
    Write-Host "  NSwag completed successfully" -ForegroundColor Green
} catch {
    Write-Host "  ERROR: NSwag failed: $_" -ForegroundColor Red
    exit 1
}

# Step 3: Verify output (operationGenerationMode prevents duplication)
Write-Host ""
Write-Host "Step 3: Verifying output" -ForegroundColor Yellow
try {
    $lines = Get-Content $OutputFile
    $interfaceCount = ($lines | Select-String -Pattern "^\s*public partial interface IKiloApiClient" | Measure-Object).Count
    $classCount = ($lines | Select-String -Pattern "^\s*public partial class KiloApiClient" | Measure-Object).Count
    
    if ($interfaceCount -eq 1 -and $classCount -eq 1) {
        Write-Host "  VERIFICATION PASSED - No duplication detected" -ForegroundColor Green
    } else {
        Write-Host "  VERIFICATION FAILED - Expected 1 interface and 1 class, found $interfaceCount interfaces and $classCount classes" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "  ERROR: Failed to verify output: $_" -ForegroundColor Red
    exit 1
}



Write-Host ""
Write-Host "=== Regeneration Complete ===" -ForegroundColor Cyan
Write-Host "Output: $OutputFile" -ForegroundColor Gray
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Build the project: dotnet build packages\kilo-visualstudio\KiloVisualStudioExtension\KiloVisualStudioExtension.csproj" -ForegroundColor Gray
Write-Host "  2. Review changes in the generated file" -ForegroundColor Gray
Write-Host "  3. Update NSWAG-GENERATION.md with new Git SHA if API changed" -ForegroundColor Gray
