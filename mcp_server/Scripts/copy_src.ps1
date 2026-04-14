param(
    [string]$ZipFileName = "copy_src.zip",
    [string]$SourceRoot = ""
)

$ErrorActionPreference = "Stop"

$scriptRepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..") | Select-Object -ExpandProperty Path
$destinationRoot = (Get-Location).Path

# Auto-detect project root from script path unless user explicitly overrides.
$sourceRoot = if ([string]::IsNullOrWhiteSpace($SourceRoot)) { $scriptRepoRoot } else { (Resolve-Path $SourceRoot).Path }

# Zip is written into the current running folder (destination), per spec.
$zipPath = Join-Path $destinationRoot $ZipFileName

# Use a unique temp directory so this script can safely run multiple times.
$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("mcp_server_copy_" + [guid]::NewGuid().ToString("N"))

function Test-IsExcludedPathSegment {
    param([Parameter(Mandatory = $true)][string]$Path)

    # Normalize separators and split into segments.
    $norm = $Path -replace '[\\/]+', '\'
    $segs = $norm.Trim('\').Split('\')

    foreach ($seg in $segs) {
        if ($seg -ieq 'bin' -or $seg -ieq 'obj' -or $seg -ieq 'Release') {
            return $true
        }
    }

    return $false
}

try {
    # If the current running folder has the zip file, delete it first.
    if (Test-Path $zipPath) {
        Remove-Item -Path $zipPath -Force
    }

    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

    # Copy all files from the current running folder, preserving relative structure.
    $files = Get-ChildItem -Path $sourceRoot -Recurse -File -Force
    $copiedFileCount = 0
    foreach ($file in $files) {
        $fullPath = $file.FullName

        # Exclude any path that contains bin/ or obj/ segments.
        if (Test-IsExcludedPathSegment -Path $fullPath) {
            continue
        }

        # Exclude the output zip if someone ran the script from within mcp_server
        # and the zip already exists (belt-and-suspenders beyond step 4).
        if ($fullPath -ieq $zipPath) {
            continue
        }

        $relative = $fullPath.Substring($sourceRoot.Length).TrimStart('\', '/')
        $destPath = Join-Path $tempDir $relative

        $destDir = Split-Path $destPath -Parent
        if (-not (Test-Path $destDir)) {
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
        }

        Copy-Item -Path $fullPath -Destination $destPath -Force
        $copiedFileCount++
    }

    # Pack copied content as a zip file in the current running folder.
    if (Test-Path $zipPath) {
        Remove-Item -Path $zipPath -Force
    }

    if ($copiedFileCount -le 0) {
        # Likely causes: wrong working directory, or everything excluded by bin/obj filters.
        throw "No files were copied into the staging directory. SourceRoot=$sourceRoot ZipPath=$zipPath"
    }

    # Pass concrete item paths (not a wildcard string) so Compress-Archive reliably creates the archive.
    $rootItems = Get-ChildItem -Path $tempDir -Force
    if ($rootItems.Count -le 0) {
        throw "Staging directory is empty after copy. TempDir=$tempDir"
    }

    Write-Output "CopiedFiles=$copiedFileCount StagingRootItems=$($rootItems.Count)"
    $rootItemPaths = $rootItems | Select-Object -ExpandProperty FullName
    Compress-Archive -Path $rootItemPaths -DestinationPath $zipPath -Force -ErrorAction Stop

    if (-not (Test-Path $zipPath)) {
        throw "Zip was not created at expected path: $zipPath"
    }
}
finally {
    # Clean the temporary folder and files.
    if (Test-Path $tempDir) {
        Remove-Item -Path $tempDir -Recurse -Force
    }
}

Write-Output "Created: $zipPath"
