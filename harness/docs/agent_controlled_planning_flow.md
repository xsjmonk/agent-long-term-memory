# Agent-Controlled Planning Flow

## Overview

This document describes the planning flow where the agent performs all actual work while the harness controls step order and validates submissions.

## Architecture Layers

| Layer | Responsibility |
|-------|----------------|
| User | Provides task in agent UI |
| Skills/Rules | Define process and when harness is mandatory |
| Harness | Controls sequence, validates artifacts, records state |
| Agent | Performs each required step, calls MCP when instructed |
| MCP Server | Provides long-term memory tools |
| Execution Agent | Executes approved worker packet later |

## User-to-Agent Flow

1. User describes a task to the agent
2. Agent determines if task is trivial or non-trivial
3. For non-trivial tasks, agent invokes the harness control plane

## Harness Control Plane

The harness does NOT:
- Call any LLM API
- Call any MCP client directly
- Generate execution plans
- Generate worker packets
- Perform memory retrieval

The harness DOES:
- Control step order (stage sequential validation)
- Validate submitted artifacts
- Record session state
- Return machine-readable nextAction

## Step Protocol

1. **Start Session**: Agent calls `start-session` with raw task
2. **Read Next Action**: Extract `nextAction` from response
3. **Perform Action**: Agent does ONLY what nextAction specifies
4. **Submit Result**: Agent calls `submit-step-result` with produced artifact
5. **Validate**: Harness validates and advances to next stage
6. **Repeat**: Continue until `stage: complete`

## MCP Invocation

The agent calls MCP tools ONLY when the harness specifies:
- `agent_call_mcp_retrieve_memory_by_chunks`
- `agent_call_mcp_merge_retrieval_results`
- `agent_call_mcp_build_memory_context_pack`

The harness returns the tool name and request skeleton in the payload.
Agent executes the tool, receives the result, and submits it back to harness.

## Completion

Planning is complete when:
1. RequirementIntent accepted
2. RetrievalChunkSet accepted
3. ChunkQualityReport accepted
4. RetrieveMemoryByChunksResponse accepted
5. MergeRetrievalResultsResponse accepted
6. BuildMemoryContextPackResponse accepted
7. ExecutionPlan accepted
8. WorkerExecutionPacket accepted

The final response contains:
- `stage: complete`
- `nextAction: complete`
- `payload.executionPlan`
- `payload.workerExecutionPacket`

## Execution Agent

The execution agent receives the accepted WorkerExecutionPacket and:
- Executes steps exactly as specified
- Does NOT retrieve memory independently
- Does NOT expand scope beyond listed steps
- Reports blocks instead of inventing behavior