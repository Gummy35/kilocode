<#
.SYNOPSIS
    Generates ApiClientInheritance.cs from OpenAPI spec by analyzing polymorphic schemas.

.DESCRIPTION
    This script analyzes the OpenAPI spec JSON file and generates a C# file containing
    partial class declarations that restore inheritance relationships defined using
    anyOf/oneOf in the OpenAPI spec.

    NSwag generates independent classes for polymorphic schemas, but the OpenAPI spec
    defines inheritance relationships that should be restored for proper polymorphic behavior.

    This script focuses on the key polymorphic types used in the Visual Studio extension:
    - ToolState (ToolStatePending, ToolStateRunning, ToolStateCompleted, ToolStateError)
    - Part (TextPart, ReasoningPart, FilePart, ToolPart, etc.)
    - FilePartSource (FileSource, SymbolSource, ResourceSource)
    - SessionMessage (SessionMessageUser, SessionMessageAssistant, etc.)
    - OutputFormat (OutputFormatText, OutputFormatJsonSchema)

.PARAMETER OpenApiSpec
    Path to the OpenAPI spec JSON file.

.PARAMETER Output
    Path to the output C# file (ApiClientInheritance.cs).

.PARAMETER IncludeAll
    If specified, includes ALL polymorphic hierarchies from the spec (not recommended).
    By default, only includes the key hierarchies used by the Visual Studio extension.

.EXAMPLE
    .\script\generate-inheritance.ps1 `
      -OpenApiSpec "packages\kilo-visualstudio\porting\docs\openapi-spec.json" `
      -Output "packages\kilo-visualstudio\KiloVisualStudioExtension\ApiClient\ApiClientInheritance.cs"
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$OpenApiSpec,
    
    [Parameter(Mandatory = $true)]
    [string]$Output,
    
    [switch]$IncludeAll
)

# Read and parse the OpenAPI spec
Write-Host "Reading OpenAPI spec from: $OpenApiSpec"
$specJson = Get-Content -Path $OpenApiSpec -Raw | ConvertFrom-Json

# Key polymorphic types for the Visual Studio extension
$keySchemas = @('ToolState', 'Part', 'FilePartSource', 'SessionMessage', 'OutputFormat')

# Find all schemas with anyOf or oneOf
Write-Host "Analyzing polymorphic schemas..."
$polymorphicSchemas = @{}

$componentsSchemas = $specJson.components.schemas
if ($componentsSchemas) {
    $componentsSchemas.PSObject.Properties | ForEach-Object {
        $schemaName = $_.Name
        $schema = $_.Value
        
        # Skip if not a key schema (unless -IncludeAll is specified)
        if (-not $IncludeAll -and $keySchemas -notcontains $schemaName) {
            return
        }
        
        # Check for anyOf or oneOf
        $anyOf = $null
        $oneOf = $null
        
        if ($schema.PSObject.Properties.Name -contains "anyOf") {
            $anyOf = $schema.anyOf
        }
        if ($schema.PSObject.Properties.Name -contains "oneOf") {
            $oneOf = $schema.oneOf
        }
        
        if ($anyOf -or $oneOf) {
            $derivedTypes = @()
            $refs = @()
            
            if ($anyOf) {
                $refs += $anyOf
            }
            if ($oneOf) {
                $refs += $oneOf
            }
            
            $refs | ForEach-Object {
                # Access the $ref property
                $refValue = $_.PSObject.Properties.Where({ $_.Name -eq '$ref' }).Value
                if ($refValue) {
                    # Extract type name from #/components/schemas/TypeName
                    $typeName = $refValue -replace '^#/components/schemas/', ''
                    $derivedTypes += $typeName
                }
            }
            
            if ($derivedTypes.Count -gt 0) {
                $polymorphicSchemas[$schemaName] = $derivedTypes
            }
        }
    }
}

# Generate the inheritance file
Write-Host "Generating inheritance declarations..."

$lines = @()
$lines += "namespace KiloVisualStudioExtension.ApiClient"
$lines += "{"
$lines += "    /// <summary>"
$lines += "    /// Inheritance declarations for polymorphic types defined in the OpenAPI spec."
$lines += "    /// NSwag generates independent classes for polymorphic schemas (anyOf/oneOf),"
$lines += "    /// but the OpenAPI spec defines inheritance relationships that should be restored"
$lines += "    /// here for proper polymorphic behavior."
$lines += "    /// </summary>"
$lines += ""

# Sort schemas alphabetically for consistent output
$sortedKeys = $polymorphicSchemas.Keys | Sort-Object

foreach ($baseType in $sortedKeys) {
    $derivedTypes = $polymorphicSchemas[$baseType] | Sort-Object
    
    # Add comment with OpenAPI definition
    $refs = $derivedTypes -join ", "
    $lines += "    // $baseType hierarchy"
    $lines += "    // Defined in OpenAPI spec as: `"$baseType`": { `"anyOf`": [$refs] }"
    
    foreach ($derivedType in $derivedTypes) {
        $lines += "    public partial class $derivedType : $baseType { }"
    }
    
    $lines += ""
}

$lines += "}"

# Write the output file
Write-Host "Writing output to: $Output"
$lines | Set-Content -Path $Output -Encoding UTF8

Write-Host "Generated $($polymorphicSchemas.Count) polymorphic hierarchies with $($lines.Count) lines"
Write-Host "Done!"
