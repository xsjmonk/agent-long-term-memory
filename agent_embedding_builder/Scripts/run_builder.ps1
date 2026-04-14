param(
    [string]$ConfigPath = "",
    [switch]$SkipDatabase,
    [switch]$SkipDependencySync
)

$ErrorActionPreference = "Stop"

# NOTE: Validation-only runner.
# It must not install/repair dependencies and must not use any external sync tool.

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$environmentFile = Join-Path $projectRoot "environment.yml"
$schemaFile = Join-Path $projectRoot "sql\schema.sql"
$indexesFile = Join-Path $projectRoot "sql\indexes.sql"
$entrypointFile = Join-Path $projectRoot "agent_embedding_builder.py"
$launchDirectory = (Get-Location).Path

function Resolve-OptionalPath {
    param([string]$PathValue)

    if (-not $PathValue) { return "" }
    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }
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

    $pythonExecutable = if ($IsWindows) {
        Join-Path $env:CONDA_PREFIX "python.exe"
    }
    else {
        Join-Path $env:CONDA_PREFIX "bin/python"
    }

    if (-not (Test-Path -LiteralPath $pythonExecutable)) {
        throw "The Conda environment is active but Python executable was not found under CONDA_PREFIX: $pythonExecutable"
    }

    return $pythonExecutable
}

function Convert-TextToJsonObject {
    param(
        [string]$Text,
        [string]$Context = "JSON payload"
    )

    if (-not $Text -or -not $Text.Trim()) {
        throw "No JSON content was provided for $Context."
    }

    $startIndex = $Text.IndexOf("{")
    $endIndex = $Text.LastIndexOf("}")
    if ($startIndex -lt 0 -or $endIndex -lt $startIndex) {
        throw "Unable to locate a JSON object in $Context. Raw output:`n$Text"
    }

    $jsonText = $Text.Substring($startIndex, $endIndex - $startIndex + 1)
    return $jsonText | ConvertFrom-Json
}

function Assert-CondaPythonBinding {
    param([string]$PythonExecutable, [string]$SelectedPythonExecutable)

    $checkScript = New-TemporaryFile
    $checkScriptContent = @'
import json
import os
import sys
from pathlib import Path

conda_prefix = Path(os.environ["CONDA_PREFIX"]).resolve()
sys_prefix = Path(sys.prefix).resolve()
sys_executable = Path(sys.executable).resolve()
selected_python = Path(os.environ["AGENT_EMBEDDING_SELECTED_PYTHON"]).resolve()

payload = {
  "sys_prefix": str(sys_prefix),
  "sys_executable": str(sys_executable),
  "selected_python": str(selected_python),
  "prefix_matches": str(sys_prefix).lower().startswith(str(conda_prefix).lower()),
  "executable_matches": sys_executable == selected_python
}
print(json.dumps(payload))
'@

    Set-Content -LiteralPath $checkScript -Value $checkScriptContent -Encoding UTF8

    $env:PYTHONUTF8 = "1"
    $env:PYTHONIOENCODING = "utf-8"
    $env:AGENT_EMBEDDING_SELECTED_PYTHON = $SelectedPythonExecutable

    $raw = & $PythonExecutable $checkScript 2>&1 | Out-String
    Remove-Item -LiteralPath $checkScript -Force -ErrorAction SilentlyContinue

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to verify that the active interpreter is bound to the Conda environment."
    }

    $payload = Convert-TextToJsonObject -Text $raw -Context "Conda Python binding probe"
    if (-not $payload.prefix_matches -or -not $payload.executable_matches) {
        throw "Refusing to proceed because the selected interpreter is not the active Conda interpreter. sys.executable=$($payload.sys_executable) sys.prefix=$($payload.sys_prefix) expected=$($payload.selected_python)"
    }
}

function Validate-RequiredRuntimePackages {
    param([string]$PythonExecutable)

    $probeScript = New-TemporaryFile
    $probeContent = @'
import json

result = {
  "pydantic": {"installed": False, "error": None},
  "jsonschema": {"installed": False, "error": None},
  "psycopg": {"installed": False, "error": None},
  "fastapi": {"installed": False, "error": None},
  "uvicorn": {"installed": False, "error": None},
  "torch": {"installed": False, "cuda_available": False, "error": None},
  "llama_cpp": {"installed": False, "gpu_offload_supported": False, "error": None},
}

try:
  import pydantic  # noqa: F401
  result["pydantic"]["installed"] = True
except Exception as exc:
  result["pydantic"]["error"] = str(exc)

try:
  from jsonschema import Draft202012Validator  # noqa: F401
  result["jsonschema"]["installed"] = True
except Exception as exc:
  result["jsonschema"]["error"] = str(exc)

try:
  import psycopg  # noqa: F401
  result["psycopg"]["installed"] = True
except Exception as exc:
  result["psycopg"]["error"] = str(exc)

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

try:
  import torch
  result["torch"]["installed"] = True
  result["torch"]["cuda_available"] = bool(torch.cuda.is_available())
except Exception as exc:
  result["torch"]["error"] = str(exc)

try:
  import llama_cpp
  result["llama_cpp"]["installed"] = True
  # Best-effort: different builds expose different helpers.
  fn = getattr(llama_cpp, "llama_supports_gpu_offload", None)
  if fn is None:
    fn = getattr(getattr(llama_cpp, "llama_cpp", None), "llama_supports_gpu_offload", None)
  result["llama_cpp"]["gpu_offload_supported"] = bool(fn()) if fn else False
except Exception as exc:
  result["llama_cpp"]["error"] = str(exc)

print(json.dumps(result))
'@
    Set-Content -LiteralPath $probeScript -Value $probeContent -Encoding UTF8

    $env:PYTHONUTF8 = "1"
    $env:PYTHONIOENCODING = "utf-8"
    $env:PYTHONPATH = $projectRoot

    $raw = & $PythonExecutable $probeScript 2>&1 | Out-String
    Remove-Item -LiteralPath $probeScript -Force -ErrorAction SilentlyContinue

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to validate required runtime packages in the active Conda environment."
    }

    $payload = Convert-TextToJsonObject -Text $raw -Context "GPU runtime probe"

    if (-not [bool]$payload.pydantic.installed) {
        throw "Active Conda environment is missing required package `pydantic`. `run_builder.ps1` does not install dependencies. Recreate/update the Conda environment from `environment.yml`."
    }
    if (-not [bool]$payload.jsonschema.installed) {
        throw "Active Conda environment is missing required package `jsonschema`. `run_builder.ps1` does not install dependencies. Recreate/update the Conda environment from `environment.yml`."
    }
    if (-not [bool]$payload.psycopg.installed) {
        throw "Active Conda environment is missing required package `psycopg`. `run_builder.ps1` does not install dependencies. Recreate/update the Conda environment from `environment.yml`."
    }
    if (-not [bool]$payload.fastapi.installed) {
        throw "Active Conda environment is missing required package `fastapi`. `run_builder.ps1` does not install dependencies. Recreate/update the Conda environment from `environment.yml`."
    }
    if (-not [bool]$payload.uvicorn.installed) {
        throw "Active Conda environment is missing required package `uvicorn`. `run_builder.ps1` does not install dependencies. Recreate/update the Conda environment from `environment.yml`."
    }

    $torchInstalled = [bool]$payload.torch.installed
    $torchCuda = [bool]$payload.torch.cuda_available
    $llamaInstalled = [bool]$payload.llama_cpp.installed
    $llamaGpuOffload = [bool]$payload.llama_cpp.gpu_offload_supported

    Write-Host ("Runtime validation: torch_installed={0} cuda_available={1} llama_cpp_installed={2} llama_gpu_offload_supported={3}" -f $torchInstalled, $torchCuda, $llamaInstalled, $llamaGpuOffload)

    if (-not $torchInstalled) {
        throw "Active Conda environment is missing required package `torch`. `run_builder.ps1` does not install dependencies. Recreate/update the Conda environment from `environment.yml`."
    }
    if (-not $llamaInstalled) {
        throw "Active Conda environment is missing required package `llama-cpp-python`. `run_builder.ps1` does not install dependencies. Recreate/update the Conda environment from `environment.yml`."
    }

    if (-not $torchCuda) {
        Write-Warning "CUDA is unavailable in the current Conda environment. Embeddings may run on CPU."
    }

    # Distinct capabilities: torch CUDA availability does not guarantee llama-cpp-python GPU offload.
    if ($torchCuda -and -not $llamaGpuOffload) {
        Write-Warning @"
PyTorch CUDA is available in the current Conda environment (torch.cuda.is_available()=true), but llama-cpp-python GPU offload is NOT available.
This is allowed because the application supports CPU fallback for local llama runtime creation.
`run_builder.ps1` does not install, repair, or modify packages.
If you need llama GPU offload, recreate/update the Conda environment from `environment.yml`.
"@
    }

    if (-not $llamaGpuOffload) {
        Write-Warning "llama-cpp-python GPU offload is not available in the current Conda environment. Local LLM inference may run on CPU."
    }
}

function Initialize-DatabaseSchemaIfRequested {
    param([bool]$ShouldInit, [string]$PythonExecutable, [string]$ResolvedConfigPath, [string]$SchemaSqlPath, [string]$IndexesSqlPath)

    if (-not $ShouldInit) { return }

    $dbInitScript = New-TemporaryFile
    $dbInitContent = @'
import json
import os
from pathlib import Path

import psycopg

from app.config_loader import load_config

project_root = Path(os.environ["AGENT_EMBEDDING_PROJECT_ROOT"])
override = os.environ.get("AGENT_EMBEDDING_CONFIG_OVERRIDE") or None
cfg = load_config(project_root, override=override)

db = cfg.database
# DatabaseConfig uses `db_schema` internally (alias: "schema").
schema_name = getattr(db, "db_schema", None) or "public"

schema_sql = Path(os.environ["AGENT_SCHEMA_SQL_PATH"]).read_text(encoding="utf-8")
indexes_sql = Path(os.environ["AGENT_INDEXES_SQL_PATH"]).read_text(encoding="utf-8")

conn = psycopg.connect(
    host=db.host,
    port=int(db.port),
    user=db.username,
    password=db.password,
    dbname=db.database,
)

with conn:
    with conn.cursor() as cur:
        cur.execute(schema_sql)
        cur.execute(indexes_sql)

print(json.dumps({"ok": True, "schema": schema_name, "db_host": db.host, "db_name": db.database}))
'@
    Set-Content -LiteralPath $dbInitScript -Value $dbInitContent -Encoding UTF8

    $env:PYTHONUTF8 = "1"
    $env:PYTHONIOENCODING = "utf-8"
    $env:PYTHONPATH = $projectRoot
    $env:AGENT_EMBEDDING_PROJECT_ROOT = $projectRoot
    $env:AGENT_EMBEDDING_CONFIG_OVERRIDE = $ResolvedConfigPath
    $env:AGENT_SCHEMA_SQL_PATH = $SchemaSqlPath
    $env:AGENT_INDEXES_SQL_PATH = $IndexesSqlPath

    $raw = & $PythonExecutable $dbInitScript 2>&1 | Out-String
    Remove-Item -LiteralPath $dbInitScript -Force -ErrorAction SilentlyContinue

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to initialize database schema via psycopg.`n$raw"
    }
}

function Invoke-Builder {
    param(
        [string]$PythonExecutable,
        [string]$ResolvedConfigPath,
        [string]$EntryPoint,
        [string]$ConfigArg
    )

    $env:PYTHONUTF8 = "1"
    $env:PYTHONIOENCODING = "utf-8"
    $env:PYTHONPATH = $projectRoot

    Write-Host "Running builder..."
    if ($ConfigArg) {
        & $PythonExecutable $EntryPoint --config $ConfigArg
    }
    else {
        & $PythonExecutable $EntryPoint
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Builder execution failed."
    }
}

$resolvedConfigPath = Resolve-OptionalPath -PathValue $ConfigPath
$condaEnvironmentName = Get-CondaEnvironmentName -EnvironmentYamlPath $environmentFile

try {
    Initialize-CondaSession
    Ensure-CondaEnvironment -EnvironmentName $condaEnvironmentName -EnvironmentYamlPath $environmentFile

    $pythonExecutable = Get-PreferredPythonExecutable
    Assert-CondaPythonBinding -PythonExecutable $pythonExecutable -SelectedPythonExecutable $pythonExecutable

    Validate-RequiredRuntimePackages -PythonExecutable $pythonExecutable

    Initialize-DatabaseSchemaIfRequested -ShouldInit (-not $SkipDatabase) -PythonExecutable $pythonExecutable -ResolvedConfigPath $resolvedConfigPath -SchemaSqlPath $schemaFile -IndexesSqlPath $indexesFile

    Invoke-Builder -PythonExecutable $pythonExecutable -ResolvedConfigPath $resolvedConfigPath -EntryPoint $entrypointFile -ConfigArg $resolvedConfigPath
}
finally {
    # no-op
}

