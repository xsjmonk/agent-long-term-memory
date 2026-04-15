$ServerUrl = "http://127.0.0.1:5081/"
$Headers = @{"Accept" = "application/json, text/event-stream"}

function Invoke-McpRequest {
    param([string]$Method, [object]$Params = @{}, [int]$Id = 1)
    $body = @{jsonrpc='2.0'; id=$Id; method=$Method; params=$Params} | ConvertTo-Json -Depth 10
    $response = Invoke-WebRequest -Uri $ServerUrl -Method Post -ContentType "application/json" -Headers $Headers -Body $body -UseBasicParsing -TimeoutSec 10
    $raw = $response.Content
    $dataLine = ($raw -split "`n") | Where-Object { $_ -match "^data:" } | Select-Object -First 1
    $json = $dataLine -replace "^data:\s*", ""
    return $json | ConvertFrom-Json
}

$initParams = @{protocolVersion='2024-11-05'; capabilities=@{}; clientInfo=@{name='test-client'; version='1.0'}}
$initResult = Invoke-McpRequest -Method "initialize" -Params $initParams -Id 1
$toolsResult = Invoke-McpRequest -Method "tools/list" -Params @{} -Id 2
$toolsResult.result.tools | ForEach-Object {
    $t = $_
    Write-Host "Tool: $($t.name)"
    $schema = $t.inputSchema | ConvertTo-Json -Depth 10 -Compress
    Write-Host "  Schema: $schema"
    Write-Host ""
}