# Generate files.json for PORT-INFRA-001
# This script inventories all files in the Visual Studio extension

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootPath = Join-Path $scriptDir ".."
$outputPath = Join-Path $scriptDir "manifest\files.json"

# Get all files excluding build/temp directories
$files = Get-ChildItem -Recurse -File -Path $rootPath | Where-Object { 
    $_.FullName -notmatch '\\\.git\\' -and 
    $_.FullName -notmatch '\\\.vs\\' -and 
    $_.FullName -notmatch '\\bin\\' -and 
    $_.FullName -notmatch '\\obj\\' -and 
    $_.FullName -notmatch '\\node_modules\\'
} | Sort-Object FullName -CaseSensitive

$entries = @()

$rootPathFull = (Get-Item $rootPath).FullName
foreach ($file in $files) {
    $relativePath = $file.FullName.Substring($rootPathFull.Length + 1).Replace('\', '/')
    $ext = $file.Extension.ToLowerInvariant().TrimStart('.')
    $fileName = $file.Name
    
    # Generate fileId: file-<project>-<path-components>-<ext>
    $pathParts = $relativePath.Split('/')
    $project = $pathParts[0]
    if ($pathParts.Length -eq 2) {
        $fileId = "file-{0}-{1}-{2}" -f $project, [System.IO.Path]::GetFileNameWithoutExtension($fileName), $ext
    } else {
        $pathComponents = $pathParts[1..($pathParts.Length-2)] -join '-'
        $fileId = "file-{0}-{1}-{2}-{3}" -f $project, $pathComponents, [System.IO.Path]::GetFileNameWithoutExtension($fileName), $ext
    }
    
    # Compute SHA-256 hash
    $hash = Get-FileHash -Path $file.FullName -Algorithm SHA256
    $sha256 = $hash.Hash.ToLowerInvariant()
    
    # Count lines for text files
    $lineCount = 0
    try {
        $content = Get-Content -Path $file.FullName -Raw -ErrorAction SilentlyContinue
        if ($content) {
            $lineCount = ($content.Split("`n")).Length
        }
    } catch {}
    
    # Determine fileType, language, classification, isGenerated
    $fileType = ""
    $language = ""
    $classification = ""
    $isGenerated = $false
    
    if ($relativePath -match 'KiloVisualStudioExtension\.Tests') {
        $classification = "test"
    } elseif ($relativePath -match 'KiloVisualStudioExtension/') {
        $classification = "production"
    } elseif ($relativePath -match 'webview/') {
        $classification = "asset"
    } elseif ($ext -eq 'md') {
        $classification = "documentation"
    } else {
        $classification = "other"
    }
    
    switch ($ext) {
        'cs' { 
            $fileType = "csharp"
            $language = "csharp"
        }
        'csproj' { 
            $fileType = "csproj"
            $language = "xml"
            $isGenerated = $true
        }
        'js' {
            if ($relativePath -match 'webview/') {
                $fileType = "webview-js"
                $language = "javascript"
                $isGenerated = $true
            } else {
                $fileType = "javascript"
                $language = "javascript"
            }
        }
        'css' {
            if ($relativePath -match 'webview/') {
                $fileType = "webview-css"
                $language = "css"
            } else {
                $fileType = "css"
                $language = "css"
            }
        }
        'woff' {
            $fileType = "webview-font"
            $language = "font"
        }
        'woff2' {
            $fileType = "webview-font"
            $language = "font"
        }
        'ttf' {
            $fileType = "webview-font"
            $language = "font"
        }
        'html' {
            if ($relativePath -match 'webview/') {
                $fileType = "webview-html"
                $language = "html"
            } else {
                $fileType = "html"
                $language = "html"
            }
        }
        'png' {
            $fileType = "webview-image"
            $language = "image"
        }
        'svg' {
            $fileType = "webview-image"
            $language = "image"
        }
        'map' {
            $fileType = "webview-map"
            $language = "javascript"
            $isGenerated = $true
        }
        'vsct' {
            $fileType = "xml"
            $language = "xml"
        }
        'resx' {
            $fileType = "xml"
            $language = "xml"
        }
        'vsixmanifest' {
            $fileType = "manifest"
            $language = "xml"
        }
        'md' {
            $fileType = "markdown"
            $language = "markdown"
        }
        'json' {
            if ($relativePath -match 'webview/') {
                $fileType = "webview-json"
                $language = "json"
            } else {
                $fileType = "config"
                $language = "json"
            }
        }
        'ts' {
            $fileType = "typescript"
            $language = "typescript"
        }
        default {
            $fileType = $ext
            $language = "unknown"
        }
    }
    
    $entry = [PSCustomObject]@{
        fileId = $fileId
        relativePath = $relativePath
        sha256 = $sha256
        lineCount = $lineCount
        fileType = $fileType
        language = $language
        classification = $classification
        isGenerated = $isGenerated
        size = $file.Length
    }
    
    $entries += $entry
}

# Sort by relativePath using Ordinal comparison
$sortedEntries = $entries | Sort-Object { $_.relativePath } -CaseSensitive

# Output JSON
$json = $sortedEntries | ConvertTo-Json -Depth 10
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($outputPath, $json, $utf8NoBom)

Write-Host "Generated files.json with $($entries.Count) entries"
