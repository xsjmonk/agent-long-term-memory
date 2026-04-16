# Harness Deployment — Cursor vs Claude

This document explains how the harness is deployed for different agents.

---

## Deployment Architecture

The harness uses a **centralized, agent-agnostic skill library** that can be deployed to any agent using agent-specific mechanisms.

```
harness/agent-rules/                    ← Agent-agnostic skill source
├── 00-harness-control-plane.mdc
├── 01-harness-failure.mdc
├── 02-harness-execution.mdc
├── 03-harness-mcp-tool-calling.mdc
├── 04-harness-skill-activation.mdc
└── 05-artifact-schemas-detailed.mdc
```

This single source is deployed to agents via their native mechanisms:

---

## Cursor Deployment

Cursor has a built-in mechanism to load agent rules from `.cursor/rules/` at startup.

### Deployment for Cursor

```
my_project/
├── .cursor/
│   ├── composer.json
│   └── rules/                              ← Cursor reads these at startup
│       ├── 00-harness-control-plane.mdc
│       ├── 01-harness-failure.mdc
│       ├── 02-harness-execution.mdc
│       ├── 03-harness-mcp-tool-calling.mdc
│       ├── 04-harness-skill-activation.mdc
│       └── 05-artifact-schemas-detailed.mdc
├── harness/
│   ├── invoke-harness-control-plane.ps1    ← Cursor calls this as shell command
│   ├── HarnessMcp.ControlPlane.exe
│   └── agent-rules/                        ← Source of truth (also copied to .cursor/rules/)
└── ...
```

### How It Works in Cursor

1. Cursor starts and reads `.cursor/rules/*.mdc` automatically
2. User requests planning mode
3. Cursor invokes the harness wrapper via PowerShell
4. Cursor generates artifacts following the skill guidance
5. Cursor submits artifacts back to harness via wrapper

### Setup for Cursor

```bash
# Copy skills from source to .cursor/rules/
cp harness/agent-rules/*.mdc .cursor/rules/

# Or create symlinks (on supported systems)
ln -s harness/agent-rules/*.mdc .cursor/rules/
```

---

## Claude Deployment

Claude Code (the IDE integration) doesn't have a `.cursor/` directory. Instead, it loads configuration from `CLAUDE.md` in the project root.

### Deployment for Claude

```
my_project/
├── CLAUDE.md                                ← Claude reads this at startup
│   └── (contains guidance + links to skills)
├── harness/
│   ├── invoke-harness-control-plane.ps1    ← Claude calls this as shell command
│   ├── HarnessMcp.ControlPlane.exe
│   └── agent-rules/                        ← Skill files (Claude references these)
│       ├── 00-harness-control-plane.mdc
│       ├── 01-harness-failure.mdc
│       ├── 02-harness-execution.mdc
│       ├── 03-harness-mcp-tool-calling.mdc
│       ├── 04-harness-skill-activation.mdc
│       └── 05-artifact-schemas-detailed.mdc
└── ...
```

### How It Works in Claude

1. Claude Code starts and reads `CLAUDE.md` automatically
2. `CLAUDE.md` tells Claude where the skills are (`harness/agent-rules/`)
3. User requests planning mode
4. Claude reads the relevant skill files from disk
5. Claude invokes the harness wrapper via PowerShell
6. Claude generates artifacts following the skill guidance
7. Claude submits artifacts back to harness via wrapper

### Setup for Claude

```powershell
# No special setup needed!
# Claude Code will:
# 1. Read CLAUDE.md automatically
# 2. Load skills from harness/agent-rules/ as needed
# 3. Have access to wrapper script at harness/invoke-harness-control-plane.ps1
```

The `CLAUDE.md` file contains:
- Project overview and harness explanation
- Quick start guide
- Links to skill files with descriptions
- Common tasks and debugging
- Environment setup instructions

---

## Comparison

| Aspect | Cursor | Claude |
|--------|--------|--------|
| **Config File** | `.cursor/rules/` directory | `CLAUDE.md` file |
| **How Skills Load** | Automatic at startup (reads *.mdc files) | Manual reading from disk (referenced in CLAUDE.md) |
| **Setup Required** | Copy/symlink skills to `.cursor/rules/` | No setup (CLAUDE.md is auto-loaded) |
| **Wrapper Location** | `harness/invoke-harness-control-plane.ps1` | Same: `harness/invoke-harness-control-plane.ps1` |
| **Single Source** | `harness/agent-rules/` | Same: `harness/agent-rules/` |
| **Skill Format** | `.mdc` files | `.mdc` files (same format) |

---

## Why This Design

### Agent-Agnostic Source

The skills live in `harness/agent-rules/` (not `.cursor/rules/`) because:
1. **Single source of truth** — All agents reference the same files
2. **No duplication** — Changes to skills only happen once
3. **Future-proof** — Works for Claude, Cursor, and any agent that can read files
4. **IDE-agnostic** — No `.cursor/` or `.idea/` or `.vscode/` pollution

### Agent-Specific Deployment

Each agent loads skills via its native mechanism:
- **Cursor**: `.cursor/rules/` (built-in rule loading)
- **Claude**: `CLAUDE.md` (built-in config file)
- **Future agents**: Their native mechanism (e.g., `.agent/rules/`)

### Wrapper Script is Shared

Both Cursor and Claude invoke the same wrapper:
```
harness/invoke-harness-control-plane.ps1
```

This PowerShell script:
- Takes the same parameters for both agents
- Invokes the same executable
- Works identically for both

---

## Setting Up Both Cursor and Claude

If you want both agents to work with the harness:

### Step 1: Maintain Single Source
Keep skills only in `harness/agent-rules/`:
```bash
harness/
├── agent-rules/                 ← Single source of truth
│   ├── 00-harness-control-plane.mdc
│   ├── 01-harness-failure.mdc
│   ├── ... (all 6 skills)
```

### Step 2: Set Up Cursor (One-Time)
```bash
# Copy to Cursor's directory
cp harness/agent-rules/*.mdc .cursor/rules/
```

### Step 3: Set Up Claude (One-Time)
```bash
# Already done! CLAUDE.md references harness/agent-rules/
# No additional setup needed
```

### Step 4: Keep Them in Sync
If you update a skill in `harness/agent-rules/`:
- **For Claude**: Changes take effect immediately (it reads from `agent-rules/`)
- **For Cursor**: You need to recopy to `.cursor/rules/` (or use symlinks on supported systems)

Recommendation: Use a setup script or CI/CD to keep them in sync:
```powershell
# setup-agents.ps1
Copy-Item -Path "harness/agent-rules/*.mdc" `
          -Destination ".cursor/rules/" `
          -Force
```

---

## File Structure Reference

### Current State (Ready for Both)

```
project/
├── CLAUDE.md                                ← Claude loads this
├── DEPLOYMENT.md                           ← This file
│
├── harness/
│   ├── agent-rules/                        ← Single source of truth
│   │   ├── 00-harness-control-plane.mdc
│   │   ├── 01-harness-failure.mdc
│   │   ├── 02-harness-execution.mdc
│   │   ├── 03-harness-mcp-tool-calling.mdc
│   │   ├── 04-harness-skill-activation.mdc
│   │   └── 05-artifact-schemas-detailed.mdc
│   ├── docs/
│   ├── invoke-harness-control-plane.ps1
│   ├── HarnessMcp.ControlPlane.exe
│   ├── setup-harness-env.ps1
│   ├── SKILLS_ORGANIZATION.md
│   └── CURSOR_FIXES.md
│
├── .cursor/ (optional, for Cursor users)
│   └── rules/                              ← Copy of agent-rules/ (Cursor reads this)
│       └── (same .mdc files)
│
└── docs/
    ├── harness_design.md
    └── ...
```

---

## For New Agents

To support a new agent (e.g., "MyAI"):

1. **Don't create a new skill directory** — use `harness/agent-rules/`
2. **Create your agent's config file** — e.g., `MYAI.md` (similar to CLAUDE.md)
3. **Have your agent read the config file** at startup
4. **Reference the skills** from `harness/agent-rules/` in your config file
5. **Both agents read the same skills** — no duplication

Example `MYAI.md`:
```markdown
# MyAI Project Configuration

## Harness Planning

Read these skills from `harness/agent-rules/`:
- 00-harness-control-plane.mdc
- 05-artifact-schemas-detailed.mdc
- ... (etc)
```

---

## Summary

| Deployment | For | How | Source |
|---|---|---|---|
| **Cursor** | `.cursor/rules/` | Auto-loaded at startup | `harness/agent-rules/` (copied) |
| **Claude** | `CLAUDE.md` | Read on startup + manual reference | `harness/agent-rules/` (referenced) |
| **Future Agent** | `AGENT.md` | Read on startup + manual reference | `harness/agent-rules/` (referenced) |

**Key:** All agents read from the same source: `harness/agent-rules/`

---

**Created:** 2026-04-16  
**Status:** Deployed for both Cursor and Claude
