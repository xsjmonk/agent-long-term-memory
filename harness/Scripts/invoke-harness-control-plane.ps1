# invoke-harness-control-plane.ps1
# Wrapper script for Harness Control Plane protocol
# This is the ONLY supported harness entrypoint
# Usage: invoke-harness-control-plane.ps1 -Command <command> [options]

param(
    [Parameter(Mandatory=$true)]
    [string]$Command,

    [string]$SessionId,
    [string]$RawTask,
    [string]$Action,
    [string]$ArtifactType,
    [string]$ArtifactFile,
    [string]$Project
)

$scriptDir = Split-Path $PSScriptRoot -Parent

$exePath = $null
if ($env:HARNESS_EXE_PATH) {
    $exePath = $env:HARNESS_EXE_PATH
    if (!(Test-Path $exePath)) {
        Write-Error "HARNESS_EXE_PATH set but executable not found: $exePath"
        exit 1
    }
}
else {
    # Search order: Release/net10.0, then Debug/net10.0
    $searchPaths = @(
        "$scriptDir\src\HarnessMcp.ControlPlane\bin\Release\net10.0\HarnessMcp.ControlPlane.exe",
        "$scriptDir\src\HarnessMcp.ControlPlane\bin\Release\net10.0\HarnessMcp.ControlPlane",
        "$scriptDir\src\HarnessMcp.ControlPlane\bin\Debug\net10.0\HarnessMcp.ControlPlane.exe",
        "$scriptDir\src\HarnessMcp.ControlPlane\bin\Debug\net10.0\HarnessMcp.ControlPlane"
    )
    foreach ($path in $searchPaths) {
        if ((Test-Path $path) -or (Test-Path "$path.exe")) {
            $exePath = $path
            if (Test-Path "$path.exe") { $exePath = "$path.exe" }
            break
        }
    }
}

if (-not $exePath) {
    Write-Error "ControlPlane executable not found. Set `$env:HARNESS_EXE_PATH or build the project first."
    Write-Host "Build with: dotnet build src/HarnessMcp.ControlPlane"
    exit 1
}

$args = @($Command)

if ($SessionId) { $args += "--session-id"; $args += $SessionId }
if ($RawTask) { $args += "--raw-task"; $args += $RawTask }
if ($Action) { $args += "--action"; $args += $Action }
if ($ArtifactType) { $args += "--artifact-type"; $args += $ArtifactType }
if ($ArtifactFile) { $args += "--artifact-file"; $args += $ArtifactFile }
if ($Project) { $args += "--project"; $args += $Project }

& $exePath @args
$exitCode = $LASTEXITCODE

exit $exitCode