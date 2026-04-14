## Purpose
This document explains how an external agent (for example Cursor) can use the harness as a deterministic planning control plane.

The harness is responsible for:
1. interpreting a raw user task into a structured `RequirementIntent`
2. deterministically compiling retrieval chunk keys
3. calling the running MCP server over HTTP in the required tool sequence
4. synthesizing the execution plan
5. emitting a human-copyable worker packet that forbids worker-side memory retrieval

## When to call the harness
Call the harness when you have a raw task that you want the harness to plan and package for an execution agent.

The planning agent must call the harness before any execution work begins.

## Exact command to run
Run the harness executable (built artifacts):
```text
harness/Release/HarnessMcp.AgentClient.exe plan-task --task-text "<RAW_TASK>" --output-dir "<OUTPUT_DIR>" --mcp-base-url "<MCP_BASE_URL>" --model-base-url "<MODEL_BASE_URL>" --model-name "<MODEL_NAME>" --stdout-json true
```

If you need the protocol description:
```text
harness/Release/HarnessMcp.AgentClient.exe describe-protocol
```

## Meaning of stdout JSON manifest
When `--stdout-json true` is set, `plan-task` prints exactly one JSON object to stdout: `HarnessRunManifest`.

Key fields:
- `success`: whether planning succeeded
- `nextAction`: what the agent must do next
- `sessionJsonPath`, `executionPlanMarkdownPath`, `workerPacketMarkdownPath`: where the artifacts are written
- `usedFallbackSearches`: whether fallback retrieval paths were used
- `warnings`, `errors`: diagnostics to fix and re-run when needed

## Meaning of 12-harness-run-manifest.json
The harness always writes `12-harness-run-manifest.json` under `--output-dir`, on both success and failure.

On success (`success=true`):
- `nextAction` must be `paste_worker_packet_into_execution_agent`
- the worker packet markdown is written to `workerPacketMarkdownPath`

On failure (`success=false`):
- `nextAction` must be `fix_errors_and_rerun_harness`
- `errors` explains what to correct; re-run the harness with the corrected inputs

## What the agent must do next
1. Call `plan-task` first.
2. Consume `HarnessRunManifest` from stdout (or read `12-harness-run-manifest.json`).
3. If `nextAction == paste_worker_packet_into_execution_agent`, paste the worker packet markdown from `workerPacketMarkdownPath` into the execution agent session.

## What the execution agent must not do
The worker packet explicitly forbids the execution agent from retrieving long-term memory independently.

Specifically, the execution agent must not:
- retrieve long-term memory independently
- reinterpret the task at the architecture level
- generate a replacement plan outside the harness
- expand scope beyond the listed harness steps

