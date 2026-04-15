# invoke-harness-control-plane.ps1
# The ONLY supported entrypoint for the Harness Control-Plane protocol.
#
# COMMANDS
#   start-session
#       --raw-task <string>        Raw task description (required)
#       --project  <string>        Project name (optional)
#
#   submit-step-result
#       --session-id    <string>   Session identifier returned by start-session (required)
#       --action        <string>   Completed action name, e.g. agent_generate_requirement_intent (required)
#       --artifact-type <string>   Artifact type, e.g. RequirementIntent (required)
#       --artifact-file <string>   Path to JSON file containing the artifact (required)
#
#   get-next-step
#       --session-id    <string>   Session identifier (required)
#
#   get-session-status
#       --session-id    <string>   Session identifier (required)
#
#   cancel-session
#       --session-id    <string>   Session identifier (required)
#
# EXIT CODES
#   0   success
#   1   error (see stderr and/or JSON response for details)
#
# ENVIRONMENT
#   HARNESS_EXE_PATH   Override the control-plane executable path.

param(
    [Parameter(Mandatory=$true, Position=0)]
    [ValidateSet("start-session", "submit-step-result", "get-next-step", "get-session-status", "cancel-session")]
    [string]$Command,

    [string]$SessionId,
    [string]$RawTask,
    [string]$Action,
    [string]$ArtifactType,
    [string]$ArtifactFile,
    [string]$Project
)

$repoRoot = Split-Path $PSScriptRoot -Parent

# --- Locate the control-plane executable ---

$exePath = $null

if ($env:HARNESS_EXE_PATH) {
    $exePath = $env:HARNESS_EXE_PATH
    if (!(Test-Path $exePath)) {
        Write-Error "HARNESS_EXE_PATH is set but the executable was not found: $exePath"
        exit 1
    }
}
else {
    $searchPaths = @(
        "$repoRoot\src\HarnessMcp.ControlPlane\bin\Release\net8.0\HarnessMcp.ControlPlane.exe",
        "$repoRoot\src\HarnessMcp.ControlPlane\bin\Release\net8.0\HarnessMcp.ControlPlane",
        "$repoRoot\src\HarnessMcp.ControlPlane\bin\Debug\net8.0\HarnessMcp.ControlPlane.exe",
        "$repoRoot\src\HarnessMcp.ControlPlane\bin\Debug\net8.0\HarnessMcp.ControlPlane"
    )
    foreach ($candidate in $searchPaths) {
        if (Test-Path $candidate) {
            $exePath = $candidate
            break
        }
    }
}

if (-not $exePath) {
    Write-Error @"
ControlPlane executable not found.

To build:
    dotnet build src\HarnessMcp.ControlPlane

Or set the HARNESS_EXE_PATH environment variable to the full path of the executable.
"@
    exit 1
}

# --- Validate required arguments per command ---

switch ($Command) {
    "start-session" {
        if (-not $RawTask) {
            Write-Error "start-session requires --raw-task <string>"
            exit 1
        }
    }
    "submit-step-result" {
        $missing = @()
        if (-not $SessionId)    { $missing += "--session-id" }
        if (-not $Action)       { $missing += "--action" }
        if (-not $ArtifactType) { $missing += "--artifact-type" }
        if (-not $ArtifactFile) { $missing += "--artifact-file" }
        if ($missing.Count -gt 0) {
            Write-Error "submit-step-result requires: $($missing -join ', ')"
            exit 1
        }
        if (!(Test-Path $ArtifactFile)) {
            Write-Error "Artifact file not found: $ArtifactFile"
            exit 1
        }
    }
    "get-next-step" {
        if (-not $SessionId) {
            Write-Error "get-next-step requires --session-id <string>"
            exit 1
        }
    }
    "get-session-status" {
        if (-not $SessionId) {
            Write-Error "get-session-status requires --session-id <string>"
            exit 1
        }
    }
    "cancel-session" {
        if (-not $SessionId) {
            Write-Error "cancel-session requires --session-id <string>"
            exit 1
        }
    }
}

# --- Build argument list ---

$cmdArgs = [System.Collections.Generic.List[string]]::new()
$cmdArgs.Add($Command)

if ($SessionId)    { $cmdArgs.Add("--session-id");    $cmdArgs.Add($SessionId) }
if ($RawTask)      { $cmdArgs.Add("--raw-task");      $cmdArgs.Add($RawTask) }
if ($Action)       { $cmdArgs.Add("--action");        $cmdArgs.Add($Action) }
if ($ArtifactType) { $cmdArgs.Add("--artifact-type"); $cmdArgs.Add($ArtifactType) }
if ($ArtifactFile) { $cmdArgs.Add("--artifact-file"); $cmdArgs.Add($ArtifactFile) }
if ($Project)      { $cmdArgs.Add("--project");       $cmdArgs.Add($Project) }

# --- Invoke ---

& $exePath @cmdArgs
exit $LASTEXITCODE
