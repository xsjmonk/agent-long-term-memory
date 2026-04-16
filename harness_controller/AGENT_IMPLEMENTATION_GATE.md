# Agent Implementation Gate

**You may NOT claim this implementation is complete until every test passes.**

This is a hard gate. Passing tests are the only acceptable evidence of a correct implementation.
Weakening or deleting tests to make them pass is FORBIDDEN.

---

## Required Test Command

Run all tests from the repository root:

```
dotnet test tests/HarnessMcp.ControlPlane.Tests/HarnessMcp.ControlPlane.Tests.csproj
```

All tests must pass with exit code 0. Any failure blocks completion.

---

## What the Tests Verify

### Skill File Existence and Canonical Naming (`RuleContentTests`)

- All five canonical `.cursor/rules/*.mdc` files exist with exact canonical names
- No stale/legacy file names exist
- Planning skill contains `ALWAYS`, `MUST`, `FORBIDDEN` imperatives, stage table, do-not-skip section, resume section, completion presentation section
- Failure skill contains `HARD STOP`, 3 failure type distinctions, hard-stop checklist, repair-by-guessing prohibition
- MCP skill contains generic-agent note, negative examples with `INVALID` markers, `RAW` response rule, `payload.request` requirement
- Execution skill contains `Handoff Contract`, `DO NOT Retrieve`, `forbidden by design`
- Activation skill contains generic agent wording, decision heuristic, activation decision table, bias-toward-activation rule

### Canonical Contract Rejection (`CanonicalContractRejectionTests`)

- `RequirementIntent`: rejects missing `task_id`, `task_type`, `goal`, `complexity`, `hard_constraints`, `risk_signals`; rejects legacy alias `objective`
- `RetrievalChunkSet`: rejects missing `task_id`, `chunks`, invalid chunk types, missing `core_task`, missing `constraint` chunk when `hard_constraints` non-empty, missing `risk` chunk when `risk_signals` non-empty
- MCP responses: rejects invalid `retrieve_memory_by_chunks` (missing `chunk_results`), invalid `merge_retrieval_results` (missing `merged`), invalid `build_memory_context_pack` (missing `memory_context_pack`)
- `ExecutionPlan`: rejects missing `task_id`, `task`, `scope`, `steps`, `deliverables`; rejects empty `constraints`; rejects empty `forbidden_actions`; rejects legacy alias `objective`
- `WorkerExecutionPacket`: rejects missing `goal`, `scope`, `hard_constraints`, `forbidden_actions`, `steps`, `required_output_sections`; rejects packet without memory prohibition in `execution_rules`

### Generic Agent Activation Skill (`GenericAgentSkillActivationTests`)

- File exists at exact canonical name
- Contains generic agent wording
- Contains semantic planning intent language; explicitly rejects lexical-only activation
- Contains positive activation examples (`migration`, `approach`, `design`)
- Contains non-activation examples (`trivial`, `casual`), with `do NOT activate` wording
- Contains bias-toward-activation rule and decision heuristic
- Distinguishes planning mode from execution mode
- Links to `00-harness-control-plane.mdc` and `02-harness-execution.mdc`
- Contains activation decision table; explicitly handles `uncertain` and `non-trivial` cases

### Skill Operational Strength (`SkillOperationalStrengthTests`)

Verifies that skill files are production-grade operational runbooks, not loose guidance:

- Planning skill: `ALWAYS`, `NEVER`, `MUST`, `FORBIDDEN`; stage table with markdown `|` format; resume section with `get-next-step` and `get-session-status`; completion presentation; do-not-skip; planning+implementation-in-same-message scenario
- Failure skill: `HARD STOP`; repair-by-guessing prohibition; all 3 failure type names; `STOP IMMEDIATELY`; free-form planning prohibition
- MCP skill: generic-agent note section header; exact tool mapping with `EXACTLY`; `RAW` response rule; `payload.request`; negative examples with `INVALID`; Claude and Cursor mentioned
- Execution skill: `Handoff Contract`; `DO NOT Retrieve`; `forbidden by design`; `do not replan`; report assumptions and unresolved issues; `per_step_results`, `final_deliverables`, `validation_summary`
- Activation skill: generic agent; semantic/planning intent; decision heuristic; activation decision table; lexical + insufficient

### Skills + Harness Integration (`SkillAndHarnessFlowTests`)

- Full happy path: all 9 stages complete in order; final stage returns `complete` with `CompletionArtifacts`
- Wrong action at stage 1 (requirement intent), stage 2 (chunk set), stage 3 (validation), and MCP stages all hard-stop
- Malformed MCP responses (missing `chunk_results`, `merged`, `memory_context_pack`) hard-stop with field-specific error messages
- Malformed `ExecutionPlan` (empty `constraints`, empty `forbidden_actions`) hard-stops
- Malformed `WorkerExecutionPacket` (no memory prohibition in `execution_rules`) hard-stops
- All 3 MCP stages return correct `toolName` and `payload.request`
- Out-of-order stage attempts hard-stop; skipping to `complete` hard-stops

### Skill-Driven Loop Integration (`SkillDrivenLoopIntegrationTests`)

Each test provides dual proof: (1) skill file contains correct operational guidance AND (2) harness state machine enforces the corresponding behavior at runtime.

**8 named `SkillDrivenLoop_` tests:**
- `SkillDrivenLoop_SemanticPlanningIntent_ActivatesHarnessFlow` — skill says semantic, harness runs full loop
- `SkillDrivenLoop_ExecutionIntent_DoesNotActivatePlanningFlow` — skill has non-activation examples, harness is not the gate
- `SkillDrivenLoop_HarnessIsOnlyPlanningEntrypoint` — skill uses ALWAYS/MUST/FORBIDDEN, harness controls all transitions
- `SkillDrivenLoop_CannotSkipStages` — skill has NEVER/do-not-skip, harness hard-stops on out-of-order submits
- `SkillDrivenLoop_CannotCallMcpBeforeHarnessRequestsIt` — MCP skill prohibits premature MCP, harness rejects it
- `SkillDrivenLoop_MustSubmitAfterEachStage` — skill requires submit-per-stage, harness rejects re-submission
- `SkillDrivenLoop_StopsOnInvalidCanonicalArtifact` — failure skill mandates HARD STOP, harness hard-stops on invalid artifacts
- `SkillDrivenLoop_CompletesOnlyAfterAllCanonicalStagesAccepted` — skill covers all 9 stages, harness enforces all 8 before complete

**5 named skill strength tests:**
- `ActivationSkill_IsSemanticNotLexicalOnly` — checks "not lexical", "meaning and context", "insufficient", "Detecting the word"
- `ActivationSkill_IsGenericAgentOriented` — checks Claude, Cursor, "any other planning-capable agent", "same regardless"
- `PlanningRule_IsOperationalRunbook_NotSoftGuidance` — checks ALWAYS, MUST, FORBIDDEN, NEVER, do-not-skip, what-to-present
- `FailureRule_RequiresHardStop` — checks HARD STOP, STOP IMMEDIATELY, NEVER, FORBIDDEN, repair-by-guessing, hard-stop-checklist
- `McpToolRule_RequiresExactToolAndPayloadRequest` — checks EXACTLY, payload.request, all 3 exact tool names, RAW, no-substitutions

### Simulated Agent Loop Tests (`SimulatedAgentLoopTests` + `GenericAgentSimulator`)

Uses the `GenericAgentSimulator` in-memory test helper to prove the combined behavior of skill rules and harness enforcement through a realistic agent loop simulation.

**`GenericAgentSimulator`** — deterministic rule-based simulator that:
- Classifies planning intent semantically (mirrors 04-harness-skill-activation.mdc)
- Can run the full 9-stage happy-path loop autonomously
- Supports targeted stage operations: `SubmitValidArtifact`, `SubmitWrongAction`, `SubmitInvalidArtifact`, `AdvanceTo`, `GetNextStep`, `GetSessionStatus`
- Provides canonical valid artifact builders for all 8 action types

**6 activation tests:**
- `SemanticPlanningIntent_WithoutPlanKeyword_ActivatesHarnessLoop` — "how should we approach this refactor?" activates without "plan" keyword
- `LexicalPlan_InExecutionContext_DoesNotActivate` — "that's the plan — let's do it" is correctly rejected (lexical false positive)
- `PlanMode_ActivatesHarnessLoop` — "plan mode" always activates
- `TrivialRename_DoesNotActivate` — trivial rename never activates
- `LooksGoodProceed_DoesNotActivate` — execution approvals do not activate
- `MigrationApproach_WithoutPlanKeyword_Activates` — migration/rollout requests activate without "plan"

**6 strict loop enforcement tests:**
- `WrongStageSubmission_AtStage1_HardStops` — wrong action at stage 1 returns error
- `WrongArtifactShape_AtRequirementIntentStage_HardStops` — invalid artifact JSON fails at requirement intent stage
- `WrongMcpTool_SubmittedResult_HardStops` — wrong artifact type for MCP stage fails
- `ExecutionPlan_SubmittedTooEarly_IsRejected` — execution plan at wrong stage fails
- `WorkerPacket_SubmittedTooEarly_IsRejected` — worker packet at wrong stage fails
- `McpBeforeHarnessRequests_IsRejected` — MCP result before harness reaches MCP stage fails

**2 resume/re-sync tests:**
- `LostContext_Resume_ViaGetNextStep_Succeeds` — get-next-step returns correct stage after mid-loop context loss
- `LostContext_GetSessionStatus_ReturnsCorrectState` — get-session-status reports correct pending stage

**6 skills+harness proof tests:**
- `FullLoop_CompletesWithRequiredArtifacts_ViaSimulator` — full 9-stage loop completes via simulator
- `FreeFormSkip_ToComplete_IsRejected` — skipping to complete without stages fails
- `HarnessProvides_ExactMcpToolName_ForAgent` — harness returns exact tool name and payload for MCP stages
- `RawMcpResult_SubmittedToHarness_IsAccepted` — valid MCP response is accepted and advances stage
- `ErrorState_SessionLocked_CannotContinue` — session in error state rejects further submissions
- `MultipleSemanticRequests_EachActivatesIndependently` — multiple concurrent sessions are independent

---

## Architecture Reminder

This repository implements a **Control-Plane-Only Harness**:

- The **harness** (`HarnessMcp.ControlPlane`) controls stage sequencing, validates artifacts, and returns the next action
- The **harness does NOT** call LLM APIs, call MCP tools, do planning, or execute agent work
- The **agent** performs all work; the harness tells the agent what to do next
- The **skills** (`.cursor/rules/*.mdc`) define the semantic flow for any planning-capable agent

---

## Forbidden Actions for Agents Evaluating This Gate

- **Do NOT weaken any test** (removing `.Contain()` assertions, changing `BeFalse` to `BeTrue`, etc.)
- **Do NOT delete any test**
- **Do NOT skip tests** with `[Skip]` or by commenting out `[Fact]`
- **Do NOT claim success while any test is red**
- **Do NOT report test results you did not actually run**
- **Do NOT add `HarnessMcp.AgentClient` or any LLM API / MCP client code to the harness**

If tests fail, fix the **implementation** (skills, validators, harness code), not the tests.

---

## Implementation Checklist

Before running tests, verify:

- [ ] All 5 `.cursor/rules/*.mdc` files exist with canonical names (00 through 04)
- [ ] No stale `.cursor/rules/*.mdc` files with legacy names exist
- [ ] `ExecutionPlanValidator` requires non-empty `constraints` (always) and non-empty `forbidden_actions`
- [ ] `WorkerExecutionPacketValidator` requires memory prohibition in `execution_rules`
- [ ] All canonical artifact validators reject legacy aliases (e.g., `objective` instead of `task`/`task_id`)
- [ ] `Scripts\invoke-harness-control-plane.ps1` uses `$cmdArgs` (not `$args`) and validates required arguments per command
- [ ] `build.ps1` targets only `src\HarnessMcp.ControlPlane\HarnessMcp.ControlPlane.csproj`

---

## Test Run Summary Template

When reporting completion, include:

```
Test command:  dotnet test tests/HarnessMcp.ControlPlane.Tests/HarnessMcp.ControlPlane.Tests.csproj
Total tests:   [N]
Passed:        [N]
Failed:        0
Exit code:     0
```

Any deviation from `Failed: 0` means the gate has not been cleared.
