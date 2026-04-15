# Harness Control Plane Protocol

## Overview

The harness operates as a **control-plane-only** system. It controls step order, validates artifacts, records session state, and tells the agent the next required action. It does NOT call any LLM API or MCP client.

## Wrapper

Use `Scripts/invoke-harness-control-plane.ps1` - this is the ONLY supported harness entrypoint.

## Commands

### start-session

Creates a new planning session and returns the first required step.

**Input:**
```json
{
  "rawTask": "Add year switching feature",
  "sessionId": null,
  "metadata": { "project": "optional" }
}
```

**Output:**
```json
{
  "success": true,
  "sessionId": "sess-xxx",
  "taskId": "task-xxx",
  "stage": "need_requirement_intent",
  "nextAction": "agent_generate_requirement_intent",
  "inputContract": { "artifactType": "RequirementIntent", "schemaVersion": "1.0" },
  "instructions": ["Convert the raw task into RequirementIntent JSON."],
  "payload": { "rawTask": "Add year switching feature" },
  "errors": [],
  "warnings": []
}
```

### get-next-step

Returns the current required step without advancing.

**Input:** `{ "--session-id": "sess-xxx" }`

### submit-step-result

Validates and advances the session.

**Input:**
```json
{
  "sessionId": "sess-xxx",
  "completedAction": "agent_generate_requirement_intent",
  "artifact": {
    "artifactType": "RequirementIntent",
    "value": { ... }
  }
}
```

### get-session-status

Returns current stage, accepted artifacts, errors, warnings.

### cancel-session

Marks session as cancelled.

### describe-protocol

Returns machine-readable protocol description.

## Stages

| Stage | Next Action |
|-------|-------------|
| need_requirement_intent | agent_generate_requirement_intent |
| need_retrieval_chunk_set | agent_generate_retrieval_chunk_set |
| need_retrieval_chunk_validation | agent_validate_chunk_quality |
| need_mcp_retrieve_memory_by_chunks | agent_call_mcp_retrieve_memory_by_chunks |
| need_mcp_merge_retrieval_results | agent_call_mcp_merge_retrieval_results |
| need_mcp_build_memory_context_pack | agent_call_mcp_build_memory_context_pack |
| need_execution_plan | agent_generate_execution_plan |
| need_worker_execution_packet | agent_generate_worker_execution_packet |
| complete | complete |

## Artifact Types

- `RequirementIntent` - Task analysis JSON
- `RetrievalChunkSet` - Purpose-specific chunks
- `ChunkQualityReport` - Validation report
- `RetrieveMemoryByChunksResponse` - MCP retrieval result
- `MergeRetrievalResultsResponse` - MCP merge result
- `BuildMemoryContextPackResponse` - MCP context pack
- `ExecutionPlan` - Steps with acceptance criteria
- `WorkerExecutionPacket` - Hand-off to execution agent

## MCP Stage Payloads

For MCP stages, the harness provides explicit `toolName` and `request` in the payload:

### NeedMcpRetrieveMemoryByChunks payload:
```json
{
  "toolName": "retrieve_memory_by_chunks",
  "request": {
    "schemaVersion": "1.0",
    "requestId": "sess-xxx-retrieve",
    "taskId": "task-xxx",
    "active_only": true,
    "minimum_authority": "reviewed",
    "max_items_per_chunk": 5,
    "require_type_separation": true,
    "requirementIntent": {...},
    "retrievalChunks": [...]
  }
}
```

### NeedMcpMergeRetrievalResults payload:
```json
{
  "toolName": "merge_retrieval_results",
  "request": {
    "schemaVersion": "1.0",
    "requestId": "sess-xxx-merge",
    "taskId": "task-xxx",
    "retrieved": {...}
  }
}
```

### NeedMcpBuildMemoryContextPack payload:
```json
{
  "toolName": "build_memory_context_pack",
  "request": {
    "schemaVersion": "1.0",
    "requestId": "sess-xxx-contextpack",
    "taskId": "task-xxx",
    "requirementIntent": {...},
    "retrieved": {...},
    "merged": {...}
  }
}
```

## Validation Rules

On validation failure, the harness returns:
```json
{
  "success": false,
  "stage": "error",
  "nextAction": "stop_with_error",
  "errors": ["specific error message"]
}
```

The agent must stop and fix the specific issue.

When harness returns `nextAction: stop_with_error`, the agent MUST:
1. Stop immediately - do not continue speculatively
2. Parse the errors array
3. Fix only the failing artifact
4. Resubmit corrected artifact when the harness continues

## Session Storage

Sessions are stored as JSON files in `.harness/sessions/`.

## Wrapper

Use `Scripts/invoke-harness-control-plane.ps1` to call the harness:

## Implementation Note

> Any implementation change to the harness control plane, skills, validators, or wrapper is incomplete until the control-plane test suite passes (`dotnet test`).