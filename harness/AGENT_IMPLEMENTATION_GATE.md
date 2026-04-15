# Agent Implementation Gate

This file tells the coding agent that the harness implementation is NOT complete until specific tests pass.

## Architecture Overview

This repository implements the **Harness Control-Plane-Only Design**:
- The **agent** (you) does the actual work
- The **skills** (in `.cursor/rules/`) define the semantic flow
- The **harness** (`HarnessMcp.ControlPlane`) is control-plane only
- The harness does NOT call LLM APIs or MCP itself
- The harness provides a single strict entrypoint and deterministic protocol

## Implementation Status

### Required Tests

All tests under `tests/HarnessMcp.ControlPlane.Tests/` MUST pass:
- Protocol validation tests
- Validator tests
- Skill + harness flow tests
- Wrapper script tests

### The Implementation is NOT Complete Until:

1. All tests in `tests/HarnessMcp.ControlPlane.Tests/` pass
2. The skills contain concrete operational instructions
3. Validators reject non-canonical artifacts
4. No path exists to `HarnessMcp.AgentClient`
5. Wrapper resolves only to `HarnessMcp.ControlPlane`

### Do NOT Bypass Tests

DO NOT weaken validators to make tests pass.
DO NOT skip test execution.
DO NOT modify tests to accommodate broken implementation.
The tests define the contract - implementation must satisfy them.

## What This Means For You

When implementing tasks in this repository:

1. You MUST use `Scripts\invoke-harness-control-plane.ps1` as the harness entrypoint
2. You MUST follow the stage order defined in the harness protocol
3. You MUST NOT call MCP before harness instructs at that stage
4. You MUST NOT generate execution plan before harness reaches that stage
5. You MUST stop immediately on harness error and fix only the indicated artifact
6. The skills in `.cursor/rules/` are part of production behavior

## Final Verification Checklist

Before accepting implementation as complete:

- [ ] No active path references `HarnessMcp.AgentClient`
- [ ] Wrapper points only to `HarnessMcp.ControlPlane`
- [ ] Skills are detailed enough for real-life use
- [ ] Harness returns strict canonical MCP-stage contracts with `toolName` and `payload.request`
- [ ] Validators reject non-canonical artifacts
- [ ] Tests prove both protocol progression AND skills + harness together enforce the flow
- [ ] All tests pass