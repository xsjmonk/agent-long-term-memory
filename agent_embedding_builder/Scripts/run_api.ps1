param(
    [string]$ConfigPath = "",
    [Alias("Host")]
    [string]$ApiHost = "",
    [int]$Port = 0
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$environmentFile = Join-Path $projectRoot "environment.yml"

function Resolve-OptionalPath {
    param([string]$PathValue)

    if (-not $PathValue) { return "" }
    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }
    $launchDirectory = (Get-Location).Path
    return [System.IO.Path]::GetFullPath((Join-Path $launchDirectory $PathValue))
}

function Initialize-CondaSession {
    $condaCommand = Get-Command conda -ErrorAction SilentlyContinue
    if (-not $condaCommand) {
        throw "Conda is not available on PATH."
    }

    $hookScript = & conda shell.powershell hook
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to initialize Conda shell integration."
    }

    Invoke-Expression ($hookScript -join [Environment]::NewLine)
}

function Get-CondaEnvironmentName {
    param([string]$EnvironmentYamlPath)

    $yaml = Get-Content -LiteralPath $EnvironmentYamlPath -Raw -Encoding UTF8
    $match = [regex]::Match($yaml, '(?m)^\s*name\s*:\s*(?<name>[^\r\n#]+?)\s*$')
    if (-not $match.Success) {
        throw "Unable to determine Conda environment name from $EnvironmentYamlPath"
    }
    return $match.Groups["name"].Value.Trim()
}

function Test-CondaEnvExists {
    param([string]$EnvironmentName)
    $out = & conda env list 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $out) { return $false }
    return ($out | Select-String -Pattern ("^" + [regex]::Escape($EnvironmentName) + "\s")) -ne $null
}

function Ensure-CondaEnvironment {
    param([string]$EnvironmentName, [string]$EnvironmentYamlPath)

    if (-not (Test-CondaEnvExists -EnvironmentName $EnvironmentName)) {
        Write-Host "Creating Conda environment: $EnvironmentName"
        & conda env create -f $EnvironmentYamlPath
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to create Conda environment: $EnvironmentName"
        }
    }
    else {
        Write-Host "Conda environment already exists: $EnvironmentName"
    }

    if ($env:CONDA_DEFAULT_ENV -ne $EnvironmentName) {
        Write-Host "Activating Conda environment: $EnvironmentName"
        conda activate $EnvironmentName
    }
    else {
        Write-Host "Conda environment already active: $EnvironmentName"
    }
}

function Get-PreferredPythonExecutable {
    if (-not $env:CONDA_PREFIX) {
        throw "CONDA_PREFIX is not set. This launcher requires an active Conda environment."
    }

    if ($IsWindows) {
        $pythonExecutable = Join-Path $env:CONDA_PREFIX "python.exe"
    }
    else {
        $pythonExecutable = Join-Path $env:CONDA_PREFIX "bin/python"
    }

    if (-not (Test-Path -LiteralPath $pythonExecutable)) {
        throw "The Conda environment is active but Python executable was not found under CONDA_PREFIX: $pythonExecutable"
    }

    return $pythonExecutable
}

function Validate-RequiredApiRuntimePackages {
    param([string]$PythonExecutable)

    $probeScript = New-TemporaryFile
    $probeContent = @'
import json

result = {
  "fastapi": {"installed": False, "error": None},
  "uvicorn": {"installed": False, "error": None},
}

try:
  import fastapi  # noqa: F401
  result["fastapi"]["installed"] = True
except Exception as exc:
  result["fastapi"]["error"] = str(exc)

try:
  import uvicorn  # noqa: F401
  result["uvicorn"]["installed"] = True
except Exception as exc:
  result["uvicorn"]["error"] = str(exc)

print(json.dumps(result))
'@

    Set-Content -LiteralPath $probeScript -Value $probeContent -Encoding UTF8
    $env:PYTHONUTF8 = "1"
    $env:PYTHONIOENCODING = "utf-8"
    $env:PYTHONPATH = $projectRoot

    $raw = & $PythonExecutable $probeScript 2>&1 | Out-String
    Remove-Item -LiteralPath $probeScript -Force -ErrorAction SilentlyContinue

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to validate required API runtime packages in the active Conda environment."
    }

    # Parse JSON output robustly: allow additional log lines around it.
    $startIndex = $raw.IndexOf("{")
    $endIndex = $raw.LastIndexOf("}")
    if ($startIndex -lt 0 -or $endIndex -lt $startIndex) {
        throw "Unable to parse JSON probe output. Raw:`n$raw"
    }

    $jsonText = $raw.Substring($startIndex, $endIndex - $startIndex + 1)
    $payload = $jsonText | ConvertFrom-Json

    if (-not [bool]$payload.fastapi.installed) {
        throw "Active Conda environment is missing required package fastapi. Recreate/update the Conda environment from environment.yml."
    }
    if (-not [bool]$payload.uvicorn.installed) {
        throw "Active Conda environment is missing required package uvicorn. Recreate/update the Conda environment from environment.yml."
    }
}

$resolvedConfigPath = Resolve-OptionalPath -PathValue $ConfigPath
if (-not $resolvedConfigPath) {
    $resolvedConfigPath = Join-Path $projectRoot "config\builder_config.jsonc"
}

$condaEnvironmentName = Get-CondaEnvironmentName -EnvironmentYamlPath $environmentFile

try {
    Initialize-CondaSession
    Ensure-CondaEnvironment -EnvironmentName $condaEnvironmentName -EnvironmentYamlPath $environmentFile

    $pythonExecutable = Get-PreferredPythonExecutable
    Validate-RequiredApiRuntimePackages -PythonExecutable $pythonExecutable

    # Force the config loader to use the resolved config file for this API runner.
    $env:EMBEDDING_BUILDER_CONFIG = $resolvedConfigPath

    # Read bind host/port from config (jsonc is handled in Python).
    $repoRootPy = $projectRoot.Replace("\", "/")
    $configProbe = @"
from pathlib import Path
from app.config_loader import load_config
import json
cfg = load_config(Path(r"$repoRootPy"))
print(json.dumps({"host": cfg.api.host, "port": cfg.api.port}))
"@

    $env:PYTHONPATH = $projectRoot
    $raw = & $pythonExecutable -c $configProbe 2>&1 | Out-String
    $startIndex = $raw.IndexOf("{")
    $endIndex = $raw.LastIndexOf("}")
    if ($startIndex -lt 0 -or $endIndex -lt $startIndex) {
        throw "Unable to parse api.host/api.port from config. Raw:`n$raw"
    }
    $jsonText = $raw.Substring($startIndex, $endIndex - $startIndex + 1)
    $payload = $jsonText | ConvertFrom-Json

    if (-not $ApiHost) {
        $ApiHost = $payload.host
    }

    $ApiPort = $Port
    if ($ApiPort -eq 0) {
        $ApiPort = [int]$payload.port
    }

    $env:PYTHONUTF8 = "1"
    $env:PYTHONIOENCODING = "utf-8"
    $env:PYTHONPATH = $projectRoot

    Write-Host ("Starting query API: host={0} port={1}" -f $ApiHost, $ApiPort)
    & $pythonExecutable -m uvicorn app.query_api:app --host $ApiHost --port $ApiPort
}
finally {
    # no-op
}

