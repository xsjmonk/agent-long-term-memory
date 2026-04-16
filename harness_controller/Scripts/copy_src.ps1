param(
    [string]$ZipFileName = "harness_src.zip"
)

$ErrorActionPreference = "Stop"

# Target is always the harness folder (script lives under harness/Scripts).
$harnessRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path.TrimEnd('\', '/')

# "current running folder" == where the zip is written.
$zipPath = Join-Path (Get-Location).Path $ZipFileName
if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

# Exclude these directories entirely (build outputs, caches, VCS metadata).
$excludeDirNames = @(
    "bin",
    "obj",
    "Release",
    "Debug",
    "publish",
    "TestResults",
    "TestResult",
    "coverage",
    "artifacts",
    ".vs",
    ".idea",
    ".pytest_cache",
    ".git",
    ".github"
)

# Only include source-code-like files.
$includeExtensions = @(
    ".cs",
    ".csproj",
    ".props",
    ".targets",
    ".sln",
    ".md",
    ".mdc",
    ".txt",
    ".json",
    ".ps1",
    ".yml",
    ".yaml",
    ".editorconfig",
    ".gitignore",
    ".xml"
)

$tempRoot = Join-Path $env:TEMP ("copy_src_" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

try {
    $stageRoot = Join-Path $tempRoot "harness_src"
    New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null

    $files = Get-ChildItem -Path $harnessRoot -Recurse -File -Force

    foreach ($f in $files) {
        # Skip excluded directory names.
        $segments = $f.FullName.Split([System.IO.Path]::DirectorySeparatorChar, [System.StringSplitOptions]::RemoveEmptyEntries)
        $skip = $false
        foreach ($seg in $segments) {
            if ($excludeDirNames -contains $seg) { $skip = $true; break }
        }
        if ($skip) { continue }

        $ext = $f.Extension.ToLowerInvariant()
        if (-not ($includeExtensions -contains $ext)) { continue }

        $fPath = $f.FullName
        if ($fPath.StartsWith($harnessRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            $relative = $fPath.Substring($harnessRoot.Length).TrimStart('\', '/')
        }
        else {
            $relative = $f.Name
        }
        $dest = Join-Path $stageRoot $relative
        $destDir = Split-Path $dest -Parent
        if ([string]::IsNullOrWhiteSpace($destDir)) { continue }
        if (-not (Test-Path $destDir)) {
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
        }

        Copy-Item -Path $f.FullName -Destination $dest -Force
    }

    Compress-Archive -Path (Join-Path $stageRoot "*") -DestinationPath $zipPath -Force
}
finally {
    if (Test-Path $tempRoot) {
        Remove-Item -Recurse -Force $tempRoot
    }
}

Write-Output "Created zip: $zipPath"

