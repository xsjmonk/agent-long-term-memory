param(
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..") | Select-Object -ExpandProperty Path
$outputRoot = if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    Join-Path $repoRoot "Release"
} else {
    [System.IO.Path]::GetFullPath($OutputDir)
}

# Clean output to keep artifacts deterministic.
# Requirement: delete contents in the output folder before building (do not remove the folder itself).
if (-not (Test-Path $outputRoot)) {
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
} else {
    Get-ChildItem -Path $outputRoot -Force -ErrorAction SilentlyContinue | ForEach-Object {
        Remove-Item -Path $_.FullName -Recurse -Force
    }
}

$csprojPath = Join-Path $repoRoot "src\HarnessMcp.Host.Aot\HarnessMcp.Host.Aot.csproj"
$projectOut = Join-Path $outputRoot "HostDotnet"
New-Item -ItemType Directory -Path $projectOut -Force | Out-Null

Write-Output "Publishing (Normal .NET): HarnessMcp.Host.Aot -> $projectOut"
dotnet publish $csprojPath -c Release -r $RuntimeIdentifier --output $projectOut -p:PublishAot=false -p:PublishSingleFile=true

Write-Output "Done. Artifacts at: $projectOut"

