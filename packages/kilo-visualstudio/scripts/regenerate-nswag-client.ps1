# NSwag C# Client Regeneration Script
# Usage: .\regenerate-nswag-client.ps1

$ErrorActionPreference = "Stop"

# Configuration
$OpenApiUrl = "http://127.0.0.1:56631/doc"
$TempDir = $env:TEMP ?? "C:\Users\$env:USERNAME\AppData\Local\Temp\kilo"
$TempOpenApiFile = Join-Path $TempDir "openapi.json"
$OutputFile = "packages\kilo-visualstudio\KiloVisualStudioExtension\ApiClient\KiloApiClient.cs"
$InheritanceFile = "packages\kilo-visualstudio\KiloVisualStudioExtension\ApiClient\ApiClientInheritance.cs"
$Namespace = "KiloVisualStudioExtension.ApiClient"
$ClassName = "KiloApiClient"

# Ensure temp directory exists
if (-not (Test-Path $TempDir)) {
    New-Item -ItemType Directory -Force -Path $TempDir | Out-Null
}

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

# Step 1b: Normalize only boolean/string anyOf schemas
Write-Host ""
Write-Host "Step 1b: Normalizing boolean anyOf schemas" `
    -ForegroundColor Yellow

$openApiDocument =
    Get-Content -Raw "$TempOpenApiFile" |
    ConvertFrom-Json

function Has-Property {
    param(
        [object]$Object,
        [string]$Name
    )

    return $null -ne $Object.PSObject.Properties[$Name]
}

function Is-BooleanSchema {
    param(
        [object]$Schema
    )

    return (
        $null -ne $Schema -and
        (Has-Property $Schema "type") -and
        $Schema.type -eq "boolean"
    )
}

function Is-TrueFalseStringSchema {
    param(
        [object]$Schema
    )

    if ($null -eq $Schema) {
        return $false
    }

    if (
        -not (Has-Property $Schema "type") -or
        $Schema.type -ne "string"
    ) {
        return $false
    }

    if (-not (Has-Property $Schema "enum")) {
        return $false
    }

    $enumValues = @($Schema.enum)

    if ($enumValues.Count -ne 2) {
        return $false
    }

    $normalizedValues = @(
        $enumValues |
            ForEach-Object {
                ([string]$_).ToLowerInvariant()
            }
    )

    return (
        $normalizedValues -contains "true" -and
        $normalizedValues -contains "false"
    )
}

function Is-BooleanStringAnyOf {
    param(
        [object]$Schema
    )

    if (
        $null -eq $Schema -or
        -not (Has-Property $Schema "anyOf")
    ) {
        return $false
    }

    $anyOf = @($Schema.anyOf)

    if ($anyOf.Count -ne 2) {
        return $false
    }

    $hasBooleanSchema = $false
    $hasTrueFalseStringSchema = $false

    foreach ($variant in $anyOf) {
        if (Is-BooleanSchema $variant) {
            $hasBooleanSchema = $true
        }
        elseif (Is-TrueFalseStringSchema $variant) {
            $hasTrueFalseStringSchema = $true
        }
    }

    return (
        $hasBooleanSchema -and
        $hasTrueFalseStringSchema
    )
}

function Normalize-BooleanStringAnyOf {
    param(
        [object]$Node,
        [string]$Path = '$'
    )

    if ($null -eq $Node) {
        return 0
    }

    $changeCount = 0

    if ($Node -is [System.Array]) {
        for ($i = 0; $i -lt $Node.Count; $i++) {
            $changeCount += Normalize-BooleanStringAnyOf `
                -Node $Node[$i] `
                -Path "$Path[$i]"
        }

        return $changeCount
    }

    if (
        $Node -isnot [PSCustomObject] -and
        $Node -isnot [System.Management.Automation.PSObject]
    ) {
        return 0
    }

    if (Is-BooleanStringAnyOf $Node) {
        $Node.PSObject.Properties.Remove("anyOf")

        if (Has-Property $Node "type") {
            $Node.type = "boolean"
        }
        else {
            $Node |
                Add-Member `
                    -MemberType NoteProperty `
                    -Name "type" `
                    -Value "boolean"
        }

        Write-Host (
            "  Normalized anyOf schema at {0}" -f $Path
        ) -ForegroundColor Gray

        $changeCount++
    }

    # Copy names before recursively traversing children.
    $propertyNames = @(
        $Node.PSObject.Properties.Name
    )

    foreach ($propertyName in $propertyNames) {
        if ([string]::IsNullOrWhiteSpace($propertyName)) {
            continue
        }

        $property = $Node.PSObject.Properties.Match($propertyName)

        if ($null -eq $property -or $null -eq $property.Value) {
            continue
        }

        $changeCount += Normalize-BooleanStringAnyOf `
            -Node $property.Value `
            -Path "$Path.$propertyName"
    }

    return $changeCount
}

$normalizedCount =
    Normalize-BooleanStringAnyOf `
        -Node $openApiDocument

$openApiDocument |
    ConvertTo-Json -Depth 100 |
    Set-Content "$TempOpenApiFile" -Encoding UTF8

Write-Host (
    "  Normalized {0} boolean anyOf schema(s)" -f $normalizedCount
) -ForegroundColor Green


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

# Step 3: Generate inheritance declarations
Write-Host ""
Write-Host "Step 3: Generating inheritance declarations" -ForegroundColor Yellow
$inheritanceScript = Join-Path $PSScriptRoot ".\generate-inheritance.ps1"
if (Test-Path $inheritanceScript) {
    try {
        & $inheritanceScript -OpenApiSpec $TempOpenApiFile -Output $InheritanceFile
        Write-Host "  Inheritance declarations generated successfully" -ForegroundColor Green
    } catch {
        Write-Host "  ERROR: Failed to generate inheritance declarations: $_" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "  WARNING: Inheritance generation script not found at $inheritanceScript" -ForegroundColor Yellow
}

# Step 4: Verify output (operationGenerationMode prevents duplication)
Write-Host ""
Write-Host "Step 4: Verifying output" -ForegroundColor Yellow
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
Write-Host "Inheritance: $InheritanceFile" -ForegroundColor Gray
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Build the project: dotnet build packages\kilo-visualstudio\KiloVisualStudioExtension\KiloVisualStudioExtension.csproj" -ForegroundColor Gray
Write-Host "  2. Run tests: dotnet test packages\kilo-visualstudio\KiloVisualStudioExtension.Tests\KiloVisualStudioExtension.Tests.csproj --filter 'FullyQualifiedName~NswagPolymorphicModelTests|FullyQualifiedName~PolymorphicDeserializerTests'" -ForegroundColor Gray
Write-Host "  3. Review changes in the generated files" -ForegroundColor Gray
Write-Host "  4. Update NSWAG-GENERATION.md with new Git SHA if API changed" -ForegroundColor Gray
