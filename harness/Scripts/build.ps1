param(
    [string]$RuntimeIdentifier = "win-x64"
)

$ErrorActionPreference = "Stop"

$scriptDir = $PSScriptRoot
$harnessRoot = Resolve-Path (Join-Path $scriptDir "..")
$outputRoot = Join-Path $harnessRoot "Release"

# Deterministic builds: delete contents of output directory, keep the folder.
if (-not (Test-Path $outputRoot)) {
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
} else {
    Get-ChildItem -Path $outputRoot -Force -ErrorAction SilentlyContinue | ForEach-Object {
        Remove-Item -Path $_.FullName -Recurse -Force
    }
}

$csprojPath = Join-Path $harnessRoot "src\HarnessMcp.AgentClient\HarnessMcp.AgentClient.csproj"
if (-not (Test-Path $csprojPath)) {
    throw "Cannot find csproj: $csprojPath"
}

Write-Output "Publishing NativeAOT: $csprojPath"
dotnet publish $csprojPath -c Release -r $RuntimeIdentifier --self-contained true -o $outputRoot /p:PublishAot=true

Write-Output "Done. AOT artifacts at: $outputRoot"

