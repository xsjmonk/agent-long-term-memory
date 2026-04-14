param(
    [Parameter(Mandatory=$true)] [string]$TaskText,
    [Parameter(Mandatory=$true)] [string]$OutputDir,
    [Parameter(Mandatory=$true)] [string]$McpBaseUrl,
    [Parameter(Mandatory=$true)] [string]$ModelBaseUrl,
    [Parameter(Mandatory=$true)] [string]$ModelName,
    [Parameter(Mandatory=$false)] [string]$ApiKeyEnv = $null,
    [Parameter(Mandatory=$false)] [string]$SessionId = $null
)

$ErrorActionPreference = "Stop"

$harnessRoot = Split-Path $PSScriptRoot -Parent
$exePath = Join-Path $harnessRoot "Release\HarnessMcp.AgentClient.exe"

if (-not (Test-Path $exePath)) {
    Write-Error "Harness executable not found at: $exePath"
    exit 1
}

$args = @(
    "plan-task",
    "--task-text", $TaskText,
    "--output-dir", $OutputDir,
    "--mcp-base-url", $McpBaseUrl,
    "--model-base-url", $ModelBaseUrl,
    "--model-name", $ModelName,
    "--stdout-json", "true"
)

if ($ApiKeyEnv) {
    $args += "--api-key-env"
    $args += $ApiKeyEnv
}

if ($SessionId) {
    $args += "--session-id"
    $args += $SessionId
}

try {
    $manifestJson = & $exePath $args 2>&1
    $exitCode = $LASTEXITCODE
} catch {
    Write-Error "Failed to invoke harness: $_"
    exit 1
}

if ($exitCode -ne 0) {
    exit $exitCode
}

$manifestJson | Write-Output
exit 0
