# Harness Control-Plane-Only Design

## 1. Purpose

This revision replaces the earlier runtime-centric harness design with a **control-plane-only harness**.

In this revised design:

- the **agent** does the actual work,
- the **skills/rules** define the flow semantics,
- the **harness** controls step order, validates transitions, and tells the agent what to do next,
- the harness does **not** call any LLM API,
- the harness does **not** call MCP itself,
- the user continues to interact only with the agent.

This directly aligns the architecture with the principle that the harness is the control plane, while the agent remains the planner/worker for planning-stage actions.

---

## 2. Why the previous design is no longer sufficient

The prior design still treated the harness runtime as something that:

- invoked a hidden planning command,
- performed retrieval and plan generation internally,
- returned a manifest and artifacts.

That is useful as a pragmatic runtime, but it is not the desired final architecture.

The desired architecture is stricter:

1. user talks to the agent,
2. agent calls the harness,
3. harness tells the agent the next required step,
4. agent performs that step,
5. harness validates the result and advances the state,
6. agent continues until the plan is complete.

So the harness must become a **protocol/state-machine controller**, not a planner runtime.

---

## 3. Final architecture decision

## 3.1 Harness is control plane only

The harness owns:

- session state,
- stage sequencing,
- allowed next actions,
- schema validation,
- stop/go decisions,
- final completion status.

The harness does **not** own:

- language-model reasoning,
- MCP tool execution,
- memory analysis,
- plan writing,
- worker-packet authoring.

Those are performed by the agent under harness control.

## 3.2 Skills define the semantic flow

The skills/rules define:

- when harness must be invoked,
- which tasks are trivial vs non-trivial,
- how the agent should perform each step,
- how the agent should call MCP tools when instructed,
- how the agent should present the final plan to the user.

Harness does not replace the skill. Harness and skills work together:

- **skills** = semantic instructions and agent behavior envelope,
- **harness** = step controller and validator.

## 3.3 Harness must not require LLM API or MCP client

The harness must not require:

- Claude API,
- OpenAI API,
- Codai API,
- any direct model endpoint,
- any MCP client library.

The agent already has a model and MCP access. The harness only controls the flow.

---

## 4. Responsibility split

| Layer | Responsibility |
|---|---|
| User | Provides the task in the agent UI |
| Skills / Rules | Define the process and when harness is mandatory |
| Harness | Controls sequence, emits next step, validates submitted artifacts, records state |
| Agent | Performs each required step, calls MCP when instructed, reasons over results, generates plan |
| MCP Server | Provides long-term memory tools and retrieval results |
| Execution Agent | Executes an approved worker packet later |

---

## 5. Correct end-to-end flow

```text
User
  -> Agent
      -> Harness: start session
      <- Harness: next required step
  -> Agent performs step
      -> Harness: submit step result
      <- Harness: next required step
  -> Agent performs next step
      -> Harness: submit step result
      <- Harness: next required step
  ...
      <- Harness: planning complete
  -> Agent shows plan to user
  -> User may use worker packet in another agent
```

### Key rule

The harness does not do the step.
It only tells the agent which step is valid next.

---

## 6. Required planning stages

The harness must model the following stages.

1. `need_requirement_intent`
2. `need_retrieval_chunk_set`
3. `need_retrieval_chunk_validation`
4. `need_mcp_retrieve_memory_by_chunks`
5. `need_mcp_merge_retrieval_results`
6. `need_mcp_build_memory_context_pack`
7. `need_execution_plan`
8. `need_worker_execution_packet`
9. `complete`
10. `error`

The harness must never skip from raw task directly to execution plan.

---

## 7. Protocol design

The harness and agent must share a **machine-readable step protocol**.

The harness must provide a small CLI or local service surface that supports these operations:

1. `start-session`
2. `get-next-step`
3. `submit-step-result`
4. `get-session-status`
5. `cancel-session`

A single executable is fine, but these protocol actions must exist.

## 7.1 `start-session`

Purpose:
- create a planning session,
- register the raw task,
- return the first required step.

Input:

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

Output:

```json
{
  "success": true,
  "sessionId": "sess-001",
  "taskId": "task-001",
  "stage": "need_requirement_intent",
  "nextAction": "agent_generate_requirement_intent",
  "inputContract": {
    "artifactType": "RequirementIntent",
    "schemaVersion": "1.0"
  },
  "instructions": [
    "Convert the raw task into RequirementIntent JSON.",
    "Do not query MCP yet.",
    "Do not generate the plan yet."
  ],
  "payload": {
    "rawTask": "Add year switching without changing engine logic."
  },
  "errors": [],
  "warnings": []
}
```

## 7.2 `get-next-step`

Purpose:
- return the current required action for an existing session.

This allows the agent to re-sync if it lost context.

## 7.3 `submit-step-result`

Purpose:
- submit the artifact/result produced by the agent for the current stage,
- validate it,
- advance to the next stage,
- return the next required action.

Input example:

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

Output example:

```json
{
  "success": true,
  "sessionId": "sess-001",
  "taskId": "task-001",
  "stage": "need_retrieval_chunk_set",
  "nextAction": "agent_generate_retrieval_chunk_set",
  "inputContract": {
    "artifactType": "RetrievalChunkSet",
    "schemaVersion": "1.0"
  },
  "instructions": [
    "Generate compact purpose-specific retrieval chunks.",
    "Do not mix constraint, risk, pattern, or similar-case semantics in one chunk."
  ],
  "payload": {
    "requirementIntent": {
      "task_id": "task-001",
      "task_type": "ui-change"
    }
  },
  "errors": [],
  "warnings": []
}
```

## 7.4 `get-session-status`

Purpose:
- inspect current stage,
- inspect latest accepted artifacts,
- inspect failures,
- support deterministic resume.

## 7.5 `cancel-session`

Purpose:
- abort the session cleanly.

---

## 8. Required action types

The harness must use explicit action names.

At minimum:

- `agent_generate_requirement_intent`
- `agent_generate_retrieval_chunk_set`
- `agent_validate_chunk_quality` (optional if harness validates internally)
- `agent_call_mcp_retrieve_memory_by_chunks`
- `agent_call_mcp_merge_retrieval_results`
- `agent_call_mcp_build_memory_context_pack`
- `agent_generate_execution_plan`
- `agent_generate_worker_execution_packet`
- `complete`
- `stop_with_error`

These action names are the shared vocabulary between harness and agent.

---

## 9. Artifact contracts

The harness must validate these artifact types.

### 9.1 `RequirementIntent`

The agent produces it.
The harness validates required fields and normalizes the state.

### 9.2 `RetrievalChunkSet`

The agent produces it.
The harness validates:

- required chunk coverage,
- one-purpose-per-chunk,
- compactness,
- ambiguity preservation.

### 9.3 `RetrieveMemoryByChunksResponse`

The agent calls MCP tool `retrieve_memory_by_chunks` and submits the full result back to harness.

### 9.4 `MergeRetrievalResultsResponse`

The agent calls MCP tool `merge_retrieval_results` and submits the result back to harness.

### 9.5 `BuildMemoryContextPackResponse`

The agent calls MCP tool `build_memory_context_pack` and submits the result back to harness.

### 9.6 `ExecutionPlan`

The agent generates it from:

- raw task,
- accepted `RequirementIntent`,
- accepted `RetrievalChunkSet`,
- accepted memory context pack.

The harness validates:

- no missing constraints,
- no missing steps,
- acceptance checks present,
- no worker-side memory retrieval instruction.

### 9.7 `WorkerExecutionPacket`

The agent generates it.
The harness validates that it:

- preserves hard constraints,
- preserves forbidden actions,
- uses the approved execution plan only,
- forbids independent memory retrieval in execution phase.

---

## 10. How MCP is handled

The harness must not call MCP directly.

Instead, when the next stage is `need_mcp_retrieve_memory_by_chunks`, the harness returns:

```json
{
  "nextAction": "agent_call_mcp_retrieve_memory_by_chunks",
  "toolName": "retrieve_memory_by_chunks",
  "inputContract": {
    "artifactType": "RetrieveMemoryByChunksRequest",
    "schemaVersion": "1.0"
  },
  "payload": {
    "request": { ... }
  }
}
```

The agent then:

1. calls MCP,
2. receives the result,
3. submits the result back to harness through `submit-step-result`.

The same pattern applies to:

- `merge_retrieval_results`
- `build_memory_context_pack`

This preserves your required architecture:

- harness controls,
- agent does,
- MCP serves retrieval.

---

## 11. How skills are used

Skills/rules remain the semantic driver.

They must instruct the agent to:

1. call harness for non-trivial tasks,
2. obey `nextAction`,
3. produce only the requested artifact type,
4. call MCP only when harness says so,
5. submit every result back to harness before continuing,
6. stop if harness returns `stop_with_error`.

### Skill rule example

```text
For any non-trivial implementation, refactor, design, or debugging task:
1. Start a harness session.
2. Read the returned nextAction.
3. Perform only that nextAction.
4. Submit the produced artifact or MCP result back to harness.
5. Do not skip stages.
6. Do not generate an execution plan before harness reaches the execution-plan stage.
7. Do not perform execution work in the planning session.
```

This keeps the flow defined in skills while the harness enforces the progression.

---

## 12. Budget-saving implementation strategy

To keep cost low:

- do not add LLM provider code to the harness,
- do not add MCP client code to the harness,
- do not add browser UI,
- do not add background orchestration service,
- do not add distributed state storage,
- do not add autonomous loops,
- do not redesign the MCP server.

Implement only:

1. local session state,
2. protocol commands,
3. schema validators,
4. state transition rules,
5. compact session artifacts/logging,
6. skill files that tell the agent to use the harness.

---

## 13. Files that must exist

```text
src/
  HarnessMcp.ControlPlane/
    Program.cs
    SessionStore.cs
    HarnessProtocolService.cs
    HarnessStateMachine.cs
    Validators/
    Contracts/
    TransitionRules/

docs/
  harness_control_plane_protocol.md
  agent_controlled_planning_flow.md

.cursor/
  rules/
    00-harness-control-plane.mdc
    01-harness-failure.mdc
    02-harness-execution.mdc
```

### Notes

- This replaces the old runtime-centric `HarnessMcp.AgentClient` concept.
- If you want to keep the old project name for compatibility, that is acceptable, but its role must change to control plane only.

---

## 14. Session state model

The harness must store, at minimum:

- `sessionId`
- `taskId`
- `rawTask`
- `currentStage`
- `lastAcceptedAction`
- `acceptedRequirementIntent`
- `acceptedRetrievalChunkSet`
- `acceptedRetrieveMemoryByChunksResponse`
- `acceptedMergeRetrievalResultsResponse`
- `acceptedBuildMemoryContextPackResponse`
- `acceptedExecutionPlan`
- `acceptedWorkerExecutionPacket`
- `errors`
- `warnings`
- `createdUtc`
- `updatedUtc`

This can be stored as local JSON files for v1.

No DB is required.

---

## 15. Validation behavior

The harness must validate every step result before advancing.

### On validation failure

Return:

```json
{
  "success": false,
  "stage": "error",
  "nextAction": "stop_with_error",
  "errors": [
    "constraint chunk missing although hard_constraints is not empty"
  ],
  "warnings": []
}
```

The agent must stop and show the issue.

### On valid result

Return the next required action.

No speculative continuation is allowed.

---

## 16. Deterministic completion

Planning completes only when the harness has accepted:

1. `RequirementIntent`
2. `RetrievalChunkSet`
3. `RetrieveMemoryByChunksResponse`
4. `MergeRetrievalResultsResponse`
5. `BuildMemoryContextPackResponse`
6. `ExecutionPlan`
7. `WorkerExecutionPacket`

Only then may the harness return:

```json
{
  "success": true,
  "stage": "complete",
  "nextAction": "complete",
  "artifacts": {
    "executionPlan": "...",
    "workerExecutionPacket": "..."
  },
  "errors": [],
  "warnings": []
}
```

---

## 17. Required tests

Add low-cost deterministic tests.

### 17.1 Protocol tests

- `StartSessionReturnsRequirementIntentStepTests`
- `SubmitRequirementIntentAdvancesToChunkStepTests`
- `SubmitChunkSetAdvancesToMcpRetrieveStepTests`
- `SubmitRetrieveMemoryByChunksAdvancesToMergeStepTests`
- `SubmitMergeAdvancesToContextPackStepTests`
- `SubmitContextPackAdvancesToExecutionPlanStepTests`
- `SubmitExecutionPlanAdvancesToWorkerPacketStepTests`
- `SubmitWorkerPacketCompletesSessionTests`

### 17.2 Validation tests

- invalid `RequirementIntent` stops flow
- invalid chunk purity stops flow
- missing constraint chunk stops flow
- invalid MCP result shape stops flow
- execution plan missing constraints stops flow
- worker packet allowing independent memory retrieval stops flow

### 17.3 Rule content tests

- planning rule requires harness-first flow
- failure rule forbids bypass after harness error
- execution rule forbids independent retrieval

These tests are cheap and protect the control-plane contract.

---

## 18. Final integrated flow

```text
User
  -> Agent
     -> calls harness start-session
     <- harness says: generate RequirementIntent
  -> Agent generates RequirementIntent
     -> submits to harness
     <- harness says: generate RetrievalChunkSet
  -> Agent generates RetrievalChunkSet
     -> submits to harness
     <- harness says: call MCP retrieve_memory_by_chunks
  -> Agent calls MCP retrieve_memory_by_chunks
     -> submits result to harness
     <- harness says: call MCP merge_retrieval_results
  -> Agent calls MCP merge_retrieval_results
     -> submits result to harness
     <- harness says: call MCP build_memory_context_pack
  -> Agent calls MCP build_memory_context_pack
     -> submits result to harness
     <- harness says: generate ExecutionPlan
  -> Agent generates ExecutionPlan
     -> submits to harness
     <- harness says: generate WorkerExecutionPacket
  -> Agent generates WorkerExecutionPacket
     -> submits to harness
     <- harness says: complete
  -> Agent shows final plan to user
```

---

## 19. Final decision summary

This revised design makes the harness a **true control plane**.

It:

- does not need an LLM API,
- does not need an MCP client,
- does not do the actual planning work,
- does not do the actual memory retrieval work,
- keeps the flow semantics in skills,
- makes the agent call the harness to know the next valid step,
- gives the agent and harness a shared protocol.

That is the correct architecture for your clarified requirement.
