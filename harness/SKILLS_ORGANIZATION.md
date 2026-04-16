# Skills Source Code Organization

## Central Location

All harness-related skill files are maintained in a **single, centralized, agent-agnostic location**:

```
harness/agent-rules/
```

This is the **authoritative source** for all skills and rules. The location is generic and not IDE-specific, so it works equally well for Claude, Cursor, or any other planning-capable agent.

### Current Skills

| File | Purpose |
|------|---------|
| `00-harness-control-plane.mdc` | Core harness control plane skill — defines when harness must be invoked for non-trivial tasks |
| `01-harness-failure.mdc` | Hard-stop failure handling — mandatory error recovery playbook |
| `02-harness-execution.mdc` | Execution phase constraints — no independent memory retrieval, no replanning |
| `03-harness-mcp-tool-calling.mdc` | MCP tool calling — exact tool names, exact payloads, raw response submission |
| `04-harness-skill-activation.mdc` | Skill activation rules — when each skill applies |

## Why This Location?

### Problems Solved

1. **No Duplication**: Single source of truth eliminates inconsistencies
2. **Easier Maintenance**: Changes to a skill only happen in one place
3. **Agent-Agnostic**: `harness/agent-rules/` is not tied to any specific IDE or tool
4. **Harness Design Alignment**: Per `harness_design.md`, skills define the semantic flow and agent behavior envelope

### Integration with Harness Design

From `harness_design.md` section 11:

> Skills/rules remain the semantic driver. They must instruct the agent to:
> 1. call harness for non-trivial tasks,
> 2. obey `nextAction`,
> 3. produce only the requested artifact type,
> 4. call MCP only when harness says so,
> 5. submit every result back to harness before continuing,
> 6. stop if harness returns `stop_with_error`.

All skills in `harness/agent-rules/` enforce this contract.

## Deleted Duplicates & Old Locations

**Removed duplicate files** (were identical to central versions):
- `harness/01-harness-failure.mdc` ❌ REMOVED
- `harness/02-harness-execution.mdc` ❌ REMOVED  
- `harness/03-harness-mcp-tool-calling.mdc` ❌ REMOVED

**Removed IDE-specific directory**:
- `harness/.cursor/` ❌ REMOVED — not agent-agnostic

**Do not recreate skill files in any other location.** If you need to update a skill, edit it in `harness/agent-rules/` only.

## Related Documentation

- `harness_design.md` — Overall control-plane architecture
- `harness/docs/harness_control_plane_protocol.md` — Protocol specification
- `harness/docs/agent_controlled_planning_flow.md` — Agent workflow

## For Developers

When working with the harness:

1. **Always reference skills from** `harness/agent-rules/`
2. **Do not create skill files** outside this directory
3. **Update skills centrally** — one change affects all agents (Claude, Cursor, etc.)
4. **Document skill changes** — explain why the skill rule changed
5. **Agent-agnostic location** — no IDE-specific directories (no `.cursor/`, `.idea/`, etc.)

## IDE Integration

Each IDE can be configured to read skills from `harness/agent-rules/`:

**For Cursor**: Configure in `.cursor/composer_config.json` or settings to load rules from `harness/agent-rules/`

**For Claude Code**: Reference skills from `harness/agent-rules/` in system prompts or agent instructions

**For any agent**: Load skills from `harness/agent-rules/` as part of the planning initialization

---

**Last Updated**: 2026-04-16  
**Organization Status**: ✅ Consolidated in agent-agnostic location
