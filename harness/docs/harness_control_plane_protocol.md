# Harness Control-Plane Protocol

## Overview

The harness is a **control-plane-only** service. It:

- Controls stage sequencing
- Validates submitted artifacts
- Returns the next required action
- Records session state

The harness does **not**:
- Call any LLM API
- Call MCP tools
- Do planning or reasoning
- Execute agent work

The agent performs all work. The harness tells the agent what to do next.

---

## Single Supported Entrypoint

The ONLY supported agent-facing entrypoint is:

```
Scripts\invoke-harness-control-plane.ps1 <command> [options]
```

No other entrypoint is supported.

---

## Protocol Commands

| Command | Purpose |
|---|---|
| `start-session` | Create a new planning session |
| `get-next-step` | Re-sync: get current required action for a session |
| `submit-step-result` | Submit an artifact; validate and advance the state |
| `get-session-status` | Inspect current stage, accepted artifacts, errors |
| `cancel-session` | Abort a session cleanly |

---

## `start-session`

**Input:**
```json
{
  "rawTask": "Add year switching without changing engine logic.",
  "sessionId": null,
  "metadata": {
    "project": "optional",
    "domain": "optional"
  }
}
```

**Output:**
```json
{
  "success": true,
  "sessionId": "sess-001",
  "taskId": "task-001",
  "stage": "need_requirement_intent",
  "nextAction": "agent_generate_requirement_intent",
  "inputContract": { "artifactType": "RequirementIntent", "schemaVersion": "1.0" },
  "instructions": [
    "Convert the raw task into RequirementIntent JSON.",
    "Do not query MCP yet.",
    "Do not generate the plan yet."
  ],
  "payload": { "rawTask": "Add year switching without changing engine logic." },
  "errors": [],
  "warnings": []
}
```

---

## `submit-step-result`

**Input:**
```json
{
  "sessionId": "sess-001",
  "completedAction": "agent_generate_requirement_intent",
  "artifact": {
    "artifactType": "RequirementIntent",
    "schemaVersion": "1.0",
    "value": {
      "task_id": "task-001",
      "task_type": "ui-change",
      "goal": "support year switching via ajax",
      "hard_constraints": ["engine must not change"],
      "risk_signals": ["placement consistency"],
      "complexity": "medium"
    }
  }
}
```

**Output on valid artifact:**
```json
{
  "success": true,
  "sessionId": "sess-001",
  "taskId": "task-001",
  "stage": "need_retrieval_chunk_set",
  "nextAction": "agent_generate_retrieval_chunk_set",
  "inputContract": { "artifactType": "RetrievalChunkSet", "schemaVersion": "1.0" },
  "instructions": ["Generate compact purpose-specific retrieval chunks."],
  "errors": [],
  "warnings": []
}
```

**Output on invalid artifact (hard stop):**
```json
{
  "success": false,
  "stage": "error",
  "nextAction": "stop_with_error",
  "errors": ["constraint chunk missing although hard_constraints is not empty"],
  "warnings": []
}
```

---

## Planning Stages

| Stage | `nextAction` |
|---|---|
| `need_requirement_intent` | `agent_generate_requirement_intent` |
| `need_retrieval_chunk_set` | `agent_generate_retrieval_chunk_set` |
| `need_retrieval_chunk_validation` | `agent_validate_chunk_quality` |
| `need_mcp_retrieve_memory_by_chunks` | `agent_call_mcp_retrieve_memory_by_chunks` |
| `need_mcp_merge_retrieval_results` | `agent_call_mcp_merge_retrieval_results` |
| `need_mcp_build_memory_context_pack` | `agent_call_mcp_build_memory_context_pack` |
| `need_execution_plan` | `agent_generate_execution_plan` |
| `need_worker_execution_packet` | `agent_generate_worker_execution_packet` |
| `complete` | `complete` |
| `error` | `stop_with_error` |

Stages must be completed in order. No stage may be skipped.

---

## MCP Stage Protocol

For MCP stages, the harness response includes `toolName` at the top level and `payload.request`:

```json
{
  "nextAction": "agent_call_mcp_retrieve_memory_by_chunks",
  "toolName": "retrieve_memory_by_chunks",
  "inputContract": { "artifactType": "RetrieveMemoryByChunksResponse", "schemaVersion": "1.0" },
  "payload": {
    "request": {
      "schemaVersion": "1.0",
      "requestId": "sess-001-retrieve",
      "taskId": "task-001",
      "requirementIntent": { ... },
      "retrievalChunks": { ... },
      "search_profile": {
        "active_only": true,
        "minimum_authority": "reviewed",
        "max_items_per_chunk": 5,
        "require_type_separation": true
      }
    }
  }
}
```

The agent calls the MCP tool with `payload.request` exactly as provided and submits the raw result back.

MCP tool mapping:

| `nextAction` | `toolName` |
|---|---|
| `agent_call_mcp_retrieve_memory_by_chunks` | `retrieve_memory_by_chunks` |
| `agent_call_mcp_merge_retrieval_results` | `merge_retrieval_results` |
| `agent_call_mcp_build_memory_context_pack` | `build_memory_context_pack` |

---

## Artifact Canonical Schemas

### RequirementIntent
```json
{
  "task_id": "string (required)",
  "task_type": "string (required)",
  "goal": "string (required)",
  "hard_constraints": ["array of strings (required)"],
  "risk_signals": ["array of strings (required)"],
  "complexity": "low|medium|high (required)"
}
```

### RetrievalChunkSet
```json
{
  "task_id": "string (required)",
  "complexity": "low|medium|high (required)",
  "chunks": [
    {
      "chunk_id": "unique string (required)",
      "chunk_type": "core_task|constraint|risk|pattern|similar_case (required)",
      "text": "string (required for non-similar_case types)"
    }
  ]
}
```

Coverage rules:
- `core_task` chunk always required
- `constraint` chunk required if `hard_constraints` is non-empty
- `risk` chunk required if `risk_signals` is non-empty
- `similar_case` chunk required if complexity is `medium` or `high`

### ExecutionPlan
```json
{
  "task_id": "string (required)",
  "task": "string (required)",
  "scope": "string (required)",
  "constraints": ["array (required if hard_constraints were non-empty)"],
  "forbidden_actions": ["array (required)"],
  "steps": [
    {
      "step_number": 1,
      "title": "string (required)",
      "actions": ["required, non-empty"],
      "outputs": ["required, non-empty"],
      "acceptance_checks": ["required, non-empty"]
    }
  ],
  "deliverables": ["array (required, non-empty)"]
}
```

### WorkerExecutionPacket
```json
{
  "goal": "string (required)",
  "scope": "string (required)",
  "hard_constraints": ["array (required, non-empty)"],
  "forbidden_actions": ["array (required, non-empty)"],
  "execution_rules": ["must explicitly prohibit memory retrieval (required, non-empty)"],
  "steps": ["(required, non-empty)"],
  "required_output_sections": ["per_step_results", "final_deliverables", "validation_summary"]
}
```

---

## Validation Behavior

### On validation failure
Harness returns `success: false`, `stage: error`, `nextAction: stop_with_error`. The agent must stop immediately, surface errors verbatim, fix only the failing artifact, and resubmit the same stage. No speculative continuation is allowed.

### On valid result
Harness advances the session and returns the next required action.

---

## Session State Model

Sessions are stored as local JSON files under `.harness/sessions/`. Each session stores:

- `sessionId`, `taskId`, `rawTask`
- `currentStage`, `lastAcceptedAction`
- Accepted artifacts for each stage
- `errors`, `warnings`, `createdUtc`, `updatedUtc`

---

## Completion

Planning completes only after all seven artifacts are accepted. The completion response:

```json
{
  "success": true,
  "stage": "complete",
  "nextAction": "complete",
  "completionArtifacts": {
    "executionPlan": { ... },
    "workerExecutionPacket": { ... }
  }
}
```
