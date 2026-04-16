# Claude Code Project Configuration

This file is automatically loaded by Claude Code and provides project-specific guidance.

---

## ⚠️ CONTROL PLANE ENFORCEMENT (READ FIRST)

**You are in a project that requires harness control-plane for all planning work.**

When you receive any task that involves:
- **Designing** anything (architecture, UI flow, database schema, API contracts)
- **Planning** an implementation (steps, decomposition, approach)
- **Deciding** how to implement or change something (strategy, design decisions)
- **Complex refactoring** or large feature work
- **Root cause analysis** or debugging strategy before a fix

**YOU MUST:**
1. **STOP immediately**
2. **DO NOT produce your own plan or design**
3. **Call harness:** `.\harness\invoke-harness-control-plane.ps1 start-session -RawTask "<task>"`
4. **Follow harness control-plane** (`harness/agent-rules/00-harness-control-plane.mdc`) until it returns `stage: complete`
5. **NEVER skip this.** There is no exception. There is no "quick design." There is no "I'll just outline the approach."

**If you are uncertain whether a task requires planning → assume it does and call harness.**

**The cost of over-planning (structured analysis for a clarification question) is minimal. The cost of under-planning (shipping a broken feature because you skipped structured planning) is catastrophic.**

---

## Harness Planning Mode

This project uses a **harness control-plane** for all non-trivial planning tasks. The harness enforces a structured planning flow with mandatory stages and validation.

### When to Use Harness

For any task that requires:
- Design or architecture decisions
- Multi-step implementation planning
- Deep reasoning about constraints and risks
- Retrieval of prior decisions from memory (MCP)

**Start with:** `harness/invoke-harness-control-plane.ps1 start-session -RawTask "<your task>"`

### Harness Planning Skills

Read these skills in order before starting any planning session:

**All skills are in:** `harness/agent-rules/`

| File | Purpose | Read First? |
|------|---------|---|
| `00-harness-control-plane.mdc` | Overall planning loop and mandatory stages | ✅ YES |
| `05-artifact-schemas-detailed.mdc` | Complete schemas for all artifacts you'll generate | ✅ YES |
| `04-harness-skill-activation.mdc` | When harness mode is active | Maybe |
| `03-harness-mcp-tool-calling.mdc` | How to call MCP tools (when harness instructs) | When needed |
| `01-harness-failure.mdc` | Error handling (read if harness returns an error) | When needed |
| `02-harness-execution.mdc` | Execution phase (after planning completes) | Later |

### Quick Start: Planning Flow

```powershell
# 1. Start a planning session
$response = .\harness\invoke-harness-control-plane.ps1 start-session -RawTask "Your task description"

# 2. VALIDATE the response (CRITICAL: do not skip this)
# If success == false OR nextAction == "stop_with_error", STOP and read errors array
if ($response.success -eq $false -or $response.nextAction -eq "stop_with_error") {
  Write-Output "Error: $($response.errors)"
  # Fix the issue and retry with a new start-session
}

# 3. Read nextAction from the validated response
# Example: nextAction: "agent_generate_requirement_intent"

# 4. Generate the requested artifact (use exact schema from 05-artifact-schemas-detailed.mdc)
# Example: Create requirement_intent.json

# 5. Submit it back to harness
$response = .\harness\invoke-harness-control-plane.ps1 submit-step-result `
    -SessionId "<sessionId>" `
    -Action "agent_generate_requirement_intent" `
    -ArtifactType "RequirementIntent" `
    -ArtifactFile "requirement_intent.json"

# 6. VALIDATE every response (same as step 2)
if ($response.success -eq $false -or $response.nextAction -eq "stop_with_error") {
  Write-Output "Error: $($response.errors)"
  # Fix and resubmit with the same -SessionId
}

# 7. Repeat steps 3-6 until harness returns "stage: complete"
```

### Artifact Schemas

**Before generating any artifact, read:** `harness/agent-rules/05-artifact-schemas-detailed.mdc`

This file contains:
- Complete JSON schemas for all 8 planning stages
- Field-by-field reference tables
- Coverage rules (when each chunk type is required)
- Three complete examples (low, medium, high complexity)
- Common mistakes to avoid

### Key Rules

**ALWAYS:**
- ✅ Call `start-session` before producing any artifact
- ✅ **VALIDATE every harness response before proceeding:** Check if `success == false` OR `nextAction == "stop_with_error"`. If either is true, STOP immediately.
- ✅ Read `nextAction` from harness before taking any action
- ✅ Submit every artifact to harness via `submit-step-result`
- ✅ Stop immediately if harness returns `nextAction: stop_with_error`
- ✅ Use exact JSON schemas from `05-artifact-schemas-detailed.mdc`
- ✅ Use PowerShell syntax: `-SessionId`, not `--session-id`
- ✅ Use snake_case in JSON: `task_id`, not `taskId`

**NEVER:**
- ❌ Generate artifacts without harness instruction
- ❌ Call MCP before harness tells you to
- ❌ Skip stages or batch multiple artifacts
- ❌ Continue after `stop_with_error` without fixing the error
- ❌ Modify the harness wrapper script
- ❌ Do implementation work during planning mode

### MCP Tool Calling

When harness returns `nextAction` as one of:
- `agent_call_mcp_retrieve_memory_by_chunks`
- `agent_call_mcp_merge_retrieval_results`
- `agent_call_mcp_build_memory_context_pack`

**Procedure:**
1. Extract `toolName` and `payload.request` from harness response
2. Call the MCP tool with that exact request (do not modify)
3. Write the **RAW MCP response** to a file
4. Submit it back to harness via `submit-step-result`

**Read:** `harness/agent-rules/03-harness-mcp-tool-calling.mdc` for details.

### Error Handling

If harness returns `success: false` or `nextAction: stop_with_error`:

1. **DO NOT continue** to the next stage
2. **Read the errors array verbatim** — show it to the user exactly
3. **Fix ONLY the fields identified** in the errors array
4. **Resubmit the same stage** using the same session ID
5. **Check session status** with `get-session-status` if needed

**Read:** `harness/agent-rules/01-harness-failure.mdc` for full error recovery procedure.

### Execution Phase

After harness returns `stage: complete`:
- Extract `completionArtifacts` (ExecutionPlan and WorkerExecutionPacket)
- Present them to the user
- Only proceed to implementation if the user explicitly requests it
- During execution, use `harness/agent-rules/02-harness-execution.mdc` for rules

---

## Harness Deployment Files

| File | Purpose |
|------|---------|
| `harness/invoke-harness-control-plane.ps1` | Wrapper script (call this from Claude) |
| `harness/HarnessMcp.ControlPlane.exe` | Control-plane executable |
| `harness/agent-rules/` | All agent skills (*.mdc files) |
| `harness/docs/` | Protocol and workflow documentation |

### Environment Setup

Before calling the harness for the first time:

```powershell
# Option 1: Source the setup script
. .\harness\setup-harness-env.ps1

# Option 2: Manually set the executable path
$env:HARNESS_EXE_PATH = "$PWD\harness\HarnessMcp.ControlPlane.exe"
```

---

## Project Structure

```
.
├── CLAUDE.md                                ← You are here
├── harness/
│   ├── agent-rules/                         ← All skills (for Claude, Cursor, any agent)
│   │   ├── 00-harness-control-plane.mdc
│   │   ├── 01-harness-failure.mdc
│   │   ├── 02-harness-execution.mdc
│   │   ├── 03-harness-mcp-tool-calling.mdc
│   │   ├── 04-harness-skill-activation.mdc
│   │   └── 05-artifact-schemas-detailed.mdc ← COMPLETE SCHEMAS (use this!)
│   ├── docs/
│   │   ├── harness_control_plane_protocol.md
│   │   └── agent_controlled_planning_flow.md
│   ├── invoke-harness-control-plane.ps1     ← Wrapper (call this)
│   ├── HarnessMcp.ControlPlane.exe           ← Executable
│   ├── setup-harness-env.ps1                 ← Setup script
│   └── SKILLS_ORGANIZATION.md
├── docs/
│   ├── harness_design.md
│   └── ...
└── src/
    └── ...
```

---

## Common Tasks

### Start a Planning Session

```powershell
# Set up environment (first time only)
. .\harness\setup-harness-env.ps1

# Start session
.\harness\invoke-harness-control-plane.ps1 start-session `
    -RawTask "Add JWT authentication without breaking existing API"
```

### Submit an Artifact

```powershell
.\harness\invoke-harness-control-plane.ps1 submit-step-result `
    -SessionId "sess-001" `
    -Action "agent_generate_requirement_intent" `
    -ArtifactType "RequirementIntent" `
    -ArtifactFile "requirement_intent.json"
```

### Check Session Status

```powershell
.\harness\invoke-harness-control-plane.ps1 get-session-status -SessionId "sess-001"
```

### Resume a Session

```powershell
.\harness\invoke-harness-control-plane.ps1 get-next-step -SessionId "sess-001"
```

### Cancel a Session

```powershell
.\harness\invoke-harness-control-plane.ps1 cancel-session -SessionId "sess-001"
```

---

## Debugging

### If the harness wrapper fails

1. Verify environment variable is set:
   ```powershell
   echo $env:HARNESS_EXE_PATH
   ```

2. Verify the executable exists:
   ```powershell
   ls .\harness\HarnessMcp.ControlPlane.exe
   ```

3. Try sourcing the setup script:
   ```powershell
   . .\harness\setup-harness-env.ps1
   ```

### If an artifact is rejected

1. Read the `errors` array from harness response **verbatim**
2. Open `harness/agent-rules/05-artifact-schemas-detailed.mdc`
3. Find your artifact type (e.g., RetrievalChunkSet)
4. Compare your JSON against the exact schema shown
5. Fix ONLY the fields mentioned in the errors array
6. Resubmit with the same session ID and action name

### If MCP calls fail

1. Verify you're at an MCP-stage (harness `nextAction` starts with `agent_call_mcp_`)
2. Verify `payload.request` is valid (don't modify it)
3. Check that you're passing the exact tool name from `toolName` field
4. Ensure you submit the **raw, unmodified** MCP response back to harness

---

## Related Documentation

- `harness/SKILLS_ORGANIZATION.md` — Why skills are in `agent-rules/`
- `harness/CURSOR_FIXES.md` — Issues Cursor had (and how they're fixed)
- `docs/harness_design.md` — Overall architecture
- `harness/docs/harness_control_plane_protocol.md` — Protocol details

---

## For Cursor Users

If you're also using Cursor, you have two deployment mechanisms:
- **Cursor**: Reads skills from `.cursor/rules/` automatically
- **Claude**: Reads skills from this `CLAUDE.md` file + `harness/agent-rules/`

Both reference the same skill files in `harness/agent-rules/` (agent-agnostic location).

---

**Last Updated:** 2026-04-16  
**Status:** Ready for Claude Code
