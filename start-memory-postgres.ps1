& {
$ErrorActionPreference = 'Stop'

$scriptPath = $PSCommandPath
if ([string]::IsNullOrWhiteSpace($scriptPath)) { $scriptPath = $MyInvocation.MyCommand.Path }
if ([string]::IsNullOrWhiteSpace($scriptPath)) { $scriptPath = (Get-Location).Path }

if (Test-Path -LiteralPath $scriptPath -PathType Leaf) {
	$scriptDir = Split-Path -Parent $scriptPath
} else {
	$scriptDir = $scriptPath
}

$dataDir = Join-Path $scriptDir 'pqdata'
$containerName = 'agent-memory'
$image = 'pgvector/pgvector:pg17'
$hostPort = 54329
$dbName = 'memorydb'
$userName = 'user'
$password = 'password'

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Docker is not installed or not in PATH.' }
if (-not (Test-Path -LiteralPath $dataDir)) { New-Item -ItemType Directory -Path $dataDir | Out-Null }

$resolvedDataDir = (Resolve-Path -LiteralPath $dataDir).Path

$existing = docker ps -a --filter "name=^/${containerName}$" --format '{{.Names}}'
if (-not [string]::IsNullOrWhiteSpace($existing)) {
	$isRunning = docker ps --filter "name=^/${containerName}$" --format '{{.Names}}'
	if (-not [string]::IsNullOrWhiteSpace($isRunning)) {
		Write-Host "Container '$containerName' is already running."
		Write-Host "PostgreSQL connection: Host=localhost;Port=$hostPort;Database=$dbName;Username=$userName;Password=$password"
		return
	}

	Write-Host "Starting existing container '$containerName'..."
	docker start $containerName | Out-Null
	Write-Host "Container '$containerName' started."
	Write-Host "PostgreSQL connection: Host=localhost;Port=$hostPort;Database=$dbName;Username=$userName;Password=$password"
	return
}

Write-Host "Creating container '$containerName'..."
docker run -d `
	--name $containerName `
	-e POSTGRES_DB="$dbName" `
	-e POSTGRES_USER="$userName" `
	-e POSTGRES_PASSWORD="$password" `
	-p "${hostPort}:5432" `
	-v "${resolvedDataDir}:/var/lib/postgresql/data" `
	$image | Out-Null

Write-Host "Container '$containerName' created and started."
Write-Host "Data folder: $resolvedDataDir"
Write-Host "PostgreSQL connection: Host=localhost;Port=$hostPort;Database=$dbName;Username=$userName;Password=$password"
}