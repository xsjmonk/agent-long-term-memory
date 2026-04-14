param(
    [string]$OutputName = "agent_embedding_builder_package"
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$destinationRoot = Join-Path (Get-Location) $OutputName
$zipPath = Join-Path (Get-Location) ($OutputName + ".zip")

$excludeDirNames = @("__pycache__", ".pytest_cache", ".ruff_cache", ".mypy_cache", ".git", ".venv", "venv")
$excludeFilePatterns = @("*.pyc", "*.pyo", "*.pyd", "*.tmp", "*.cache", "*.log")

function Test-ExcludedPath {
    param([System.IO.FileSystemInfo]$Item)

    foreach ($dirName in $excludeDirNames) {
        if ($Item.FullName -match [regex]::Escape([IO.Path]::DirectorySeparatorChar + $dirName + [IO.Path]::DirectorySeparatorChar)) {
            return $true
        }
        if ($Item.PSIsContainer -and $Item.Name -eq $dirName) {
            return $true
        }
    }

    if (-not $Item.PSIsContainer) {
        foreach ($pattern in $excludeFilePatterns) {
            if ($Item.Name -like $pattern) {
                return $true
            }
        }
    }

    return $false
}

if (Test-Path -LiteralPath $destinationRoot) {
    Write-Host "[1/5] Removing existing temporary folder: $destinationRoot"
    Remove-Item -LiteralPath $destinationRoot -Recurse -Force
}

Write-Host "[2/5] Creating temporary package folder: $destinationRoot"
New-Item -ItemType Directory -Path $destinationRoot | Out-Null

$items = Get-ChildItem -LiteralPath $projectRoot -Force
$totalItems = ($items | Where-Object { -not (Test-ExcludedPath -Item $_) }).Count
$currentItem = 0
foreach ($item in $items) {
    if (Test-ExcludedPath -Item $item) {
        continue
    }

    $currentItem += 1
    Write-Host "[3/5] Copying item $currentItem of ${totalItems}: $($item.Name)"
    $targetPath = Join-Path $destinationRoot $item.Name

    if ($item.PSIsContainer) {
        if ($item.Name -eq "config") {
            New-Item -ItemType Directory -Path $targetPath -Force | Out-Null
            Get-ChildItem -LiteralPath $item.FullName -Force -File |
                Where-Object { -not (Test-ExcludedPath -Item $_) } |
                ForEach-Object {
                    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $targetPath $_.Name) -Force
                }
            continue
        }

        Copy-Item -LiteralPath $item.FullName -Destination $targetPath -Recurse -Force -Exclude $excludeFilePatterns

        Get-ChildItem -LiteralPath $targetPath -Recurse -Force |
            Where-Object { Test-ExcludedPath -Item $_ } |
            Sort-Object FullName -Descending |
            ForEach-Object {
                Remove-Item -LiteralPath $_.FullName -Recurse -Force
            }
    }
    else {
        Copy-Item -LiteralPath $item.FullName -Destination $targetPath -Force
    }
}

if (Test-Path -LiteralPath $zipPath) {
    Write-Host "[4/5] Removing existing zip: $zipPath"
    Remove-Item -LiteralPath $zipPath -Force
}

Write-Host "[4/5] Creating zip archive: $zipPath"
Compress-Archive -Path (Join-Path $destinationRoot "*") -DestinationPath $zipPath -Force

Write-Host "[5/5] Cleaning up temporary folder: $destinationRoot"
Remove-Item -LiteralPath $destinationRoot -Recurse -Force

Write-Host "Created zip: $zipPath"
