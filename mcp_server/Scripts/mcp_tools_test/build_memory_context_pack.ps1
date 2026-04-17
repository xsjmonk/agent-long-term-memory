$ServerUrl = "http://127.0.0.1:5081/"
$Headers = @{ "Accept" = "application/json, text/event-stream" }

function Invoke-McpRequest {
    param ([string]$Method, [object]$Params = @{}, [int]$Id = 1)
    $body = @{ jsonrpc = "2.0"; id = $Id; method = $Method; params = $Params } | ConvertTo-Json -Depth 30
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

$taskId = "demo-task-001"
$intent = @{
    taskType = "bugfix"; domain = $null; module = $null; feature = $null
    hardConstraints = @("Must not modify engine model"); riskSignals = @("threading")
}

# Step 1: retrieve_memory_by_chunks
$r1 = Invoke-McpRequest -Method "tools/call" -Params @{
    name = "retrieve_memory_by_chunks"
    arguments = @{ request = @{
        schemaVersion = "1.0"; requestId = [guid]::NewGuid().ToString(); taskId = $taskId
        requirementIntent = $intent
        retrievalChunks = @(
            @{ chunkId = "c1"; chunkType = 0; text = "ViewModel property sync WPF"; structuredScopes = $null; taskShape = $null },
            @{ chunkId = "c2"; chunkType = 1; text = "must not modify engine model"; structuredScopes = $null; taskShape = $null },
            @{ chunkId = "c3"; chunkType = 2; text = "accessor subscription threading"; structuredScopes = $null; taskShape = $null }
        )
        searchProfile = @{ activeOnly = $true; minimumAuthority = 1; maxItemsPerChunk = 5; requireTypeSeparation = $false }
    } }
} -Id 10
$retrieved = @($r1.result.content)[0].text | ConvertFrom-Json

# Step 2: merge_retrieval_results
$r2 = Invoke-McpRequest -Method "tools/call" -Params @{
    name = "merge_retrieval_results"
    arguments = @{ request = @{
        schemaVersion = "1.0"; requestId = [guid]::NewGuid().ToString(); taskId = $taskId
        retrieved = $retrieved
    } }
} -Id 11
$merged = @($r2.result.content)[0].text | ConvertFrom-Json

# Step 3: build_memory_context_pack — assembles the final context for the agent
$r3 = Invoke-McpRequest -Method "tools/call" -Params @{
    name = "build_memory_context_pack"
    arguments = @{
        request = @{
            schemaVersion     = "1.0"
            requestId         = [guid]::NewGuid().ToString()
            taskId            = $taskId
            requirementIntent = $intent
            retrieved         = $retrieved
            merged            = $merged
        }
    }
} -Id 12

if ($r3.result.isError) {
    Write-Host "FAIL: $(@($r3.result.content)[0].text)"
} else {
    $result = @($r3.result.content)[0].text | ConvertFrom-Json
    Write-Host "SUCCESS — kind: $($result.kind)"
    Write-Host "Context pack — decisions: $($result.contextPack.decisions.Count), constraints: $($result.contextPack.constraints.Count)"
    Write-Host "Diagnostics: items=$($result.diagnostics.distinctKnowledgeItems), chunks=$($result.diagnostics.chunksProcessed)"
}
