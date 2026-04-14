param(
    [string]$ZipFileName = "harness_src.zip",
    [string[]]$ExcludeDirectories = @(
        "bin",
        "obj",
        ".vs",
        ".idea",
        ".pytest_cache",
        "TestResults",
        "TestResult",
        "coverage",
        "artifacts",
        "Release",
        "Debug",
        "publish",
        "_output",
        "out",
        ".git",
        ".github"
    )
)

$ErrorActionPreference = "Stop"

# "current running folder" == script working directory.
$sourceRoot = (Get-Location).Path
$zipPath = Join-Path $sourceRoot $ZipFileName

if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

$tempRoot = Join-Path $env:TEMP ("copy_src_" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

try {
    # Stage copy into temp dir (preserve structure) while excluding build folders.
    $tempCopyRoot = Join-Path $tempRoot "src_copy"
    New-Item -ItemType Directory -Path $tempCopyRoot -Force | Out-Null

    # Build robocopy args to exclude directories.
    $xdArgs = @()
    foreach ($d in $ExcludeDirectories) {
        # robocopy expects raw names (not full paths)
        $xdArgs += "/XD"
        $xdArgs += $d
    }

    # Copy everything under sourceRoot into tempCopyRoot, excluding directories above.
    # /E copies subdirectories including empty ones.
    # /Z allows restartable mode (best-effort for large copies).
    # /NFL /NDL reduce verbosity.
    $null = & robocopy $sourceRoot $tempCopyRoot /E /Z /NFL /NDL /R:2 /W:1 @xdArgs

    # Zip staged content.
    # Place files under a top-level folder inside the zip.
    $stagedTop = Join-Path $tempCopyRoot (Split-Path $tempCopyRoot -Leaf)
    if (-not (Test-Path $stagedTop)) {
        # robocopy copies contents directly; stage root is $tempCopyRoot.
        $stagedTop = $tempCopyRoot
    }

    Compress-Archive -Path (Join-Path $tempCopyRoot "*") -DestinationPath $zipPath -Force
}
finally {
    # Clean temporary folder and files.
    if (Test-Path $tempRoot) {
        Remove-Item -Recurse -Force $tempRoot
    }
}

Write-Output "Created zip: $zipPath"

