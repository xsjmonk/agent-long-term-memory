# test_mcp_tools.ps1
# Tests the local MCP server at http://127.0.0.1:5081/ and lists available tools.

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

    # Response is SSE: "event: message\ndata: {...}"
    $raw = $response.Content
    $dataLine = ($raw -split "`n") | Where-Object { $_ -match "^data:" } | Select-Object -First 1
    $json = $dataLine -replace "^data:\s*", ""
    return $json | ConvertFrom-Json
}

# --- Step 1: Initialize ---
Write-Host "`n=== MCP Server Test ===" -ForegroundColor Cyan
Write-Host "Server : $ServerUrl"
Write-Host "Date   : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"

Write-Host "`n[1] Sending initialize..." -ForegroundColor Yellow
$initParams = @{
    protocolVersion = "2024-11-05"
    capabilities    = @{}
    clientInfo      = @{ name = "test-client"; version = "1.0" }
}

$initResult = Invoke-McpRequest -Method "initialize" -Params $initParams -Id 1

Write-Host "    Protocol : $($initResult.result.protocolVersion)"
Write-Host "    Server   : $($initResult.result.serverInfo.name) v$($initResult.result.serverInfo.version)"
Write-Host "    Caps     : $($initResult.result.capabilities | ConvertTo-Json -Compress)"

# --- Step 2: List Tools ---
Write-Host "`n[2] Fetching tools/list..." -ForegroundColor Yellow
$toolsResult = Invoke-McpRequest -Method "tools/list" -Params @{} -Id 2

$tools = $toolsResult.result.tools
Write-Host "    Found $($tools.Count) tool(s):`n"

$index = 1
foreach ($tool in $tools) {
    Write-Host "  [$index] $($tool.name)" -ForegroundColor Green
    if ($tool.description) {
        Write-Host "       $($tool.description)"
    }

    # Show top-level input properties
    $props = $tool.inputSchema.properties
    if ($props) {
        $propNames = ($props | Get-Member -MemberType NoteProperty).Name
        Write-Host "       Input params: $($propNames -join ', ')" -ForegroundColor DarkGray
    } else {
        Write-Host "       Input params: (none)" -ForegroundColor DarkGray
    }
    $index++
}

# --- Step 3: Call get_server_info ---
Write-Host "`n[3] Calling get_server_info..." -ForegroundColor Yellow
$infoResult = Invoke-McpRequest -Method "tools/call" -Params @{
    name      = "get_server_info"
    arguments = @{}
} -Id 3

$infoContent = $infoResult.result.content
if ($infoContent) {
    Write-Host "    Response: $($infoContent | ConvertTo-Json -Compress -Depth 5)"
} else {
    Write-Host "    Response: $($infoResult | ConvertTo-Json -Compress -Depth 5)"
}

Write-Host "`n=== Test Complete ===" -ForegroundColor Cyan
