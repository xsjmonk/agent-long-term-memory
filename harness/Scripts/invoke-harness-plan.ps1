param(
    [Parameter(Mandatory=$true)] [string]$TaskText,
    [Parameter(Mandatory=$true)] [string]$OutputDir,
    [Parameter(Mandatory=$true)] [string]$McpBaseUrl,
    [Parameter(Mandatory=$true)] [string]$ModelBaseUrl,
    [Parameter(Mandatory=$true)] [string]$ModelName
)
$ErrorActionPreference = "Stop"
$harnessRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$exePath = Join-Path $harnessRoot "Release\HarnessMcp.AgentClient.exe"
$manifestJson = & $exePath plan-task --task-text $TaskText --output-dir $OutputDir --mcp-base-url $McpBaseUrl --model-base-url $ModelBaseUrl --model-name $ModelName --stdout-json true
Write-Output $manifestJson
$manifest = $manifestJson | ConvertFrom-Json
if (-not $manifest.Success) { exit 1 }
exit 0

