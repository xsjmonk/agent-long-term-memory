# Harness Setup Checklist

Quick reference for setting up the harness with Cursor, Claude, or both.

---

## ✅ What's Already Done

The harness is **ready to use right now**:

- [x] Skills are in `harness/agent-rules/` (agent-agnostic, single source)
- [x] No IDE-specific directories (`.cursor/`, `.idea/`, etc.)
- [x] `CLAUDE.md` created for Claude Code
- [x] Detailed schema file created (`05-artifact-schemas-detailed.mdc`)
- [x] All issues documented and fixed (CURSOR_FIXES.md)
- [x] Deployment guide created (DEPLOYMENT.md)
- [x] Skills organization documented (SKILLS_ORGANIZATION.md)

---

## For Cursor Users

### One-Time Setup

```bash
# Option 1: Copy skills to .cursor/rules/
mkdir -p .cursor/rules/
cp harness/agent-rules/*.mdc .cursor/rules/
```

Or on systems that support symlinks:

```bash
# Option 2: Create symlinks
mkdir -p .cursor/rules/
cd .cursor/rules/
ln -s ../harness/agent-rules/*.mdc .
cd ../..
```

### Verify

```bash
# Check that .cursor/rules/ has all 6 .mdc files
ls .cursor/rules/
# Should see:
# 00-harness-control-plane.mdc
# 01-harness-failure.mdc
# ... (all 6 files)
```

### Done!

Cursor will auto-load the skills at startup. You're ready to use the harness.

---

## For Claude Code Users

### One-Time Setup

**No setup needed!** `CLAUDE.md` is already in the project root.

Claude Code will auto-load it at startup.

### Verify

```bash
# Check that CLAUDE.md exists in project root
ls -la CLAUDE.md
# Should output the file info
```

### Done!

Claude will load CLAUDE.md at startup and know where the skills are. You're ready to use the harness.

---

## For Both Cursor and Claude

### Step 1: Set Up Cursor
Follow "For Cursor Users" above.

### Step 2: Set Up Claude
No additional setup (CLAUDE.md is already there).

### Step 3: Keep Them in Sync

If you later modify a skill in `harness/agent-rules/`:

**Cursor:**
```bash
# Copy updated file to .cursor/rules/
cp harness/agent-rules/05-artifact-schemas-detailed.mdc .cursor/rules/
```

**Claude:**
```bash
# No action needed — Claude reads from harness/agent-rules/ directly
```

---

## Quick Start: Using the Harness

Once setup is complete, here's how to start a planning session:

### 1. Open PowerShell
```powershell
# From project root
cd C:\Docs\Hackthon\2026\
```

### 2. Set Up Environment (First Time)
```powershell
. .\harness\setup-harness-env.ps1
```

### 3. Start Planning Session
```powershell
.\harness\invoke-harness-control-plane.ps1 start-session `
    -RawTask "Add JWT authentication without breaking existing API"
```

### 4. Read the Response
The harness will return `nextAction` (e.g., `agent_generate_requirement_intent`).

### 5. Generate Artifact
Create the requested artifact using the exact schema from:
```
harness/agent-rules/05-artifact-schemas-detailed.mdc
```

### 6. Submit Result
```powershell
.\harness\invoke-harness-control-plane.ps1 submit-step-result `
    -SessionId "<sessionId from step 3>" `
    -Action "agent_generate_requirement_intent" `
    -ArtifactType "RequirementIntent" `
    -ArtifactFile "requirement_intent.json"
```

### 7. Repeat
Go back to Step 4 and repeat until harness returns `stage: complete`.

---

## File Locations

### Skills (Single Source)
```
harness/agent-rules/
├── 00-harness-control-plane.mdc        ← Start here
├── 01-harness-failure.mdc
├── 02-harness-execution.mdc
├── 03-harness-mcp-tool-calling.mdc
├── 04-harness-skill-activation.mdc
└── 05-artifact-schemas-detailed.mdc    ← COMPLETE SCHEMAS
```

### Cursor Deployment (Optional)
```
.cursor/
└── rules/                               ← Copy of skills
    └── (all 6 .mdc files)
```

### Claude Deployment (Auto)
```
CLAUDE.md                                ← Claude reads this
```

### Configuration Files
```
CLAUDE.md                   ← Claude startup config (REQUIRED)
DEPLOYMENT.md               ← Cursor vs Claude comparison
SETUP_CHECKLIST.md          ← This file
harness/SKILLS_ORGANIZATION.md
harness/CURSOR_FIXES.md
```

### Harness Executables
```
harness/
├── invoke-harness-control-plane.ps1    ← Wrapper (call this)
├── HarnessMcp.ControlPlane.exe          ← Control plane
└── setup-harness-env.ps1                ← Setup script
```

---

## Troubleshooting

### Cursor doesn't load skills

**Check:**
```bash
ls -la .cursor/rules/
```

Should see 6 `.mdc` files.

**Fix:** Recopy from `harness/agent-rules/`:
```bash
cp harness/agent-rules/*.mdc .cursor/rules/
```

### Claude doesn't find CLAUDE.md

**Check:**
```bash
ls -la CLAUDE.md
```

File should exist in project root.

**Fix:** Ensure file is in correct location:
```bash
ls -la C:\Docs\Hackthon\2026\CLAUDE.md
```

### Harness wrapper fails

**Check environment:**
```powershell
echo $env:HARNESS_EXE_PATH
ls .\harness\HarnessMcp.ControlPlane.exe
```

**Fix: Source setup script:**
```powershell
. .\harness\setup-harness-env.ps1
```

### Harness rejects artifact

**Check:**
1. Open `harness/agent-rules/05-artifact-schemas-detailed.mdc`
2. Find your artifact type (e.g., RetrievalChunkSet)
3. Compare your JSON exactly against the schema shown
4. Fix ONLY the fields mentioned in harness errors array

**Read error handling:** `harness/agent-rules/01-harness-failure.mdc`

---

## Key Files to Know

| File | Read When | Purpose |
|------|-----------|---------|
| `CLAUDE.md` | Starting planning | Quick reference for Claude |
| `DEPLOYMENT.md` | Setting up agents | Understand Cursor vs Claude |
| `harness/agent-rules/00-harness-control-plane.mdc` | Starting planning | Overall planning loop |
| `harness/agent-rules/05-artifact-schemas-detailed.mdc` | Generating artifact | Exact schema + examples |
| `harness/agent-rules/01-harness-failure.mdc` | Harness returns error | Error recovery |
| `harness/agent-rules/03-harness-mcp-tool-calling.mdc` | Calling MCP tools | MCP stage rules |
| `harness/CURSOR_FIXES.md` | Understanding schema issues | Why fields are required |

---

## Common Commands

### Start planning session
```powershell
.\harness\invoke-harness-control-plane.ps1 start-session -RawTask "<task>"
```

### Submit artifact
```powershell
.\harness\invoke-harness-control-plane.ps1 submit-step-result `
    -SessionId "<id>" -Action "<action>" -ArtifactType "<type>" -ArtifactFile "<file>"
```

### Check session status
```powershell
.\harness\invoke-harness-control-plane.ps1 get-session-status -SessionId "<id>"
```

### Get next step (resume)
```powershell
.\harness\invoke-harness-control-plane.ps1 get-next-step -SessionId "<id>"
```

### Cancel session
```powershell
.\harness\invoke-harness-control-plane.ps1 cancel-session -SessionId "<id>"
```

---

## Status: ✅ Ready to Use

- [x] Skills in `harness/agent-rules/` (agent-agnostic)
- [x] CLAUDE.md created (Claude auto-loads)
- [x] Documentation complete
- [x] All issues fixed and documented
- [x] No additional setup needed for Claude
- [x] Copy-to-.cursor/rules/ optional for Cursor users
- [x] Ready for both agents to use harness

**No further work needed. You can start using the harness now.**

---

**Created:** 2026-04-16  
**For:** Cursor, Claude, or both  
**Status:** ✅ Complete
