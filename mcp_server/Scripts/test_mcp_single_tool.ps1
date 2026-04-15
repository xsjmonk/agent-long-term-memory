# test_mcp_single_tool.ps1
# Tests a single tool from the local MCP server at http://127.0.0.1:5081/

$ServerUrl = "http://127.0.0.1:5081/"
$Headers = @{
    "Accept" = "application/json, text/event-stream"
}

function Invoke-McpRequest {
    param (
        [string]$Method,
        [object]$Params = @{},
        [int]$Id = 1
    )

    $body = @{
        jsonrpc = "2.0"
        id      = $Id
        method  = $Method
        params  = $Params
    } | ConvertTo-Json -Depth 10

    $response = Invoke-WebRequest `
        -Uri $ServerUrl `
        -Method Post `
        -ContentType "application/json" `
        -Headers $Headers `
        -Body $body `
        -UseBasicParsing `
        -TimeoutSec 10

    $raw = $response.Content
    $dataLine = ($raw -split "`n") | Where-Object { $_ -match "^data:" } | Select-Object -First 1
    $json = $dataLine -replace "^data:\s*", ""
    return $json | ConvertFrom-Json
}

# --- Initialize ---
Write-Host "`n=== MCP Single Tool Test ===" -ForegroundColor Cyan
Write-Host "Server : $ServerUrl"

Write-Host "`n[1] Sending initialize..." -ForegroundColor Yellow
$initParams = @{
    protocolVersion = "2024-11-05"
    capabilities    = @{}
    clientInfo      = @{ name = "test-client"; version = "1.0" }
}
$initResult = Invoke-McpRequest -Method "initialize" -Params $initParams -Id 1
Write-Host "    Protocol : $($initResult.result.protocolVersion)"
Write-Host "    Server   : $($initResult.result.serverInfo.name) v$($initResult.result.serverInfo.version)"

# Send initialized notification (required by MCP protocol)
$null = Invoke-McpRequest -Method "initialized" -Params @{} -Id 0

# --- Call search_knowledge tool ---
Write-Host "`n[2] Calling search_knowledge tool..." -ForegroundColor Yellow

$toolParams = @{
    name      = "search_knowledge"
    arguments = @{
        request = @{
            schemaVersion     = "1.0"
            requestId         = [guid]::NewGuid().ToString()
            queryText         = "test query"
            queryKind         = 1
            scopes            = @{
                domains    = @("domain1")
                modules    = @("module1")
                features   = @("feature1")
                layers     = @("layer1")
                concerns   = @("concern1")
                repos      = @("repo1")
                services   = @("service1")
                symbols    = @()
            }
            retrievalClasses   = @(1, 2, 3)
            minimumAuthority  = 1
            status             = 1
            topK               = 3
            includeEvidence    = $false
            includeRawDetails  = $false
        }
    }
}

$toolResult = Invoke-McpRequest -Method "tools/call" -Params $toolParams -Id 2

Write-Host "`n[3] Tool Response:" -ForegroundColor Yellow
$isError = $toolResult.result.isError
if ($isError) {
    Write-Host "    [Error response - tool was invoked but backend returned error]" -ForegroundColor DarkYellow
} else {
    Write-Host "    [Success]" -ForegroundColor Green
}
$toolResult | ConvertTo-Json -Depth 10 | Write-Host

Write-Host "`n=== Test Complete ===" -ForegroundColor Cyan