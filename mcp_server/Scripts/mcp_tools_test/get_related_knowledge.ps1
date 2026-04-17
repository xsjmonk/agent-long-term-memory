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

# Retrieve knowledge items related to a given knowledge item UUID.
# relationTypes: integer array (relation type filter)
# Returns empty items array if the knowledgeItemId does not exist.
$r = Invoke-McpRequest -Method "tools/call" -Params @{
    name = "get_related_knowledge"
    arguments = @{
        request = @{
            schemaVersion   = "1.0"
            requestId       = [guid]::NewGuid().ToString()
            knowledgeItemId = "00000000-0000-0000-0000-000000000001"
            relationTypes   = @(1)
            topK            = 5
        }
    }
} -Id 2

if ($r.result.isError) {
    Write-Host "FAIL: $(@($r.result.content)[0].text)"
} else {
    $result = @($r.result.content)[0].text | ConvertFrom-Json
    Write-Host "SUCCESS — kind: $($result.kind)"
    Write-Host "Related items: $($result.items.Count)"
    $result.items | ForEach-Object { Write-Host "  - $($_.knowledgeItemId): $($_.title)" }
}
