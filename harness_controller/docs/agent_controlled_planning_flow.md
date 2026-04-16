# Agent-Controlled Planning Flow

## Architecture Layers

| Layer | Responsibility |
|---|---|
| User | Provides the task in the agent UI |
| Skills / Rules | Define the process and when harness is mandatory |
| Harness | Controls sequence, emits next action, validates artifacts, records state |
| Agent | Performs each required step, calls MCP when instructed, generates plan |
| MCP Server | Provides long-term memory retrieval tools |
| Execution Agent | Executes the approved WorkerExecutionPacket later |

The harness is a **control plane only**. It does not call any LLM API or MCP tool.

---

## Skill Files (Canonical Set)

Skills define when and how the harness is used. The canonical skill set under `.cursor/rules/` is:

| File | Purpose |
|---|---|
| `04-harness-skill-activation.mdc` | Semantic planning-intent detection gate |
| `00-harness-control-plane.mdc` | Mandatory harness-first planning loop |
| `01-harness-failure.mdc` | Hard-stop behavior on harness errors |
| `03-harness-mcp-tool-calling.mdc` | Exact MCP tool invocation during planning |
| `02-harness-execution.mdc` | Constrained execution from WorkerExecutionPacket |

### Activation Skill (`04-harness-skill-activation.mdc`)

The activation skill is the semantic gate. It activates for:

- Non-trivial implementation, design, refactoring, migration, or debugging tasks
- Requests for plan, approach, strategy, outline, or decomposition
- Tasks where plan quality matters before execution begins

It does NOT activate for:
- Casual questions, explanations, or trivial single-line changes
- Direct execution of an already accepted WorkerExecutionPacket

Activation is **semantic, not lexical**. The meaning and context decide activation, not keyword presence.

---

## The Planning Loop

```text
User
  -> Agent detects planning intent (04-harness-skill-activation.mdc)
  -> Agent activates planning skill (00-harness-control-plane.mdc)
  -> Agent: Scripts\invoke-harness-control-plane.ps1 start-session --rawTask "..."
  <- Harness: nextAction = agent_generate_requirement_intent

  -> Agent generates RequirementIntent
  -> Agent: submit-step-result (RequirementIntent)
  <- Harness: nextAction = agent_generate_retrieval_chunk_set

  -> Agent generates RetrievalChunkSet
  -> Agent: submit-step-result (RetrievalChunkSet)
  <- Harness: nextAction = agent_validate_chunk_quality

  -> Agent validates chunk quality, produces ChunkQualityReport
  -> Agent: submit-step-result (ChunkQualityReport)
  <- Harness: nextAction = agent_call_mcp_retrieve_memory_by_chunks
              toolName = retrieve_memory_by_chunks
              payload.request = { exact request skeleton }

  -> Agent calls MCP tool retrieve_memory_by_chunks with payload.request exactly
  -> Agent: submit-step-result (raw MCP result)
  <- Harness: nextAction = agent_call_mcp_merge_retrieval_results
              toolName = merge_retrieval_results

  -> Agent calls MCP tool merge_retrieval_results
  -> Agent: submit-step-result (raw MCP result)
  <- Harness: nextAction = agent_call_mcp_build_memory_context_pack
              toolName = build_memory_context_pack

  -> Agent calls MCP tool build_memory_context_pack
  -> Agent: submit-step-result (raw MCP result)
  <- Harness: nextAction = agent_generate_execution_plan

  -> Agent generates ExecutionPlan from all accepted artifacts
  -> Agent: submit-step-result (ExecutionPlan)
  <- Harness: nextAction = agent_generate_worker_execution_packet

  -> Agent generates WorkerExecutionPacket
  -> Agent: submit-step-result (WorkerExecutionPacket)
  <- Harness: stage = complete, nextAction = complete
              completionArtifacts = { executionPlan, workerExecutionPacket }

  -> Agent shows ExecutionPlan and WorkerExecutionPacket to user
  -> User may use WorkerExecutionPacket in an execution agent
```

---

## Error Handling

If any artifact fails validation:

1. Harness returns `success: false`, `stage: error`, `nextAction: stop_with_error`
2. Agent stops immediately (see `01-harness-failure.mdc`)
3. Agent surfaces harness errors verbatim to the user
4. Agent fixes only the failing artifact
5. Agent resubmits the same stage with the corrected artifact

The agent must never continue speculatively after a harness error.

---

## MCP Invocation Rules

The harness returns explicit `toolName` (at top level) and `payload.request` for each MCP stage. The agent:

1. Reads `toolName` from the harness response
2. Uses `payload.request` exactly as the MCP tool input — no modification
3. Submits the raw MCP result back to harness without modification

The agent must not call MCP at any other time during planning.

---

## Execution Phase (Post-Planning)

After planning completes:

1. User has an accepted `WorkerExecutionPacket`
2. Execution agent uses `02-harness-execution.mdc`
3. Execution agent follows the packet exactly:
   - No replanning
   - No independent memory retrieval
   - No scope expansion
   - Report blocks rather than guessing

The execution phase is separate from planning. Harness is not involved during execution.

---

## Key Design Decisions

- **Harness is control-plane only**: no LLM API, no MCP client, no autonomous loops
- **Skills define the semantic flow**: activation, harness loop, failure handling, MCP, execution
- **One strict entrypoint**: `Scripts\invoke-harness-control-plane.ps1`
- **Canonical contracts per stage**: one artifact schema per stage, validated strictly
- **Hard stops on error**: agent must stop and resubmit, never continue speculatively
- **MCP is agent-executed**: harness instructs the tool name and request; agent calls; result is submitted back
- **Completion requires all stages**: no shortcuts, no stage skipping
- **Generic agent design**: skills work with any agent, not tied to a specific product
