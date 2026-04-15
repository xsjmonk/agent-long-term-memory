# How to Deploy the Harness to a Target Project

This guide explains how to deploy the harness planning loop into any project so a generic agent (Cursor, Claude Code, or any planning-capable agent) can use it.

---

## What Gets Deployed and Where

There are **three distinct pieces**, each with a different destination.

```
<your-project>/
    .cursor/
        rules/                                  ← agent reads these at startup
            00-harness-control-plane.mdc
            01-harness-failure.mdc
            02-harness-execution.mdc
            03-harness-mcp-tool-calling.mdc
            04-harness-skill-activation.mdc
    harness/
        invoke-harness-control-plane.ps1        ← agent calls this as a shell command
        HarnessMcp.ControlPlane.exe             ← wrapper calls this; must be in the same folder
    .harness/
        sessions/                               ← created automatically at runtime; do not edit
```

**The exe and wrapper script always go together in the same folder.** They do not belong in `.cursor/rules/`. The skill files do not go next to the exe.

| Piece | Consumer | Destination |
|---|---|---|
| `*.mdc` skill files | Agent reads at startup | `.cursor/rules/` |
| `invoke-harness-control-plane.ps1` | Agent runs as a shell command | `harness/` (or any folder) |
| `HarnessMcp.ControlPlane.exe` | Wrapper script calls it | Same folder as the wrapper |

Session state is written automatically to `.harness/sessions/` inside your project when the wrapper runs.

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) installed
- PowerShell 7+
- Cursor or Claude Code as the agent

---

## Step 1 — Build the Executable

Run this once from the `harness` repository root:

```powershell
dotnet build src\HarnessMcp.ControlPlane
```

The output is at:

```
harness\src\HarnessMcp.ControlPlane\bin\Debug\net8.0\HarnessMcp.ControlPlane.exe
```

---

## Step 2 — Copy Files into Your Target Project

### 2a — Skill files → `.cursor\rules\`

Create the folder if it does not exist, then copy all five `.mdc` files:

```
harness\.cursor\rules\00-harness-control-plane.mdc   →  <your-project>\.cursor\rules\
harness\.cursor\rules\01-harness-failure.mdc          →  <your-project>\.cursor\rules\
harness\.cursor\rules\02-harness-execution.mdc        →  <your-project>\.cursor\rules\
harness\.cursor\rules\03-harness-mcp-tool-calling.mdc →  <your-project>\.cursor\rules\
harness\.cursor\rules\04-harness-skill-activation.mdc →  <your-project>\.cursor\rules\
```

Cursor picks up `.mdc` files from `.cursor\rules\` automatically when the project opens.

### 2b — Wrapper + exe → a `harness\` folder inside your project

Copy these two files **together into the same folder**:

```
harness\Scripts\invoke-harness-control-plane.ps1                        →  <your-project>\harness\
harness\src\HarnessMcp.ControlPlane\bin\Debug\net8.0\HarnessMcp.ControlPlane.exe  →  <your-project>\harness\
```

The wrapper script is pre-configured to look for the exe next to itself when `HARNESS_EXE_PATH` is set. Set that variable once so the auto-discovery works from the new location:

```powershell
$env:HARNESS_EXE_PATH = "$PSScriptRoot\HarnessMcp.ControlPlane.exe"
```

The simplest way to make this permanent is to add a one-liner at the top of the wrapper, or to set the variable in your terminal profile, or to document it in `CLAUDE.md` so the agent sets it before calling the script.

Your project layout will look like this after both steps:

```
<your-project>\
    .cursor\
        rules\
            00-harness-control-plane.mdc
            01-harness-failure.mdc
            02-harness-execution.mdc
            03-harness-mcp-tool-calling.mdc
            04-harness-skill-activation.mdc
    harness\
        invoke-harness-control-plane.ps1
        HarnessMcp.ControlPlane.exe
```

---

## Step 3 — Tell the Agent Where the Wrapper Is

Add this to your project's `CLAUDE.md` or `.cursorrules`:

```
The harness control-plane wrapper is at: harness\invoke-harness-control-plane.ps1
Run it with PowerShell from the project root.
Before calling it, set: $env:HARNESS_EXE_PATH = "harness\HarnessMcp.ControlPlane.exe"
See the inline comments at the top of the script for the full command reference.
```

If you have no `CLAUDE.md`, tell the agent directly when starting a planning session:

> "The harness wrapper is at `harness\invoke-harness-control-plane.ps1`. The exe is in the same folder. Set `HARNESS_EXE_PATH` to `harness\HarnessMcp.ControlPlane.exe` before calling it."

---

## Step 4 — Smoke Test

Verify the setup works before involving the agent. Run from your **target project's root**:

```powershell
# Point the wrapper at the local exe
$env:HARNESS_EXE_PATH = "harness\HarnessMcp.ControlPlane.exe"

# Start a session
$r = (& "harness\invoke-harness-control-plane.ps1" `
        start-session --raw-task "Design the migration") | ConvertFrom-Json

$r.sessionId   # a unique session ID — save this
$r.stage       # should be: need_requirement_intent
$r.nextAction  # should be: agent_generate_requirement_intent

# Confirm session is readable
& "harness\invoke-harness-control-plane.ps1" get-session-status --session-id $r.sessionId
```

A correct first response looks like:

```json
{
  "success": true,
  "sessionId": "ses-...",
  "stage": "need_requirement_intent",
  "nextAction": "agent_generate_requirement_intent",
  "instructions": [
    "Convert the raw task into RequirementIntent JSON.",
    "Do not query MCP yet.",
    "Do not generate the plan yet."
  ]
}
```

---

## Step 5 — Test with the Agent

Open your target project in Cursor (or start a Claude Code session). Give the agent a planning-intent request:

> "How should we approach refactoring the auth module?"

The agent will:

1. Detect planning intent via `04-harness-skill-activation.mdc`
2. Activate the harness loop via `00-harness-control-plane.mdc`
3. Call `invoke-harness-control-plane.ps1 start-session --raw-task "..."` → receive `need_requirement_intent`
4. Generate a `RequirementIntent` JSON artifact, write it to a temp file
5. Call `invoke-harness-control-plane.ps1 submit-step-result --session-id ... --action agent_generate_requirement_intent --artifact-type RequirementIntent --artifact-file ./artifact.json`
6. Read the next stage from the response and continue
7. Repeat through all stages until `stage: complete`
8. Present the `ExecutionPlan` and `WorkerExecutionPacket` from the completion response

---

## Wrapper Command Reference

```powershell
# Start a new planning session
.\harness\invoke-harness-control-plane.ps1 start-session `
    --raw-task  "Describe the task here"   # required
    --project   "my-project"               # optional

# Submit the result of a completed stage
.\harness\invoke-harness-control-plane.ps1 submit-step-result `
    --session-id    <id>                   # required
    --action        <action-name>          # required  e.g. agent_generate_requirement_intent
    --artifact-type <type>                 # required  e.g. RequirementIntent
    --artifact-file <path-to-json-file>    # required

# Get the next required action for an in-progress session
.\harness\invoke-harness-control-plane.ps1 get-next-step `
    --session-id <id>

# Get full status (stage, accepted artifacts, errors)
.\harness\invoke-harness-control-plane.ps1 get-session-status `
    --session-id <id>

# Cancel a session
.\harness\invoke-harness-control-plane.ps1 cancel-session `
    --session-id <id>
```

---

## Session Storage

Sessions are stored as JSON files in `.harness\sessions\` inside your project, created automatically the first time you call `start-session`. They persist across agent restarts. If a session is interrupted mid-loop, the agent can resume by calling `get-next-step` with the saved session ID.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `ControlPlane executable not found` | `HARNESS_EXE_PATH` not set and exe not in the expected path | Set `$env:HARNESS_EXE_PATH = "harness\HarnessMcp.ControlPlane.exe"` |
| `Expected action 'X', got 'Y'` | Agent submitted the wrong action for the current stage | Call `get-next-step` to read the correct expected action, then resubmit |
| `Session is in error state` | A previous submit failed validation | Call `get-session-status` to read the errors; fix the artifact or cancel and restart |
| Agent skips the harness entirely | Skill files not loaded | Confirm all 5 `.mdc` files are in `.cursor\rules\` and reopen the project in Cursor |
| Skill files load but agent does not activate | Request phrased as execution, not planning | Use planning-intent language: "design", "approach", "how should we", "plan the…" |
