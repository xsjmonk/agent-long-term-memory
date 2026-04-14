# Agent-Controlled Flow

This document describes the complete, fully-automated flow where a coding agent (such as Cursor) uses the harness as its invisible deterministic planning control plane. The end user never runs harness commands manually.

## Roles

### Planning Agent
The planning agent is the agent session that receives the user's raw task description. It is responsible for:
1. Receiving the user's task description
2. Determining whether the task is trivial or non-trivial
3. Invoking the harness via the hidden wrapper (`Scripts/invoke-harness-plan.ps1`)
4. Consuming `HarnessRunManifest` from the harness output
5. Presenting the execution plan summary to the user
6. Passing the worker packet to the execution agent

The planning agent MUST NOT begin coding, direct planning, or retrieve long-term memory independently before the harness succeeds.

### Execution Agent
The execution agent is a separate agent session that receives the worker packet produced by the harness. It is responsible for:
1. Executing the steps listed in the worker packet
2. Producing per-step results, deliverables, and a validation summary
3. Reporting blocks instead of inventing behavior

The execution agent MUST NOT:
- Retrieve long-term memory independently
- Reinterpret the task at the architecture level
- Generate a replacement plan
- Expand scope beyond the listed steps
- Change forbidden layers or components

## Task Classification

| Task Type | Example | Harness Required |
|---|---|---|
| Trivial | rename a variable, fix a typo, add a comment | No |
| Non-trivial | implement a feature, refactor a module, debug a failure, design a system | Yes |

For non-trivial tasks, the planning agent MUST invoke the harness before any other action.

## Complete Flow

### Step 1: User submits task
The user gives a natural-language task description in the agent UI. The user is unaware of the harness.

### Step 2: Planning agent invokes harness
The planning agent calls the hidden wrapper:

```powershell
Scripts\invoke-harness-plan.ps1 -TaskText "<USER_TASK>" -OutputDir "<SESSION_DIR>" -McpBaseUrl "<MCP_URL>" -ModelBaseUrl "<MODEL_URL>" -ModelName "<MODEL>"
```

This invocation is invisible to the user. The wrapper:
1. Locates the harness executable
2. Passes `--stdout-json true` (always)
3. Passes `--print-worker-packet false` (default)
4. Returns only the `HarnessRunManifest` JSON to stdout
5. Exits non-zero if `success=false`

### Step 3: Harness executes deterministic planning
The harness runs through these stages in strict order:

1. **Requirement interpretation** - LLM parses raw task into `RequirementIntent`
2. **Chunk compilation** - deterministic retrieval chunk generation
3. **Chunk quality gate** - validates chunk text length and protocol marker cleanliness
4. **MCP preflight** - calls `get_server_info`
5. **Primary retrieval** - calls `retrieve_memory_by_chunks`, `merge_retrieval_results`, `build_memory_context_pack`
6. **Evidence hydration** - fetches full knowledge item details
7. **Fallback retrieval** - optional targeted searches if primary retrieval missed critical items
8. **Execution plan synthesis** - LLM synthesizes the execution plan
9. **Plan validation** - validates hard constraints, detects forbidden worker behavior
10. **Worker packet generation** - emits the execution handoff

### Step 4: Harness returns `HarnessRunManifest`
The harness writes `12-harness-run-manifest.json` and prints it to stdout (when `--stdout-json true`).

**On success** (`success=true`):
- `nextAction` = `paste_worker_packet_into_execution_agent`
- `executionPlanMarkdownPath` = path to the execution plan markdown
- `workerPacketMarkdownPath` = path to the worker packet markdown
- The planning agent reads the worker packet and passes it to the execution agent

**On failure** (`success=false`):
- `nextAction` = `fix_errors_and_rerun_harness`
- `errors` = list of error messages
- The planning agent MUST stop and display the errors. It MUST NOT continue with speculative manual planning.

### Step 5: Planning agent presents the plan
The planning agent reads `executionPlanMarkdownPath` and presents the plan to the user in natural language.

### Step 6: Execution agent drives execution
The execution agent receives the worker packet (from `workerPacketMarkdownPath`) and executes the listed steps. It reports per-step results, deliverables, and a validation summary.

## Artifact Contract

All artifacts are written under `--output-dir`:

| Artifact | Purpose | When Written |
|---|---|---|
| `00-session.json` | Session metadata and artifact paths | Always |
| `01-raw-task.txt` | Raw task text | Always |
| `02-requirement-intent.json` | Structured requirement intent | On interpretation success |
| `03-retrieval-chunks.json` | Deterministic retrieval chunks | With `--emit-intermediates true` |
| `04-chunk-quality-report.json` | Chunk quality gate results | With `--emit-intermediates true` |
| `05-retrieve-memory-by-chunks.json` | MCP retrieve_memory_by_chunks response | With `--emit-intermediates true` |
| `06-merge-retrieval-results.json` | MCP merge_retrieval_results response | With `--emit-intermediates true` |
| `07-build-memory-context-pack.json` | MCP build_memory_context_pack response | With `--emit-intermediates true` |
| `08-planning-memory-summary.md` | Human-readable memory summary | Always |
| `09-execution-plan.json` | Execution plan JSON | On plan synthesis |
| `10-execution-plan.md` | Execution plan markdown | Always |
| `11-worker-packet.md` | Worker execution handoff packet | On plan validation success |
| `12-harness-run-manifest.json` | Canonical result manifest | Always |

## Hidden Wrapper Contract

`Scripts/invoke-harness-plan.ps1` is the single, stable invocation surface for the planning agent. It:

- Accepts `-TaskText`, `-OutputDir`, `-McpBaseUrl`, `-ModelBaseUrl`, `-ModelName`
- Accepts optional `-ApiKeyEnv` to override the API key environment variable
- Always passes `--stdout-json true` to the harness
- Returns only the manifest JSON to stdout
- Returns non-zero exit code on harness failure
- Emits no extra decorative output

The end user never calls this script directly. The planning agent calls it invisibly.

## Repo-Local Rules

The `.cursor/rules/` directory contains machine-followable rules that bind the agent to harness-first planning:

- `00-harness-planning.mdc` - Mandates harness invocation for non-trivial tasks
- `01-harness-failure.mdc` - Prohibits fallback manual planning on harness failure
- `02-harness-execution.mdc` - Prohibits worker-side independent memory retrieval

These rules are loaded by Cursor and govern agent behavior automatically.

## Why the End User Remains Unaware

The end user sees only:
1. A natural-language task description prompt
2. A presented execution plan
3. An executing agent

The harness invocation, artifact management, and worker packet handoff happen entirely behind the scenes. The user is never asked to run commands, read manifests, or understand the planning pipeline.

## Failure Behavior

When the harness fails (`success=false`):

1. The planning agent receives `HarnessRunManifest` with `nextAction=fix_errors_and_rerun_harness`
2. The planning agent stops immediately
3. The planning agent displays the harness error messages to the user
4. The user corrects the inputs (e.g., clarifies the task, fixes configuration)
5. The planning agent reruns the harness

The planning agent MUST NOT attempt speculative manual planning or continue with execution work when the harness has failed.

## Execution Handoff

The worker packet at `workerPacketMarkdownPath` (`11-worker-packet.md`) is the authoritative execution handoff. It contains:

- **AllowedScope** - permitted domains, modules, features, and layers
- **ForbiddenActions** - hard rules the execution agent must follow
- **HardConstraints** - non-negotiable constraints from the requirement intent
- **KeyMemory** - top-3 knowledge items per retrieval class, distilled into bullets
- **Steps** - ordered execution steps with acceptance checks
- **RequiredOutputSections** - what the execution agent must produce

The execution agent MUST operate strictly within the worker packet scope.
