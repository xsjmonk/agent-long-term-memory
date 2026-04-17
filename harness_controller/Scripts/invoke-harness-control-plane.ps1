<#
.SYNOPSIS
Harness Control-Plane Planning Orchestrator

.DESCRIPTION
The ONLY supported entrypoint for the Harness Control-Plane protocol. Manages structured AI-driven planning sessions with MCP integration.

.PARAMETER Command
Required command: start-session, submit-step-result, get-next-step, get-session-status, or cancel-session

.PARAMETER RawTask
Raw task description (required for start-session)

.PARAMETER SessionId
Session identifier returned by start-session (required for most commands)

.PARAMETER Action
Completed action name (required for submit-step-result), e.g. agent_generate_requirement_intent

.PARAMETER ArtifactType
Artifact type (required for submit-step-result), e.g. RequirementIntent

.PARAMETER ArtifactFile
Path to JSON file containing artifact (required for submit-step-result)

.PARAMETER Project
Project name (optional for start-session)

.EXAMPLE
# Start a planning session
.\harness\invoke-harness-control-plane.ps1 start-session -RawTask "Fix fluid name sync in ReservoirMapping"

.EXAMPLE
# Submit a completed artifact
.\harness\invoke-harness-control-plane.ps1 submit-step-result `
    -SessionId "sess-xyz" `
    -Action "agent_generate_requirement_intent" `
    -ArtifactType "RequirementIntent" `
    -ArtifactFile "requirement_intent.json"

.IMPORTANT
When harness returns nextAction: agent_call_mcp_*, you MUST initialize the MCP protocol before calling tools.
See: harness/agent-rules/03-harness-mcp-tool-calling.mdc (Critical initialization code at top)

.EXIT_CODES
0 = success
1 = error (see stderr and/or JSON response for details)

.ENVIRONMENT
HARNESS_EXE_PATH - Override the control-plane executable path
#>

# invoke-harness-control-plane.ps1
# The ONLY supported entrypoint for the Harness Control-Plane protocol.
#
# COMMANDS
#   start-session
#       -RawTask <string>        Raw task description (required)
#       -Project <string>        Project name (optional)
#
#   submit-step-result
#       -SessionId    <string>   Session identifier returned by start-session (required)
#       -Action       <string>   Completed action name, e.g. agent_generate_requirement_intent (required)
#       -ArtifactType <string>   Artifact type, e.g. RequirementIntent (required)
#       -ArtifactFile <string>   Path to JSON file containing the artifact (required)
#
#   get-next-step
#       -SessionId    <string>   Session identifier (required)
#
#   get-session-status
#       -SessionId    <string>   Session identifier (required)
#
#   cancel-session
#       -SessionId    <string>   Session identifier (required)
#
# ⚠️  MCP INITIALIZATION REQUIRED
# When harness returns nextAction: agent_call_mcp_*, you MUST:
# 1. Initialize MCP protocol (see harness/agent-rules/03-harness-mcp-tool-calling.mdc)
# 2. Send initialize request
# 3. Send initialized notification
# 4. Only THEN call tools/call
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
    # Search paths: prioritize same directory (deployment), then source build paths
    $searchPaths = @(
        # Same directory as this script (typical deployment structure)
        "$PSScriptRoot\HarnessMcp.ControlPlane.exe",
        "$PSScriptRoot\HarnessMcp.ControlPlane",
        # Source build paths (development structure)
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
