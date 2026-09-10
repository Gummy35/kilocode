# NSwag C# Client Regeneration Script
# Usage: .\regenerate-nswag-client.ps1

$ErrorActionPreference = "Stop"

# Configuration
$OpenApiUrl = "http://127.0.0.1:56631/doc"
$TempDir = $env:TEMP ?? "C:\Users\$env:USERNAME\AppData\Local\Temp\kilo"
$TempOpenApiFile = Join-Path $TempDir "openapi.json"
$OutputFile = "packages\kilo-visualstudio\KiloVisualStudioExtension\ApiClient\KiloApiClient.cs"
$InheritanceFile = "packages\kilo-visualstudio\KiloVisualStudioExtension\ApiClient\ApiClientInheritance.generated.cs"
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

function Get-DeterministicTypeName {
    param(
        [string]$Name
    )

    $parts =
        ([string]$Name -replace '[^A-Za-z0-9]+', ' ') `
            -split '\s+' |
        Where-Object {
            -not [string]::IsNullOrWhiteSpace($_)
        }

    $result = ""

    foreach ($part in $parts) {
        if ($part.Length -eq 1) {
            $result += $part.ToUpperInvariant()
        }
        else {
            $result +=
                $part.Substring(0, 1).ToUpperInvariant() +
                $part.Substring(1)
        }
    }

    if ([string]::IsNullOrWhiteSpace($result)) {
        return "Anonymous"
    }

    if ($result -match '^[0-9]') {
        return "Schema$result"
    }

    return $result
}

function Is-ReferenceSchema {
    param(
        [object]$Schema
    )

    return (
        $null -ne $Schema -and
        $null -ne $Schema.PSObject.Properties['$ref']
    )
}

function Is-ComplexSchema {
    param(
        [object]$Schema
    )

    if ($null -eq $Schema) {
        return $false
    }

    return (
        (Has-Property $Schema "properties") -or
        (Has-Property $Schema "items") -or
        (Has-Property $Schema "anyOf") -or
        (Has-Property $Schema "oneOf") -or
        (Has-Property $Schema "allOf")
    )
}

$script:UsedSchemaTitles = @{}
$script:SchemaTitleByIdentity = @{}

# Reserve component names because NSwag also generates these types.
if ($null -ne $openApiDocument.components.schemas) {
    foreach (
        $schemaProperty in
        $openApiDocument.components.schemas.PSObject.Properties
    ) {
        $componentName =
            Get-DeterministicTypeName $schemaProperty.Name

        $script:UsedSchemaTitles[$componentName] = $true
    }
}

function Get-SchemaIdentity {
    param(
        [object]$Schema
    )

    return [System.Runtime.CompilerServices.RuntimeHelpers]::GetHashCode(
        $Schema
    )
}

function Get-TitleSuffixFromPath {
    param(
        [string]$Path
    )

    $suffix =
        $Path `
            -replace '[^A-Za-z0-9]+', ' ' `
            -split '\s+' |
        Where-Object {
            -not [string]::IsNullOrWhiteSpace($_)
        } |
        ForEach-Object {
            $part = [string]$_

            if ($part.Length -eq 1) {
                $part.ToUpperInvariant()
            }
            else {
                $part.Substring(0, 1).ToUpperInvariant() +
                $part.Substring(1)
            }
        }

    return ($suffix -join '')
}

function Get-UniqueSchemaTitle {
    param(
        [object]$Schema,
        [string]$BaseTitle,
        [string]$Path
    )

    $identity =
        Get-SchemaIdentity $Schema

    $identityKey =
        [string]$identity

    # If the same schema object is encountered again, reuse its title.
    if ($script:SchemaTitleByIdentity.ContainsKey($identityKey)) {
        return $script:SchemaTitleByIdentity[$identityKey]
    }

    $candidate =
        Get-DeterministicTypeName $BaseTitle

    if (
        -not $script:UsedSchemaTitles.ContainsKey($candidate)
    ) {
        $script:UsedSchemaTitles[$candidate] = $true
        $script:SchemaTitleByIdentity[$identityKey] = $candidate

        return $candidate
    }

    # First collision: include the schema path.
    $pathSuffix =
        Get-TitleSuffixFromPath $Path

    if (-not [string]::IsNullOrWhiteSpace($pathSuffix)) {
        $candidate =
            Get-DeterministicTypeName "$BaseTitle$pathSuffix"
    }

    # Final deterministic fallback.
    $index = 2
    $baseCandidate = $candidate

    while ($script:UsedSchemaTitles.ContainsKey($candidate)) {
        $candidate =
            "${baseCandidate}$index"

        $index++
    }

    $script:UsedSchemaTitles[$candidate] = $true
    $script:SchemaTitleByIdentity[$identityKey] = $candidate

    return $candidate
}

function Visit-Schema {
    param(
        [object]$Schema,
        [string]$Title,
        [string]$Path,
        [System.Collections.Generic.HashSet[string]]$Visited
    )

    if (
        $null -eq $Schema -or
        (Is-ReferenceSchema $Schema) -or
        -not (Is-ComplexSchema $Schema)
    ) {
        return
    }

    $identity =
        [string](Get-SchemaIdentity $Schema)

    if ($Visited.Contains($identity)) {
        return
    }

    $Visited.Add($identity) | Out-Null

    Set-SchemaTitle `
        -Schema $Schema `
        -Title $Title `
        -Path $Path

    if (Has-Property $Schema "properties") {
        foreach (
            $property in
            $Schema.properties.PSObject.Properties
        ) {
            $propertyName =
                Get-DeterministicTypeName $property.Name

            Visit-Schema `
                -Schema $property.Value `
                -Title "${Title}${propertyName}" `
                -Path "$Path/properties/$($property.Name)" `
                -Visited $Visited
        }
    }

    if (Has-Property $Schema "items") {
        Visit-Schema `
            -Schema $Schema.items `
            -Title "${Title}Item" `
            -Path "$Path/items" `
            -Visited $Visited
    }

    if (Has-Property $Schema "anyOf") {
        $index = 1

        foreach ($variant in @($Schema.anyOf)) {
            Visit-Schema `
                -Schema $variant `
                -Title "${Title}Variant$index" `
                -Path "$Path/anyOf/$index" `
                -Visited $Visited

            $index++
        }
    }

    if (Has-Property $Schema "oneOf") {
        $index = 1

        foreach ($variant in @($Schema.oneOf)) {
            Visit-Schema `
                -Schema $variant `
                -Title "${Title}Variant$index" `
                -Path "$Path/oneOf/$index" `
                -Visited $Visited

            $index++
        }
    }

    if (Has-Property $Schema "allOf") {
        $index = 1

        foreach ($variant in @($Schema.allOf)) {
            Visit-Schema `
                -Schema $variant `
                -Title "${Title}Base$index" `
                -Path "$Path/allOf/$index" `
                -Visited $Visited

            $index++
        }
    }
}

function Set-SchemaTitle {
    param(
        [object]$Schema,
        [string]$Title,
        [string]$Path
    )

    if (
        $null -eq $Schema -or
        (Is-ReferenceSchema $Schema) -or
        -not (Is-ComplexSchema $Schema)
    ) {
        return
    }

    $assignedTitle =
        Get-UniqueSchemaTitle `
            -Schema $Schema `
            -BaseTitle $Title `
            -Path $Path

    if (
        -not (Has-Property $Schema "title") -or
        [string]::IsNullOrWhiteSpace(
            [string]$Schema.title)
    ) {
        $Schema |
            Add-Member `
                -MemberType NoteProperty `
                -Name "title" `
                -Value $assignedTitle `
                -Force
    }
    else {
        $Schema.title =
            $assignedTitle
    }
}

function Get-OperationTypeName {
    param(
        [object]$Operation,
        [string]$Method,
        [string]$Path
    )

    if (
        $null -ne $Operation.operationId -and
        -not [string]::IsNullOrWhiteSpace(
            [string]$Operation.operationId)
    ) {
        return Get-DeterministicTypeName `
            ([string]$Operation.operationId)
    }

    return Get-DeterministicTypeName `
        "$Method $Path"
}

function Add-DeterministicSchemaTitles {
    param(
        [object]$OpenApiDocument
    )

    foreach (
        $pathProperty in
        $OpenApiDocument.paths.PSObject.Properties
    ) {
        $path =
            $pathProperty.Value

        foreach (
            $operationProperty in
            $path.PSObject.Properties
        ) {
            $method =
                $operationProperty.Name.ToLowerInvariant()

            if (
                $method -notin @(
                    "get",
                    "post",
                    "put",
                    "patch",
                    "delete",
                    "options",
                    "head",
                    "trace"
                )
            ) {
                continue
            }

            $operation =
                $operationProperty.Value

            $operationName =
                Get-OperationTypeName `
                    -Operation $operation `
                    -Method $method `
                    -Path $pathProperty.Name

            # Request body schemas
            if ($null -ne $operation.requestBody) {
                $content =
                    $operation.requestBody.content

                if ($null -ne $content) {
                    foreach (
                        $contentProperty in
                        $content.PSObject.Properties
                    ) {
                        $schema =
                            $contentProperty.Value.schema

                        $visited =
                            [System.Collections.Generic.HashSet[string]]::new()

                        Visit-Schema `
                            -Schema $schema `
                            -Title "${operationName}Request" `
                            -Path "$($pathProperty.Name)/$method/requestBody/$($contentProperty.Name)" `
                            -Visited $visited
                    }
                }
            }

            # Response schemas
            if ($null -ne $operation.responses) {
                $responseProperties = @(
                    $operation.responses.PSObject.Properties |
                        Where-Object {
                            $null -ne $_.Value.content
                        }
                )

                $hasOnlyResponse200 = (
                    $responseProperties.Count -eq 1 -and
                    [string]$responseProperties[0].Name -eq "200"
                )

                foreach ($responseProperty in $responseProperties) {
                    $response =
                        $responseProperty.Value

                    $statusCode =
                        [string]$responseProperty.Name

                    if ($hasOnlyResponse200) {
                        $responseTitle =
                            "${operationName}Response"
                    }
                    else {
                        $responseTitle =
                            "${operationName}Response$(
                                Get-DeterministicTypeName $statusCode
                            )"
                    }

                    foreach (
                        $contentProperty in
                        $response.content.PSObject.Properties
                    ) {
                        $schema =
                            $contentProperty.Value.schema

                        Set-SchemaTitle `
                            -Schema $schema `
                            -Title $responseTitle
                    }
                }
            }
        }
    }
}


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

Write-Host ""

Write-Host "Step 1c: Adding deterministic schema titles" `
    -ForegroundColor Yellow

Add-DeterministicSchemaTitles `
    -OpenApiDocument $openApiDocument

Write-Host "  Deterministic schema titles added" `
    -ForegroundColor Green

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

    "/input:$TempOpenApiFile",
    "/output:$OutputFile",
    "/namespace:$Namespace",
    "/ClassName:$ClassName",

    "/GenerateClientInterfaces:true",
    "/GenerateExceptionClasses:true",
    "/ExceptionClass:ApiException",

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

    "/GenerateResponseClasses:false",
  
    "/operationGenerationMode:SingleClientFromOperationId"
)
#  "/GenerateResponseClasses:true",
  #  "/ResponseClass:ApiResponse",

Write-Host "  Command: nswag $($nswagArgs -join ' ')" -ForegroundColor Gray
try {
    & nswag $nswagArgs

    if ($LASTEXITCODE -ne 0) {
        throw "NSwag exited with code $LASTEXITCODE."
    }

    Write-Host `
        "  NSwag completed successfully" `
        -ForegroundColor Green
}
catch {
    Write-Host `
        "  ERROR: NSwag failed: $_" `
        -ForegroundColor Red

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
