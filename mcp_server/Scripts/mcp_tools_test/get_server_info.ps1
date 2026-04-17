$ServerUrl = "http://127.0.0.1:5081/"
$Headers = @{ "Accept" = "application/json, text/event-stream" }

function Invoke-McpRequest {
    param ([string]$Method, [object]$Params = @{}, [int]$Id = 1)
    $body = @{ jsonrpc = "2.0"; id = $Id; method = $Method; params = $Params } | ConvertTo-Json -Depth 20
    $response = Invoke-WebRequest -Uri $ServerUrl -Method Post -ContentType "application/json" `
        -Headers $Headers -Body $body -UseBasicParsing -TimeoutSec 15
    $dataLine = ($response.Content -split "`n") | Where-Object { $_ -match "^data:" } | Select-Object -First 1
    return ($dataLine -replace "^data:\s*", "") | ConvertFrom-Json
}

$null = Invoke-McpRequest -Method "initialize" -Params @{
    protocolVersion = "2024-11-05"; capabilities = @{}
    clientInfo = @{ name = "test-client"; version = "1.0" }
} -Id 1
$null = Invoke-McpRequest -Method "initialized" -Params @{} -Id 0

$r = Invoke-McpRequest -Method "tools/call" -Params @{
    name = "get_server_info"
    arguments = @{}
} -Id 2

$result = @($r.result.content)[0].text | ConvertFrom-Json
Write-Host "Server: $($result.serverName) v$($result.serverVersion)"
Write-Host "Protocol: $($result.protocolMode)"
Write-Host "Features:"
$result.features.PSObject.Properties | ForEach-Object { Write-Host "  $($_.Name): $($_.Value)" }
