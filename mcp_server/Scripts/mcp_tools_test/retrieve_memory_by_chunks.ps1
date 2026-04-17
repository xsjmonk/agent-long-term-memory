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

# chunkType is an INTEGER (0-indexed enum):
#   0=CoreTask, 1=Constraint, 2=Risk, 3=Pattern
# Only values 0-3 are supported — type 4 (SimilarCase) and above cause server errors
# requirementIntent.domain/module/feature are REQUIRED but nullable — must be present as null
# structuredScopes and taskShape per chunk are REQUIRED but nullable — must be present as null
$r = Invoke-McpRequest -Method "tools/call" -Params @{
    name = "retrieve_memory_by_chunks"
    arguments = @{
        request = @{
            schemaVersion = "1.0"
            requestId     = [guid]::NewGuid().ToString()
            taskId        = "demo-task-001"
            requirementIntent = @{
                taskType        = "bugfix"
                domain          = $null
                module          = $null
                feature         = $null
                hardConstraints = @("Must not break existing API")
                riskSignals     = @("threading", "UI binding")
            }
            retrievalChunks = @(
                @{
                    chunkId          = "c1"
                    chunkType        = 0
                    text             = "ViewModel property synchronization WPF grid"
                    structuredScopes = $null
                    taskShape        = $null
                },
                @{
                    chunkId          = "c2"
                    chunkType        = 1
                    text             = "must not modify engine model directly"
                    structuredScopes = $null
                    taskShape        = $null
                },
                @{
                    chunkId          = "c3"
                    chunkType        = 2
                    text             = "accessor subscription threading dispatcher"
                    structuredScopes = $null
                    taskShape        = $null
                }
            )
            searchProfile = @{
                activeOnly            = $true
                minimumAuthority      = 1
                maxItemsPerChunk      = 5
                requireTypeSeparation = $false
            }
        }
    }
} -Id 2

if ($r.result.isError) {
    Write-Host "FAIL: $(@($r.result.content)[0].text)"
} else {
    $result = @($r.result.content)[0].text | ConvertFrom-Json
    Write-Host "SUCCESS — kind: $($result.kind), taskId: $($result.taskId)"
    $result.chunkResults | ForEach-Object {
        Write-Host "  chunk $($_.chunkId) (type $($_.chunkType)): $($_.diagnostics.finalCandidateCount) results"
    }
}
