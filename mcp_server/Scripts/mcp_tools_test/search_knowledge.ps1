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

# queryKind: 1 = semantic search
# retrievalClasses: integers (1=decisions, 2=bestPractices, 3=antipatterns, ...)
# minimumAuthority: integer (1 = lowest, returns broadest results)
# status: integer (0 = any)
$r = Invoke-McpRequest -Method "tools/call" -Params @{
    name = "search_knowledge"
    arguments = @{
        request = @{
            schemaVersion    = "1.0"
            requestId        = [guid]::NewGuid().ToString()
            queryText        = "ViewModel property synchronization WPF"
            queryKind        = 1
            scopes           = @{
                domains  = @(); modules  = @(); features = @()
                layers   = @(); concerns = @(); repos    = @()
                services = @(); symbols  = @()
            }
            retrievalClasses = @(1, 2, 3)
            minimumAuthority = 1
            status           = 0
            topK             = 5
            includeEvidence  = $false
            includeRawDetails = $false
        }
    }
} -Id 2

if ($r.result.isError) {
    Write-Host "FAIL: $(@($r.result.content)[0].text)"
} else {
    $result = @($r.result.content)[0].text | ConvertFrom-Json
    Write-Host "SUCCESS — candidates: $($result.candidates.Count)"
    $result.candidates | ForEach-Object { Write-Host "  - $($_.title)" }
    Write-Host "Diagnostics: $($result.diagnostics | ConvertTo-Json -Compress)"
}
